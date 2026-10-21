using System;
using System.IO;
using LockSuite.Service.Config;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LockSuite.Service.Tests;

public class ConfigManagerTests : IDisposable
{
    private readonly string _tempConfigFile;

    public ConfigManagerTests()
    {
        _tempConfigFile = Path.Combine(Path.GetTempPath(), "locksuite_cfg_" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempConfigFile))
        {
            try { File.Delete(_tempConfigFile); } catch { }
        }
    }

    [Fact]
    public void Constructor_CreatesDefaultConfig_WhenFileDoesNotExist()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);

        Assert.True(File.Exists(_tempConfigFile));
        var cfg = manager.CurrentConfig;
        Assert.NotEmpty(cfg.Packs);
        Assert.True(manager.IsAllowed("google.com"));
        Assert.True(manager.IsAllowed("mail.google.com"));
        Assert.True(manager.IsAllowed("mercadopago.com.ar"));
        Assert.False(manager.IsAllowed("facebook.com"));
    }

    [Fact]
    public void AddDomain_AddsNewDomainAndPersists()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);

        Assert.False(manager.IsAllowed("sitio-permitido.org"));

        bool added = manager.AddDomain("sitio-permitido.org");
        Assert.True(added);
        Assert.True(manager.IsAllowed("sitio-permitido.org"));
        Assert.True(manager.IsAllowed("sub.sitio-permitido.org"));

        // Recarga desde disco
        var reloadManager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);
        Assert.True(reloadManager.IsAllowed("sitio-permitido.org"));
    }

    [Fact]
    public void TogglePack_DisablesAndEnablesPack()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);

        Assert.True(manager.IsAllowed("mercadopago.com.ar"));

        manager.TogglePack("Mercado Pago", false);
        Assert.False(manager.IsAllowed("mercadopago.com.ar"));

        manager.TogglePack("Mercado Pago", true);
        Assert.True(manager.IsAllowed("mercadopago.com.ar"));
    }

    [Fact]
    public void RemoveDomain_RemovesDomainFromConfig()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);

        manager.AddDomain("temporal.com");
        Assert.True(manager.IsAllowed("temporal.com"));

        bool removed = manager.RemoveDomain("temporal.com");
        Assert.True(removed);
        Assert.False(manager.IsAllowed("temporal.com"));
    }

    [Fact]
    public void AddDomain_RejectsInvalidPattern()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);

        Assert.False(manager.AddDomain("com"));
        Assert.False(manager.AddDomain("*"));
        Assert.False(manager.AddDomain("   "));
        Assert.False(manager.IsAllowed("com"));
    }

    [Fact]
    public void SetLearningMode_TogglesAndPersists()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);

        manager.SetLearningMode(false);
        Assert.False(manager.LearningMode);

        var reload = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);
        Assert.False(reload.LearningMode);
    }

    [Fact]
    public void ActivePatternCount_ReflectsAddedDomain()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);

        int before = manager.ActivePatternCount;
        Assert.True(before > 0);

        manager.AddDomain("nuevo-dominio-unico-test.com");
        Assert.Equal(before + 1, manager.ActivePatternCount);
    }
}
