using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LockSuite.Core;
using LockSuite.Service.Config;
using LockSuite.Service.Dns;
using LockSuite.Service.Learning;
using LockSuite.Service.Security;
using LockSuite.Service.Trial;
using LockSuite.Service.Wfp;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Ipc;

public sealed class NamedPipeServer : IDisposable
{
    private readonly ILogger<NamedPipeServer> _logger;
    private readonly ConfigManager _configManager;
    private readonly SecurityManager _securityManager;
    private readonly IWfpManager _wfpManager;
    private readonly DnsResolver _dnsResolver;
    private readonly LearningManager _learningManager;
    private readonly TrialManager? _trialManager;
    private readonly LockSuite.Service.CaptivePortal.CaptivePortalManager? _captivePortal;
    private readonly string _pipeName;

    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public NamedPipeServer(
        ILogger<NamedPipeServer> logger,
        ConfigManager configManager,
        SecurityManager securityManager,
        IWfpManager wfpManager,
        DnsResolver dnsResolver,
        LearningManager learningManager,
        TrialManager? trialManager = null,
        string? pipeName = null,
        LockSuite.Service.CaptivePortal.CaptivePortalManager? captivePortal = null)
    {
        _logger = logger;
        _configManager = configManager;
        _securityManager = securityManager;
        _wfpManager = wfpManager;
        _dnsResolver = dnsResolver;
        _learningManager = learningManager;
        _trialManager = trialManager;
        _captivePortal = captivePortal;
        _pipeName = pipeName ?? IpcConstants.PipeName;
    }

    public void Start()
    {
        if (_serverTask != null) return;

        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoopAsync(_cts.Token));
        _logger.LogInformation("Servidor IPC de Named Pipe iniciado en '{PipeName}'", _pipeName);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _serverTask = null;
        _logger.LogInformation("Servidor IPC de Named Pipe detenido");
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var pipeServer = CreatePipeServer(_pipeName);
                await pipeServer.WaitForConnectionAsync(token);

