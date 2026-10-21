using System;
using System.IO;
using System.Text.Json;
using LockSuite.Core;
using LockSuite.Service.Config;
using LockSuite.Service.Dns;
using LockSuite.Service.Ipc;
using LockSuite.Service.Learning;
using LockSuite.Service.Security;
using LockSuite.Service.Trial;
using LockSuite.Service.Wfp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LockSuite.Service.Tests;

public class TrialIpcTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigManager _configManager;
    private readonly SecurityManager _securityManager;
    private readonly WfpManager _wfpManager;
    private readonly LearningManager _learningManager;
    private readonly DnsResolver _dnsResolver;
    private readonly TrialManager _trialManager;
    private readonly NamedPipeServer _server;

    public TrialIpcTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "locksuite_trialipc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configManager = new ConfigManager(NullLogger<ConfigManager>.Instance, Path.Combine(_tempDir, "whitelist.json"));
        _securityManager = new SecurityManager(NullLogger<SecurityManager>.Instance, Path.Combine(_tempDir, "auth.dat"));
        _wfpManager = new WfpManager(NullLogger<WfpManager>.Instance);
        _wfpManager.Initialize(failClosed: false);
        _learningManager = new LearningManager(NullLogger<LearningManager>.Instance, Path.Combine(_tempDir, "blocked.json"));
        _dnsResolver = new DnsResolver(NullLogger<DnsResolver>.Instance, _configManager, _wfpManager, _learningManager, listenPort: 5355);
        _trialManager = new TrialManager(NullLogger<TrialManager>.Instance, Path.Combine(_tempDir, "trial.json"));
        _server = new NamedPipeServer(
            NullLogger<NamedPipeServer>.Instance,
            _configManager, _securityManager, _wfpManager, _dnsResolver, _learningManager, _trialManager);
    }

    public void Dispose()
    {
        _server.Dispose();
        _dnsResolver.Dispose();
        _wfpManager.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void GetTrialStatus_NoTrial_ReturnsDisabled()
    {
        var resp = _server.ProcessRequest(new IpcRequest { Command = "GetTrialStatus" });
        Assert.True(resp.Success);
        var dto = JsonSerializer.Deserialize<TrialStatusDto>(resp.Payload!);
        Assert.NotNull(dto);
        Assert.False(dto!.Enabled);
    }

    [Fact]
    public void DecideTrial_Keep_Works_WithoutAuth()
    {
        _trialManager.StartTrial(60, TrialEndAction.Prompt, false);
        var resp = _server.ProcessRequest(new IpcRequest
        {
            Command = "DecideTrial",
            Payload = JsonSerializer.Serialize(new TrialDecisionDto { Keep = true })
        });
        Assert.True(resp.Success);
        Assert.False(_trialManager.GetStatus().Enabled); // quedo permanente
    }

    [Fact]
    public void DecideTrial_Remove_WithTutorLock_RequiresAuth()
    {
        _trialManager.StartTrial(60, TrialEndAction.Prompt, requireTutorToRemove: true);
        var denied = _server.ProcessRequest(new IpcRequest
        {
            Command = "DecideTrial",
            Payload = JsonSerializer.Serialize(new TrialDecisionDto { Keep = false })
        });
        Assert.False(denied.Success);
        Assert.Equal("Active", _trialManager.GetStatus().State); // sigue sin decidirse
    }

    [Fact]
    public void DecideTrial_Remove_WithoutTutorLock_RejectedBeforeExpiry()
    {
        // En minuto 1 de una prueba de 60 min, un proceso del usuario estándar intenta quitar
        // sin contraseña: DEBE ser rechazado (Finding A1).
        _trialManager.StartTrial(60, TrialEndAction.Prompt, requireTutorToRemove: false);
        var resp = _server.ProcessRequest(new IpcRequest
        {
            Command = "DecideTrial",
            Payload = JsonSerializer.Serialize(new TrialDecisionDto { Keep = false })
        });
        Assert.False(resp.Success);
        Assert.Equal("Active", _trialManager.GetStatus().State);
    }

    [Fact]
    public void DecideTrial_Remove_WithoutTutorLock_AllowedAfterExpiry()
    {
        // Al expirar (estado AwaitingDecision), si no exige tutorlock, el usuario puede quitar sin auth.
        _trialManager.StartTrial(10, TrialEndAction.Prompt, requireTutorToRemove: false, startUtc: DateTimeOffset.UtcNow.AddMinutes(-15));
        var resp = _server.ProcessRequest(new IpcRequest
        {
            Command = "DecideTrial",
            Payload = JsonSerializer.Serialize(new TrialDecisionDto { Keep = false })
        });
        Assert.True(resp.Success);
        Assert.Equal("Removing", _trialManager.GetStatus().State);
    }

    [Fact]
    public void SetTrial_RequiresAuth()
    {
        var denied = _server.ProcessRequest(new IpcRequest
        {
            Command = "SetTrial",
            Payload = JsonSerializer.Serialize(new SetTrialDto { DurationMinutes = 60, EndAction = "prompt" })
        });
        Assert.False(denied.Success);
    }
}
