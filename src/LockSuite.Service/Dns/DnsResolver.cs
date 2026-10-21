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

    private const int MinTtlSeconds = 180; // Minimo 3 minutos de validez de regla WFP
    private const int UpstreamTimeoutMs = 3000;

    public long TotalQueries { get; private set; }
    public long AllowedQueries { get; private set; }
    public long BlockedQueries { get; private set; }

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

    public void Start()
    {
        if (_listenerSocket != null) return;

        try
        {
            _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _listenerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listenerSocket.Bind(_listenEndpoint);

            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
            _logger.LogInformation("Resolutor DNS local iniciado y escuchando en {Endpoint}", _listenEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al iniciar el resolutor DNS local en {Endpoint}", _listenEndpoint);
            throw;
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

                _ = Task.Run(() => ProcessQueryAsync(queryData, clientEp, token), token);
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
                }
            }
        }
    }

    private async Task ProcessQueryAsync(byte[] queryData, EndPoint clientEndpoint, CancellationToken token)
    {
        TotalQueries++;
        var packet = DnsPacket.Parse(queryData, queryData.Length);
        if (packet == null || string.IsNullOrWhiteSpace(packet.QuestionName))
        {
            return;
        }

        string domain = packet.QuestionName;
        bool isAllowed = _configManager.IsAllowed(domain);

        if (isAllowed)
        {
            AllowedQueries++;
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
            BlockedQueries++;
            _logger.LogInformation("BLOQUEADO: {Domain} desde {Client}", domain, clientEndpoint);

            // Registrar en modo aprendizaje
            if (_configManager.CurrentConfig.LearningMode)
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
                upstreamSocket.ReceiveTimeout = UpstreamTimeoutMs;
                upstreamSocket.SendTimeout = UpstreamTimeoutMs;

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
            if (_listenerSocket != null && responseData.Length > 0)
            {
                await _listenerSocket.SendToAsync(responseData, SocketFlags.None, clientEndpoint);
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
    }
}
