using System.Linq;
using LockSuite.Core;
using Xunit;

namespace LockSuite.Core.Tests;

/// <summary>
/// Migracion de configs de v0.2/v0.3 (paquete "Google (Gmail, Chat, Drive, Fotos)" con google.com entero)
/// y sincronizacion de los paquetes de fabrica (sesion 9).
/// </summary>
public class PresetSyncTests
{
    // Config tal como la escribia v0.3.0 (sin ids, Google entero).
    private static string LegacyJson(bool googleEnabled = true, bool communityEnabled = true,
                                     string extraGoogleDomain = "", string extraCommunityDomain = "")
    {
        string g = googleEnabled ? "true" : "false";
        string c = communityEnabled ? "true" : "false";
        string extraG = extraGoogleDomain.Length > 0 ? $", \"{extraGoogleDomain}\"" : "";
        string extraC = extraCommunityDomain.Length > 0 ? $", \"{extraCommunityDomain}\"" : "";
        return $$"""
        {
          "version": 1,
          "failClosed": true,
          "learningMode": true,
          "packs": [
            { "name": "Esenciales del sistema", "enabled": true, "builtIn": true,
              "domains": [ "time.windows.com", "ntp.org", "msftconnecttest.com", "msftncsi.com", "digicert.com",
                           "verisign.com", "globalsign.com", "letsencrypt.org", "pki.goog", "api.github.com",
                           "github.com", "githubusercontent.com" ] },
            { "name": "Google (Gmail, Chat, Drive, Fotos)", "enabled": {{g}}, "builtIn": true,
              "domains": [ "google.com", "googleapis.com", "gstatic.com", "googleusercontent.com",
                           "google.com.ar", "googlemail.com"{{extraG}} ] },
            { "name": "Mercado Pago", "enabled": true, "builtIn": true,
              "domains": [ "mercadopago.com.ar", "mercadopago.com", "mercadolibre.com.ar", "mlstatic.com" ] },
            { "name": "Sitios de la comunidad", "enabled": {{c}}, "builtIn": true,
              "domains": [ "mitmachim.top", "prog.co.il"{{extraC}} ] },
            { "name": "Personalizados", "enabled": true, "builtIn": false, "domains": [ "ejemplo.org" ] }
          ]
        }
        """;
    }

    private static DomainPack Pack(WhitelistConfig cfg, string id) => cfg.Packs.Single(p => p.Id == id);

    [Fact]
    public void LegacyGoogleEnabled_SplitsIntoFourEnabledPacks_AndClosesTheSearchEngine()
    {
        var cfg = WhitelistConfig.FromJson(LegacyJson());
        Assert.True(cfg.IsAllowed("www.google.com"));          // asi estaba antes: buscador abierto

        Assert.True(PresetSync.Apply(cfg));

        Assert.DoesNotContain(cfg.Packs, p => p.Name == "Google (Gmail, Chat, Drive, Fotos)");
        foreach (var id in new[] { Presets.IdGmail, Presets.IdGoogleChat, Presets.IdGoogleDrive, Presets.IdGooglePhotos })
        {
            var p = Pack(cfg, id);
            Assert.True(p.Enabled, id);
            Assert.True(p.BuiltIn, id);
        }

        Assert.True(cfg.IsAllowed("mail.google.com"));
        Assert.True(cfg.IsAllowed("drive.google.com"));
        Assert.False(cfg.IsAllowed("www.google.com"));
        Assert.False(cfg.IsAllowed("google.com"));
        Assert.False(cfg.IsAllowed("www.google.com.ar"));
        Assert.False(cfg.IsAllowed("gemini.google.com"));
        Assert.Equal(WhitelistConfig.CurrentVersion, cfg.Version);
    }

    [Fact]
    public void LegacyGoogleDisabled_NewPacksStayDisabled()
    {
        var cfg = WhitelistConfig.FromJson(LegacyJson(googleEnabled: false));
        PresetSync.Apply(cfg);

        Assert.False(Pack(cfg, Presets.IdGmail).Enabled);
        Assert.False(Pack(cfg, Presets.IdGoogleDrive).Enabled);
        Assert.False(cfg.IsAllowed("mail.google.com"));
    }

    [Fact]
    public void LegacyCommunityPack_SplitsInProgAndMitmachim_KeepingState()
    {
        var on = WhitelistConfig.FromJson(LegacyJson());
        PresetSync.Apply(on);
        Assert.True(Pack(on, Presets.IdProg).Enabled);
        Assert.True(Pack(on, Presets.IdMitmachim).Enabled);
        Assert.DoesNotContain(on.Packs, p => p.Name == "Sitios de la comunidad");

        var off = WhitelistConfig.FromJson(LegacyJson(communityEnabled: false));
        PresetSync.Apply(off);
        Assert.False(Pack(off, Presets.IdProg).Enabled);
        Assert.False(Pack(off, Presets.IdMitmachim).Enabled);
        Assert.False(off.IsAllowed("mitmachim.top"));
    }

