using System;
using System.Collections.Generic;

namespace LockSuite.Core;

/// <summary>
/// Decide si un hostname esta permitido segun una lista blanca de patrones.
///
/// Reglas de los patrones:
///   "example.com"      -> permite example.com Y todos sus subdominios (www., a.b., ...)
///   "*.example.com"    -> permite subdominios (www.example.com) pero NO example.com
///   ".example.com"     -> equivalente a "example.com"
///   "=example.com"     -> SOLO el host exacto example.com (ningun subdominio)
///   "lh*.example.com"  -> comodin DENTRO de la primera etiqueta: matchea exactamente una
///                         etiqueta en esa posicion (lh3.example.com, lh7-rt.example.com),
///                         nunca cruza un punto (NO matchea a.lh3.example.com ni example.com).
///                         Tambien "*-docs.example.com" (doc-0s-1a-docs.example.com).
///
/// Garantias de seguridad (ver tests):
///   - Nunca matchea por substring: "notgoogle.com" NO entra por "google.com".
///   - Listar un subdominio no abre el padre: "mail.google.com" NO habilita "google.com".
///   - Un comodin de etiqueta jamas abarca mas de una etiqueta.
///   - Case-insensitive y tolera el punto final de un FQDN.
///
/// Bloqueos: la sobrecarga IsAllowed(lista blanca, host, lista de bloqueo) recibe
/// ademas una lista de patrones de bloqueo que tiene PRIORIDAD sobre la lista blanca
/// (ej. el paquete "Bloqueo fijo: buscador de Google y Gemini").
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
        var h = Normalize(host);
        if (h.Length == 0) return false;
        return MatchesNormalizedHost(pattern, h);
    }

    /// <summary>True si algun patron de la lista matchea el host.</summary>
    public static bool MatchesAny(IEnumerable<string>? patterns, string? host)
    {
        if (patterns is null) return false;
        var h = Normalize(host);
        if (h.Length == 0) return false;
        foreach (var pat in patterns)
            if (pat is not null && MatchesNormalizedHost(pat, h)) return true;
        return false;
    }

    /// <summary>True si algun patron de la lista blanca matchea el host.</summary>
    public static bool IsAllowed(IEnumerable<string> whitelist, string? host)
        => MatchesAny(whitelist, host);

    /// <summary>
    /// True si el host esta en la lista blanca Y no esta en la lista de bloqueo.
    /// El bloqueo tiene prioridad: sirve para habilitar un servicio completo
    /// sin abrir partes puntuales (p. ej. el buscador de Google o Gemini).
    /// </summary>
    public static bool IsAllowed(IEnumerable<string> whitelist, string? host, IEnumerable<string>? blocklist)
        => MatchesAny(whitelist, host) && !MatchesAny(blocklist, host);

    // h ya viene normalizado (minusculas, sin espacios ni punto final) y no vacio.
    private static bool MatchesNormalizedHost(string pattern, string h)
    {
        var p = pattern.Trim().ToLowerInvariant();
        if (p.EndsWith(".", StringComparison.Ordinal)) p = p[..^1];
        if (p.Length == 0) return false;

        // "=host": solo ese host, sin subdominios.
        if (p[0] == '=')
        {
            p = p[1..];
            return p.Length > 0 && p.IndexOf('*') < 0 && string.Equals(h, p, StringComparison.Ordinal);
        }

        bool subdomainsOnly = false;
        if (p.StartsWith("*.", StringComparison.Ordinal)) { subdomainsOnly = true; p = p[2..]; }
        else if (p[0] == '.') { p = p[1..]; }

        if (p.Length == 0) return false;

        // Comodin dentro de una etiqueta ("lh*.x.com", "*-docs.x.com").
        if (p.IndexOf('*') >= 0)
            return !subdomainsOnly && LabelGlobMatches(p, h);

        if (string.Equals(h, p, StringComparison.Ordinal))
            return !subdomainsOnly;                 // "*.x" no matchea "x"; "x" si

        // Subdominio, en ambos casos: h termina en "." + p (sin crear strings nuevos).
        return h.Length > p.Length
            && h[h.Length - p.Length - 1] == '.'
            && h.EndsWith(p, StringComparison.Ordinal);
    }

    // Patron con '*' en la PRIMERA etiqueta y el resto fijo. El host debe tener exactamente
    // la misma cantidad de etiquetas: el '*' nunca puede abarcar un punto.
    private static bool LabelGlobMatches(string pattern, string host)
    {
        int pDot = pattern.IndexOf('.');
        int hDot = host.IndexOf('.');
        if (pDot <= 0 || hDot <= 0) return false;

        var pRest = pattern.AsSpan(pDot + 1);
        if (pRest.IndexOf('*') >= 0) return false;          // solo la primera etiqueta admite '*'
        if (!host.AsSpan(hDot + 1).SequenceEqual(pRest)) return false;

        return GlobMatch(pattern.AsSpan(0, pDot), host.AsSpan(0, hDot));
    }

    // '*' = cualquier secuencia (incluso vacia) de caracteres dentro de UNA etiqueta.
    private static bool GlobMatch(ReadOnlySpan<char> glob, ReadOnlySpan<char> text)
    {
        int g = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (g < glob.Length && glob[g] == '*') { star = g++; mark = t; }
            else if (g < glob.Length && glob[g] == text[t]) { g++; t++; }
            else if (star >= 0) { g = star + 1; t = ++mark; }
            else return false;
        }
        while (g < glob.Length && glob[g] == '*') g++;
        return g == glob.Length;
    }

    /// <summary>
    /// Valida que un patron de lista blanca sea seguro de agregar.
    /// Rechaza entradas peligrosas o malformadas que, por error del tutor,
    /// abririan demasiado:
    ///   - vacio o solo espacios
    ///   - un unico label o un TLD suelto ("com", "localhost") -> habilitaria medio internet
    ///   - comodin fuera de lugar: en otra etiqueta que no sea la primera ("a.*.com"),
    ///     combinado con "*." / "." / "=", sobre un dominio de un solo nivel ("*google.com"),
    ///     o una etiqueta que sea solo comodines ("**.x.com")
    ///   - caracteres invalidos (espacios, '/', ':', '@', etc.)
    ///   - labels vacios ("a..b.com", ".", "..") o que empiezan/terminan en '-'
    /// Acepta: "ejemplo.com", "*.ejemplo.com", ".ejemplo.com", "=www.ejemplo.com",
    ///         "sub.ejemplo.co.il", "lh*.ejemplo.com", "*-docs.ejemplo.com".
    /// </summary>
    public static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        var raw = pattern.Trim().ToLowerInvariant();
        if (raw.EndsWith(".", StringComparison.Ordinal)) raw = raw[..^1];

        bool exact = false, prefixed = false;
        string p;
        if (raw.StartsWith("=", StringComparison.Ordinal)) { exact = true; p = raw[1..]; }
        else if (raw.StartsWith("*.", StringComparison.Ordinal)) { prefixed = true; p = raw[2..]; }
        else if (raw.StartsWith(".", StringComparison.Ordinal)) { prefixed = true; p = raw[1..]; }
        else p = raw;

        if (p.Length == 0) return false;

        // Debe tener al menos dos labels no vacios (evita TLD/single-label como "com").
        var labels = p.Split('.');
        if (labels.Length < 2) return false;

        for (int i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (label.Length == 0) return false;        // no "..", ni bordes vacios

            bool hasStar = label.IndexOf('*') >= 0;
            if (hasStar)
            {
                // Comodin de etiqueta: solo en la primera, escrito "a secas" (sin "=", "*." ni "."),
                // con al menos un caracter fijo y dejando al menos dos etiquetas fijas detras
                // ("lh*.google.com" si; "*google.com" no, porque abarcaria todo un TLD).
                if (i != 0 || exact || prefixed) return false;
                if (label.Replace("*", string.Empty).Length == 0) return false;
                if (labels.Length < 3) return false;
            }

            // Caracteres permitidos: letras, digitos, '-' (y '*' solo en el caso de arriba).
            foreach (char c in label)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || (hasStar && c == '*');
                if (!ok) return false;
            }

            if (label[0] == '-' || label[^1] == '-') return false;
        }

        return true;
    }
}
