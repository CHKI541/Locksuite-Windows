using System.Linq;
using LockSuite.Core;
using Xunit;

namespace LockSuite.Core.Tests;

/// <summary>
/// Sesión 11: validación de certificados (OCSP/CRL de CAs), actualizaciones de Windows/Defender y
/// de los navegadores, recursos web comunes (fuentes/CDNs), captchas, red local y caché DNS.
/// Comprueba que los paquetes nuevos habilitan lo suyo y que NADA de esto abre el buscador, Gemini,
/// YouTube, etc.
/// </summary>
public class PresetsExtraTests
{
    private static WhitelistConfig AllEnabled()
    {
        var cfg = Presets.DefaultConfig();
        foreach (var p in cfg.Packs) p.Enabled = true;
        return cfg;
    }

    [Theory]
    // Let's Encrypt (la CA más usada de la web) sirve OCSP/CRL desde lencr.org, NO letsencrypt.org.
    [InlineData("o.lencr.org")]
    [InlineData("e5.o.lencr.org")]
    [InlineData("x1.c.lencr.org")]
    // Amazon (sitios en AWS/CloudFront)
    [InlineData("ocsp.rootca1.amazontrust.com")]
    [InlineData("crl.rootca1.amazontrust.com")]
    // Sectigo / Comodo / USERTrust
    [InlineData("ocsp.sectigo.com")]
    [InlineData("crl.sectigo.com")]
    [InlineData("ocsp.comodoca.com")]
    [InlineData("ocsp.usertrust.com")]
    // CAs de Cloudflare (Let's Encrypt, Google Trust Services, SSL.com)
    [InlineData("ocsp.pki.goog")]
    [InlineData("crls.ssl.com")]
    [InlineData("ocsps.ssl.com")]
    // Actualización de raíces de Windows (sin esto: "La conexión no es privada" con raíces nuevas)
    [InlineData("ctldl.windowsupdate.com")]
    // DigiCert / GlobalSign (ya estaban)
    [InlineData("ocsp.digicert.com")]
    [InlineData("ocsp.globalsign.com")]
    public void SystemEssentials_AllowsCertificateValidationHosts(string host)
        => Assert.True(AllEnabled().IsAllowed(host), host);

    [Theory]
    [InlineData("fonts.googleapis.com")]
    [InlineData("fonts.gstatic.com")]
    [InlineData("cdnjs.cloudflare.com")]
    [InlineData("code.jquery.com")]
    [InlineData("ajax.googleapis.com")]
    [InlineData("use.fontawesome.com")]
    public void WebCommons_AllowsFontsAndLibraries(string host)
        => Assert.True(AllEnabled().IsAllowed(host), host);

    [Theory]
    [InlineData("challenges.cloudflare.com")]  // Cloudflare Turnstile
    [InlineData("hcaptcha.com")]
    [InlineData("newassets.hcaptcha.com")]
    [InlineData("recaptcha.net")]
    public void Captcha_AllowsVerificationServices(string host)
        => Assert.True(AllEnabled().IsAllowed(host), host);

    [Theory]
    [InlineData("update.microsoft.com")]
    [InlineData("fe3cr.delivery.mp.microsoft.com")]
    [InlineData("ctldl.windowsupdate.com")]
    [InlineData("definitionupdates.microsoft.com")]
    [InlineData("wdcp.microsoft.com")]
    public void WindowsUpdate_AllowsUpdateAndDefender(string host)
        => Assert.True(AllEnabled().IsAllowed(host), host);

    [Theory]
    [InlineData("msedge.api.cdp.microsoft.com")]
    [InlineData("update.googleapis.com")]
    [InlineData("dl.google.com")]
    [InlineData("firefox.settings.services.mozilla.com")]
    [InlineData("firefox-settings-attachments.cdn.mozilla.net")]
    public void BrowserUpdates_AllowsBrowserAndCertListUpdates(string host)
        => Assert.True(AllEnabled().IsAllowed(host), host);

    [Fact]
    public void AllNewDomains_AreValidPatterns()
    {
        // Ya lo cubre Catalog_AllPatternsAreValid, pero dejamos explícitos los casos con guiones/puntos.
        foreach (var d in new[]
        {
            "lencr.org", "amazontrust.com", "ssl.com", "ctldl.windowsupdate.com",
            "firefox-settings-attachments.cdn.mozilla.net", "content-signature-2.cdn.mozilla.net",
            "msedge.api.cdp.microsoft.com", "ka-f.fontawesome.com", "ocsp-certum.com",
        })
            Assert.True(DomainMatcher.IsValidPattern(d), d);
    }

    [Theory]
    // Con TODOS los paquetes activos (incluidos los nuevos), esto sigue bloqueado.
    [InlineData("www.google.com")]
    [InlineData("gemini.google.com")]
    [InlineData("www.youtube.com")]
    [InlineData("storage.googleapis.com")]
    [InlineData("encrypted-tbn0.gstatic.com")]
    [InlineData("t0.gstatic.com")]
    [InlineData("www.googletagmanager.com")]
    [InlineData("bing.com")]
    public void NewPacks_DoNotOpenBlockedSites(string host)
        => Assert.False(AllEnabled().IsAllowed(host), host);

    [Theory]
    [InlineData("ocsp.laica.gob.ar", true)]      // CA local desconocida: la heurística la reconoce
    [InlineData("crl3.una-ca-chica.com", true)]
    [InlineData("ocsp2.alguna-ca.net", true)]
    [InlineData("www.ejemplo.com", false)]
    [InlineData("ocsptsomething.com", false)]     // "ocsp" pegado a letras: NO es infraestructura
    public void LooksLikeCertificateInfrastructure_Heuristic(string host, bool expected)
        => Assert.Equal(expected, Presets.LooksLikeCertificateInfrastructure(host));

    [Fact]
    public void ExplainBlocked_ExplainsUnknownOcspHost()
        => Assert.NotNull(Presets.ExplainBlocked("ocsp.una-ca-cualquiera.com"));

    [Fact]
    public void AllowLocalNetwork_DefaultsFalse_AndRoundTripsThroughJson()
    {
        var cfg = Presets.DefaultConfig();
        Assert.False(cfg.AllowLocalNetwork);

        cfg.AllowLocalNetwork = true;
        var back = WhitelistConfig.FromJson(cfg.ToJson());
        Assert.True(back.AllowLocalNetwork);
    }

    [Fact]
    public void Catalog_NewInformationalPacks_AreNotServiceCards()
    {
        foreach (var id in new[] { Presets.IdWindowsUpdate, Presets.IdWebCommons, Presets.IdCaptcha, Presets.IdBrowserUpdates })
        {
            var def = Presets.Find(id);
            Assert.NotNull(def);
            Assert.False(def!.IsService, id);
            Assert.False(def.Blocks, id);
        }
    }
}
