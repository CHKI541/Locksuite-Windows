using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LockSuite.Service.Config;
using LockSuite.Service.Learning;
using LockSuite.Service.Wfp;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Dns;

public sealed class DnsResolver : IDisposable
{
    private readonly ILogger<DnsResolver> _logger;
    private readonly ConfigManager _configManager;
    private readonly IWfpManager _wfpManager;
    private readonly LearningManager _learningManager;

    private readonly IPEndPoint _listenEndpoint;
    private readonly List<IPEndPoint> _upstreamEndpoints = new();
    private Socket? _listenerSocket;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    // Limita las consultas concurrentes en vuelo para no crear tareas/sockets sin tope
    // ante una rafaga (back-pressure). 256 es amplio para una sola maquina.
    private readonly SemaphoreSlim _concurrency = new(256, 256);

    private const int MinTtlSeconds = 180; // Minimo 3 minutos de validez de regla WFP
    private const int UpstreamTimeoutMs = 3000;
    private const int MaxBindAttempts = 3;

    // Estadisticas: se actualizan desde multiples hebras -> Interlocked.
    private long _totalQueries;
    private long _allowedQueries;
    private long _blockedQueries;

    public long TotalQueries => Interlocked.Read(ref _totalQueries);
    public long AllowedQueries => Interlocked.Read(ref _allowedQueries);
    public long BlockedQueries => Interlocked.Read(ref _blockedQueries);

    /// <summary>True si el socket UDP esta efectivamente escuchando en 127.0.0.1:53.</summary>
    public bool IsListening { get; private set; }

    public DnsResolver(
        ILogger<DnsResolver> logger,
        ConfigManager configManager,
        IWfpManager wfpManager,
        LearningManager learningManager,
        int listenPort = 53)
    {
        _logger = logger;
        _configManager = configManager;
        _wfpManager = wfpManager;
        _learningManager = learningManager;

        _listenEndpoint = new IPEndPoint(IPAddress.Loopback, listenPort);

        // Servidores DNS upstream por defecto: Cloudflare (1.1.1.1) y Google (8.8.8.8)
        _upstreamEndpoints.Add(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53));
        _upstreamEndpoints.Add(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
    }

    /// <summary>
    /// Inicia el resolutor. No lanza excepcion si el bind falla: reintenta y, de no poder,
    /// deja el servicio en pie con IsListening=false (el estado WFP sigue siendo fail-closed).
    /// Asi el servicio "arranca" siempre y la GUI puede reportar el problema al tutor.
    /// </summary>
    public void Start()
    {
        if (_listenerSocket != null) return;

        for (int attempt = 1; attempt <= MaxBindAttempts; attempt++)
        {
            Socket? socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                DisableUdpConnReset(socket);
                socket.Bind(_listenEndpoint);

                _listenerSocket = socket;
                _cts = new CancellationTokenSource();
                _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
                IsListening = true;
                _logger.LogInformation("Resolutor DNS local iniciado y escuchando en {Endpoint}", _listenEndpoint);
                return;
            }
            catch (Exception ex)
            {
                try { socket?.Dispose(); } catch { /* best-effort */ }
                _logger.LogError(ex, "Fallo al enlazar el resolutor DNS en {Endpoint} (intento {Attempt}/{Max})",
                    _listenEndpoint, attempt, MaxBindAttempts);
                if (attempt < MaxBindAttempts)
                {
                    Thread.Sleep(1000);
                }
            }
        }

