using System;
using System.Collections.Generic;
using System.Linq;

namespace LockSuite.Core;

/// <summary>
/// Definicion de un paquete de fabrica (preset): sus dominios, su estado inicial y los
/// datos que muestra la interfaz (tarjeta "Permitir Gmail", etc.).
/// La configuracion en disco guarda solo id/nombre/estado/dominios; el resto vive aca.
/// </summary>
public sealed class PresetDefinition
{
    /// <summary>Identificador estable ("gmail", "google-drive", ...). No se traduce ni se cambia.</summary>
    public string Id { get; init; } = "";

    /// <summary>Nombre del paquete tal como se guarda en la config y se muestra en la lista.</summary>
    public string Name { get; init; } = "";

    /// <summary>True: paquete de bloqueo (sus dominios se bloquean aunque otro paquete los permita).</summary>
    public bool Blocks { get; init; }

    public IReadOnlyList<string> Domains { get; init; } = Array.Empty<string>();

    /// <summary>Estado en una instalacion nueva.</summary>
    public bool EnabledByDefault { get; init; } = true;

    /// <summary>Estado si el paquete aparece por primera vez en una config existente (actualizacion).</summary>
    public bool EnabledOnUpgrade { get; init; }

    /// <summary>Nombres con los que este paquete existia en versiones anteriores (configs sin id).</summary>
    public IReadOnlyList<string> LegacyNames { get; init; } = Array.Empty<string>();

    /// <summary>Dominios que tuvo en versiones anteriores y que se descartan a proposito al migrar.</summary>
    public IReadOnlyList<string> LegacyDomains { get; init; } = Array.Empty<string>();

    // ----- Datos para la interfaz -----

    /// <summary>True: se muestra como tarjeta con boton "Permitir X" en la vista Lista Blanca.</summary>
    public bool IsService { get; init; }
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Aviso corto: que queda afuera a proposito o que tener en cuenta.</summary>
    public string Notice { get; init; } = "";
    /// <summary>Direccion para el boton "Abrir" (entra directo al inicio de sesion si hace falta).</summary>
    public string OpenUrl { get; init; } = "";
}

/// <summary>Paquete de una version anterior que se reparte en varios paquetes nuevos.</summary>
public sealed class LegacySplit
{
    public LegacySplit(string legacyName, string[] targetIds, string[] legacyDomains)
    {
        LegacyName = legacyName;
        TargetIds = targetIds;
        LegacyDomains = legacyDomains;
    }

    public string LegacyName { get; }
    public IReadOnlyList<string> TargetIds { get; }
    /// <summary>Dominios del paquete viejo que se descartan (no se mueven a "Personalizados").</summary>
    public IReadOnlyList<string> LegacyDomains { get; }
}

/// <summary>
/// Paquetes de dominios de fabrica.
///
/// Sintaxis (ver <see cref="DomainMatcher"/>): "x.com" habilita x.com y TODOS sus subdominios;
/// "=x.com" solo el host exacto; "lh*.x.com" una sola etiqueta que empiece con "lh".
///
/// Criterio kosher de los paquetes de Google (analisis completo en docs/ANALISIS_DOMINIOS_SERVICIOS.md):
///   - NUNCA se habilita "google.com" entero: eso abre www.google.com (buscador), gemini.google.com,
///     news., books., play., etc. Se habilitan solo los hosts de cada app.
///   - Tampoco "googleapis.com", "gstatic.com" ni "googleusercontent.com" enteros: abririan
///     almacenamiento publico (storage.googleapis.com), la API de Gemini, miniaturas del buscador
///     de imagenes (encrypted-tbn*.gstatic.com), imagenes de YouTube/Play, etc.
///   - Las APIs internas de las apps viven en *.clients6.google.com y se habilitan juntas
///     (son muchas y cambian); las de IA (Gemini en Gmail/Docs) las corta el paquete de bloqueo
///     "Bloqueo fijo: buscador de Google y Gemini", que tiene prioridad sobre cualquier permiso.
///   - Cada paquete de Google incluye el inicio de sesion y los estaticos, asi cada boton funciona solo.
/// </summary>
public static class Presets
{
    public const string IdSystem = "system";
    public const string IdKosherBlock = "kosher-block";
    public const string IdGmail = "gmail";
    public const string IdGoogleChat = "google-chat";
    public const string IdGoogleDrive = "google-drive";
    public const string IdGooglePhotos = "google-photos";
    public const string IdProg = "prog";
    public const string IdMitmachim = "mitmachim";
    public const string IdMercadoPago = "mercadopago";
    public const string IdWindowsUpdate = "windows-update";
    public const string IdWebCommons = "web-commons";
    public const string IdCaptcha = "captcha";
    public const string IdBrowserUpdates = "browser-updates";

    /// <summary>Paquete por defecto para los dominios que agrega el tutor.</summary>
    public const string CustomPackName = "Personalizados";

    // ------------------------------------------------------------------------------------
    // Dominios
    // ------------------------------------------------------------------------------------

