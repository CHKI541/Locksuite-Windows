using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using LockSuite.Core;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Config;

/// <summary>
/// Administra la carga y guardado de WhitelistConfig en disco.
/// Ruta: %ProgramData%\LockSuite\config\whitelist.json
///
/// Rendimiento y concurrencia:
///   - El resolutor DNS consulta IsAllowed() en el camino caliente, por consulta.
///     Para no recalcular LINQ (flatten+distinct) ni tomar el lock en cada consulta,
///     se mantiene un snapshot inmutable de patrones activos (_activePatterns) que se
///     reemplaza atomicamente cuando cambia la config. Los lectores (DNS) lo leen sin lock.
///   - Todas las mutaciones y la serializacion toman _lock, por lo que nunca se serializa
///     una lista que otra hebra esta mutando.
/// </summary>
public sealed class ConfigManager
{
    private readonly string _configPath;
    private readonly ILogger<ConfigManager> _logger;
    private readonly object _lock = new();
    private WhitelistConfig _currentConfig;

    // Snapshot inmutable de patrones activos; se reemplaza (no se muta) en cada cambio.
    private volatile string[] _activePatterns = Array.Empty<string>();

    public event Action<WhitelistConfig>? ConfigChanged;

    public ConfigManager(ILogger<ConfigManager> logger, string? customPath = null)
    {
        _logger = logger;

        if (!string.IsNullOrEmpty(customPath))
        {
            _configPath = customPath;
        }
        else
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string baseDir = Path.Combine(programData, "LockSuite", "config");
            _configPath = Path.Combine(baseDir, "whitelist.json");
        }

        _currentConfig = LoadOrCreateDefault();
        RebuildPatternSnapshot();
    }

    public WhitelistConfig CurrentConfig
    {
        get
        {
            lock (_lock)
            {
                return _currentConfig;
            }
        }
    }

    /// <summary>Lectura del hot path: sin lock, sobre el snapshot inmutable.</summary>
    public bool IsAllowed(string? host)
    {
        return DomainMatcher.IsAllowed(_activePatterns, host);
    }

    /// <summary>Cantidad de patrones activos (desde el snapshot, sin recalcular).</summary>
    public int ActivePatternCount => _activePatterns.Length;

    /// <summary>Estado actual del modo aprendizaje.</summary>
    public bool LearningMode
    {
        get { lock (_lock) { return _currentConfig.LearningMode; } }
    }

    /// <summary>Serializa la config de forma consistente (bajo lock).</summary>
    public string GetConfigJson()
    {
        lock (_lock)
        {
            return _currentConfig.ToJson();
        }
    }

    public void UpdateConfig(WhitelistConfig newConfig)
    {
        lock (_lock)
        {
            _currentConfig = newConfig ?? new WhitelistConfig();
            SaveToDisk(_currentConfig);
            RebuildPatternSnapshot();
        }

        _logger.LogInformation("Configuracion de lista blanca actualizada. Patrones activos: {Count}",
            _activePatterns.Length);

        ConfigChanged?.Invoke(CurrentConfig);
    }

    public void TogglePack(string packName, bool enabled)
    {
        bool changed = false;
        lock (_lock)
        {
            var pack = _currentConfig.Packs.Find(p => string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase));
            if (pack is not null)
            {
                pack.Enabled = enabled;
                SaveToDisk(_currentConfig);
                RebuildPatternSnapshot();
                changed = true;
                _logger.LogInformation("Paquete '{Pack}' {Status}", packName, enabled ? "activado" : "desactivado");
            }
        }
        if (changed) ConfigChanged?.Invoke(CurrentConfig);
    }

    /// <summary>Activa o desactiva el registro de dominios bloqueados (modo aprendizaje).</summary>
    public void SetLearningMode(bool enabled)
    {
        lock (_lock)
        {
            _currentConfig.LearningMode = enabled;
            SaveToDisk(_currentConfig);
        }
        _logger.LogInformation("Modo aprendizaje {Status}", enabled ? "activado" : "desactivado");
        ConfigChanged?.Invoke(CurrentConfig);
    }

    public bool AddDomain(string domain, string? targetPackName = null)
    {
        domain = DomainMatcher.Normalize(domain);
        if (!DomainMatcher.IsValidPattern(domain))
        {
            _logger.LogWarning("Dominio rechazado por patron invalido o peligroso: '{Domain}'", domain);
            return false;
        }

        lock (_lock)
        {
            string packName = string.IsNullOrWhiteSpace(targetPackName) ? "Personalizados" : targetPackName;
            var pack = _currentConfig.Packs.Find(p => string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase));
            if (pack is null)
            {
                pack = new DomainPack
                {
                    Name = packName,
                    BuiltIn = false,
                    Enabled = true,
                    Domains = new()
                };
                _currentConfig.Packs.Add(pack);
            }

            if (!pack.Domains.Exists(d => string.Equals(DomainMatcher.Normalize(d), domain, StringComparison.OrdinalIgnoreCase)))
            {
                pack.Domains.Add(domain);
                SaveToDisk(_currentConfig);
                RebuildPatternSnapshot();
                _logger.LogInformation("Dominio '{Domain}' agregado al paquete '{Pack}'", domain, packName);
            }
        }

        ConfigChanged?.Invoke(CurrentConfig);
        return true;
    }

    public bool RemoveDomain(string domain)
    {
        domain = DomainMatcher.Normalize(domain);
        if (string.IsNullOrWhiteSpace(domain)) return false;

        bool removedAny = false;
        lock (_lock)
        {
            foreach (var pack in _currentConfig.Packs)
            {
                int removed = pack.Domains.RemoveAll(d => string.Equals(DomainMatcher.Normalize(d), domain, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) removedAny = true;
            }

            if (removedAny)
            {
                SaveToDisk(_currentConfig);
                RebuildPatternSnapshot();
                _logger.LogInformation("Dominio '{Domain}' eliminado de la lista blanca", domain);
            }
        }

        if (removedAny)
        {
            ConfigChanged?.Invoke(CurrentConfig);
        }
        return removedAny;
    }

    /// <summary>Recalcula el snapshot inmutable de patrones. Debe llamarse con _lock tomado.</summary>
    private void RebuildPatternSnapshot()
    {
        _activePatterns = _currentConfig.ActivePatterns().ToArray();
    }

    private WhitelistConfig LoadOrCreateDefault()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                var loaded = WhitelistConfig.FromJson(json);
                _logger.LogInformation("Configuracion cargada desde {Path}. Patrones activos: {Count}",
                    _configPath, loaded.ActivePatterns().Count);
                return loaded;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al leer configuracion en {Path}. Usando valores predeterminados.", _configPath);
        }

        var defaultConfig = Presets.DefaultConfig();
        try
        {
            SaveToDisk(defaultConfig);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo guardar la configuracion inicial en {Path}", _configPath);
        }

        return defaultConfig;
    }

    private void SaveToDisk(WhitelistConfig config)
    {
        string? dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempFile = _configPath + ".tmp";
        string json = config.ToJson();
        File.WriteAllText(tempFile, json);
        File.Move(tempFile, _configPath, overwrite: true);
    }
}
