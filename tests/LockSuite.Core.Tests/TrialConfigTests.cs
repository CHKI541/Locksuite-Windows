using System;
using LockSuite.Core;
using Xunit;

namespace LockSuite.Core.Tests;

public class TrialConfigTests
{
    [Fact]
    public void StartNow_IsActive_BeforeExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var t = TrialConfig.StartNow(60, TrialEndAction.Prompt, false, now);
        Assert.Equal(TrialState.Active, t.GetState(now.AddMinutes(30)));
        Assert.False(t.IsExpired(now.AddMinutes(59)));
        Assert.True(t.RemainingSeconds(now.AddMinutes(30)) > 0);
    }

    [Fact]
    public void Expires_AndAwaitsDecision()
    {
        var now = DateTimeOffset.UtcNow;
        var t = TrialConfig.StartNow(60, TrialEndAction.Prompt, false, now);
        Assert.True(t.IsExpired(now.AddMinutes(61)));
        Assert.Equal(TrialState.AwaitingDecision, t.GetState(now.AddMinutes(61)));
        Assert.Equal(0, t.RemainingSeconds(now.AddMinutes(61)));
    }

    [Fact]
    public void Decided_Kept_And_Removed_States()
    {
        var now = DateTimeOffset.UtcNow;
        var t = TrialConfig.StartNow(60, TrialEndAction.Prompt, false, now);
        t.Decided = "kept";
        Assert.Equal(TrialState.Kept, t.GetState(now.AddMinutes(61)));
        t.Decided = "removed";
        Assert.Equal(TrialState.Removing, t.GetState(now.AddMinutes(61)));
    }

    [Fact]
    public void Disabled_IsNone()
    {
        var t = new TrialConfig();
        Assert.Equal(TrialState.None, t.GetState(DateTimeOffset.UtcNow));
        Assert.Equal(0, t.RemainingSeconds(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void JsonRoundTrip_PreservesFields()
    {
        var now = DateTimeOffset.UtcNow;
        var t = TrialConfig.StartNow(90, TrialEndAction.AutoRemove, true, now);
        var json = t.ToJson();
        var back = TrialConfig.FromJson(json);
        Assert.True(back.Enabled);
        Assert.Equal(90, back.DurationMinutes);
        Assert.Equal(TrialEndAction.AutoRemove, back.EndAction);
        Assert.True(back.RequireTutorToRemove);
        Assert.Equal("none", back.Decided);
    }

    [Fact]
    public void EndAction_SerializesAsString()
    {
        var t = TrialConfig.StartNow(10, TrialEndAction.AutoRemove, false, DateTimeOffset.UtcNow);
        Assert.Contains("AutoRemove", t.ToJson());
    }
}