    private static readonly string[] SystemDomains =
    {
        "time.windows.com",       // sincronizacion de hora
        "ntp.org",                // hora (fallback), cubre pool.ntp.org etc.
        "msftconnecttest.com",    // deteccion de conectividad de Windows (cubre www.msftconnecttest.com)
        "msftncsi.com",           // deteccion de conectividad (cubre www.msftncsi.com, dns.msftncsi.com)
        // --- Validacion de certificados (OCSP / CRL / AIA de las CAs mas usadas) ---
        // Son hosts de PKI (estado de certificados y certificados intermedios), no contenido
        // navegable. Sin esto, los sitios HTTPS de esas CAs validan lento o fallan (sesion 11).
        "digicert.com",           // ocsp.digicert.com, crl3./crl4.digicert.com, cacerts.digicert.com
        "verisign.com",
        "globalsign.com",         // ocsp.globalsign.com, crl.globalsign.com
        "letsencrypt.org",
        "lencr.org",              // OCSP/CRL/intermedios de Let's Encrypt (o.lencr.org, c.lencr.org, x1.c.lencr.org) — la CA mas usada de la web
        "amazontrust.com",        // OCSP/CRL de Amazon (certificados de sitios en AWS/CloudFront): ocsp.rootca1.amazontrust.com, crl.*
        "sectigo.com",            // ocsp.sectigo.com, crl.sectigo.com, crt.sectigo.com
        "usertrust.com",          // raiz USERTrust (familia Sectigo/Comodo)
        "comodoca.com",           // ocsp.comodoca.com, crl.comodoca.com, crt.comodoca.com
        "identrust.com",          // IdenTrust (cadena historica de Let's Encrypt)
        "entrust.net",            // ocsp.entrust.net, crl.entrust.net
        "geotrust.com",           // marcas viejas de DigiCert (status.geotrust.com, ...)
        "thawte.com",
        "rapidssl.com",
        "quovadisglobal.com",     // QuoVadis (DigiCert, comun en Europa): ocsp./crl.quovadisglobal.com
        "ocsp.godaddy.com",       // GoDaddy: solo los hosts de PKI (no la tienda)
        "crl.godaddy.com",
        "certificates.godaddy.com",
        "ocsp.starfieldtech.com", // Starfield (GoDaddy)
        "crl.starfieldtech.com",
        "certificates.starfieldtech.com",
        "ssl.com",                // SSL.com: una de las 3 CAs de los certificados de Cloudflare (ocsps., crls., cert.ssl.com)
        "certum.pl",              // Certum: firma cruzada de la cadena de SSL.com (Cloudflare)
        "ocsp-certum.com",        // OCSP de Certum (subca.ocsp-certum.com, ...)
        "msocsp.com",             // PKI de Microsoft (ocsp.msocsp.com)
        "oneocsp.microsoft.com",  // OCSP de Microsoft (sitios en Azure con certificados de Microsoft)
        "crl.microsoft.com",      // CRL de Microsoft
        "mscrl.microsoft.com",    // CRL de Microsoft
        "ctldl.windowsupdate.com",// Actualizacion automatica de certificados RAIZ de Windows: sin esto, Edge y los
                                  // programas que usan el almacen de Windows muestran "La conexion no es privada"
                                  // con sitios cuya raiz todavia no se descargo (p. ej. raices nuevas de Cloudflare/SSL.com).
        "pki.goog",               // Google Trust Services (otra CA de Cloudflare): ocsp.pki.goog, crl.pki.goog, c.pki.goog, i.pki.goog
        "api.github.com",         // Actualizaciones automaticas de LockSuite (metadata del release)
        "github.com",             // Pagina del release / redireccion de descarga
        "githubusercontent.com"   // Descarga del instalador: cubre objects./raw./release-assets. (todos los subdominios)
    };

    /// <summary>
    /// Recursos de terceros que usan muchísimos sitios para verse bien: fuentes y CDNs de librerías
    /// (jQuery, Bootstrap, Font Awesome, cdnjs). Evita el caso 2.1 (páginas "rotas" sin estilo).
    /// A propósito NO se incluyen CDNs que sirven archivos arbitrarios de npm/GitHub
    /// (jsdelivr, unpkg): si un sitio puntual los necesita, el tutor los agrega desde
    /// "Bloqueados recientes". Ninguno de estos sirve contenido navegable por sí solo.
    /// </summary>
    private static readonly string[] WebCommonsDomains =
    {
        // Fuentes
        "fonts.googleapis.com",
        "fonts.gstatic.com",
        // CDNs de librerías (curadas, no suben contenido de usuario arbitrario)
        "cdnjs.cloudflare.com",
        "ajax.googleapis.com",        // jQuery alojado por Google
        "code.jquery.com",
        "stackpath.bootstrapcdn.com",
        "maxcdn.bootstrapcdn.com",
        "use.fontawesome.com",
        "kit.fontawesome.com",
        "ka-f.fontawesome.com",
    };

    /// <summary>
    /// Verificación "no soy un robot" (captcha) de sitios permitidos: sin esto no se puede
    /// iniciar sesión ni enviar formularios en muchos sitios (caso 2.3).
    /// Nota: el reCAPTCHA clásico se sirve desde www.google.com, que está en el Bloqueo fijo
    /// (el buscador): ese reCAPTCHA NO va a funcionar. Cloudflare Turnstile, hCaptcha y el
    /// reCAPTCHA servido por recaptcha.net (dominio alternativo de Google) sí funcionan.
    /// </summary>
    private static readonly string[] CaptchaDomains =
    {
        "challenges.cloudflare.com",  // Cloudflare Turnstile
        "hcaptcha.com",               // hCaptcha (cubre newassets., imgs., api.)
        "recaptcha.net",              // reCAPTCHA por el dominio alternativo de Google
    };