        IsListening = false;
        _logger.LogCritical(
            "No se pudo iniciar el resolutor DNS local en {Endpoint}. " +
            "El filtrado por dominio no funcionara hasta resolver esto (¿puerto 53 ocupado por otro servicio DNS?). " +
            "La red permanece bloqueada por WFP (fail-closed).", _listenEndpoint);
    }

    /// <summary>
    /// En Windows, si se le responde a un cliente UDP que ya cerro su puerto, llega un ICMP
    /// "puerto inalcanzable" y el SIGUIENTE ReceiveFrom del socket de escucha falla con 10054
    /// (WSAECONNRESET). Es inofensivo, pero ensuciaba el registro de eventos cada pocos segundos.
    /// SIO_UDP_CONNRESET = false lo desactiva.
    /// </summary>
    private static void DisableUdpConnReset(Socket socket)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            const int SIO_UDP_CONNRESET = -1744830452;
            socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
        }
        catch
        {
            // best-effort: si falla, el bucle de escucha ya tolera el error.
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listenerSocket?.Close();
        }
        catch { }
        _listenerSocket = null;
        IsListening = false;
        _logger.LogInformation("Resolutor DNS local detenido");
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        byte[] buffer = new byte[4096];
        EndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);

        while (!token.IsCancellationRequested && _listenerSocket != null)
        {
            try
            {
                var result = await _listenerSocket.ReceiveFromAsync(
                    buffer,
                    SocketFlags.None,
                    remoteEndpoint,
                    token);

                int bytesRead = result.ReceivedBytes;
                var clientEp = result.RemoteEndPoint;

                byte[] queryData = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, queryData, 0, bytesRead);

                // Back-pressure: espera un cupo antes de procesar; limita tareas/sockets en vuelo.
                await _concurrency.WaitAsync(token);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessQueryAsync(queryData, clientEp, token);
                    }
                    finally
                    {
                        _concurrency.Release();
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Error al recibir consulta DNS");
                    // Evita un bucle caliente (CPU/bateria) si el error se repite sin parar.
                    try { await Task.Delay(50, token); } catch (OperationCanceledException) { break; }
                }
            }
        }
    }

    private async Task ProcessQueryAsync(byte[] queryData, EndPoint clientEndpoint, CancellationToken token)
    {
        Interlocked.Increment(ref _totalQueries);
        var packet = DnsPacket.Parse(queryData, queryData.Length);
        if (packet == null || string.IsNullOrWhiteSpace(packet.QuestionName))
        {
            return;
        }

        string domain = packet.QuestionName;
        bool isAllowed = _configManager.IsAllowed(domain);

        if (isAllowed)
        {
            Interlocked.Increment(ref _allowedQueries);
            _logger.LogDebug("Consulta permitida: {Domain}", domain);

            byte[]? upstreamResponse = await QueryUpstreamAsync(queryData, token);
            if (upstreamResponse != null && upstreamResponse.Length > 0)
            {
                // Extraer IPs resueltas y abrir reglas en WFP
                var responsePacket = DnsPacket.Parse(upstreamResponse, upstreamResponse.Length);
                if (responsePacket != null)
                {
                    foreach (var ans in responsePacket.Answers)
                    {
                        if (ans.IpAddress != null)
                        {
                            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(ans.Ttl, MinTtlSeconds));
                            _wfpManager.AllowIpTemporary(ans.IpAddress, duration);
                            _logger.LogInformation("Regla dinámica autorizada para IP {IP} ({Domain}), TTL: {Sec}s",
                                ans.IpAddress, domain, (int)duration.TotalSeconds);
                        }
                    }
                }

                // Enviar respuesta al cliente
                await SendResponseAsync(upstreamResponse, clientEndpoint);
            }
            else
            {
                // Fallback si upstream no responde
                byte[] nxResponse = DnsPacket.CreateNxDomainResponse(queryData, queryData.Length);
                await SendResponseAsync(nxResponse, clientEndpoint);
            }
        }
        else
        {
            Interlocked.Increment(ref _blockedQueries);
            _logger.LogInformation("BLOQUEADO: {Domain} desde {Client}", domain, clientEndpoint);

            // Registrar en modo aprendizaje
            if (_configManager.LearningMode)
            {
                string clientIp = clientEndpoint is IPEndPoint ipEp ? ipEp.Address.ToString() : "127.0.0.1";
                _learningManager.RecordBlocked(domain, clientIp);
            }

            // Responder NXDOMAIN
            byte[] nxResponse = DnsPacket.CreateNxDomainResponse(queryData, queryData.Length);
            await SendResponseAsync(nxResponse, clientEndpoint);
        }
    }

    private async Task<byte[]?> QueryUpstreamAsync(byte[] queryData, CancellationToken token)
    {
        foreach (var upstream in _upstreamEndpoints)
        {
            try
            {
                using var upstreamSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                await upstreamSocket.SendToAsync(queryData, SocketFlags.None, upstream);

                byte[] respBuffer = new byte[4096];
                using var ctsTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                ctsTimeout.CancelAfter(UpstreamTimeoutMs);

                EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                var result = await upstreamSocket.ReceiveFromAsync(respBuffer, SocketFlags.None, remoteEp, ctsTimeout.Token);

                byte[] response = new byte[result.ReceivedBytes];
                Buffer.BlockCopy(respBuffer, 0, response, 0, result.ReceivedBytes);
                return response;
            }
            catch
            {
                // Intenta con el siguiente upstream
            }
        }
        return null;
    }

    private async Task SendResponseAsync(byte[] responseData, EndPoint clientEndpoint)
    {
        try
        {
            var sock = _listenerSocket;
            if (sock != null && responseData.Length > 0)
            {
                await sock.SendToAsync(responseData, SocketFlags.None, clientEndpoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al enviar respuesta DNS al cliente {Endpoint}", clientEndpoint);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _concurrency.Dispose();
    }
}
