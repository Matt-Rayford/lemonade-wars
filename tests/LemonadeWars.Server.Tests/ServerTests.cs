using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LemonadeWars.Server.Tests;

/// <summary>
/// End-to-end games over real WebSockets: create/join rooms, then headless clients that
/// pick random server-provided legal moves until the server reports the game is over.
/// This exercises the full stack — protocol, room locking, per-seat views, bot pump.
/// </summary>
public class ServerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ServerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------- test client

    private sealed class TestClient : IAsyncDisposable
    {
        private readonly WebSocket _socket;
        private readonly Channel<JObject> _inbox = Channel.CreateUnbounded<JObject>();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly CancellationTokenSource _cts = new();

        private TestClient(WebSocket socket)
        {
            _socket = socket;
            _ = ReceiveLoopAsync();
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
        /// Play randomly: whenever an update offers moves, submit one; returns when the
        /// game finishes. The seeded picker keeps runs reproducible-ish per seat.
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
                    continue; // ignore room updates and stale-action errors
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
            _cts.Cancel();
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch
            {
                // already gone
            }
        }
    }

    // --------------------------------------------------------------- tests

    [Fact]
    public async Task FourHumansPlayARandomGameToCompletion()
    {
        await using var host = await TestClient.ConnectAsync(_factory);
        await host.SendAsync(new { type = "create_room", name = "Host" });
        var room = await host.NextOfTypeAsync("room");
        string code = (string)room["code"]!;
        Assert.Equal(5, code.Length);

        var guests = new List<TestClient>();
        for (int i = 1; i <= 3; i++)
        {
            var guest = await TestClient.ConnectAsync(_factory);
            guests.Add(guest);
            await guest.SendAsync(new { type = "join_room", code, name = $"Guest{i}" });
            await guest.NextOfTypeAsync("room");
        }

        await host.SendAsync(new { type = "start_game" });

        var players = new List<TestClient> { host };
        players.AddRange(guests);
        var finals = await Task.WhenAll(
            players.Select((p, i) => p.PlayRandomlyUntilGameOverAsync(seed: 1000 + i)));

        foreach (var final in finals)
        {
            var winners = (JArray)final["view"]!["winners"]!;
            Assert.NotEmpty(winners);
        }

        foreach (var guest in guests)
        {
            await guest.DisposeAsync();
        }
    }

    [Fact]
    public async Task HostPlaysAgainstThreeServerBots()
    {
        await using var host = await TestClient.ConnectAsync(_factory);
        await host.SendAsync(new { type = "create_room", name = "Solo" });
        var room = await host.NextOfTypeAsync("room");
        string code = (string)room["code"]!;

        for (int i = 0; i < 3; i++)
        {
            await host.SendAsync(new { type = "add_bot" });
            await host.NextOfTypeAsync("room");
        }
        await host.SendAsync(new { type = "start_game" });

        var final = await host.PlayRandomlyUntilGameOverAsync(seed: 7);
        Assert.NotEmpty((JArray)final["view"]!["winners"]!);
        Assert.Equal(code, (string)room["code"]!); // sanity: same room throughout
    }

    [Fact]
    public async Task ReconnectWithTokenReclaimsTheSeat()
    {
        var creator = await TestClient.ConnectAsync(_factory);
        await creator.SendAsync(new { type = "create_room", name = "Flaky" });
        var room = await creator.NextOfTypeAsync("room");
        string code = (string)room["code"]!;
        string token = (string)room["token"]!;
        Assert.False(string.IsNullOrEmpty(token));

        await using (var other = await TestClient.ConnectAsync(_factory))
        {
            await other.SendAsync(new { type = "join_room", code, name = "Steady" });
            await other.NextOfTypeAsync("room");

            // Creator drops and comes back with their token.
            await creator.DisposeAsync();
            await using var reconnected = await TestClient.ConnectAsync(_factory);
            await reconnected.SendAsync(new { type = "join_room", code, name = "ignored", token });
            var rejoin = await reconnected.NextOfTypeAsync("room");

            Assert.Equal(0, (int)rejoin["yourSeat"]!);
            Assert.Equal(2, ((JArray)rejoin["seats"]!).Count);
            Assert.Equal(token, (string)rejoin["token"]!);
        }
    }

    [Fact]
    public async Task JoiningAMissingRoomFails()
    {
        await using var client = await TestClient.ConnectAsync(_factory);
        await client.SendAsync(new { type = "join_room", code = "ZZZZZ", name = "Lost" });
        var error = await client.NextOfTypeAsync("error");
        Assert.Contains("No room", (string)error["message"]!);
    }
}
