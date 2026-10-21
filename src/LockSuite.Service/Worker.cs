using System;
using System.Threading;
using System.Threading.Tasks;
using LockSuite.Service.Config;
using LockSuite.Service.Dns;
using LockSuite.Service.Ipc;
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
    private readonly bool _testWfpMode;

    public Worker(
        ILogger<Worker> logger,
        ConfigManager configManager,
        IWfpManager wfpManager,
        DnsResolver dnsResolver,
        DnsConfigurator dnsConfigurator,
        NamedPipeServer pipeServer,
        UpdateManager updateManager,
        bool testWfpMode = false)
    {
        _logger = logger;
        _configManager = configManager;
        _wfpManager = wfpManager;
        _dnsResolver = dnsResolver;
        _dnsConfigurator = dnsConfigurator;
        _pipeServer = pipeServer;
        _updateManager = updateManager;
        _testWfpMode = testWfpMode;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("=================================================");
        _logger.LogInformation("  Iniciando LockSuite Windows Service            ");
        _logger.LogInformation("=================================================");

        // 1. Inicializar WFP (default-deny). No lanza: ante error pasa a simulación.
        bool failClosed = _configManager.CurrentConfig.FailClosed;
        _wfpManager.Initialize(failClosed);

        // 2. Iniciar resolutor DNS local (127.0.0.1:53). No lanza: si no puede enlazar,
        //    queda IsListening=false y el servicio sigue en pie (la red sigue bloqueada).
        _dnsResolver.Start();

        // 3. Aplicar redirección DNS del sistema hacia 127.0.0.1
        _dnsConfigurator.ApplyNrptRedirect();

        // 4. Iniciar servidor IPC (Named Pipe para la GUI)
        _pipeServer.Start();

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LockSuite Windows Service está en ejecución y filtrando.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10_000, stoppingToken);
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
