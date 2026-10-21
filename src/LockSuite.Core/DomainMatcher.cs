using System;
using System.Collections.Generic;

namespace LockSuite.Core;

/// <summary>
/// Decide si un hostname esta permitido segun una lista blanca de patrones.
///
/// Reglas de los patrones:
///   "example.com"    -> permite example.com Y todos sus subdominios (www., a.b., ...)
///   "*.example.com"  -> permite subdominios (www.example.com) pero NO example.com
///   ".example.com"   -> equivalente a "example.com"
///
/// Garantias de seguridad (ver tests):
///   - Nunca matchea por substring: "notgoogle.com" NO entra por "google.com".
///   - Listar un subdominio no abre el padre: "mail.google.com" NO habilita "google.com".
///   - Case-insensitive y tolera el punto final de un FQDN.
/// </summary>
public static class DomainMatcher
{
    /// <summary>Normaliza un host: minusculas, sin espacios, sin punto final.</summary>
    public static string Normalize(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return string.Empty;
        var h = host.Trim().ToLowerInvariant();
        if (h.EndsWith(".", StringComparison.Ordinal)) h = h[..^1];
        return h;
    }

    /// <summary>True si un unico patron matchea el host.</summary>
    public static bool PatternMatches(string pattern, string? host)
    {
        if (pattern is null) return false;
        var p = pattern.Trim().ToLowerInvariant();
        if (p.EndsWith(".", StringComparison.Ordinal)) p = p[..^1];
        var h = Normalize(host);
        if (p.Length == 0 || h.Length == 0) return false;

        bool wildcard = false;
        if (p.StartsWith("*.", StringComparison.Ordinal)) { wildcard = true; p = p[2..]; }
        else if (p.StartsWith(".", StringComparison.Ordinal)) { p = p[1..]; }

        if (p.Length == 0) return false;

        if (string.Equals(h, p, StringComparison.Ordinal))
            return !wildcard;                       // "*.x" no matchea "x"; "x" si

        return h.EndsWith("." + p, StringComparison.Ordinal);  // subdominio, en ambos casos
    }

    /// <summary>True si algun patron de la lista blanca matchea el host.</summary>
    public static bool IsAllowed(IEnumerable<string> whitelist, string? host)
    {
        if (whitelist is null) return false;
        foreach (var pat in whitelist)
            if (PatternMatches(pat, host)) return true;
        return false;
    }

    /// <summary>
    /// Valida que un patron de lista blanca sea seguro de agregar.
    /// Rechaza entradas peligrosas o malformadas que, por error del tutor,
    /// abririan demasiado:
    ///   - vacio o solo espacios
    ///   - un unico label o un TLD suelto ("com", "localhost") -> habilitaria medio internet
    ///   - comodin que no sea el prefijo "*." (ej. "a.*.com", "*google.com")
    ///   - caracteres invalidos (espacios, '/', ':', '@', etc.)
    ///   - labels vacios ("a..b.com", ".", "..")
    /// Acepta: "ejemplo.com", "*.ejemplo.com", ".ejemplo.com", "sub.ejemplo.co.il".
    /// </summary>
    public static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        var raw = pattern.Trim().ToLowerInvariant();
        if (raw.EndsWith(".", StringComparison.Ordinal)) raw = raw[..^1];

        // Solo se admite el comodin como prefijo "*." (o el atajo ".").
        string p;
        if (raw.StartsWith("*.", StringComparison.Ordinal)) p = raw[2..];
        else if (raw.StartsWith(".", StringComparison.Ordinal)) p = raw[1..];
        else p = raw;

        if (p.Length == 0) return false;
        if (p.Contains('*')) return false;              // '*' solo valido como prefijo "*."

        // Caracteres permitidos en el cuerpo del dominio: letras, digitos, '-', '.'
        foreach (char c in p)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '.';
            if (!ok) return false;
        }

        // Debe tener al menos dos labels no vacios (evita TLD/single-label como "com").
        var labels = p.Split('.');
        if (labels.Length < 2) return false;
        foreach (var label in labels)
        {
            if (label.Length == 0) return false;        // no "..", ni bordes vacios
            if (label.StartsWith("-") || label.EndsWith("-")) return false;
        }

        return true;
    }
}
