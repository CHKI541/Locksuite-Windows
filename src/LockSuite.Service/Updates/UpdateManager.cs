using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Updates;

public sealed class UpdateManager : IDisposable
{
    private readonly ILogger<UpdateManager> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _repoOwner = "CHKI541";
    private readonly string _repoName = "Locksuite-Windows";
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private Timer? _periodicTimer;
    private bool _disposed;

    // Intervalo de chequeo: cada 4 horas
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);
    // Demora inicial al arrancar el servicio: 60 segundos para permitir estabilizar red/WFP
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);

    public UpdateManager(ILogger<UpdateManager> logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LockSuite-Updater", Program.Version));
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public void StartPeriodicChecks()
    {
        _periodicTimer = new Timer(async _ =>
        {
            try
            {
                await CheckAndApplyUpdateAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error en ciclo periódico de auto-actualización.");
            }
        }, null, InitialDelay, CheckInterval);

        _logger.LogInformation("Actualizador automático iniciado. Próximo chequeo en {Delay}s.", (int)InitialDelay.TotalSeconds);
    }

    public async Task<bool> CheckAndApplyUpdateAsync(CancellationToken cancellationToken)
    {
        if (!await _updateLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogInformation("Ya hay una verificación o descarga de actualización en curso.");
            return false;
        }

        try
        {
            _logger.LogInformation("Verificando si hay nuevas actualizaciones en GitHub ({Owner}/{Repo})...", _repoOwner, _repoName);

            string apiUrl = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub API devolvió código {StatusCode} al buscar actualizaciones.", response.StatusCode);
                return false;
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var jsonDoc = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElem))
            {
                _logger.LogWarning("Respuesta de GitHub Release no contiene 'tag_name'.");
                return false;
            }

            string tagName = tagElem.GetString() ?? "";
            string cleanRemoteVersion = tagName.TrimStart('v', 'V');

            if (!Version.TryParse(cleanRemoteVersion, out var remoteVer) ||
                !Version.TryParse(Program.Version, out var currentVer))
            {
                _logger.LogWarning("No se pudo comparar versiones: Remota='{Remote}', Local='{Local}'", cleanRemoteVersion, Program.Version);
                return false;
            }

            if (remoteVer <= currentVer)
            {
                _logger.LogInformation("LockSuite está actualizado a la última versión (v{Current}).", Program.Version);
                return false;
            }

            _logger.LogInformation("Nueva versión detectada: v{Remote} (actual: v{Current}). Buscando instalador...", remoteVer, currentVer);

            string? downloadUrl = null;
            string? assetName = null;

            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp) &&
                        asset.TryGetProperty("browser_download_url", out var urlProp))
                    {
                        string name = nameProp.GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            name.Contains("LockSuiteSetup", StringComparison.OrdinalIgnoreCase))
                        {
                            assetName = name;
                            downloadUrl = urlProp.GetString();
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(assetName))
            {
                _logger.LogWarning("El release v{Version} no contiene un ejecutable de instalador válido.", remoteVer);
                return false;
            }

            string updatesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LockSuite",
                "updates");

            Directory.CreateDirectory(updatesDir);
            string targetPath = Path.Combine(updatesDir, assetName);

            _logger.LogInformation("Descargando actualización desde {Url} hacia {Path}...", downloadUrl, targetPath);

            using (var downloadResp = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                downloadResp.EnsureSuccessStatusCode();
                await using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await downloadResp.Content.CopyToAsync(fileStream, cancellationToken);
                }
            }

            var fileInfo = new FileInfo(targetPath);
            if (!fileInfo.Exists || fileInfo.Length < 1_000_000)
            {
                _logger.LogError("El archivo descargado en {Path} es inválido o corrupto ({Size} bytes).", targetPath, fileInfo.Length);
                return false;
            }

            _logger.LogInformation("Instalador descargado con éxito ({Size:N0} bytes). Ejecutando actualización silenciosa...", fileInfo.Length);

            // Inno Setup /VERYSILENT /SUPPRESSMSGBOXES /NORESTART detiene el servicio viejo,
            // sobreescribe los ejecutables en C:\Program Files\LockSuite y levanta el nuevo servicio.
            var psi = new ProcessStartInfo
            {
                FileName = targetPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true
            };

            Process.Start(psi);
            _logger.LogInformation("Instalador de LockSuite lanzado con éxito para actualización a v{Version}.", remoteVer);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo durante la auto-actualización de LockSuite.");
            return false;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _periodicTimer?.Dispose();
        _httpClient.Dispose();
        _updateLock.Dispose();
    }
}
