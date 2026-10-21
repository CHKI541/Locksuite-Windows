using System;
using System.IO;
using LockSuite.Core;
using LockSuite.Service.Trial;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LockSuite.Service.Tests;

public class TrialManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _trialPath;

    public TrialManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "locksuite_trial_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _trialPath = Path.Combine(_tempDir, "trial.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private TrialManager NewManager() => new(NullLogger<TrialManager>.Instance, _trialPath);

    [Fact]
    public void StartTrial_PersistsAndReportsActive()
    {
        var tm = NewManager();
        tm.StartTrial(60, TrialEndAction.Prompt, false);
        var s = tm.GetStatus();
        Assert.True(s.Enabled);
        Assert.Equal("Active", s.State);
        Assert.Equal(60, s.DurationMinutes);
        Assert.True(File.Exists(_trialPath));
    }

    [Fact]
    public void Persisted_Trial_LoadsInNewInstance()
    {
        NewManager().StartTrial(120, TrialEndAction.AutoRemove, true);
        var tm2 = NewManager();
        var s = tm2.GetStatus();
        Assert.True(s.Enabled);
        Assert.Equal(120, s.DurationMinutes);
        Assert.Equal("AutoRemove", s.EndAction);
        Assert.True(s.RequireTutorToRemove);
    }

    [Fact]
    public void Decide_Keep_DisablesTrial_Permanent()
    {
        var tm = NewManager();
        tm.StartTrial(60, TrialEndAction.Prompt, false);
        tm.Decide(true);
        var s = tm.GetStatus();
        Assert.False(s.Enabled);        // instalacion permanente
        Assert.Equal("None", s.State);  // ya no cuenta como prueba
    }

    [Fact]
    public void Decide_Remove_SetsRemovingState()
    {
        // En el entorno de test no existe unins000.exe: LaunchUninstall es no-op,
        // pero la decision terminal queda registrada en el estado cuando está autorizada.
        var tm = NewManager();
        tm.StartTrial(60, TrialEndAction.Prompt, false);
        bool res = tm.Decide(false, isAuthorized: true);
        Assert.True(res);
        Assert.Equal("Removing", tm.GetStatus().State);
    }

    [Fact]
    public void Decide_Remove_Unauthorized_BeforeExpiry_IsRejected()
    {
        var tm = NewManager();
        tm.StartTrial(60, TrialEndAction.Prompt, false);
        bool res = tm.Decide(false, isAuthorized: false);
        Assert.False(res);
        Assert.Equal("Active", tm.GetStatus().State);
    }

    [Fact]
    public void RequiresTutorToRemove_ReflectsConfig()
    {
        var tm = NewManager();
        tm.StartTrial(60, TrialEndAction.Prompt, true);
        Assert.True(tm.RequiresTutorToRemove);
    }

    [Fact]
    public void CheckAutoRemove_OnlyActsWhenExpiredAndAutoRemove()
    {
        // Prompt no auto-remueve aunque este activo.
        var tm = NewManager();
        tm.StartTrial(60, TrialEndAction.Prompt, false);
        tm.CheckAutoRemove();
        Assert.Equal("Active", tm.GetStatus().State);
    }
}
