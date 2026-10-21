using System;
using System.IO;
using LockSuite.Service.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LockSuite.Service.Tests;

public class SecurityManagerTests : IDisposable
{
    private readonly string _tempAuthFile;

    public SecurityManagerTests()
    {
        _tempAuthFile = Path.Combine(Path.GetTempPath(), "locksuite_test_" + Guid.NewGuid().ToString("N") + ".dat");
    }

    public void Dispose()
    {
        if (File.Exists(_tempAuthFile))
        {
            try { File.Delete(_tempAuthFile); } catch { }
        }
    }

    [Fact]
    public void SetInitialPassword_WhenNotSet_Succeeds()
    {
        var manager = new SecurityManager(NullLogger<SecurityManager>.Instance, _tempAuthFile);

        Assert.False(manager.IsPasswordSet());
        bool success = manager.SetInitialPassword("ClaveSegura123!");
        Assert.True(success);
        Assert.True(manager.IsPasswordSet());
    }

    [Fact]
    public void SetInitialPassword_WhenAlreadySet_ReturnsFalse()
    {
        var manager = new SecurityManager(NullLogger<SecurityManager>.Instance, _tempAuthFile);
        manager.SetInitialPassword("ClaveSegura123!");

        bool secondAttempt = manager.SetInitialPassword("OtraClave456!");
        Assert.False(secondAttempt);
    }

    [Fact]
    public void Authenticate_CorrectPassword_ReturnsSuccessAndToken()
    {
        var manager = new SecurityManager(NullLogger<SecurityManager>.Instance, _tempAuthFile);
        manager.SetInitialPassword("MiPasswordTutor2026");

        var result = manager.Authenticate("MiPasswordTutor2026", out string? token);

        Assert.Equal(AuthResult.Success, result);
        Assert.NotNull(token);
        Assert.True(manager.ValidateSession(token));
    }

    [Fact]
    public void Authenticate_WrongPassword_Fails()
    {
        var manager = new SecurityManager(NullLogger<SecurityManager>.Instance, _tempAuthFile);
        manager.SetInitialPassword("MiPasswordTutor2026");

        var result = manager.Authenticate("PasswordErroneo", out string? token);

        Assert.Equal(AuthResult.InvalidPassword, result);
        Assert.Null(token);
    }

    [Fact]
    public void Authenticate_MultipleFailures_LocksOut()
    {
        var manager = new SecurityManager(NullLogger<SecurityManager>.Instance, _tempAuthFile);
        manager.SetInitialPassword("Correcto123");

        for (int i = 0; i < 5; i++)
        {
            var res = manager.Authenticate("Incorrecto", out _);
            Assert.Equal(AuthResult.InvalidPassword, res);
        }

        // 6to intento: debe estar bloqueado por rate limit
        var lockedResult = manager.Authenticate("Correcto123", out string? token);
        Assert.Equal(AuthResult.LockedOut, lockedResult);
        Assert.Null(token);
    }

    [Fact]
    public void ChangePassword_WithValidOldPassword_UpdatesSuccessfully()
    {
        var manager = new SecurityManager(NullLogger<SecurityManager>.Instance, _tempAuthFile);
        manager.SetInitialPassword("Vieja123!");

        bool changed = manager.ChangePassword("Vieja123!", "Nueva456!");
        Assert.True(changed);

        var oldAuth = manager.Authenticate("Vieja123!", out _);
        Assert.Equal(AuthResult.InvalidPassword, oldAuth);

        var newAuth = manager.Authenticate("Nueva456!", out string? token);
        Assert.Equal(AuthResult.Success, newAuth);
        Assert.NotNull(token);
    }
}