    [Fact]
    public void TutorDomainInLegacyPack_IsMovedToPersonalizados()
    {
        var cfg = WhitelistConfig.FromJson(LegacyJson(extraGoogleDomain: "classroom.google.com"));
        PresetSync.Apply(cfg);

        var custom = cfg.Packs.Single(p => p.Name == Presets.CustomPackName);
        Assert.Contains("classroom.google.com", custom.Domains);
        Assert.Contains("ejemplo.org", custom.Domains);           // lo que ya tenia se conserva
        Assert.True(cfg.IsAllowed("classroom.google.com"));
    }

    [Fact]
    public void TutorDomainInDisabledLegacyPack_StaysNotAllowed()
    {
        var cfg = WhitelistConfig.FromJson(LegacyJson(communityEnabled: false, extraCommunityDomain: "otro-foro.co.il"));
        PresetSync.Apply(cfg);

        Assert.False(cfg.IsAllowed("otro-foro.co.il"));
        var moved = cfg.Packs.Single(p => p.Domains.Contains("otro-foro.co.il"));
        Assert.False(moved.Enabled);
        Assert.False(moved.BuiltIn);
    }

    [Fact]
    public void KosherBlock_IsAddedOnUpgrade_EnabledAndAfterTheSystemPack()
    {
        var cfg = WhitelistConfig.FromJson(LegacyJson());
        PresetSync.Apply(cfg);

        int sys = cfg.Packs.FindIndex(p => p.Id == Presets.IdSystem);
        int block = cfg.Packs.FindIndex(p => p.Id == Presets.IdKosherBlock);
        Assert.Equal(sys + 1, block);
        Assert.True(cfg.Packs[block].Enabled);
        Assert.True(cfg.Packs[block].Blocks);
    }

    [Fact]
    public void Apply_IsIdempotent_AlsoAfterJsonRoundTrip()
    {
        var cfg = WhitelistConfig.FromJson(LegacyJson(extraGoogleDomain: "classroom.google.com"));
        Assert.True(PresetSync.Apply(cfg));
        Assert.False(PresetSync.Apply(cfg));

        string json = cfg.ToJson();
        var reloaded = WhitelistConfig.FromJson(json);
        Assert.False(PresetSync.Apply(reloaded));
        Assert.Equal(json, reloaded.ToJson());
    }

    [Fact]
    public void Apply_OnDefaultConfig_ChangesNothing()
    {
        Assert.False(PresetSync.Apply(Presets.DefaultConfig()));
    }

    [Fact]
    public void Sync_RestoresOutdatedBuiltInDomains()
    {
        var cfg = Presets.DefaultConfig();
        var gmail = Pack(cfg, Presets.IdGmail);
        gmail.Domains.Clear();
        gmail.Domains.Add("mail.google.com");

        Assert.True(PresetSync.Apply(cfg));
        Assert.Equal(Presets.Find(Presets.IdGmail)!.Domains.Count, gmail.Domains.Count);
    }

    [Fact]
    public void Sync_RespectsDomainsTheTutorRemoved()
    {
        var cfg = Presets.DefaultConfig();
        var gmail = Pack(cfg, Presets.IdGmail);
        gmail.Domains.Remove("keep.google.com");
        gmail.Excluded.Add("keep.google.com");

        Assert.False(PresetSync.Apply(cfg));                    // ya consistente
        Assert.DoesNotContain("keep.google.com", gmail.Domains);
        Assert.False(cfg.IsAllowed("keep.google.com"));

        gmail.Excluded.Clear();                                 // el tutor lo vuelve a habilitar
        Assert.True(PresetSync.Apply(cfg));
        Assert.Contains("keep.google.com", gmail.Domains);
    }

    [Fact]
    public void Sync_RenamesTutorPackThatUsesAPresetName()
    {
        var cfg = Presets.DefaultConfig();
        cfg.Packs.Add(new DomainPack { Name = "Gmail", Enabled = true, Domains = { "otro.com" } });

        Assert.True(PresetSync.Apply(cfg));
        Assert.Single(cfg.Packs, p => p.Name == "Gmail");
        Assert.Contains(cfg.Packs, p => p.Id == null && p.Name == "Gmail (personalizado)" && p.Domains.Contains("otro.com"));
    }

    [Fact]
    public void Sync_ExtrasInsideABlockPackKeepBlocking()
    {
        var cfg = Presets.DefaultConfig();
        Pack(cfg, Presets.IdKosherBlock).Domains.Add("ia-de-ejemplo.com");
        cfg.Packs.Add(new DomainPack { Name = "Personalizados", Enabled = true, Domains = { "ia-de-ejemplo.com" } });

        PresetSync.Apply(cfg);

        Assert.Contains("ia-de-ejemplo.com", Pack(cfg, Presets.IdKosherBlock).Domains);
        Assert.False(cfg.IsAllowed("ia-de-ejemplo.com"));
    }

    [Fact]
    public void Apply_ToleratesNullLists()
    {
        var cfg = new WhitelistConfig { Packs = null! };
        Assert.True(PresetSync.Apply(cfg));
        Assert.Equal(Presets.Catalog.Count, cfg.Packs.Count);

        var cfg2 = WhitelistConfig.FromJson("""{ "packs": [ { "name": "X", "domains": null, "excluded": null } ] }""");
        PresetSync.Apply(cfg2);
        Assert.NotNull(cfg2.Packs.Single(p => p.Name == "X").Domains);
    }
}
