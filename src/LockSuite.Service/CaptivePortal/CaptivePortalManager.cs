using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using LockSuite.Service.Dns;
using LockSuite.Service.Wfp;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.CaptivePortal;

/// <summary>
/// "Modo portal cautivo / Wi-Fi de hotel" (caso 2.2).
///
/// Problema: en una red con portal cautivo (hotel, café, aeropuerto) la página para aceptar los
/// términos o poner la clave vive en el router/gateway de esa red, no en internet. Con el filtro
/// default-deny + DNS forzado a 127.0.0.1, esa página nunca aparece y el equipo queda sin conexión.
///
/// Solución (conservadora, pensada para mantener el equipo kosher):
///   - NO se apaga el filtro. El bloqueo default-deny sigue activo todo el tiempo.
///   - Por una ventana corta (el tutor elige, máx. 15 min) se abren SOLO las IP del gateway y de los
///     servidores DNS que entregó la red (direcciones locales), reutilizando la MISMA regla temporal
///     por-IP que ya usa el filtrado normal (con autovencimiento). No se abre internet.
///   - Mientras dura, se suspende la redirección NRPT para que el navegador resuelva el nombre del
///     portal con el DNS de la red (que acaba de abrirse). Al vencer, NRPT se vuelve a aplicar sola.
///   - Requiere contraseña del tutor (lo exige la capa IPC).
///
/// Límite honesto: funciona con los portales alojados en el gateway/DNS de la red (el caso común).
/// Si un portal redirige a una IP pública propia, puede no completar; ahí el tutor puede usar el modo
/// de prueba o permitir ese dominio a mano. Documentado en docs/ANALISIS_INCOMODIDADES_Y_FALLAS.md §2.2.
/// </summary>
public sealed class CaptivePortalManager
{
    private readonly ILogger<CaptivePortalManager> _logger;
    private readonly IWfpManager _wfp;
    private readonly DnsConfigurator _dns;
    private readonly object _lock = new();

    private Timer? _timer;
    private DateTimeOffset _endsAtUtc;
    private bool _active;

    public const int MaxMinutes = 15;
    public const int DefaultMinutes = 5;

    public CaptivePortalManager(ILogger<CaptivePortalManager> logger, IWfpManager wfp, DnsConfigurator dns)
    {
        _logger = logger;
        _wfp = wfp;
        _dns = dns;
    }

    public bool IsActive { get { lock (_lock) { return _active && DateTimeOffset.UtcNow < _endsAtUtc; } } }

    public int RemainingSeconds
    {
        get
        {
            lock (_lock)
            {
                if (!_active) return 0;
                var s = (_endsAtUtc - DateTimeOffset.UtcNow).TotalSeconds;
                return s > 0 ? (int)Math.Round(s) : 0;
            }
        }
    }

    /// <summary>
    /// Abre la ventana de portal cautivo por <paramref name="minutes"/> minutos (1..MaxMinutes).
    /// Devuelve la cantidad de direcciones locales que se abrieron (0 = no se encontró gateway;
    /// casi siempre significa que no hay red conectada).
    /// </summary>
    public int Start(int minutes)
    {
        if (minutes < 1) minutes = DefaultMinutes;
        if (minutes > MaxMinutes) minutes = MaxMinutes;
        var window = TimeSpan.FromMinutes(minutes);

        var locals = GetLocalGatewayAndDnsAddresses();

        lock (_lock)
        {
            // Abrir (o refrescar) las IP locales por la ventana. Reutiliza la regla temporal por-IP.
            foreach (var ip in locals)
                _wfp.AllowIpTemporary(ip, window);

            // Suspender la redirección NRPT para que el nombre del portal resuelva con el DNS de la red.
            _dns.RemoveNrptRedirect();

            _endsAtUtc = DateTimeOffset.UtcNow.Add(window);
            _active = true;

            _timer?.Dispose();
            _timer = new Timer(_ => End(), null, window, Timeout.InfiniteTimeSpan);

            _logger.LogWarning("Modo portal cautivo ACTIVADO por {Min} min. Direcciones locales abiertas: {Count}.",
                minutes, locals.Count);
        }
        return locals.Count;
    }

    /// <summary>Cierra la ventana antes de tiempo y restaura la redirección NRPT.</summary>
    public void End()
    {
        lock (_lock)
        {
            if (!_active) return;
            _active = false;
            _timer?.Dispose();
            _timer = null;

            // Reaplicar NRPT (vuelve a forzar el DNS local). Las reglas por-IP vencen solas.
            _dns.ApplyNrptRedirect();
            _logger.LogWarning("Modo portal cautivo finalizado. Redirección DNS restaurada.");
        }
    }

    /// <summary>
    /// IPs del gateway por defecto y de los servidores DNS que entregó la red, filtrando a solo
    /// direcciones privadas/locales (nunca se abre una IP pública por esta vía).
    /// </summary>
    private static System.Collections.Generic.List<IPAddress> GetLocalGatewayAndDnsAddresses()
    {
        var result = new System.Collections.Generic.List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = ni.GetIPProperties();
                foreach (var gw in props.GatewayAddresses)
                    if (gw?.Address != null && IsLocal(gw.Address)) AddUnique(result, gw.Address);
                foreach (var dns in props.DnsAddresses)
                    if (IsLocal(dns)) AddUnique(result, dns);
            }
        }
        catch { /* best-effort */ }
        return result;
    }

    private static void AddUnique(System.Collections.Generic.List<IPAddress> list, IPAddress ip)
    {
        if (!list.Any(x => x.Equals(ip))) list.Add(ip);
    }

    // Solo direcciones que no salen a internet: privadas IPv4, link-local IPv4/IPv6, ULA IPv6.
    private static bool IsLocal(IPAddress ip)
    {
        if (ip == null) return false;
        if (IPAddress.IsLoopback(ip)) return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            if (b[0] == 10) return true;                         // 10.0.0.0/8
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true; // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;         // 192.168.0.0/16
            if (b[0] == 169 && b[1] == 254) return true;         // 169.254.0.0/16 (link-local)
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true; // 100.64.0.0/10 (CGNAT, común en hoteles)
            return false;
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || IsUniqueLocalV6(ip);

        return false;
    }

    private static bool IsUniqueLocalV6(IPAddress ip)
    {
        byte[] b = ip.GetAddressBytes();
        return (b[0] & 0xFE) == 0xFC;   // fc00::/7
    }
}
