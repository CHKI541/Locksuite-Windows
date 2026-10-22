using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LockSuite.Core;

/// <summary>Un grupo de dominios con un nombre legible (ej. "Gmail", "Mercado Pago").</summary>
public sealed class DomainPack
{
    /// <summary>
    /// Identificador estable de los paquetes de fabrica ("gmail", "google-drive", ...).
    /// Null en los paquetes creados por el tutor. Lo usa la sincronizacion de presets
    /// (<see cref="PresetSync"/>) para actualizar los paquetes de fabrica sin depender del nombre.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>True si es un preset de fabrica (no se puede borrar, solo desactivar).</summary>
    [JsonPropertyName("builtIn")]
    public bool BuiltIn { get; set; }

    /// <summary>
    /// True si es un paquete de BLOQUEO: mientras este activo, sus dominios quedan bloqueados
    /// aunque otro paquete los permita (el bloqueo tiene prioridad). Ej.: el paquete de fabrica
    /// "Bloqueo fijo: buscador de Google y Gemini".
    /// </summary>
    [JsonPropertyName("blocks")]
    public bool Blocks { get; set; }

    [JsonPropertyName("domains")]
    public List<string> Domains { get; set; } = new();

    /// <summary>
    /// Solo paquetes de fabrica: dominios del preset que el tutor quito a mano.
    /// La sincronizacion de presets respeta esta lista y no los vuelve a agregar.
    /// </summary>
    [JsonPropertyName("excluded")]
    public List<string> Excluded { get; set; } = new();
}

/// <summary>
/// Configuracion completa de la lista blanca. Se serializa a JSON.
/// El hash de la contrasena NO vive aca: lo maneja el servicio aparte,
/// cifrado con DPAPI. Este objeto es solo la politica de dominios.
/// </summary>
public sealed class WhitelistConfig
{
    /// <summary>Version del formato. 2 = paquetes con id/blocks/excluded (sesion 9).</summary>
    public const int CurrentVersion = 2;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Si true y el servicio no puede aplicar los filtros, la red queda cortada (fail-closed).</summary>
    [JsonPropertyName("failClosed")]
    public bool FailClosed { get; set; } = true;

    /// <summary>Registrar los dominios bloqueados para el "modo aprendizaje" de la interfaz.</summary>
    [JsonPropertyName("learningMode")]
    public bool LearningMode { get; set; } = true;

    [JsonPropertyName("packs")]
    public List<DomainPack> Packs { get; set; } = new();

    /// <summary>Patrones de la lista blanca activos (paquetes habilitados que permiten), aplanados y sin duplicados.</summary>
    public IReadOnlyList<string> ActivePatterns() => Flatten(p => p.Enabled && !p.Blocks);

    /// <summary>Patrones de bloqueo activos (paquetes de bloqueo habilitados). Tienen prioridad sobre la lista blanca.</summary>
    public IReadOnlyList<string> ActiveBlockPatterns() => Flatten(p => p.Enabled && p.Blocks);

    /// <summary>Atajo: decide si un host esta permitido con la config actual (lista blanca menos bloqueos).</summary>
    public bool IsAllowed(string? host) => DomainMatcher.IsAllowed(ActivePatterns(), host, ActiveBlockPatterns());

    /// <summary>True si el host cae en un bloqueo activo (queda bloqueado aunque algun paquete lo permita).</summary>
    public bool IsHardBlocked(string? host) => DomainMatcher.MatchesAny(ActiveBlockPatterns(), host);

    private List<string> Flatten(System.Func<DomainPack, bool> filter) =>
        (Packs ?? new List<DomainPack>())
             .Where(p => p is not null && filter(p))
             .SelectMany(p => p.Domains ?? new List<string>())
             .Where(d => d is not null)
             .Select(d => d.Trim().ToLowerInvariant())
             .Where(d => d.Length > 0)
             .Distinct()
             .ToList();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static WhitelistConfig FromJson(string json) =>
        JsonSerializer.Deserialize<WhitelistConfig>(json) ?? new WhitelistConfig();
}
