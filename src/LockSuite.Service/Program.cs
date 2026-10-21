using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LockSuite.Service.Config;
using LockSuite.Service.Dns;
using LockSuite.Service.Ipc;
using LockSuite.Service.Learning;
using LockSuite.Service.Security;
using LockSuite.Service.Wfp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service;

public static class Program
{
    public const string ServiceName = "LockSuiteService";
    public const string DisplayName = "LockSuite Filtering Service";

    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            string cmd = args[0].ToLowerInvariant();
            switch (cmd)
            {
                case "--install":
                    return InstallService();
                case "--uninstall":
                    return UninstallService();
                case "--start":
                    return StartServiceCommand();
                case "--stop":
                    return StopServiceCommand();
                case "--run":
                case "--console":
                case "--test-wfp":
                    return RunConsole(args);
                case "--help":
                case "-h":
                    PrintHelp();
                    return 0;
            }
        }

        // Ejecución como servicio de Windows (o fallback a consola)
        try
        {
            CreateHostBuilder(args, testWfpMode: false).Build().Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error fatal en el servicio: {ex.Message}");
            return 1;
        }
    }

    private static int RunConsole(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("  LockSuite Service — Modo Interactivo / Consola ");
        Console.WriteLine("==================================================");

        bool testWfp = args.Contains("--test-wfp", StringComparer.OrdinalIgnoreCase);
        if (testWfp)
        {
            Console.WriteLine("AVISO: Modo --test-wfp activo con auto-rollback en 30s.");
        }

        try
        {
            CreateHostBuilder(args, testWfpMode: testWfp).Build().Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error en ejecución de consola: {ex.Message}");
            return 1;
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args, bool testWfpMode = false) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = ServiceName;
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                if (OperatingSystem.IsWindows())
                {
                    logging.AddEventLog(settings =>
                    {
                        settings.SourceName = ServiceName;
                    });
                }
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton<ConfigManager>();
                services.AddSingleton<SecurityManager>();
                services.AddSingleton<IWfpManager, WfpManager>();
                services.AddSingleton<LearningManager>();
                services.AddSingleton<DnsResolver>();
                services.AddSingleton<DnsConfigurator>();
                services.AddSingleton<NamedPipeServer>();

                services.AddHostedService(sp => new Worker(
                    sp.GetRequiredService<ILogger<Worker>>(),
                    sp.GetRequiredService<ConfigManager>(),
                    sp.GetRequiredService<IWfpManager>(),
                    sp.GetRequiredService<DnsResolver>(),
                    sp.GetRequiredService<DnsConfigurator>(),
                    sp.GetRequiredService<NamedPipeServer>(),
                    testWfpMode: testWfpMode
                ));
            });

    private static int InstallService()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Console.Error.WriteLine("No se pudo determinar la ruta del ejecutable.");
            return 1;
        }

        Console.WriteLine($"Instalando servicio '{ServiceName}'...");
        string scArgs = $"create {ServiceName} binPath= \"\\\"{exePath}\\\"\" start= auto DisplayName= \"{DisplayName}\"";
        return RunSc(scArgs);
    }

    private static int UninstallService()
    {
        Console.WriteLine($"Desinstalando servicio '{ServiceName}'...");
        RunSc($"stop {ServiceName}");
        return RunSc($"delete {ServiceName}");
    }

    private static int StartServiceCommand()
    {
        Console.WriteLine($"Iniciando servicio '{ServiceName}'...");
        return RunSc($"start {ServiceName}");
    }

    private static int StopServiceCommand()
    {
        Console.WriteLine($"Deteniendo servicio '{ServiceName}'...");
        return RunSc($"stop {ServiceName}");
    }

    private static int RunSc(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return 1;
            proc.WaitForExit();
            Console.WriteLine(proc.StandardOutput.ReadToEnd());
            string err = proc.StandardError.ReadToEnd();
            if (!string.IsNullOrEmpty(err)) Console.Error.WriteLine(err);
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error al invocar sc.exe: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Uso de LockSuite.Service:");
        Console.WriteLine("  LockSuite.Service.exe [opción]");
        Console.WriteLine();
        Console.WriteLine("Opciones:");
        Console.WriteLine("  --run / --console  Ejecuta el servicio en consola interactiva");
        Console.WriteLine("  --test-wfp         Prueba WFP con auto-rollback automático en 30 segundos");
        Console.WriteLine("  --install          Instala como Windows Service (requiere Administrador)");
        Console.WriteLine("  --uninstall        Desinstala el Windows Service (requiere Administrador)");
        Console.WriteLine("  --start            Inicia el Windows Service");
        Console.WriteLine("  --stop             Detiene el Windows Service");
        Console.WriteLine("  --help             Muestra esta ayuda");
    }
}