    /// <summary>
    /// Actualizaciones de seguridad de Windows y firmas de Microsoft Defender (sesion 11).
    /// Sin esto, el equipo deja de recibir parches y el antivirus queda con firmas viejas
    /// (falla silenciosa: no se ve ningun error). Lista tomada de la documentacion oficial de
    /// Microsoft ("Configure and validate Microsoft Defender Antivirus network connections" y
    /// "Windows Update troubleshooting"). Son hosts de descarga/servicio, no contenido navegable.
    /// No incluye la Tienda de Microsoft, Bing, MSN ni Copilot (siguen bloqueados).
    /// </summary>
    private static readonly string[] WindowsUpdateDomains =
    {
        // Windows Update / Microsoft Update
        "update.microsoft.com",             // *.update.microsoft.com (fe2., sls., ...)
        "windowsupdate.microsoft.com",
        "windowsupdate.com",                // *.windowsupdate.com, download.windowsupdate.com (puerto 80)
        "delivery.mp.microsoft.com",        // *.delivery.mp.microsoft.com, *.dl.delivery.mp.microsoft.com, fe3cr.
        "dsp.mp.microsoft.com",             // Optimizacion de distribucion y tsfe.trafficshaping.dsp.mp.microsoft.com
        "emdl.ws.microsoft.com",
        "download.microsoft.com",
        // Microsoft Defender: firmas (security intelligence) y proteccion en la nube
        "definitionupdates.microsoft.com",
        "go.microsoft.com",                 // redireccionador usado por la descarga alternativa de firmas (puerto 80)
        "wdcp.microsoft.com",               // proteccion en la nube (MAPS)
        "wdcpalt.microsoft.com",
        "wd.microsoft.com",                 // *.wd.microsoft.com
        // SmartScreen (reputacion de programas descargados: sin esto Windows no puede verificarlos)
        "smartscreen.microsoft.com",
        "smartscreen-prod.microsoft.com",
        "checkappexec.microsoft.com",
        "urs.microsoft.com",
    };

    /// <summary>
    /// Actualizaciones de los navegadores y de SUS listas de certificados (sesion 11):
    ///   - Chrome actualiza su almacen de raices (Chrome Root Store) y su lista de revocados (CRLSet)
    ///     con el actualizador de componentes; Edge baja sus listas de revocacion desde edge.microsoft.com;
    ///     Firefox baja los certificados intermedios que le faltan a los sitios mal configurados
    ///     ("intermediate preloading") y su lista de revocados (CRLite) desde Remote Settings.
    ///   - Sin esto los navegadores quedan desactualizados (sin parches de seguridad) y, con el tiempo,
    ///     aparecen errores de certificado (raices nuevas que no conocen, SEC_ERROR_UNKNOWN_ISSUER en Firefox).
    /// Fuentes: "Allow list for Microsoft Edge endpoints" (Microsoft), ayuda de Chrome Enterprise (Google),
    /// blog de seguridad de Mozilla (intermediate preloading). No incluye tiendas de extensiones ni sitios.
    /// </summary>
    private static readonly string[] BrowserUpdateDomains =
    {
        // Microsoft Edge (las descargas van por *.dl.delivery.mp.microsoft.com: paquete de Windows Update)
        "msedge.api.cdp.microsoft.com",     // servicio de actualizacion de Edge
        "edge.microsoft.com",               // componentes de Edge: listas de revocacion, proteccion de rastreo
        // Google Chrome
        "tools.google.com",                 // Omaha: configuracion de actualizacion
        "update.googleapis.com",            // actualizador de Chrome y de sus componentes (Root Store, CRLSet)
        "dl.google.com",                    // descargas de Chrome
        "edgedl.me.gvt1.com",               // descargas de Chrome y componentes
        // Mozilla Firefox
        "firefox.settings.services.mozilla.com",       // Remote Settings: intermedios, CRLite, revocaciones
        "firefox-settings-attachments.cdn.mozilla.net", // adjuntos de Remote Settings (certificados intermedios)
        "content-signature-2.cdn.mozilla.net",          // firmas de Remote Settings
        "aus5.mozilla.org",                             // chequeo de actualizaciones
        "download.mozilla.org",                         // descarga de actualizaciones (redirige al CDN)
        "download-installer.cdn.mozilla.net",
    };

