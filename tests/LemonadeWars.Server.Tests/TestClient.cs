using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Server.Tests;

/// <summary>A headless player on a real WebSocket to the in-memory test server.</summary>
internal sealed class TestClient : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly Channel<JObject> _inbox = Channel.CreateUnbounded<JObject>();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;

    private TestClient(WebSocket socket)
    {
        _socket = socket;
        _receiveLoop = ReceiveLoopAsync();
    }

    public static async Task<TestClient> ConnectAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.Server.CreateWebSocketClient();
        var socket = await client.ConnectAsync(
            new Uri("ws://localhost/ws"), CancellationToken.None);
        return new TestClient(socket);
    }

    public async Task SendAsync(object message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
        await _sendLock.WaitAsync();
        try
        {
            await _socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<JObject> HelloAsync(string playerKey, string name)
    {
        await SendAsync(new { type = "hello", playerKey, name });
        return await NextOfTypeAsync("welcome");
    }

    public async Task<JObject> CreateRoomAsync(string name)
    {
        await SendAsync(new { type = "create_room", name });
        return await NextOfTypeAsync("room");
    }

    public async Task<JObject> JoinRoomAsync(string code, string name)
    {
        await SendAsync(new { type = "join_room", code, name });
        return await NextOfTypeAsync("room");
    }

    /// <summary>Next message matching the predicate; earlier non-matching ones are dropped.</summary>
    public async Task<JObject> NextAsync(Func<JObject, bool> match, int timeoutSeconds = 20)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        while (true)
        {
            var message = await _inbox.Reader.ReadAsync(timeout.Token);
            if (match(message))
            {
                return message;
            }
        }
    }

    public async Task<JObject> NextOfTypeAsync(string type, int timeoutSeconds = 20) =>
        await NextAsync(m => (string?)m["type"] == type, timeoutSeconds);

    /// <summary>
    /// Submits a seeded-random offered move on every update; returns the update that
    /// reports the game finished.
    /// </summary>
    public async Task<JObject> PlayRandomlyUntilGameOverAsync(int seed, int timeoutSeconds = 120)
    {
        var random = new Random(seed);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        while (true)
        {
            var message = await _inbox.Reader.ReadAsync(timeout.Token);
            if ((string?)message["type"] != "update")
            {
                continue; // room updates and stale-action errors
            }
            if ((string?)message["view"]?["stage"] == "finished")
            {
                return message;
            }
            if (message["moves"] is JArray moves && moves.Count > 0)
            {
                var pick = (JObject)moves[random.Next(moves.Count)]!;
                await SendAsync(new { type = "action", action = pick["action"] });
            }
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _inbox.Writer.TryComplete();
                        return;
                    }
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                _inbox.Writer.TryWrite(JObject.Parse(Encoding.UTF8.GetString(stream.ToArray())));
            }
        }
        catch
        {
            _inbox.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // CloseOutputAsync, not CloseAsync: CloseAsync does its own receive for the peer's
            // ack, and a second receive racing the receive loop deadlocks TestWebSocket for good.
            await _socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch
        {
            // already gone
        }
        // The receive loop drains the server's close reply first; bounded, so a silent
        // server cannot wedge disposal.
        await Task.WhenAny(_receiveLoop, Task.Delay(2000));
        _cts.Cancel();
        _socket.Dispose();
    }
}
