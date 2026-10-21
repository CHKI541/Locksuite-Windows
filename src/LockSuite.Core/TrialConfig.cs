using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LockSuite.Core;

/// <summary>Qué hacer cuando termina el período de prueba.</summary>
public enum TrialEndAction
{
    /// <summary>Preguntar al usuario si conservar o quitar (flujo por defecto).</summary>
    Prompt = 0,
    /// <summary>Quitar automáticamente sin preguntar (prueba descartable, p. ej. 10 min en una VM).</summary>
    AutoRemove = 1
}

/// <summary>Estado derivado del período de prueba en un instante dado.</summary>
public enum TrialState
{
    /// <summary>No hay prueba activa (instalación normal/permanente).</summary>
    None = 0,
    /// <summary>Prueba en curso, aún no venció.</summary>
    Active = 1,
    /// <summary>La prueba venció y espera la decisión del usuario (conservar/quitar).</summary>
    AwaitingDecision = 2,
    /// <summary>El usuario decidió conservarlo: queda permanente.</summary>
    Kept = 3,
    /// <summary>Se decidió quitarlo: desinstalación en curso.</summary>
    Removing = 4
}

/// <summary>
/// Configuración persistida del "modo de prueba". Vive en
/// %ProgramData%\LockSuite\config\trial.json (carpeta con ACL restringida a SYSTEM/admin),
/// por lo que un usuario estándar no puede editarla; y adelantar el reloj del sistema
/// también requiere admin. El modelo es puro (sin dependencias de Windows) para poder
/// testear la lógica de tiempo en la librería Core multiplataforma.
/// </summary>
public sealed class TrialConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("startUtc")]
    public DateTimeOffset StartUtc { get; set; }

    [JsonPropertyName("durationMinutes")]
    public int DurationMinutes { get; set; }

    [JsonPropertyName("endAction")]
    public TrialEndAction EndAction { get; set; } = TrialEndAction.Prompt;

    /// <summary>Si es true, quitar al expirar pide la contraseña del tutor (opcional; por defecto false).</summary>
    [JsonPropertyName("requireTutorToRemove")]
    public bool RequireTutorToRemove { get; set; }

    /// <summary>"none" | "kept" | "removed". Decisión terminal ya tomada.</summary>
    [JsonPropertyName("decided")]
    public string Decided { get; set; } = "none";

    public DateTimeOffset EndUtc => StartUtc.AddMinutes(DurationMinutes);

    public int RemainingSeconds(DateTimeOffset now)
    {
        if (!Enabled) return 0;
        var secs = (EndUtc - now).TotalSeconds;
        if (secs < 0) secs = 0;
        return (int)Math.Round(secs);
    }

    public bool IsExpired(DateTimeOffset now) => Enabled && now >= EndUtc;

    /// <summary>Estado derivado para un instante dado.</summary>
    public TrialState GetState(DateTimeOffset now)
    {
        if (!Enabled) return TrialState.None;
        if (string.Equals(Decided, "kept", StringComparison.OrdinalIgnoreCase)) return TrialState.Kept;
        if (string.Equals(Decided, "removed", StringComparison.OrdinalIgnoreCase)) return TrialState.Removing;
        return IsExpired(now) ? TrialState.AwaitingDecision : TrialState.Active;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static TrialConfig FromJson(string json) =>
        JsonSerializer.Deserialize<TrialConfig>(json, JsonOpts) ?? new TrialConfig();

    /// <summary>Crea una config de prueba que empieza "ahora".</summary>
    public static TrialConfig StartNow(int durationMinutes, TrialEndAction endAction, bool requireTutorToRemove, DateTimeOffset now)
        => new()
        {
            Enabled = true,
            StartUtc = now,
            DurationMinutes = durationMinutes,
            EndAction = endAction,
            RequireTutorToRemove = requireTutorToRemove,
            Decided = "none"
        };
}
