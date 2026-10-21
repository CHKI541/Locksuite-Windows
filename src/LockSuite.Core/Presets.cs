using System.Collections.Generic;

namespace LockSuite.Core;

/// <summary>
/// Paquetes de dominios predefinidos. Se afinan con el "modo aprendizaje":
/// una pagina moderna pide recursos de muchos dominios (CDN, fuentes, APIs),
/// asi que estos presets son un punto de partida razonable, no una lista cerrada.
/// </summary>
public static class Presets
{
    /// <summary>Esenciales del sistema: sin esto Windows muestra "sin internet" o fallan los certificados.
    /// Viene activado por defecto y conviene no desactivarlo.</summary>
    public static DomainPack SystemEssentials() => new()
    {
        Name = "Esenciales del sistema",
        BuiltIn = true,
        Enabled = true,
        Domains = new()
        {
            "time.windows.com",           // sincronizacion de hora
            "ntp.org", "*.ntp.org",       // hora (fallback)
            "msftconnecttest.com",        // deteccion de conectividad de Windows
            "msftncsi.com",
            "*.digicert.com",             // OCSP/CRL (validacion de certificados)
            "*.verisign.com",
            "ocsp.pki.goog",
            "*.globalsign.com",
            "*.letsencrypt.org", "*.int-x3.letsencrypt.org"
        }
    };

    public static DomainPack Google() => new()
    {
        Name = "Google (Gmail, Chat, Drive, Fotos)",
        BuiltIn = true,
        Enabled = true,
        Domains = new()
        {
            "google.com",                 // bare: cubre accounts., mail., drive., chat., photos., ...
            "*.google.com",
            "googleapis.com", "*.googleapis.com",
            "gstatic.com", "*.gstatic.com",
            "googleusercontent.com", "*.googleusercontent.com",
            "gstatic.com",
            "google.com.ar",
            "googlemail.com", "*.googlemail.com"
        }
    };

    public static DomainPack MercadoPago() => new()
    {
        Name = "Mercado Pago",
        BuiltIn = true,
        Enabled = true,
        Domains = new()
        {
            "mercadopago.com.ar", "*.mercadopago.com.ar",
            "mercadolibre.com.ar", "*.mercadolibre.com.ar",  // MP se apoya en dominios de ML
            "mlstatic.com", "*.mlstatic.com",
            "mercadopago.com", "*.mercadopago.com"
        }
    };

    public static DomainPack CommunitySites() => new()
    {
        Name = "Sitios de la comunidad",
        BuiltIn = true,
        Enabled = true,
        Domains = new()
        {
            "mitmachim.top", "*.mitmachim.top",
            "prog.co.il", "*.prog.co.il"
        }
    };

    /// <summary>Configuracion inicial de fabrica, con todos los presets activos.</summary>
    public static WhitelistConfig DefaultConfig() => new()
    {
        Packs = new()
        {
            SystemEssentials(),
            Google(),
            MercadoPago(),
            CommunitySites()
        }
    };
}
