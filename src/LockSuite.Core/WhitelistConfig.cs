using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LockSuite.Core;

/// <summary>Un grupo de dominios con un nombre legible (ej. "Google", "Mercado Pago").</summary>
public sealed class DomainPack
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>True si es un preset de fabrica (no se puede borrar, solo desactivar).</summary>
    [JsonPropertyName("builtIn")]
    public bool BuiltIn { get; set; }

    [JsonPropertyName("domains")]
    public List<string> Domains { get; set; } = new();
}

/// <summary>
/// Configuracion completa de la lista blanca. Se serializa a JSON.
/// El hash de la contrasena NO vive aca: lo maneja el servicio aparte,
/// cifrado con DPAPI. Este objeto es solo la politica de dominios.
/// </summary>
public sealed class WhitelistConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Si true y el servicio no puede aplicar los filtros, la red queda cortada (fail-closed).</summary>
    [JsonPropertyName("failClosed")]
    public bool FailClosed { get; set; } = true;

    /// <summary>Registrar los dominios bloqueados para el "modo aprendizaje" de la interfaz.</summary>
    [JsonPropertyName("learningMode")]
    public bool LearningMode { get; set; } = true;

    [JsonPropertyName("packs")]
    public List<DomainPack> Packs { get; set; } = new();

    /// <summary>Todos los patrones de dominio activos, aplanados y sin duplicados.</summary>
    public IReadOnlyList<string> ActivePatterns() =>
        Packs.Where(p => p.Enabled)
             .SelectMany(p => p.Domains)
             .Select(d => d.Trim().ToLowerInvariant())
             .Where(d => d.Length > 0)
             .Distinct()
             .ToList();

    /// <summary>Atajo: decide si un host esta permitido con la config actual.</summary>
    public bool IsAllowed(string? host) => DomainMatcher.IsAllowed(ActivePatterns(), host);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static WhitelistConfig FromJson(string json) =>
        JsonSerializer.Deserialize<WhitelistConfig>(json) ?? new WhitelistConfig();
}