    /// <summary>
    /// Inicio de sesion, cuenta y recursos comunes de las apps web de Google.
    /// Se repite dentro de cada paquete de Google para que cada boton funcione por si solo.
    /// </summary>
    private static readonly string[] GoogleCore =
    {
        // Inicio de sesion y cuenta
        "accounts.google.com",
        "accounts.google.com.ar",            // cookies de sesion por pais (Argentina)
        "accounts.google.co.il",             // cookies de sesion por pais (Israel)
        "accounts.youtube.com",              // SOLO el chequeo de sesion del login (iframe). NO habilita YouTube.
        "myaccount.google.com",              // "Administrar tu cuenta": contrasena, seguridad
        "gds.google.com",                    // pantallas despues del login ("confirma tu telefono de recuperacion")
        "policies.google.com",               // privacidad y condiciones (alta de cuenta)
        // Barra superior de Google: menu de la cuenta y "Cerrar sesion"
        "ogs.google.com",
        // Cargador de APIs y APIs internas de las apps. clients6.google.com incluye los
        // *-pa.clients6.google.com (contactos, notificaciones en tiempo real, etc.).
        // Los de IA los corta el paquete "Bloqueo fijo" (prioridad sobre esta lista).
        "apis.google.com",
        "clients1.google.com",
        "clients2.google.com",
        "clients3.google.com",
        "clients4.google.com",
        "clients5.google.com",
        "clients6.google.com",
        "www.googleapis.com",
        "content.googleapis.com",
        "oauth2.googleapis.com",
        "oauthaccountmanager.googleapis.com",
        // Archivos estaticos, iconos y fuentes
        "www.gstatic.com",
        "ssl.gstatic.com",
        "fonts.gstatic.com",
        "fonts.googleapis.com",
        // Fotos de perfil e imagenes alojadas por Google (lh3, lh4, lh5, lh6, lh7-rt, ...)
        "lh*.googleusercontent.com",
        // Contenido de usuario en el dominio nuevo de Google (descargas de Drive, imagenes de Fotos)
        "usercontent.google.com",
    };

    private static readonly string[] GmailDomains = Combine(GoogleCore, new[]
    {
        "mail.google.com",
        "gmail.com",                               // gmail.com -> mail.google.com (y imap./smtp. para programas de correo)
        "googlemail.com",
        "mail-attachment.google.com",
        "mail-attachment.googleusercontent.com",   // adjuntos: vista previa y descarga
        "ci*.googleusercontent.com",               // proxy de imagenes de los correos (ci3, ci4, ci5, ci6)
        "drive-thirdparty.googleusercontent.com",  // iconos de tipo de archivo
        "lh*.google.com",                          // miniaturas de adjuntos de Drive
        "client-channel.google.com",               // canal en tiempo real (y sus subdominios 0., 1., ...)
        "contacts.google.com",                     // contactos y tarjetas de contacto
        "calendar.google.com",                     // invitaciones y panel lateral
        "tasks.google.com",                        // panel lateral: Tareas
        "keep.google.com",                         // panel lateral: Keep
    });

    private static readonly string[] ChatDomains = Combine(GoogleCore, new[]
    {
        "chat.google.com",
        "client-channel.google.com",               // canal en tiempo real
        "contacts.google.com",                     // tarjetas de contacto
        "ci*.googleusercontent.com",               // vistas previas de imagenes y enlaces
    });

    private static readonly string[] DriveDomains = Combine(GoogleCore, new[]
    {
        "drive.google.com",                        // incluye *.drive.google.com (reproduccion de videos de Drive)
        "docs.google.com",                         // Documentos, Hojas, Presentaciones, Formularios (incluye *.docs.google.com)
        "sheets.google.com",                       // atajos que redirigen a docs.google.com
        "slides.google.com",
        "forms.google.com",
        "spreadsheets.google.com",
        "drivefrontend-pa.googleapis.com",
        "drivelabels.googleapis.com",
        "fcmconnection.googleapis.com",            // "Drive para computadora"
        "signaler-pa.googleapis.com",              // "Drive para computadora"
        "drive-thirdparty.googleusercontent.com",  // iconos de tipo de archivo
        "*-docs.googleusercontent.com",            // exportaciones y descargas (doc-0s-1a-docs...)
        "*-apidata.googleusercontent.com",         // descargas por API
        "lh*.google.com",                          // miniaturas (lh3.google.com)
        "inputtools.google.com",                   // teclados y herramientas de escritura de Documentos
        "contacts.google.com",                     // tarjetas de contacto y dialogo "Compartir"
    });

    private static readonly string[] PhotosDomains = Combine(GoogleCore, new[]
    {
        "photos.google.com",
        "photos.app.goo.gl",                       // enlaces para compartir albumes
        "photosdata-pa.googleapis.com",
        "photos.googleapis.com",
        "photosfirstparty-pa.googleapis.com",
        "video-downloads.googleusercontent.com",   // descarga de videos
    });

    /// <summary>
    /// Bloqueo fijo (prioridad sobre la lista blanca): buscador de Google y asistentes de IA.
    /// Protege aunque un paquete (o el tutor, por error) habilite algo mas amplio como "google.com".
    /// </summary>
    private static readonly string[] KosherBlockDomains =
    {
        // Buscador de Google y sus variantes
        "=google.com",
        "www.google.com",
        "ipv4.google.com",
        "ipv6.google.com",
        "encrypted.google.com",
        "images.google.com",
        "lens.google.com",
        "news.google.com",
        "books.google.com",
        "=google.com.ar",
        "www.google.com.ar",
        "=google.co.il",
        "www.google.co.il",
        // Gemini y otras IA de Google
        "gemini.google.com",
        "bard.google.com",
        "gemini.google",
        "aistudio.google.com",
        "notebooklm.google.com",
        "notebooklm.google",
        "labs.google",
        "labs.google.com",
        "assistant.google.com",
        "gemini.gstatic.com",
        "generativelanguage.googleapis.com",
        "aiplatform.googleapis.com",
        "*-aiplatform.googleapis.com",
        "robinfrontend-pa.googleapis.com",
        "robinfrontend-pa.clients6.google.com",
        "opa-pa.googleapis.com",
        "opa-pa.clients6.google.com",
        "aisandbox-pa.googleapis.com",
        "alkalimakersuite-pa.clients6.google.com",
        "appsgenaiserver-pa.clients6.google.com",  // Gemini dentro de Gmail, Documentos y Drive
        // Promociones de otros productos de Google dentro de las apps ("Proba Gemini", etc.)
        "ogads-pa.clients6.google.com",
    };

