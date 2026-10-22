using System;
using System.Collections.Generic;
using System.Linq;
using LockSuite.Core;
using Xunit;

namespace LockSuite.Core.Tests;

/// <summary>
/// Invariantes de los paquetes de fabrica (sesion 9):
///   - cada boton habilita lo que su servicio necesita;
///   - NINGUNA combinacion de paquetes de fabrica abre el buscador de Google, Gemini, YouTube, etc.
/// </summary>
public class PresetsTests
{
    private static WhitelistConfig ConfigWith(params string[] enabledIds)
    {
        var cfg = Presets.DefaultConfig();
        foreach (var p in cfg.Packs)
            p.Enabled = p.Blocks || p.Id == Presets.IdSystem || (p.Id != null && enabledIds.Contains(p.Id));
        return cfg;
    }

    [Fact]
    public void Catalog_HasUniqueIdsAndNames()
    {
        Assert.Equal(Presets.Catalog.Count, Presets.Catalog.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Presets.Catalog.Count, Presets.Catalog.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Catalog_AllPatternsAreValid()
    {
        foreach (var def in Presets.Catalog)
            foreach (var d in def.Domains)
                Assert.True(DomainMatcher.IsValidPattern(d), $"Patron invalido en '{def.Id}': {d}");
    }

    [Fact]
    public void Catalog_ServiceCardsAreTheSixRequestedServices()
    {
        var services = Presets.Catalog.Where(d => d.IsService).Select(d => d.Id).ToList();
        Assert.Equal(new[]
        {
            Presets.IdGmail, Presets.IdGoogleChat, Presets.IdGoogleDrive, Presets.IdGooglePhotos,
            Presets.IdProg, Presets.IdMitmachim
        }, services);

        foreach (var def in Presets.Catalog.Where(d => d.IsService))
        {
            Assert.False(string.IsNullOrWhiteSpace(def.Title));
            Assert.False(string.IsNullOrWhiteSpace(def.Description));
            Assert.StartsWith("https://", def.OpenUrl);
        }
    }

    [Fact]
    public void DefaultConfig_HasEveryPresetAndTheKosherBlockIsActive()
    {
        var cfg = Presets.DefaultConfig();
        Assert.Equal(Presets.Catalog.Count, cfg.Packs.Count);
        var block = cfg.Packs.Single(p => p.Id == Presets.IdKosherBlock);
        Assert.True(block.Blocks);
        Assert.True(block.Enabled);
        Assert.True(block.BuiltIn);
        Assert.All(cfg.Packs, p => Assert.True(p.BuiltIn));
    }

    // ---------------------------------------------------------------------------------
    // Lo que NUNCA tiene que quedar permitido, ni siquiera con todos los paquetes activos
    // ---------------------------------------------------------------------------------

    public static IEnumerable<object[]> NeverAllowedHosts() => new[]
    {
        // Buscador de Google
        "google.com", "www.google.com", "www.google.com.ar", "google.com.ar", "www.google.co.il", "google.co.il",
        "ipv4.google.com", "encrypted.google.com", "images.google.com", "lens.google.com", "news.google.com",
        "books.google.com", "encrypted-tbn0.gstatic.com", "t0.gstatic.com",
        // Gemini y otras IA
        "gemini.google.com", "bard.google.com", "gemini.google", "aistudio.google.com", "notebooklm.google.com",
        "labs.google", "assistant.google.com", "generativelanguage.googleapis.com", "aiplatform.googleapis.com",
        "us-central1-aiplatform.googleapis.com", "appsgenaiserver-pa.clients6.google.com",
        "alkalimakersuite-pa.clients6.google.com", "robinfrontend-pa.googleapis.com", "gemini.gstatic.com",
        // YouTube y video
        "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be", "i.ytimg.com", "yt3.ggpht.com",
        "rr1---sn-abc.googlevideo.com", "youtubei.googleapis.com", "youtube.googleapis.com",
        "www.youtube-nocookie.com", "yt3.googleusercontent.com",
        // Otros servicios de Google con contenido de terceros
        "play.google.com", "play-lh.googleusercontent.com", "sites.google.com", "script.google.com",
        "colab.research.google.com", "translate.goog", "example-com.translate.goog", "maps.google.com",
        "blogger.googleusercontent.com", "webcache.googleusercontent.com", "storage.googleapis.com",
        "firebasestorage.googleapis.com", "workspace.google.com", "meet.google.com",
        // GIFs, redes, publicidad
        "media.tenor.com", "tenor.googleapis.com", "facebook.com", "connect.facebook.net",
        "www.googletagmanager.com", "www.google-analytics.com", "stats.g.doubleclick.net",
        // Hosting generico
        "github.io", "imgur.com", "i.imgur.com",
    }.Select(h => new object[] { h });

    [Theory]
    [MemberData(nameof(NeverAllowedHosts))]
    public void AllPresetsEnabled_NeverAllows(string host)
    {
        var cfg = Presets.DefaultConfig();
        foreach (var p in cfg.Packs) p.Enabled = true;
        Assert.False(cfg.IsAllowed(host), $"'{host}' no deberia estar permitido");
    }

    [Theory]
    [MemberData(nameof(NeverAllowedHosts))]
    public void GooglePacksAlone_NeverAllow_EvenWithoutTheKosherBlock(string host)
    {
        // Defensa en profundidad: las listas de Google por si solas tampoco abren el buscador.
        // (Excepto lo que solo corta el bloqueo fijo: los backends de IA dentro de clients6.)
        if (host.EndsWith(".clients6.google.com", StringComparison.Ordinal)) return;

        var allow = Presets.Catalog
            .Where(d => d.Id is Presets.IdGmail or Presets.IdGoogleChat or Presets.IdGoogleDrive or Presets.IdGooglePhotos)
            .SelectMany(d => d.Domains);
        Assert.False(DomainMatcher.IsAllowed(allow, host), $"'{host}' no deberia estar en ninguna lista de Google");
    }

    [Fact]
    public void KosherBlock_CannotBeBypassedByAddingGoogleDotCom()
    {
        var cfg = Presets.DefaultConfig();
        cfg.Packs.Add(new DomainPack { Name = "Personalizados", Enabled = true, Domains = { "google.com", "googleapis.com" } });

        Assert.True(cfg.IsAllowed("mail.google.com"));
        Assert.False(cfg.IsAllowed("www.google.com"));
        Assert.False(cfg.IsAllowed("google.com"));
        Assert.False(cfg.IsAllowed("gemini.google.com"));
        Assert.False(cfg.IsAllowed("generativelanguage.googleapis.com"));
        Assert.True(cfg.IsHardBlocked("www.google.com"));
    }

    [Fact]
    public void KosherBlock_DoesNotBlockLoginOrServices()
    {
        string[] needed =
        {
            "accounts.google.com", "accounts.google.com.ar", "accounts.google.co.il", "accounts.youtube.com",
            "mail.google.com", "chat.google.com", "drive.google.com", "docs.google.com", "photos.google.com",
            "ssl.gstatic.com", "www.gstatic.com", "fonts.gstatic.com", "lh3.googleusercontent.com",
            "people-pa.clients6.google.com", "waa-pa.clients6.google.com", "signaler-pa.clients6.google.com"
        };
        foreach (var h in needed)
            Assert.False(Presets.IsKosherBlocked(h), $"El bloqueo fijo no deberia cortar '{h}'");
    }

    // ---------------------------------------------------------------------------------
    // Lo que cada boton tiene que habilitar (con solo ese servicio activo)
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("accounts.youtube.com")]
    [InlineData("mail.google.com")]
    [InlineData("gmail.com")]
    [InlineData("imap.gmail.com")]
    [InlineData("ssl.gstatic.com")]
    [InlineData("www.gstatic.com")]
    [InlineData("fonts.gstatic.com")]
    [InlineData("fonts.googleapis.com")]
    [InlineData("apis.google.com")]
    [InlineData("ogs.google.com")]
    [InlineData("clients6.google.com")]
    [InlineData("people-pa.clients6.google.com")]
    [InlineData("signaler-pa.clients6.google.com")]
    [InlineData("waa-pa.clients6.google.com")]
    [InlineData("0.client-channel.google.com")]
    [InlineData("contacts.google.com")]
    [InlineData("lh3.googleusercontent.com")]
    [InlineData("ci3.googleusercontent.com")]
    [InlineData("ci6.googleusercontent.com")]
    [InlineData("mail-attachment.googleusercontent.com")]
    [InlineData("drive-thirdparty.googleusercontent.com")]
    [InlineData("lh3.google.com")]
    [InlineData("calendar.google.com")]
    [InlineData("myaccount.google.com")]
    public void Gmail_AllowsWhatItNeeds(string host)
        => Assert.True(ConfigWith(Presets.IdGmail).IsAllowed(host), host);

    [Theory]
    [InlineData("chat.google.com")]
    [InlineData("accounts.google.com")]
    [InlineData("peoplestack-pa.clients6.google.com")]
    [InlineData("chat-pa.clients6.google.com")]
    [InlineData("lh3.googleusercontent.com")]
    [InlineData("1.client-channel.google.com")]
    [InlineData("www.gstatic.com")]
    public void Chat_AllowsWhatItNeeds(string host)
        => Assert.True(ConfigWith(Presets.IdGoogleChat).IsAllowed(host), host);

    [Theory]
    [InlineData("drive.google.com")]
    [InlineData("rr3---sn-abc.c.drive.google.com")]
    [InlineData("docs.google.com")]
    [InlineData("0.docs.google.com")]
    [InlineData("drive.usercontent.google.com")]
    [InlineData("doc-0s-3c-docs.googleusercontent.com")]
    [InlineData("drive-thirdparty.googleusercontent.com")]
    [InlineData("lh3.google.com")]
    [InlineData("lh7-rt.googleusercontent.com")]
    [InlineData("drivefrontend-pa.clients6.google.com")]
    [InlineData("drivefrontend-pa.googleapis.com")]
    [InlineData("www.googleapis.com")]
    [InlineData("content.googleapis.com")]
    [InlineData("contacts.google.com")]
    public void Drive_AllowsWhatItNeeds(string host)
        => Assert.True(ConfigWith(Presets.IdGoogleDrive).IsAllowed(host), host);

    [Theory]
    [InlineData("photos.google.com")]
    [InlineData("photos.fife.usercontent.google.com")]
    [InlineData("lh3.googleusercontent.com")]
    [InlineData("photosdata-pa.googleapis.com")]
    [InlineData("video-downloads.googleusercontent.com")]
    [InlineData("photos.app.goo.gl")]
    public void Photos_AllowsWhatItNeeds(string host)
        => Assert.True(ConfigWith(Presets.IdGooglePhotos).IsAllowed(host), host);

    [Theory]
    [InlineData("www.prog.co.il")]
    [InlineData("s3.prog.co.il")]
    [InlineData("challenges.cloudflare.com")]
    [InlineData("code.jquery.com")]
    [InlineData("cdnjs.cloudflare.com")]
    [InlineData("cdn.jsdelivr.net")]
    [InlineData("www.hebcal.com")]
    [InlineData("prod-progsite-s3.s3.eu-central-1.wasabisys.com")]
    [InlineData("s3.eu-central-003.backblazeb2.com")]
    public void Prog_AllowsWhatItNeeds(string host)
        => Assert.True(ConfigWith(Presets.IdProg).IsAllowed(host), host);

    [Theory]
    [InlineData("mitmachim.top")]
    [InlineData("fonts.googleapis.com")]
    [InlineData("fonts.gstatic.com")]
    [InlineData("accounts.google.com")]
    public void Mitmachim_AllowsWhatItNeeds(string host)
        => Assert.True(ConfigWith(Presets.IdMitmachim).IsAllowed(host), host);

    [Fact]
    public void EachButtonIsIndependent()
    {
        // Gmail solo no habilita Drive ni Chat; Drive solo no habilita Gmail.
        var gmail = ConfigWith(Presets.IdGmail);
        Assert.False(gmail.IsAllowed("drive.google.com"));
        Assert.False(gmail.IsAllowed("chat.google.com"));
        Assert.False(gmail.IsAllowed("photos.google.com"));

        var drive = ConfigWith(Presets.IdGoogleDrive);
        Assert.False(drive.IsAllowed("mail.google.com"));

        var prog = ConfigWith(Presets.IdProg);
        Assert.False(prog.IsAllowed("mitmachim.top"));
        Assert.False(prog.IsAllowed("mail.google.com"));
    }

    [Theory]
    [InlineData("google.com", true)]
    [InlineData("*.google.com", true)]
    [InlineData("gemini.google.com", true)]
    [InlineData("mail.google.com", false)]
    [InlineData("mitmachim.top", false)]
    public void CoversKosherBlockedSites_Cases(string pattern, bool expected)
        => Assert.Equal(expected, Presets.CoversKosherBlockedSites(pattern));

    [Theory]
    [InlineData("www.google.com", true)]
    [InlineData("gemini.google.com", true)]
    [InlineData("play.google.com", true)]
    [InlineData("www.youtube.com", true)]
    [InlineData("encrypted-tbn1.gstatic.com", true)]
    [InlineData("mail.google.com", false)]
    [InlineData("algun-sitio-desconocido.com", false)]
    public void ExplainBlocked_HasNotesForKnownHosts(string host, bool hasNote)
        => Assert.Equal(hasNote, Presets.ExplainBlocked(host) is not null);
}