                _ = Task.Run(async () =>
                {
                    using (pipeServer)
                    {
                        await HandleClientAsync(pipeServer, token);
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Error en el bucle del servidor Named Pipe");
                    await Task.Delay(200, token);
                }
            }
        }
    }

    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var ps = new PipeSecurity();
                var sidWorld = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                ps.AddAccessRule(new PipeAccessRule(sidWorld, PipeAccessRights.ReadWrite, AccessControlType.Allow));

                var current = WindowsIdentity.GetCurrent();
                if (current.User != null)
                {
                    ps.SetOwner(current.User);
                    ps.AddAccessRule(new PipeAccessRule(current.User, PipeAccessRights.FullControl, AccessControlType.Allow));
                }

                return NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0,
                    0,
                    ps);
            }
            catch
            {
                // Fallback a creación sin ACL personalizada si no se tienen permisos
            }
        }

        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    // Una conexion IPC legitima envia su peticion de inmediato; si no llega nada en este tiempo
    // se cierra, para que un cliente colgado no retenga recursos del servicio indefinidamente.
    private static readonly TimeSpan ClientIdleTimeout = TimeSpan.FromSeconds(30);

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        // UTF-8 SIN BOM y sin AutoFlush: ver LockSuiteClient.IpcEncoding (evita un deadlock de BOM
        // entre cliente y servidor, ya que el pipe no tiene buffer).
        using var reader = new StreamReader(pipe, LockSuiteClient.IpcEncoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, LockSuiteClient.IpcEncoding, bufferSize: 4096, leaveOpen: true);

        try
        {
            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                idle.CancelAfter(ClientIdleTimeout);

                string? line = await reader.ReadLineAsync(idle.Token);
                if (line == null) break;

                IpcRequest? request;
                try
                {
                    request = IpcRequest.Deserialize(line);
                }
                catch (JsonException)
                {
                    request = null;
                }

                IpcResponse response;
                if (request == null)
                {
                    response = IpcResponse.Fail("Petición IPC inválida");
                }
                else
                {
                    response = ProcessRequest(request);
                }

                string responseJson = IpcResponse.Serialize(response);
                await writer.WriteLineAsync(responseJson.AsMemory(), idle.Token);
                await writer.FlushAsync(idle.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Servicio deteniendose o cliente inactivo: cierre normal.
        }
        catch (IOException)
        {
            // El cliente cerro la conexion a mitad de una operacion: normal.
        }
    }

    internal IpcResponse ProcessRequest(IpcRequest request)
    {
        try
        {
            switch (request.Command)
            {
                case "Ping":
                    return IpcResponse.Ok("Pong");

                case "GetStatus":
                    var status = new ServiceStatusDto
                    {
                        IsRunning = true,
                        IsWfpActive = _wfpManager.IsActive,
                        IsSimulationMode = _wfpManager.IsSimulationMode,
                        IsDnsListening = _dnsResolver.IsListening,
                        FailClosed = _configManager.CurrentConfig.FailClosed,
                        LearningMode = _configManager.CurrentConfig.LearningMode,
                        IsPasswordConfigured = _securityManager.IsPasswordSet(),
                        ActiveDynamicRulesCount = _wfpManager.ActiveDynamicRulesCount,
                        TotalQueries = _dnsResolver.TotalQueries,
                        AllowedQueries = _dnsResolver.AllowedQueries,
                        BlockedQueries = _dnsResolver.BlockedQueries,
                        WhitelistPatternsCount = _configManager.ActivePatternCount,
                        AllowLocalNetwork = _configManager.AllowLocalNetwork,
                        CaptivePortalActive = _captivePortal?.IsActive ?? false,
                        CaptivePortalRemainingSeconds = _captivePortal?.RemainingSeconds ?? 0
                    };
                    return IpcResponse.Ok(JsonSerializer.Serialize(status));

                case "GetConfig":
                    return IpcResponse.Ok(_configManager.GetConfigJson());

                case "GetRecentBlocked":
                    var recent = _learningManager.GetRecent();
                    return IpcResponse.Ok(JsonSerializer.Serialize(recent));

                case "Authenticate":
                    var authDto = JsonSerializer.Deserialize<AuthRequestDto>(request.Payload ?? "{}");
                    if (authDto == null || string.IsNullOrEmpty(authDto.Password))
                    {
                        return IpcResponse.Fail("Contraseña no suministrada");
                    }
                    var authResult = _securityManager.Authenticate(authDto.Password, out string? sessionToken);
                    if (authResult == AuthResult.Success)
                    {
                        return IpcResponse.Ok(sessionToken);
                    }
                    else if (authResult == AuthResult.LockedOut)
                    {
                        return IpcResponse.Fail("Cuenta temporalmente bloqueada por límite de intentos fallidos");
                    }
                    else if (authResult == AuthResult.NotInitialized)
                    {
                        return IpcResponse.Fail("Contraseña maestra no inicializada");
                    }
                    return IpcResponse.Fail("Contraseña incorrecta");

                case "SetInitialPassword":
                    var initDto = JsonSerializer.Deserialize<AuthRequestDto>(request.Payload ?? "{}");
                    if (initDto == null || string.IsNullOrWhiteSpace(initDto.Password))
                    {
                        return IpcResponse.Fail("Contraseña inválida");
                    }
                    bool initSuccess = _securityManager.SetInitialPassword(initDto.Password);
                    return initSuccess
                        ? IpcResponse.Ok()
                        : IpcResponse.Fail("Ya existe una contraseña configurada o la contraseña es inválida");

                case "ChangePassword":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    var changeDto = JsonSerializer.Deserialize<AuthRequestDto>(request.Payload ?? "{}");
                    if (changeDto == null || string.IsNullOrWhiteSpace(changeDto.Password) || string.IsNullOrWhiteSpace(changeDto.NewPassword))
                    {
                        return IpcResponse.Fail("Datos de cambio de contraseña inválidos");
                    }
                    bool changeSuccess = _securityManager.ChangePassword(changeDto.Password, changeDto.NewPassword);
                    return changeSuccess
                        ? IpcResponse.Ok()
                        : IpcResponse.Fail("Fallo al cambiar la contraseña");

                case "ClearRecentBlocked":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    _learningManager.Clear();
                    return IpcResponse.Ok();

                case "SaveConfig":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    if (string.IsNullOrWhiteSpace(request.Payload)) return IpcResponse.Fail("Configuración vacía");
                    var newConfig = WhitelistConfig.FromJson(request.Payload);
                    _configManager.UpdateConfig(newConfig);
                    return IpcResponse.Ok();

                case "TogglePack":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    var toggleDto = JsonSerializer.Deserialize<TogglePackDto>(request.Payload ?? "{}");
                    if (toggleDto == null) return IpcResponse.Fail("Datos inválidos");
                    _configManager.TogglePack(toggleDto.PackName, toggleDto.Enabled);
                    return IpcResponse.Ok();

                case "AddDomain":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    var addDto = JsonSerializer.Deserialize<AddDomainDto>(request.Payload ?? "{}");
                    if (addDto == null || string.IsNullOrWhiteSpace(addDto.Domain))
                    {
                        return IpcResponse.Fail("Dominio inválido");
                    }
                    bool added = _configManager.AddDomain(addDto.Domain, addDto.PackName);
                    return added ? IpcResponse.Ok() : IpcResponse.Fail("No se pudo agregar el dominio");

                case "RemoveDomain":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    var domain = request.Payload?.Trim();
                    if (string.IsNullOrWhiteSpace(domain)) return IpcResponse.Fail("Dominio inválido");
                    bool removed = _configManager.RemoveDomain(domain);
                    return removed ? IpcResponse.Ok() : IpcResponse.Fail("Dominio no encontrado");

                case "TestWfp":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    _wfpManager.StartAutoRollback(TimeSpan.FromSeconds(30));
                    return IpcResponse.Ok("WFP Test activado. Auto-rollback en 30 segundos.");

                case "SetLearningMode":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    if (!bool.TryParse(request.Payload, out bool learningEnabled))
                        return IpcResponse.Fail("Valor invalido para modo aprendizaje");
                    _configManager.SetLearningMode(learningEnabled);
                    return IpcResponse.Ok();

                case "GetTrialStatus":
                    if (_trialManager == null) return IpcResponse.Fail("Modo de prueba no disponible");
                    return IpcResponse.Ok(JsonSerializer.Serialize(_trialManager.GetStatus()));

                case "DecideTrial":
                    if (_trialManager == null) return IpcResponse.Fail("Modo de prueba no disponible");
                    var decideDto = JsonSerializer.Deserialize<TrialDecisionDto>(request.Payload ?? "{}");
                    if (decideDto == null) return IpcResponse.Fail("Datos inválidos");
                    if (decideDto.Keep)
                    {
                        // Conservar es la acción protectora: siempre permitida.
                        _trialManager.Decide(true);
                        return IpcResponse.Ok();
                    }
                    // Quitar: si la prueba se configuró con "pedir contraseña del tutor", exigir sesión válida.
                    bool hasAuth = RequireAuth(request);
                    if (_trialManager.RequiresTutorToRemove && !hasAuth)
                        return IpcResponse.Fail("Se requiere la contraseña del tutor para quitar LockSuite");

                    bool success = _trialManager.Decide(false, isAuthorized: hasAuth);
                    if (!success)
                        return IpcResponse.Fail("No se puede quitar LockSuite antes de que venza el período de prueba a menos que se cuente con autorización del tutor.");

                    return IpcResponse.Ok();

                case "SetTrial":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    if (_trialManager == null) return IpcResponse.Fail("Modo de prueba no disponible");
                    var setDto = JsonSerializer.Deserialize<SetTrialDto>(request.Payload ?? "{}");
                    if (setDto == null || setDto.DurationMinutes < 1) return IpcResponse.Fail("Datos inválidos");
                    var endAction = string.Equals(setDto.EndAction, "autoremove", StringComparison.OrdinalIgnoreCase)
                        ? TrialEndAction.AutoRemove
                        : TrialEndAction.Prompt;
                    _trialManager.StartTrial(setDto.DurationMinutes, endAction, setDto.RequireTutorToRemove);
                    return IpcResponse.Ok();

                case "SetAllowLocalNetwork":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    if (!bool.TryParse(request.Payload, out bool allowLocal))
                        return IpcResponse.Fail("Valor invalido");
                    _configManager.SetAllowLocalNetwork(allowLocal);
                    return IpcResponse.Ok();

                case "StartCaptivePortal":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    if (_captivePortal == null) return IpcResponse.Fail("Modo portal cautivo no disponible");
                    int minutes = int.TryParse(request.Payload, out int m) ? m : LockSuite.Service.CaptivePortal.CaptivePortalManager.DefaultMinutes;
                    int opened = _captivePortal.Start(minutes);
                    return opened > 0
                        ? IpcResponse.Ok(opened.ToString())
                        : IpcResponse.Fail("No se encontró una red local (¿el equipo está conectado a la Wi-Fi?).");

                case "EndCaptivePortal":
                    if (!RequireAuth(request)) return IpcResponse.Fail("No autorizado");
                    _captivePortal?.End();
                    return IpcResponse.Ok();

                default:
                    return IpcResponse.Fail($"Comando desconocido: '{request.Command}'");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar comando IPC: {Command}", request.Command);
            return IpcResponse.Fail($"Error interno: {ex.Message}");
        }
    }

    private bool RequireAuth(IpcRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionToken)) return false;
        return _securityManager.ValidateSession(request.SessionToken);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