    private static readonly string[] ProgDomains =
    {
        "prog.co.il",                                      // www., s3. (imagenes y avisos propios), amember.
        "prod-progsite-s3.s3.eu-central-1.wasabisys.com",  // archivos del sitio (bucket propio)
        "s3.eu-central-003.backblazeb2.com",               // imagenes de secciones (bucket "prog-www")
        "challenges.cloudflare.com",                       // verificacion "Un momento..." de Cloudflare
        "code.jquery.com",                                 // jQuery: sin esto el sitio no funciona
        "cdnjs.cloudflare.com",                            // carruseles (Swiper, OwlCarousel)
        "cdn.jsdelivr.net",                                // estilos de carrusel y calendario hebreo
        "www.hebcal.com",                                  // fecha hebrea y zmanim
        "cdn.userway.org",                                 // boton de accesibilidad
        "api.userway.org",
        // "Ingresar con Google"
        "accounts.google.com",
        "accounts.youtube.com",
        "ssl.gstatic.com",
        "www.gstatic.com",
        "fonts.gstatic.com",
    };

    private static readonly string[] MitmachimDomains =
    {
        "mitmachim.top",
        "fonts.googleapis.com",                            // fuentes hebreas (Assistant, Arimo)
        "fonts.gstatic.com",
        // "Ingresar con Google"
        "accounts.google.com",
        "accounts.youtube.com",
        "ssl.gstatic.com",
        "www.gstatic.com",
    };

    private static readonly string[] MercadoPagoDomains =
    {
        "mercadopago.com.ar",
        "mercadopago.com",
        "mercadolibre.com.ar",    // MP se apoya en dominios de ML
        "mlstatic.com"            // estaticos/CDN de ML
    };

    // ------------------------------------------------------------------------------------
    // Catalogo (el orden es el de una instalacion nueva y el de las tarjetas de la interfaz)
    // ------------------------------------------------------------------------------------

