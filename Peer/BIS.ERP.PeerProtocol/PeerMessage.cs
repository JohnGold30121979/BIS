using System.Net.WebSockets;
using System.Text.Json;

namespace BIS.ERP.PeerProtocol;

public sealed record PeerMessage(string Type, Guid RequestId, string Target, string? Sender = null);

public static class PeerWire
{
    public static async Task SendAsync(WebSocket socket, PeerMessage message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, token);
    }

    public static async Task<PeerMessage?> ReceiveAsync(WebSocket socket, CancellationToken token)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[4096];
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), token);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text || stream.Length + result.Count > 16384)
                throw new InvalidDataException("Expected a text message of at most 16384 bytes.");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var message = JsonSerializer.Deserialize<PeerMessage>(stream.ToArray())
            ?? throw new InvalidDataException("Empty message.");
        if (message.RequestId == Guid.Empty || message.Type is not ("ping" or "pong" or "unavailable")
            || message.Target is not ("A" or "B"))
            throw new InvalidDataException("Invalid peer message.");
        return message;
    }
}