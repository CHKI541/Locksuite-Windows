using System;
using System.Threading;
using System.Threading.Tasks;
using LockSuite.Service.Config;
using LockSuite.Service.Dns;
using LockSuite.Service.Ipc;
using LockSuite.Service.Security;
using LockSuite.Service.Trial;
using LockSuite.Service.Updates;
using LockSuite.Service.Wfp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ConfigManager _configManager;
    private readonly IWfpManager _wfpManager;
    private readonly DnsResolver _dnsResolver;
    private readonly DnsConfigurator _dnsConfigurator;
    private readonly NamedPipeServer _pipeServer;
    private readonly UpdateManager _updateManager;
    private readonly TrialManager _trialManager;
    private readonly bool _testWfpMode;

    public Worker(
        ILogger<Worker> logger,
        ConfigManager configManager,
        IWfpManager wfpManager,
        DnsResolver dnsResolver,
        DnsConfigurator dnsConfigurator,
        NamedPipeServer pipeServer,
        UpdateManager updateManager,
        TrialManager trialManager,
        bool testWfpMode = false)
    {
        _logger = logger;
        _configManager = configManager;
        _wfpManager = wfpManager;
        _dnsResolver = dnsResolver;
        _dnsConfigurator = dnsConfigurator;
        _pipeServer = pipeServer;
        _updateManager = updateManager;
        _trialManager = trialManager;
        _testWfpMode = testWfpMode;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("=================================================");
        _logger.LogInformation("  Iniciando LockSuite Windows Service            ");
        _logger.LogInformation("=================================================");

        // 0. Endurecer permisos NTFS de %ProgramData%\LockSuite (remover herencia de Users)
        AclHelper.SecureProgramDataDirectory();

        // 1. Inicializar WFP (default-deny). No lanza: ante error pasa a simulación.
        var cfg = _configManager.CurrentConfig;
        _wfpManager.Initialize(cfg.FailClosed, cfg.AllowLocalNetwork);

        // Al cambiar la config: aplicar en vivo el permiso de red local y vaciar la caché DNS
        // (para que un dominio recién permitido abra enseguida, sin arrastrar el NXDOMAIN cacheado).
        _configManager.ConfigChanged += OnConfigChanged;

        // 2. Iniciar resolutor DNS local (127.0.0.1:53). No lanza: si no puede enlazar,
        //    queda IsListening=false y el servicio sigue en pie (la red sigue bloqueada).
        _dnsResolver.Start();

        // 3. Iniciar servidor IPC (Named Pipe para la GUI) ANTES de tocar NRPT: aplicar la regla
        //    llama a PowerShell y puede demorar varios segundos; asi la ventana puede conectarse
        //    de inmediato en vez de esperar a que termine.
        _pipeServer.Start();

        // 4. Aplicar redirección DNS del sistema hacia 127.0.0.1 SOLO si el resolutor local
        //    realmente esta escuchando. Si el puerto 53 estaba ocupado (Docker, ICS, WSL...) y se
        //    redirigiera igual, TODA la resolucion DNS del equipo quedaria apuntando a un puerto
        //    muerto y la PC se quedaria sin internet. En ese caso se limpia cualquier regla vieja.
        if (_dnsResolver.IsListening)
        {
            _dnsConfigurator.ApplyNrptRedirect();
        }
        else
        {
            _logger.LogError("El resolutor DNS local no esta escuchando: NO se redirige el DNS del sistema (se evita cortar el internet).");
            _dnsConfigurator.RemoveNrptRedirect();
        }

        // 5. Iniciar verificador de actualizaciones automáticas
        _updateManager.StartPeriodicChecks();

        // 6. Si está en modo test WFP, activar auto-rollback
        if (_testWfpMode)
        {
            _logger.LogWarning("MODO TEST WFP ACTIVO: Se iniciará auto-rollback en 30 segundos.");
            _wfpManager.StartAutoRollback(TimeSpan.FromSeconds(30), () =>
            {
                _dnsConfigurator.RemoveNrptRedirect();
            });
        }

        return base.StartAsync(cancellationToken);
    }

    private void OnConfigChanged(LockSuite.Core.WhitelistConfig cfg)
    {
        try { _wfpManager.SetAllowLocalNetwork(cfg.AllowLocalNetwork); } catch (Exception ex) { _logger.LogWarning(ex, "No se pudo aplicar el permiso de red local."); }
        // Un cambio de lista casi siempre permite algo nuevo: vaciar la caché DNS para que abra ya.
        _dnsConfigurator.FlushDnsCache();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LockSuite Windows Service está en ejecución y filtrando.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10_000, stoppingToken);

                // Modo de prueba descartable: si venció y está en AutoRemove, desinstala solo.
                // (El flujo Prompt NO borra nada acá: espera la decisión del usuario desde la GUI.)
                _trialManager.CheckAutoRemove();

                _logger.LogDebug("Estado LockSuite: Reglas dinámicas={Rules}, Consultas={Queries} (Permitidas={Allowed}, Bloqueadas={Blocked}), DNS escuchando={Dns}",
                    _wfpManager.ActiveDynamicRulesCount,
                    _dnsResolver.TotalQueries,
                    _dnsResolver.AllowedQueries,
                    _dnsResolver.BlockedQueries,
                    _dnsResolver.IsListening);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deteniendo LockSuite Windows Service (parada ordenada)...");

        _configManager.ConfigChanged -= OnConfigChanged;

        // 1. Detener IPC
        _pipeServer.Stop();

        // 2. Detener actualizador
        _updateManager.Dispose();

        // 3. Restaurar DNS del sistema
        _dnsConfigurator.RemoveNrptRedirect();

        // 4. Detener resolutor DNS
        _dnsResolver.Stop();

        // 5. Revertir filtros WFP.
        //    Una parada ORDENADA (sc stop / apagado / desinstalación) es una acción
        //    intencional del administrador: restauramos la conectividad para no dejar
        //    la máquina sin red. El escenario fail-closed corresponde a una CAÍDA del
        //    proceso (que NO invoca StopAsync); allí, con FailClosed=true, los filtros
        //    de la sesión no dinámica persisten y la red queda bloqueada hasta el reinicio.
        _wfpManager.Rollback();

        _logger.LogInformation("LockSuite Windows Service detenido correctamente. Conectividad restaurada.");
        return base.StopAsync(cancellationToken);
    }
}