    public static IReadOnlyList<PresetDefinition> Catalog { get; } = new[]
    {
        new PresetDefinition
        {
            Id = IdSystem,
            Name = "Esenciales del sistema",
            Domains = SystemDomains,
            EnabledByDefault = true,
            EnabledOnUpgrade = true,
            LegacyNames = new[] { "Esenciales del sistema" },
            LegacyDomains = new[]
            {
                "*.ntp.org", "*.digicert.com", "*.verisign.com", "ocsp.pki.goog", "*.globalsign.com",
                "*.letsencrypt.org", "*.int-x3.letsencrypt.org", "objects.githubusercontent.com",
                "raw.githubusercontent.com"
            },
            Title = "Esenciales del sistema",
            Icon = "⚙️",
            Description = "Hora, validación de certificados y actualizaciones de LockSuite."
        },
        new PresetDefinition
        {
            Id = IdKosherBlock,
            Name = "Bloqueo fijo: buscador de Google y Gemini",
            Blocks = true,
            Domains = KosherBlockDomains,
            EnabledByDefault = true,
            EnabledOnUpgrade = true,
            Title = "Bloqueo fijo: buscador de Google y Gemini",
            Icon = "🚫",
            Description = "Mantiene bloqueados el buscador de Google, Gemini y otras IA de Google aunque se permitan Gmail, Drive, Chat o Fotos."
        },
        new PresetDefinition
        {
            Id = IdWindowsUpdate,
            Name = "Actualizaciones de Windows y Defender",
            Domains = WindowsUpdateDomains,
            EnabledByDefault = true,
            EnabledOnUpgrade = true,
            Title = "Actualizaciones de Windows y Defender",
            Icon = "🛡️",
            Description = "Parches de seguridad de Windows, firmas del antivirus Microsoft Defender y SmartScreen. No incluye la Tienda, Bing, MSN ni Copilot."
        },
        new PresetDefinition
        {
            Id = IdBrowserUpdates,
            Name = "Actualizaciones de navegadores y sus certificados",
            Domains = BrowserUpdateDomains,
            EnabledByDefault = true,
            EnabledOnUpgrade = true,
            Title = "Actualizaciones de navegadores",
            Icon = "🔐",
            Description = "Chrome, Edge y Firefox: parches de seguridad y sus listas de certificados (raíces, intermedios y revocados). Evita errores de certificado con el tiempo."
        },
        new PresetDefinition
        {
            Id = IdGmail,
            Name = "Gmail",
            Domains = GmailDomains,
            IsService = true,
            Title = "Gmail",
            Icon = "✉️",
            Description = "Correo, adjuntos, contactos y el panel lateral (Calendario, Tareas, Keep).",
            Notice = "Las imágenes de los correos se ven por el proxy de Gmail. Para previsualizar adjuntos de Office, permití también Google Drive.",
            OpenUrl = "https://mail.google.com/mail/"
        },
        new PresetDefinition
        {
            Id = IdGoogleChat,
            Name = "Google Chat",
            Domains = ChatDomains,
            IsService = true,
            Title = "Google Chat",
            Icon = "💬",
            Description = "Mensajes y espacios de Google Chat (chat.google.com).",
            Notice = "Sin GIFs animados (Tenor) ni videollamadas (Meet). El chat dentro de Gmail necesita también el botón de Gmail.",
            OpenUrl = "https://chat.google.com/u/0/"
        },
        new PresetDefinition
        {
            Id = IdGoogleDrive,
            Name = "Google Drive",
            Domains = DriveDomains,
            IsService = true,
            Title = "Google Drive",
            Icon = "📁",
            Description = "Drive, Documentos, Hojas de cálculo, Presentaciones y Formularios.",
            Notice = "Permite ver todo lo que le compartan. Si un video no se reproduce en la vista previa, se puede descargar.",
            OpenUrl = "https://drive.google.com/drive/my-drive"
        },
        new PresetDefinition
        {
            Id = IdGooglePhotos,
            Name = "Google Fotos",
            Domains = PhotosDomains,
            IsService = true,
            Title = "Google Fotos",
            Icon = "🖼️",
            Description = "Fotos y álbumes de Google Fotos.",
            Notice = "Permite ver todo lo que le compartan. Sin mapas ni búsqueda con Lens.",
            OpenUrl = "https://photos.google.com/login"
        },
        new PresetDefinition
        {
            Id = IdProg,
            Name = "prog.co.il",
            Domains = ProgDomains,
            IsService = true,
            Title = "prog.co.il",
            Icon = "🗨️",
            Description = "Foro y comunidad Prog, con sus imágenes y la verificación de Cloudflare.",
            Notice = "Sin publicidad ni estadísticas de terceros, ni videos de YouTube incrustados.",
            OpenUrl = "https://www.prog.co.il/"
        },
        new PresetDefinition
        {
            Id = IdMitmachim,
            Name = "mitmachim.top",
            Domains = MitmachimDomains,
            IsService = true,
            Title = "mitmachim.top",
            Icon = "💻",
            Description = "Foro Mitmachim Top, con sus fuentes hebreas.",
            Notice = "Crear una cuenta nueva usa reCAPTCHA de www.google.com (bloqueado): registrarse con «Ingresar con Google» o desde otro equipo. Entrar con una cuenta existente funciona.",
            OpenUrl = "https://mitmachim.top/"
        },
        new PresetDefinition
        {
            Id = IdWebCommons,
            Name = "Recursos web comunes (fuentes y librerías)",
            Domains = WebCommonsDomains,
            EnabledByDefault = true,
            EnabledOnUpgrade = true,
            Title = "Recursos web comunes",
            Icon = "🧩",
            Description = "Fuentes y librerías que usan muchísimos sitios para verse bien (Google Fonts, cdnjs, jQuery, Bootstrap, Font Awesome). No sirven contenido por sí solos."
        },
        new PresetDefinition
        {
            Id = IdCaptcha,
            Name = "Verificación «no soy un robot»",
            Domains = CaptchaDomains,
            EnabledByDefault = true,
            EnabledOnUpgrade = true,
            Title = "Verificación «no soy un robot»",
            Icon = "✅",
            Description = "Cloudflare Turnstile, hCaptcha y reCAPTCHA (por recaptcha.net), para poder iniciar sesión o enviar formularios en sitios permitidos. El reCAPTCHA servido desde www.google.com sigue bloqueado (es el buscador)."
        },
        new PresetDefinition
        {
            Id = IdMercadoPago,
            Name = "Mercado Pago",
            Domains = MercadoPagoDomains,
            EnabledOnUpgrade = true,
            LegacyNames = new[] { "Mercado Pago" },
            LegacyDomains = new[] { "*.mercadopago.com.ar", "*.mercadolibre.com.ar", "*.mlstatic.com", "*.mercadopago.com" },
            Title = "Mercado Pago",
            Icon = "💳",
            Description = "Pagos con Mercado Pago."
        },
    };

    /// <summary>
    /// Paquetes de versiones anteriores (sin id) que se reparten en los paquetes nuevos.
    /// Los paquetes nuevos heredan el estado (habilitado/deshabilitado) del viejo.
    /// </summary>
    public static IReadOnlyList<LegacySplit> LegacySplits { get; } = new[]
    {
        new LegacySplit(
            "Google (Gmail, Chat, Drive, Fotos)",
            new[] { IdGmail, IdGoogleChat, IdGoogleDrive, IdGooglePhotos },
            // Se descartan a proposito: abrian el buscador (google.com, google.com.ar), Gemini, etc.
            new[]
            {
                "google.com", "*.google.com", "googleapis.com", "*.googleapis.com", "gstatic.com", "*.gstatic.com",
                "googleusercontent.com", "*.googleusercontent.com", "google.com.ar", "googlemail.com", "*.googlemail.com"
            }),
        new LegacySplit(
            "Sitios de la comunidad",
            new[] { IdProg, IdMitmachim },
            new[] { "mitmachim.top", "*.mitmachim.top", "prog.co.il", "*.prog.co.il" }),
    };

