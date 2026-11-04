using System.Collections.Concurrent;
using System.Net.WebSockets;
using BIS.ERP.PeerProtocol;

var builder = WebApplication.CreateBuilder(args);
// This unauthenticated test endpoint is deliberately limited to loopback.
builder.WebHost.UseUrls("http://127.0.0.1:5080");
var app = builder.Build();
var sessions = new ConcurrentDictionary<string, RelaySession>();
app.UseWebSockets();
app.Map("/peer", async context =>
{
    var id = context.Request.Query["node"].ToString();
    if (!context.WebSockets.IsWebSocketRequest || id is not ("A" or "B"))
    {
        context.Response.StatusCode = 400;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var session = new RelaySession(socket);
    if (!sessions.TryAdd(id, session))
    {
        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Node already connected", context.RequestAborted);
        return;
    }
    app.Logger.LogInformation("Node {Node} connected", id);
    try
    {
        while (await PeerWire.ReceiveAsync(socket, context.RequestAborted) is { } message)
        {
            if (message.Target == id)
                throw new InvalidDataException("Target must be the other node.");
            if (sessions.TryGetValue(message.Target, out var target))
            {
                await target.SendAsync(message with { Sender = id }, context.RequestAborted);
            }
            else
            {
                await session.SendAsync(new PeerMessage("unavailable", message.RequestId, id), context.RequestAborted);
            }
        }
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed", context.RequestAborted);
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException)
    {
        app.Logger.LogInformation("Node {Node} disconnected: {Reason}", id, ex.Message);
    }
    finally
    {
        sessions.TryRemove(id, out _);
    }
});
app.Run();

sealed class RelaySession(WebSocket socket)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task SendAsync(PeerMessage message, CancellationToken token)
    {
        await _sendLock.WaitAsync(token);
        try { await PeerWire.SendAsync(socket, message, token); }
        finally { _sendLock.Release(); }
    }
}
