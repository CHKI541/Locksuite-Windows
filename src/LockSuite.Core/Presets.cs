using System.Collections.Generic;

namespace LockSuite.Core;

/// <summary>
/// Paquetes de dominios predefinidos. Se afinan con el "modo aprendizaje":
/// una pagina moderna pide recursos de muchos dominios (CDN, fuentes, APIs),
/// asi que estos presets son un punto de partida razonable, no una lista cerrada.
///
/// Nota de semantica: un dominio "pelado" (ej. "google.com") ya habilita el dominio
/// y TODOS sus subdominios (mail., drive., ...). Por eso aca NO se listan wildcards
/// "*.google.com": serian redundantes. Solo se usaria "*.x" si se quisiera habilitar
/// los subdominios de x pero NO x a secas, que no es el caso de estos presets.
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
            "time.windows.com",       // sincronizacion de hora
            "ntp.org",                // hora (fallback), cubre pool.ntp.org etc.
            "msftconnecttest.com",    // deteccion de conectividad de Windows
            "msftncsi.com",
            "digicert.com",           // OCSP/CRL (validacion de certificados)
            "verisign.com",
            "globalsign.com",
            "letsencrypt.org",
            "pki.goog",               // OCSP de Google (cubre ocsp.pki.goog)
            "api.github.com",         // Actualizaciones automaticas de LockSuite
            "github.com",
            "objects.githubusercontent.com",
            "raw.githubusercontent.com"
        }
    };

    public static DomainPack Google() => new()
    {
        Name = "Google (Gmail, Chat, Drive, Fotos)",
        BuiltIn = true,
        Enabled = true,
        Domains = new()
        {
            "google.com",             // cubre accounts., mail., drive., chat., photos., ...
            "googleapis.com",
            "gstatic.com",
            "googleusercontent.com",
            "google.com.ar",
            "googlemail.com"
        }
    };

    public static DomainPack MercadoPago() => new()
    {
        Name = "Mercado Pago",
        BuiltIn = true,
        Enabled = true,
        Domains = new()
        {
            "mercadopago.com.ar",
            "mercadopago.com",
            "mercadolibre.com.ar",    // MP se apoya en dominios de ML
            "mlstatic.com"            // estaticos/CDN de ML
        }
    };

    public static DomainPack CommunitySites() => new()
    {
        Name = "Sitios de la comunidad",
        BuiltIn = true,
        Enabled = true,
        Domains = new()
        {
            "mitmachim.top",
            "prog.co.il"
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