    /// <summary>
    /// Hosts que el bloqueo fijo protege; sirven para avisarle al tutor si un patron que
    /// quiere agregar los abarcaria (p. ej. "google.com").
    /// </summary>
    public static IReadOnlyList<string> KosherProbeHosts { get; } = new[]
    {
        "google.com", "www.google.com", "gemini.google.com", "www.google.com.ar", "www.google.co.il",
        "appsgenaiserver-pa.clients6.google.com"
    };

    // Notas para "Bloqueados recientes": ayudan al tutor a no habilitar lo que no hace falta.
    private static readonly (string Pattern, string Note)[] BlockedNotes =
    {
        ("play.google.com", "Google Play (tienda) y estadísticas de uso: no hace falta para Gmail, Drive, Chat ni Fotos."),
        ("mail-ads.google.com", "Publicidad de Gmail: no hace falta."),
        ("csp.withgoogle.com", "Reportes técnicos de Google: no hace falta."),
        ("workspace.google.com", "Página publicitaria de Google: para entrar usá los botones «Abrir» de Lista Blanca."),
        ("support.google.com", "Centro de ayuda y foros de Google: no hace falta."),
        ("meet.google.com", "Videollamadas (Meet): no está incluido."),
        ("maps.google.com", "Google Maps: no está incluido."),
        ("maps.googleapis.com", "Mapas y Street View: no está incluido."),
        ("maps.gstatic.com", "Mapas: no está incluido."),
        ("streetviewpixels-pa.googleapis.com", "Street View: no está incluido."),
        ("encrypted-tbn*.gstatic.com", "Miniaturas del buscador de imágenes de Google: bloqueado a propósito."),
        ("storage.googleapis.com", "Almacenamiento público de Google Cloud (contenido de cualquiera): no recomendado."),
        ("firebasestorage.googleapis.com", "Archivos públicos de Firebase (contenido de cualquiera): no recomendado."),
        ("accounts.youtube.com", "Chequeo de sesión del inicio de sesión de Google: lo incluyen los botones de Google (no habilita YouTube)."),
        ("youtube.com", "YouTube: bloqueado a propósito."),
        ("youtube-nocookie.com", "Videos de YouTube incrustados: bloqueado a propósito."),
        ("youtubei.googleapis.com", "YouTube: bloqueado a propósito."),
        ("youtube.googleapis.com", "Reproductor de YouTube: bloqueado a propósito."),
        ("ytimg.com", "Imágenes de YouTube: bloqueado a propósito."),
        ("googlevideo.com", "Videos de YouTube: bloqueado a propósito."),
        ("ggpht.com", "Imágenes de YouTube: bloqueado a propósito."),
        ("youtu.be", "YouTube: bloqueado a propósito."),
        ("tenor.com", "GIFs animados (Tenor): se bloquean a propósito."),
        ("tenor.googleapis.com", "GIFs animados (Tenor): se bloquean a propósito."),
        ("google-analytics.com", "Estadísticas: no hace falta."),
        ("analytics.google.com", "Estadísticas: no hace falta."),
        ("googletagmanager.com", "Estadísticas y publicidad: no hace falta."),
        ("doubleclick.net", "Publicidad: no hace falta."),
        ("googlesyndication.com", "Publicidad: no hace falta."),
        ("googleadservices.com", "Publicidad: no hace falta."),
        ("facebook.net", "Seguimiento de Facebook: no hace falta."),
        ("facebook.com", "Facebook: no hace falta."),
        ("cloudflareinsights.com", "Estadísticas: no hace falta."),
        ("weather2day.co.il", "Widget del clima de prog.co.il: opcional, no incluido."),
        // Validacion de certificados (sesion 11): los hosts de PKI de las CAs grandes ya vienen en
        // "Esenciales del sistema". Estos avisan de los que quedan afuera a proposito.
        ("www.microsoft.com", "Sitio de Microsoft. Solo hace falta si un programa o un sitio alojado en Azure falla por certificado (usa www.microsoft.com/pkiops)."),
        // CDNs que sirven archivos de cualquiera
        ("cdn.jsdelivr.net", "CDN que sirve archivos de cualquiera (npm/GitHub): permitir solo si un sitio permitido no funciona sin esto."),
        ("unpkg.com", "CDN que sirve archivos de cualquiera (npm): permitir solo si un sitio permitido no funciona sin esto."),
        // DNS seguro de los navegadores (LockSuite lo desactiva por directiva)
        ("cloudflare-dns.com", "«DNS seguro» del navegador (Cloudflare): no hace falta, LockSuite resuelve los nombres. No permitir."),
        ("dns.google", "«DNS seguro» del navegador (Google): no hace falta, LockSuite resuelve los nombres. No permitir."),
        ("use-application-dns.net", "Prueba de Firefox para su «DNS seguro»: que quede bloqueada hace que Firefox use el DNS de LockSuite. No permitir."),
        // Microsoft: lo que Windows intenta en segundo plano y no hace falta
        ("bing.com", "Bing (buscador de Microsoft): bloqueado a propósito."),
        ("msn.com", "MSN (noticias de los Widgets de Windows): bloqueado a propósito."),
        ("copilot.microsoft.com", "Copilot (IA de Microsoft): bloqueado a propósito."),
        ("events.data.microsoft.com", "Telemetría de Windows: no hace falta."),
        ("storeedgefd.dsx.mp.microsoft.com", "Tienda de Microsoft: no incluida."),
        ("displaycatalog.mp.microsoft.com", "Tienda de Microsoft: no incluida."),
        ("login.live.com", "Cuenta Microsoft (OneDrive, Outlook.com, inicio de sesión de Windows): permitir solo si se usa una cuenta Microsoft."),
    };

