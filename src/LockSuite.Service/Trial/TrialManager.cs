using System;
using System.Diagnostics;
using System.IO;
using LockSuite.Core;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Trial;

/// <summary>
/// Gestiona el "modo de prueba". Persiste trial.json en
/// %ProgramData%\LockSuite\config\, calcula el estado contra el reloj y ejecuta
/// la decisión final.
///
/// Flujo normal (EndAction=Prompt): el período corre; al vencer, el estado pasa a
/// AwaitingDecision y la GUI pregunta al usuario si conservar o quitar.
///   - "Conservar" -> queda permanente (Enabled=false). Desde ahí solo el admin lo
///     saca con el desinstalador normal (que requiere admin).
///   - "Quitar"    -> lanza el desinstalador silencioso de Inno Setup, que revierte
///     WFP/NRPT (sus UninstallRun) y borra todo.
///
/// Fail-closed: mientras no haya decisión, el filtro SIGUE activo. Nada se borra
/// "solo", salvo que EndAction=AutoRemove (prueba descartable, p. ej. 10 min en VM),
/// en cuyo caso el Worker llama a CheckAutoRemove() y al vencer desinstala sin preguntar.
/// </summary>
public sealed class TrialManager
{
    private readonly ILogger<TrialManager> _logger;
    private readonly string _trialPath;
    private readonly object _lock = new();
    private TrialConfig _config;
    private DateTime _lastLoadedMtime = DateTime.MinValue;
    private bool _removalLaunched;

    public TrialManager(ILogger<TrialManager> logger, string? customPath = null)
    {
        _logger = logger;
        if (!string.IsNullOrEmpty(customPath))
        {
            _trialPath = customPath;
        }
        else
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            _trialPath = Path.Combine(programData, "LockSuite", "config", "trial.json");
        }
        _config = Load();
    }

    private void ReloadIfChanged()
    {
        try
        {
            if (!File.Exists(_trialPath)) return;
            var mtime = File.GetLastWriteTimeUtc(_trialPath);
            if (mtime > _lastLoadedMtime)
            {
                _config = Load();
            }
        }
        catch { /* best-effort */ }
    }

    public TrialStatusDto GetStatus()
    {
        lock (_lock)
        {
            ReloadIfChanged();
            var now = DateTimeOffset.UtcNow;
            return new TrialStatusDto
            {
                Enabled = _config.Enabled,
                State = _config.GetState(now).ToString(),
                RemainingSeconds = _config.RemainingSeconds(now),
                DurationMinutes = _config.DurationMinutes,
                EndUtc = _config.EndUtc,
                EndAction = _config.EndAction.ToString(),
                RequireTutorToRemove = _config.RequireTutorToRemove
            };
        }
    }

    /// <summary>Lee (sin lock de escritura) si quitar requiere contraseña del tutor.</summary>
    public bool RequiresTutorToRemove
    {
        get { lock (_lock) { ReloadIfChanged(); return _config.Enabled && _config.RequireTutorToRemove; } }
    }

    public void StartTrial(int durationMinutes, TrialEndAction endAction, bool requireTutorToRemove, DateTimeOffset? startUtc = null)
    {
        if (durationMinutes < 1) durationMinutes = 1;
        lock (_lock)
        {
            _config = TrialConfig.StartNow(durationMinutes, endAction, requireTutorToRemove, startUtc ?? DateTimeOffset.UtcNow);
            _removalLaunched = false;
            Save(_config);
        }
        _logger.LogInformation("Modo de prueba iniciado: {Min} min, acción al terminar={Action}, pedir tutor para quitar={Tutor}.",
            durationMinutes, endAction, requireTutorToRemove);
    }

    /// <summary>Decisión del usuario al terminar la prueba. keep=true conserva; keep=false quita todo.</summary>
    public bool Decide(bool keep, bool isAuthorized = false)
    {
        lock (_lock)
        {
            ReloadIfChanged();
            if (!_config.Enabled) return false;
            var now = DateTimeOffset.UtcNow;

            if (!keep)
            {
                var state = _config.GetState(now);
                if (state != TrialState.AwaitingDecision && !isAuthorized)
                {
                    _logger.LogWarning("Intento de quitar LockSuite por decisión de prueba antes de que la prueba haya vencido sin autorización. Rechazado.");
                    return false;
                }
            }

            if (keep)
            {
                _config.Decided = "kept";
                _config.Enabled = false; // instalación permanente normal
                Save(_config);
                _logger.LogInformation("Prueba: el usuario eligió CONSERVAR LockSuite. Instalación permanente.");
                return true;
            }
            else
            {
                _config.Decided = "removed";
                Save(_config);
                _logger.LogInformation("Prueba: el usuario eligió QUITAR LockSuite. Lanzando desinstalación.");
                LaunchUninstall();
                return true;
            }
        }
    }

    /// <summary>Llamado periódicamente por el Worker. Si EndAction=AutoRemove y venció, desinstala sin preguntar.</summary>
    public void CheckAutoRemove()
    {
        lock (_lock)
        {
            ReloadIfChanged();
            if (!_config.Enabled) return;
            if (_config.EndAction != TrialEndAction.AutoRemove) return;
            var now = DateTimeOffset.UtcNow;
            if (_config.IsExpired(now) && !string.Equals(_config.Decided, "removed", StringComparison.OrdinalIgnoreCase))
            {
                _config.Decided = "removed";
                Save(_config);
                _logger.LogInformation("Prueba descartable vencida (AutoRemove): desinstalando automáticamente.");
                LaunchUninstall();
            }
        }
    }

    private void LaunchUninstall()
    {
        if (_removalLaunched) return;
        _removalLaunched = true;
        try
        {
            string? uninstaller = FindUninstaller();
            if (uninstaller == null)
            {
                _logger.LogWarning("No se encontró el desinstalador (unins000.exe). ¿Ejecución no instalada? No se desinstala.");
                _removalLaunched = false;
                return;
            }
            var psi = new ProcessStartInfo
            {
                FileName = uninstaller,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true
            };
            Process.Start(psi);
            _logger.LogInformation("Desinstalador lanzado: {Path}", uninstaller);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al lanzar el desinstalador de LockSuite.");
            _removalLaunched = false; // permitir reintento
        }
    }

    /// <summary>Busca el desinstalador de Inno Setup. El servicio corre desde {app}\service\;
    /// Inno deja unins000.exe en {app}\.</summary>
    private static string? FindUninstaller()
    {
        try
        {
            string serviceDir = AppContext.BaseDirectory;            // ...\LockSuite\service\
            string appDir = Path.GetFullPath(Path.Combine(serviceDir, ".."));
            for (int i = 0; i <= 9; i++)
            {
                string candidate = Path.Combine(appDir, $"unins{i:D3}.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { /* best-effort */ }
        return null;
    }

    private TrialConfig Load()
    {
        try
        {
            if (File.Exists(_trialPath))
            {
                _lastLoadedMtime = File.GetLastWriteTimeUtc(_trialPath);
                return TrialConfig.FromJson(File.ReadAllText(_trialPath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo leer trial.json; se asume sin prueba.");
        }
        return new TrialConfig();
    }

    private void Save(TrialConfig cfg)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_trialPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = _trialPath + ".tmp";
            File.WriteAllText(tmp, cfg.ToJson());
            File.Move(tmp, _trialPath, overwrite: true);
            _lastLoadedMtime = File.GetLastWriteTimeUtc(_trialPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo guardar trial.json.");
        }
    }
}
