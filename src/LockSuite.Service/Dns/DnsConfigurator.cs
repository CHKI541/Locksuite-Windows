using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Dns;

/// <summary>
/// Configura Windows para dirigir las resoluciones DNS hacia el resolutor local (127.0.0.1)
/// y permite restaurar la configuración anterior. Usa reglas NRPT (Name Resolution Policy Table)
/// para el namespace "." (todos los dominios).
/// </summary>
public sealed class DnsConfigurator
{
    private readonly ILogger<DnsConfigurator> _logger;
    private bool _nrptApplied;
    public bool IsNrptApplied => _nrptApplied;

    public DnsConfigurator(ILogger<DnsConfigurator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Aplica (de forma idempotente) la regla NRPT que redirige todas las consultas DNS del
    /// sistema al resolutor local 127.0.0.1. Primero remueve cualquier regla previa de LockSuite
    /// y luego crea la nueva, todo en UNA sola invocación de PowerShell para acelerar el arranque.
    /// </summary>
    public bool ApplyNrptRedirect()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        try
        {
            string psCommand =
                "Get-DnsClientNrptRule | Where-Object { $_.Comment -eq 'LockSuiteDNS' } | Remove-DnsClientNrptRule -Force -ErrorAction SilentlyContinue; " +
                "Add-DnsClientNrptRule -Namespace '.' -NameServers '127.0.0.1' -Comment 'LockSuiteDNS'";

            int exit = RunPowerShell(psCommand, out string err);
            if (exit == 0)
            {
                _nrptApplied = true;
                _logger.LogInformation("Regla NRPT aplicada: todo el tráfico DNS del sistema redirigido a 127.0.0.1");
                return true;
            }

            _logger.LogWarning("Fallo al aplicar regla NRPT (ExitCode: {Code}): {Error}", exit, err);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al configurar redirección NRPT de DNS");
        }

        return false;
    }

    /// <summary>
    /// Vacía la caché DNS del sistema (ipconfig /flushdns). Se llama cuando el tutor permite un
    /// dominio nuevo: Windows cachea las respuestas NXDOMAIN (negativas) de los dominios bloqueados,
    /// así que sin esto el dominio recién permitido podía seguir sin abrir unos minutos (caso 3.7).
    /// </summary>
    public void FlushDnsCache()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return;
            if (!p.WaitForExit(5000)) { try { p.Kill(true); } catch { } return; }
            _ = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd();
            _logger.LogInformation("Caché DNS del sistema vaciada (flushdns).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo vaciar la caché DNS.");
        }
    }

    /// <summary>Remueve la regla NRPT de LockSuite si fue aplicada.</summary>
    public void RemoveNrptRedirect()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            string psCommand = "Get-DnsClientNrptRule | Where-Object { $_.Comment -eq 'LockSuiteDNS' } | Remove-DnsClientNrptRule -Force -ErrorAction SilentlyContinue";
            RunPowerShell(psCommand, out _);
            _nrptApplied = false;
            _logger.LogInformation("Regla NRPT de LockSuite removida");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al remover regla NRPT");
        }
    }

    private static int RunPowerShell(string command, out string stderr)
    {
        stderr = string.Empty;
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(psi);
        if (process == null) return -1;

        // Esperar primero (la salida de estos cmdlets es minima) y luego leer, para no
        // bloquear el hilo si algun stream se demora.
        if (!process.WaitForExit(8000))
        {
            try { process.Kill(true); } catch { /* best-effort */ }
            stderr = "timeout";
            return -1;
        }

        stderr = process.StandardError.ReadToEnd();
        _ = process.StandardOutput.ReadToEnd();
        return process.ExitCode;
    }
}