    // ------------------------------------------------------------------------------------
    // API
    // ------------------------------------------------------------------------------------

    /// <summary>Busca un preset por id (null si no existe).</summary>
    public static PresetDefinition? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var def in Catalog)
            if (string.Equals(def.Id, id, StringComparison.OrdinalIgnoreCase)) return def;
        return null;
    }

    /// <summary>Crea el paquete de config de un preset.</summary>
    public static DomainPack ToPack(PresetDefinition def, bool enabled) => new()
    {
        Id = def.Id,
        Name = def.Name,
        Enabled = enabled,
        BuiltIn = true,
        Blocks = def.Blocks,
        Domains = def.Domains.ToList()
    };

    /// <summary>Esenciales del sistema: sin esto Windows muestra "sin internet" o fallan los certificados.
    /// Viene activado por defecto y conviene no desactivarlo.</summary>
    public static DomainPack SystemEssentials() => ToPack(Find(IdSystem)!, true);

    public static DomainPack MercadoPago() => ToPack(Find(IdMercadoPago)!, true);

    public static DomainPack KosherBlock() => ToPack(Find(IdKosherBlock)!, true);

    /// <summary>Configuracion inicial de fabrica, con los presets en su estado por defecto.</summary>
    public static WhitelistConfig DefaultConfig() => new()
    {
        Version = WhitelistConfig.CurrentVersion,
        Packs = Catalog.Select(d => ToPack(d, d.EnabledByDefault)).ToList()
    };

    /// <summary>True si el host esta en el bloqueo fijo de fabrica (buscador de Google / Gemini).</summary>
    public static bool IsKosherBlocked(string? host)
        => DomainMatcher.MatchesAny(KosherBlockDomains, host);

    /// <summary>True si un patron que se quiere permitir abarcaria el buscador de Google o Gemini.</summary>
    public static bool CoversKosherBlockedSites(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        foreach (var probe in KosherProbeHosts)
            if (DomainMatcher.PatternMatches(pattern, probe)) return true;
        return false;
    }

    /// <summary>
    /// Explicacion corta para un dominio bloqueado (pestana "Bloqueados recientes"):
    /// por que esta bloqueado y si conviene o no habilitarlo. Null si no hay nada que decir.
    /// </summary>
    public static string? ExplainBlocked(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        if (IsKosherBlocked(host))
            return "Bloqueo fijo (buscador de Google o Gemini): queda bloqueado aunque se permitan Gmail, Drive, Chat o Fotos.";
        foreach (var (pattern, note) in BlockedNotes)
            if (DomainMatcher.PatternMatches(pattern, host)) return note;
        if (LooksLikeCertificateInfrastructure(host))
            return "Parece un servidor de validación de certificados (OCSP/CRL) de una autoridad certificante: no es contenido. " +
                   "Conviene permitirlo si un sitio permitido muestra «La conexión no es privada».";
        return null;
    }

    /// <summary>
    /// Heuristica para "Bloqueados recientes": hosts cuya primera etiqueta indica infraestructura de
    /// certificados (ocsp, ocsp2, crl, crl3, ...). Las CAs grandes ya vienen en "Esenciales del sistema";
    /// esto ayuda al tutor a reconocer las de CAs chicas o locales (p. ej. de un sitio del gobierno).
    /// </summary>
    public static bool LooksLikeCertificateInfrastructure(string? host)
    {
        var h = DomainMatcher.Normalize(host);
        int dot = h.IndexOf('.');
        if (dot <= 0 || h.IndexOf('.', dot + 1) < 0) return false;   // al menos tres etiquetas: ocsp.ca.com
        var first = h.AsSpan(0, dot);
        return StartsWithThenDigits(first, "ocsp") || StartsWithThenDigits(first, "crl");
    }

    // "ocsp", "ocsp2", "crl", "crl3"... pero no "crlx" ni "ocspfoo".
    private static bool StartsWithThenDigits(ReadOnlySpan<char> label, string prefix)
    {
        if (!label.StartsWith(prefix.AsSpan(), StringComparison.Ordinal)) return false;
        foreach (char c in label[prefix.Length..])
            if (c < '0' || c > '9') return false;
        return true;
    }

    // Une listas sin repetir y respetando el orden.
    private static string[] Combine(params string[][] lists)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var list in lists)
            foreach (var d in list)
                if (seen.Add(d)) result.Add(d);
        return result.ToArray();
    }
}
