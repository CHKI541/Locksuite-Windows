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
        Assert.True(manager.IsAllowed("mail.google.com"));
        Assert.True(manager.IsAllowed("drive.google.com"));
        Assert.True(manager.IsAllowed("mercadopago.com.ar"));
        Assert.False(manager.IsAllowed("facebook.com"));

        // Sesion 9: Gmail/Drive permitidos NO abren el buscador de Google ni Gemini.
        Assert.False(manager.IsAllowed("google.com"));
        Assert.False(manager.IsAllowed("www.google.com"));
        Assert.False(manager.IsAllowed("gemini.google.com"));
        Assert.True(manager.IsHardBlocked("www.google.com"));
    }

    [Fact]
    public void Constructor_MigratesLegacyConfig_AndPersistsIt()
    {
        // Config como la escribia v0.3.0: paquete Google con google.com entero.
        File.WriteAllText(_tempConfigFile, """
        {
          "version": 1,
          "failClosed": true,
          "learningMode": true,
          "packs": [
            { "name": "Esenciales del sistema", "enabled": true, "builtIn": true, "domains": [ "time.windows.com" ] },
            { "name": "Google (Gmail, Chat, Drive, Fotos)", "enabled": true, "builtIn": true,
              "domains": [ "google.com", "googleapis.com", "gstatic.com", "googleusercontent.com", "google.com.ar", "googlemail.com" ] },
            { "name": "Sitios de la comunidad", "enabled": true, "builtIn": true, "domains": [ "mitmachim.top", "prog.co.il" ] }
          ]
        }
        """);

        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);

        Assert.True(manager.IsAllowed("mail.google.com"));
        Assert.True(manager.IsAllowed("mitmachim.top"));
        Assert.False(manager.IsAllowed("www.google.com"));
        Assert.False(manager.IsAllowed("google.com.ar"));

        // Quedo guardada migrada: al recargar no hay paquete "Google (...)" y el bloqueo fijo esta.
        string json = File.ReadAllText(_tempConfigFile);
        Assert.DoesNotContain("Google (Gmail, Chat, Drive, Fotos)", json);
        Assert.Contains("\"kosher-block\"", json);

        var reload = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);
        Assert.False(reload.IsAllowed("www.google.com"));
        Assert.True(reload.IsAllowed("drive.google.com"));
    }

    [Fact]
    public void TogglePack_ServicePack_EnablesAndDisablesTheService()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);

        manager.TogglePack("Gmail", false);
        Assert.False(manager.IsAllowed("mail.google.com"));
        Assert.True(manager.IsAllowed("drive.google.com"));     // Drive sigue permitido

        manager.TogglePack("Gmail", true);
        Assert.True(manager.IsAllowed("mail.google.com"));
        Assert.False(manager.IsAllowed("www.google.com"));
    }

    [Fact]
    public void RemoveDomain_FromBuiltInPack_StaysRemovedAfterReload_AndCanBeRestored()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);
        Assert.True(manager.IsAllowed("keep.google.com"));

        Assert.True(manager.RemoveDomain("keep.google.com"));
        Assert.False(manager.IsAllowed("keep.google.com"));

        // La sincronizacion de presets al recargar no lo vuelve a agregar.
        var reload = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);
        Assert.False(reload.IsAllowed("keep.google.com"));

        // Agregarlo de nuevo lo restituye en su paquete de fabrica.
        Assert.True(reload.AddDomain("keep.google.com"));
        Assert.True(reload.IsAllowed("keep.google.com"));
        var gmail = reload.CurrentConfig.Packs.Find(p => p.Id == "gmail");
        Assert.NotNull(gmail);
        Assert.Contains("keep.google.com", gmail!.Domains);
        Assert.DoesNotContain("keep.google.com", gmail.Excluded);
    }

    [Fact]
    public void AddDomain_ToBuiltInPack_GoesToPersonalizados()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);

        Assert.True(manager.AddDomain("sitio-extra-test.com", "Gmail"));
        Assert.True(manager.IsAllowed("sitio-extra-test.com"));

        var gmail = manager.CurrentConfig.Packs.Find(p => p.Id == "gmail");
        Assert.DoesNotContain("sitio-extra-test.com", gmail!.Domains);
        var custom = manager.CurrentConfig.Packs.Find(p => p.Name == "Personalizados");
        Assert.Contains("sitio-extra-test.com", custom!.Domains);
    }

    [Fact]
    public void AddDomain_ToBlockPack_IsRejected()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);
        Assert.False(manager.AddDomain("cualquiera-test.com", "Bloqueo fijo: buscador de Google y Gemini"));
        Assert.False(manager.IsAllowed("cualquiera-test.com"));
    }

    [Fact]
    public void KosherBlock_WinsOverACustomGoogleDotCom()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);
        Assert.True(manager.AddDomain("google.com"));

        Assert.True(manager.IsAllowed("mail.google.com"));
        Assert.False(manager.IsAllowed("www.google.com"));
        Assert.False(manager.IsAllowed("gemini.google.com"));

        // Si el tutor desactiva el bloqueo fijo, lo que agrego queda habilitado (decision suya).
        manager.TogglePack("Bloqueo fijo: buscador de Google y Gemini", false);
        Assert.True(manager.IsAllowed("www.google.com"));
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

    [Fact]
    public void SetAllowLocalNetwork_TogglesAndPersists()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);
        Assert.False(manager.AllowLocalNetwork);   // por defecto la red local está bloqueada

        manager.SetAllowLocalNetwork(true);
        Assert.True(manager.AllowLocalNetwork);

        var reload = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);
        Assert.True(reload.AllowLocalNetwork);
    }

    [Fact]
    public void ConfigChanged_FiresOnAddDomain()
    {
        var manager = new ConfigManager(NullLogger<ConfigManager>.Instance, _tempConfigFile);
        int fired = 0;
        manager.ConfigChanged += _ => fired++;

        manager.AddDomain("evento-test.com");
        Assert.True(fired >= 1);
    }
}
