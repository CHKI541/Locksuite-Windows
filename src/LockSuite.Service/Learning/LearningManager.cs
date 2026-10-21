using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using LockSuite.Core;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Learning;

/// <summary>
/// Mantiene y persiste los dominios bloqueados recientes para el "modo aprendizaje"
/// de la GUI, permitiendo que el tutor apruebe dominios con un solo clic.
///
/// Rendimiento: RecordBlocked se invoca por cada consulta bloqueada (puede ser una rafaga,
/// p.ej. una pagina con muchos trackers). Para no escribir el JSON a disco en cada evento,
/// se actualiza la lista en memoria al instante y se marca "sucio"; un temporizador vuelca a
/// disco a lo sumo cada pocos segundos, y tambien al vaciar o al apagar el servicio.
/// </summary>
public sealed class LearningManager : IDisposable
{
    private const int MaxEntries = 200;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly string _logFilePath;
    private readonly ILogger<LearningManager> _logger;
    private readonly object _lock = new();
    private readonly List<BlockedDomainEvent> _events = new();
    private readonly Timer _flushTimer;
    private volatile bool _dirty;
    private bool _disposed;

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
        _flushTimer = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);
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

            _dirty = true; // Se vuelca a disco en el proximo tick del temporizador.
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
            _dirty = false;
            SaveToDiskNoLock();
        }
        _logger.LogInformation("Historial de dominios bloqueados vaciado");
    }

    /// <summary>Vuelca a disco si hay cambios pendientes. Idempotente y barato si no hay nada sucio.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (!_dirty) return;
            SaveToDiskNoLock();
            _dirty = false;
        }
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

    private void SaveToDiskNoLock()
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _flushTimer.Dispose(); } catch { }
        Flush();
    }
}
