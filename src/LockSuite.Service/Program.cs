using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LockSuite.Core;
using LockSuite.Service.Config;
using LockSuite.Service.Dns;
using LockSuite.Service.Ipc;
using LockSuite.Service.Learning;
using LockSuite.Service.Security;
using LockSuite.Service.Trial;
using LockSuite.Service.Updates;
using LockSuite.Service.Wfp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service;

public static class Program
{
    public const string ServiceName = "LockSuiteService";
    public const string DisplayName = "LockSuite Filtering Service";
    public const string Version = "0.4.0";

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
                case "--set-password-file":
                    return SetPasswordFromFile(args);
                case "--set-trial":
                    return SetTrial(args);
                case "--trial-status":
                    return TrialStatus();
                case "--reset-wfp":
                    return ResetWfp();
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
        Console.WriteLine($"  LockSuite Service {Version} — Modo Consola      ");
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
                services.AddSingleton<UpdateManager>();
                services.AddSingleton<TrialManager>();

                services.AddHostedService(sp => new Worker(
                    sp.GetRequiredService<ILogger<Worker>>(),
                    sp.GetRequiredService<ConfigManager>(),
                    sp.GetRequiredService<IWfpManager>(),
                    sp.GetRequiredService<DnsResolver>(),
                    sp.GetRequiredService<DnsConfigurator>(),
                    sp.GetRequiredService<NamedPipeServer>(),
                    sp.GetRequiredService<UpdateManager>(),
                    sp.GetRequiredService<TrialManager>(),
                    testWfpMode: testWfpMode
                ));
            });

    private static ILoggerFactory CreateCliLoggerFactory() =>
        LoggerFactory.Create(b =>
        {
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Information);
        });

    /// <summary>
    /// Fija la contraseña maestra inicial leyéndola de un archivo (lo usa el instalador para
    /// que el tutor la defina durante la instalación, ANTES de que el usuario estándar pueda
    /// abrir la app y fijarla él mismo). El archivo se sobrescribe y borra tras leerlo.
    /// </summary>
    private static int SetPasswordFromFile(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Uso: --set-password-file <ruta-del-archivo>");
            return 2;
        }

        string path = args[1];
        string password = string.Empty;
        try
        {
            password = File.ReadAllText(path).Trim();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"No se pudo leer el archivo de contraseña: {ex.Message}");
            return 1;
        }
        finally
        {
            TryWipeAndDelete(path);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("El archivo de contraseña está vacío.");
            return 1;
        }

        using var lf = CreateCliLoggerFactory();
        var security = new SecurityManager(lf.CreateLogger<SecurityManager>());

        if (security.IsPasswordSet())
        {
            Console.WriteLine("La contraseña maestra ya estaba configurada; no se sobrescribe.");
            return 0;
        }

        bool ok = security.SetInitialPassword(password);
        Console.WriteLine(ok ? "Contraseña maestra configurada." : "No se pudo configurar la contraseña maestra.");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Configura el modo de prueba. Lo usa el instalador (y puede usarse a mano para una
    /// prueba descartable). Uso:
    ///   --set-trial &lt;minutos&gt; [prompt|autoremove] [tutorlock]
    /// Ejemplos:
    ///   --set-trial 1440 prompt           (24 h; al terminar pregunta conservar/quitar)
    ///   --set-trial 10 autoremove         (10 min; al terminar se quita solo — prueba en VM)
    ///   --set-trial 4320 prompt tutorlock (72 h; al terminar pregunta, y quitar pide contraseña del tutor)
    /// </summary>
    private static int SetTrial(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out int minutes) || minutes < 1)
        {
            Console.Error.WriteLine("Uso: --set-trial <minutos> [prompt|autoremove] [tutorlock]");
            return 2;
        }

        var action = TrialEndAction.Prompt;
        bool tutorLock = false;
        for (int i = 2; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            if (a == "autoremove") action = TrialEndAction.AutoRemove;
            else if (a == "prompt") action = TrialEndAction.Prompt;
            else if (a == "tutorlock") tutorLock = true;
        }

        using var lf = CreateCliLoggerFactory();
        var tm = new TrialManager(lf.CreateLogger<TrialManager>());
        tm.StartTrial(minutes, action, tutorLock);
        Console.WriteLine($"Modo de prueba configurado: {minutes} min, al terminar={action}, tutorLock={tutorLock}.");
        return 0;
    }

    private static int TrialStatus()
    {
        using var lf = CreateCliLoggerFactory();
        var tm = new TrialManager(lf.CreateLogger<TrialManager>());
        var s = tm.GetStatus();
        Console.WriteLine($"Enabled={s.Enabled} State={s.State} RemainingSeconds={s.RemainingSeconds} EndUtc={s.EndUtc:o} EndAction={s.EndAction} RequireTutorToRemove={s.RequireTutorToRemove}");
        return 0;
    }

    private static void TryWipeAndDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var len = new FileInfo(path).Length;
                if (len > 0) File.WriteAllBytes(path, new byte[len]);
                File.Delete(path);
            }
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Limpieza total de los artefactos de red de LockSuite (filtros WFP base + subcapa + NRPT).
    /// Pensado para el desinstalador y para recuperación manual del administrador.
    /// </summary>
    private static int ResetWfp()
    {
        using var lf = CreateCliLoggerFactory();
        try
        {
            var wfp = new WfpManager(lf.CreateLogger<WfpManager>());
            wfp.HardReset();

            var dns = new DnsConfigurator(lf.CreateLogger<DnsConfigurator>());
            dns.RemoveNrptRedirect();

            Console.WriteLine("Reset de WFP y NRPT completado.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error en --reset-wfp: {ex.Message}");
            return 1;
        }
    }

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
        int rc = RunSc(scArgs);
        if (rc != 0)
        {
            // Idempotencia (actualización/upgrade): el servicio probablemente ya existe.
            // En vez de fallar, actualizamos la ruta del binario y el modo de arranque.
            Console.WriteLine("El servicio ya existe o no se pudo crear; intentando actualizar su configuración (upgrade)...");
            RunSc($"config {ServiceName} binPath= \"\\\"{exePath}\\\"\" start= auto");
        }

        // Descripción visible en services.msc (idempotente).
        RunSc($"description {ServiceName} \"Filtro de internet por lista blanca (LockSuite). Bloquea todo el trafico saliente salvo los dominios permitidos por el tutor.\"");

        // Auto-recuperación: reiniciar el servicio ante caídas (resiliencia / anti-evasión).
        // reset= 86400s (contador de fallos se reinicia cada 24h); 3 reintentos con 5s.
        RunSc($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000");
        RunSc($"failureflag {ServiceName} 1");

        return 0;
    }

    private static int UninstallService()
    {
        Console.WriteLine($"Desinstalando servicio '{ServiceName}'...");
        RunSc($"stop {ServiceName}");
        int rc = RunSc($"delete {ServiceName}");
        // Limpieza de artefactos de red por si la parada no alcanzó a revertirlos.
        ResetWfp();
        return rc;
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
        Console.WriteLine($"LockSuite.Service {Version}");
        Console.WriteLine("Uso: LockSuite.Service.exe [opción]");
        Console.WriteLine();
        Console.WriteLine("Opciones:");
        Console.WriteLine("  --run / --console          Ejecuta el servicio en consola interactiva");
        Console.WriteLine("  --test-wfp                 Prueba WFP con auto-rollback automático en 30 segundos");
        Console.WriteLine("  --install                  Instala como Windows Service (requiere Administrador)");
        Console.WriteLine("  --uninstall                Desinstala el Windows Service (requiere Administrador)");
        Console.WriteLine("  --start                    Inicia el Windows Service");
        Console.WriteLine("  --stop                     Detiene el Windows Service");
        Console.WriteLine("  --set-password-file <ruta> Fija la contraseña maestra inicial desde un archivo");
        Console.WriteLine("  --set-trial <min> [prompt|autoremove] [tutorlock]  Configura el modo de prueba");
        Console.WriteLine("  --trial-status             Muestra el estado del modo de prueba");
        Console.WriteLine("  --reset-wfp                Remueve filtros WFP + NRPT de LockSuite (recuperación)");
        Console.WriteLine("  --help                     Muestra esta ayuda");
    }
}
