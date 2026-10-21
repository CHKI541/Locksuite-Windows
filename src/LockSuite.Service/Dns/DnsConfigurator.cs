using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Dns;

/// <summary>
/// Configura Windows para dirigir las resoluciones DNS hacia el resolutor local (127.0.0.1)
/// y permite restaurar la configuración anterior.
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
    /// Aplica una regla NRPT (Name Resolution Policy Table) en Windows para redirigir
    /// todas las consultas DNS del sistema al resolutor local 127.0.0.1.
    /// </summary>
    public bool ApplyNrptRedirect()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        try
        {
            // Primero removemos cualquier regla previa de LockSuite si quedó remanente
            RemoveNrptRedirect();

            // Comando PowerShell para crear la regla NRPT para namespace "." (todos los dominios)
            string psCommand = "Add-DnsClientNrptRule -Namespace '.' -NameServers '127.0.0.1' -Comment 'LockSuiteDNS'";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(5000);
                if (process.ExitCode == 0)
                {
                    _nrptApplied = true;
                    _logger.LogInformation("Regla NRPT aplicada: todo el tráfico DNS del sistema redirigido a 127.0.0.1");
                    return true;
                }
                else
                {
                    string err = process.StandardError.ReadToEnd();
                    _logger.LogWarning("Fallo al aplicar regla NRPT (ExitCode: {Code}): {Error}", process.ExitCode, err);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al configurar redirección NRPT de DNS");
        }

        return false;
    }

    /// <summary>
    /// Remueve la regla NRPT de LockSuite si fue aplicada.
    /// </summary>
    public void RemoveNrptRedirect()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            string psCommand = "Get-DnsClientNrptRule | Where-Object { $_.Comment -eq 'LockSuiteDNS' } | Remove-DnsClientNrptRule -Force -ErrorAction SilentlyContinue";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            _nrptApplied = false;
            _logger.LogInformation("Regla NRPT de LockSuite removida");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al remover regla NRPT");
        }
    }
}
