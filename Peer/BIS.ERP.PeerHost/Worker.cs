using System.Net.WebSockets;
using BIS.ERP.PeerProtocol;

namespace BIS.ERP.PeerHost;

public sealed class Worker(ILogger<Worker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var node = configuration["Peer:NodeId"] ?? "A";
        if (node is not ("A" or "B"))
            throw new InvalidOperationException("Peer:NodeId must be A or B.");
        var target = node == "A" ? "B" : "A";
        var relay = new Uri(configuration["Peer:RelayUrl"] ?? "ws://127.0.0.1:5080/peer");
        if (relay.Scheme != "ws" || !relay.IsLoopback)
            throw new InvalidOperationException("This test supports only loopback ws connections.");
        while (!stoppingToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            Task? receiver = null;
            try
            {
                await socket.ConnectAsync(new UriBuilder(relay) { Query = $"node={node}" }.Uri, stoppingToken);
                logger.LogInformation("Node {Node} connected", node);
                using var sendLock = new SemaphoreSlim(1, 1);
                async Task SendAsync(PeerMessage message)
                {
                    await sendLock.WaitAsync(session.Token);
                    try { await PeerWire.SendAsync(socket, message, session.Token); }
                    finally { sendLock.Release(); }
                }
                var pending = new Dictionary<Guid, DateTimeOffset>();
                var pendingLock = new object();
                async Task ReceiveAsync()
                {
                    try
                    {
                        while (await PeerWire.ReceiveAsync(socket, session.Token) is { } message)
                        {
                            if (message.Target != node)
                                throw new InvalidDataException("Unexpected target.");
                            if (message.Type == "ping" && message.Sender == target)
                            {
                                await SendAsync(new PeerMessage("pong", message.RequestId, target));
                                logger.LogInformation("Ping received, pong sent {RequestId}", message.RequestId);
                            }
                            else
                            {
                                bool matched;
                                lock (pendingLock) matched = pending.Remove(message.RequestId);
                                if (matched)
                                    logger.LogInformation("{Type} received {RequestId}", message.Type, message.RequestId);
                            }
                        }
                    }
                    finally { session.Cancel(); }
                }
                receiver = ReceiveAsync();
                while (!session.IsCancellationRequested)
                {
                    lock (pendingLock)
                    {
                        if (pending.Values.Any(time => DateTimeOffset.UtcNow - time > TimeSpan.FromSeconds(15)))
                            throw new TimeoutException("Peer response timed out.");
                    }
                    var id = Guid.NewGuid();
                    lock (pendingLock) pending.Add(id, DateTimeOffset.UtcNow);
                    await SendAsync(new PeerMessage("ping", id, target));
                    logger.LogInformation("Ping sent to {Target} {RequestId}", target, id);
                    await Task.Delay(TimeSpan.FromSeconds(5), session.Token);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or TimeoutException or InvalidDataException or System.Text.Json.JsonException)
            {
                logger.LogInformation("Connection ended: {Reason}", ex.Message);
            }
            finally
            {
                session.Cancel();
                socket.Abort();
                if (receiver is not null)
                {
                    try { await receiver; }
                    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException)
                    {
                        logger.LogDebug("Receive loop ended: {Reason}", ex.Message);
                    }
                }
            }
            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}
