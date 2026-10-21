using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LockSuite.Core;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Learning;

/// <summary>
/// Mantiene y persiste los dominios bloqueados recientes para el "modo aprendizaje"
/// de la GUI, permitiendo que el tutor apruebe dominios con un solo clic.
/// </summary>
public sealed class LearningManager
{
    private const int MaxEntries = 200;
    private readonly string _logFilePath;
    private readonly ILogger<LearningManager> _logger;
    private readonly object _lock = new();
    private readonly List<BlockedDomainEvent> _events = new();

    public LearningManager(ILogger<LearningManager> logger, string? customLogPath = null)
    {
        _logger = logger;
        if (!string.IsNullOrEmpty(customLogPath))
        {
            _logFilePath = customLogPath;
        }
        else
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            _logFilePath = Path.Combine(programData, "LockSuite", "logs", "recent_blocked.json");
        }

        LoadFromDisk();
    }

    public void RecordBlocked(string domain, string? clientIp = null)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        domain = domain.Trim().ToLowerInvariant();

        lock (_lock)
        {
            var existing = _events.Find(e => string.Equals(e.Domain, domain, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Timestamp = DateTimeOffset.UtcNow;
                existing.Count++;
                if (!string.IsNullOrEmpty(clientIp)) existing.ClientIp = clientIp;
            }
            else
            {
                if (_events.Count >= MaxEntries)
                {
                    _events.RemoveAt(_events.Count - 1);
                }

                _events.Insert(0, new BlockedDomainEvent
                {
                    Domain = domain,
                    Timestamp = DateTimeOffset.UtcNow,
                    ClientIp = clientIp ?? "127.0.0.1",
                    Count = 1
                });
            }

            SaveToDisk();
        }
    }

    public IReadOnlyList<BlockedDomainEvent> GetRecent(int count = 100)
    {
        lock (_lock)
        {
            return _events.OrderByDescending(e => e.Timestamp).Take(count).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
            SaveToDisk();
        }
        _logger.LogInformation("Historial de dominios bloqueados vaciado");
    }

    private void LoadFromDisk()
    {
        try
        {
            if (File.Exists(_logFilePath))
            {
                string json = File.ReadAllText(_logFilePath);
                var items = JsonSerializer.Deserialize<List<BlockedDomainEvent>>(json);
                if (items is not null)
                {
                    _events.AddRange(items.Take(MaxEntries));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo leer el archivo de bloqueados recientes en {Path}", _logFilePath);
        }
    }

    private void SaveToDisk()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(_events, new JsonSerializerOptions { WriteIndented = true });
            string temp = _logFilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _logFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al guardar bloqueados recientes en {Path}", _logFilePath);
        }
    }
}
