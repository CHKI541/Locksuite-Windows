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
        string? pipeName = null)
    {
        _logger = logger;
        _configManager = configManager;
        _securityManager = securityManager;
        _wfpManager = wfpManager;
        _dnsResolver = dnsResolver;
        _learningManager = learningManager;
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

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        while (!token.IsCancellationRequested && pipe.IsConnected)
        {
            string? line = await reader.ReadLineAsync(token);
            if (line == null) break;

            var request = IpcRequest.Deserialize(line);
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
            await writer.WriteLineAsync(responseJson.AsMemory(), token);
            await writer.FlushAsync(token);
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
                        FailClosed = _configManager.CurrentConfig.FailClosed,
                        LearningMode = _configManager.CurrentConfig.LearningMode,
                        IsPasswordConfigured = _securityManager.IsPasswordSet(),
                        ActiveDynamicRulesCount = _wfpManager.ActiveDynamicRulesCount,
                        TotalQueries = _dnsResolver.TotalQueries,
                        AllowedQueries = _dnsResolver.AllowedQueries,
                        BlockedQueries = _dnsResolver.BlockedQueries,
                        WhitelistPatternsCount = _configManager.CurrentConfig.ActivePatterns().Count
                    };
                    return IpcResponse.Ok(JsonSerializer.Serialize(status));

                case "GetConfig":
                    return IpcResponse.Ok(_configManager.CurrentConfig.ToJson());

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
