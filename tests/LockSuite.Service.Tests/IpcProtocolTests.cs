using System;
using System.IO;
using System.Text.Json;
using LockSuite.Core;
using LockSuite.Service.Config;
using LockSuite.Service.Dns;
using LockSuite.Service.Ipc;
using LockSuite.Service.Learning;
using LockSuite.Service.Security;
using LockSuite.Service.Wfp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LockSuite.Service.Tests;

public class IpcProtocolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigManager _configManager;
    private readonly SecurityManager _securityManager;
    private readonly WfpManager _wfpManager;
    private readonly LearningManager _learningManager;
    private readonly DnsResolver _dnsResolver;
    private readonly NamedPipeServer _server;

    public IpcProtocolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "locksuite_ipc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        string cfgPath = Path.Combine(_tempDir, "whitelist.json");
        string authPath = Path.Combine(_tempDir, "auth.dat");
        string logPath = Path.Combine(_tempDir, "blocked.json");

        _configManager = new ConfigManager(NullLogger<ConfigManager>.Instance, cfgPath);
        _securityManager = new SecurityManager(NullLogger<SecurityManager>.Instance, authPath);
        _wfpManager = new WfpManager(NullLogger<WfpManager>.Instance);
        _wfpManager.Initialize(failClosed: false);
        _learningManager = new LearningManager(NullLogger<LearningManager>.Instance, logPath);
        _dnsResolver = new DnsResolver(NullLogger<DnsResolver>.Instance, _configManager, _wfpManager, _learningManager, listenPort: 5354);

        _server = new NamedPipeServer(
            NullLogger<NamedPipeServer>.Instance,
            _configManager,
            _securityManager,
            _wfpManager,
            _dnsResolver,
            _learningManager);
    }

    public void Dispose()
    {
        _server.Dispose();
        _dnsResolver.Dispose();
        _wfpManager.Dispose();

        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void PingCommand_ReturnsPong()
    {
        var resp = _server.ProcessRequest(new IpcRequest { Command = "Ping" });

        Assert.True(resp.Success);
        Assert.Equal("Pong", resp.Payload);
    }

    [Fact]
    public void GetStatusCommand_ReturnsServiceStatus()
    {
        var resp = _server.ProcessRequest(new IpcRequest { Command = "GetStatus" });

        Assert.True(resp.Success);
        Assert.NotNull(resp.Payload);
        var status = JsonSerializer.Deserialize<ServiceStatusDto>(resp.Payload);
        Assert.NotNull(status);
        Assert.True(status.IsRunning);
        Assert.True(status.IsWfpActive);
        Assert.True(status.WhitelistPatternsCount > 0);
    }

    [Fact]
    public void GetConfigCommand_ReturnsConfigJson()
    {
        var resp = _server.ProcessRequest(new IpcRequest { Command = "GetConfig" });

        Assert.True(resp.Success);
        Assert.NotNull(resp.Payload);
        var cfg = WhitelistConfig.FromJson(resp.Payload);
        Assert.NotNull(cfg);
        Assert.NotEmpty(cfg.Packs);
    }

    [Fact]
    public void PasswordAndProtectedOperations_WorkflowSucceeds()
    {
        // 1. Establecer contraseña inicial
        var initResp = _server.ProcessRequest(new IpcRequest
        {
            Command = "SetInitialPassword",
            Payload = JsonSerializer.Serialize(new AuthRequestDto { Password = "ClaveMaestra123!" })
        });
        Assert.True(initOk(initResp));

        // 2. Autenticar con contraseña errónea
        var badAuth = _server.ProcessRequest(new IpcRequest
        {
            Command = "Authenticate",
            Payload = JsonSerializer.Serialize(new AuthRequestDto { Password = "ClaveIncorrecta" })
        });
        Assert.False(badAuth.Success);

        // 3. Autenticar con contraseña correcta
        var goodAuth = _server.ProcessRequest(new IpcRequest
        {
            Command = "Authenticate",
            Payload = JsonSerializer.Serialize(new AuthRequestDto { Password = "ClaveMaestra123!" })
        });
        Assert.True(goodAuth.Success);
        string? token = goodAuth.Payload;
        Assert.NotNull(token);

        // 4. Agregar dominio sin token -> debe fallar
        var addNoToken = _server.ProcessRequest(new IpcRequest
        {
            Command = "AddDomain",
            Payload = JsonSerializer.Serialize(new AddDomainDto { Domain = "permitido-test.com", PackName = "Personalizados" })
        });
        Assert.False(addNoToken.Success);

        // 5. Agregar dominio con token válido -> debe ser exitoso
        var addWithToken = _server.ProcessRequest(new IpcRequest
        {
            Command = "AddDomain",
            SessionToken = token,
            Payload = JsonSerializer.Serialize(new AddDomainDto { Domain = "permitido-test.com", PackName = "Personalizados" })
        });
        Assert.True(addWithToken.Success);
        Assert.True(_configManager.IsAllowed("permitido-test.com"));

        // 6. Remover dominio con token válido
        var removeWithToken = _server.ProcessRequest(new IpcRequest
        {
            Command = "RemoveDomain",
            SessionToken = token,
            Payload = "permitido-test.com"
        });
        Assert.True(removeWithToken.Success);
        Assert.False(_configManager.IsAllowed("permitido-test.com"));
    }

    private static bool initOk(IpcResponse r) => r.Success;
}
