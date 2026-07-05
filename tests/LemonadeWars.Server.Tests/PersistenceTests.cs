using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace LemonadeWars.Server.Tests;

/// <summary>
/// The Railway-redeploy story: a running game's action log on disk must rebuild the room
/// (deterministic replay) in a brand-new server process, and the reconnect token must
/// drop the player straight back into the same game.
/// </summary>
public class PersistenceTests
{
    [Fact]
    public async Task ServerRestartRehydratesTheGameFromDisk()
    {
        string dataDir = Path.Combine(Path.GetTempPath(), "lw-persist-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("DATA_DIR", dataDir);
        Environment.SetEnvironmentVariable("BOT_DELAY_MS", "0");

        string code;
        string token;
        JToken moneyBefore;

        // ---- First server lifetime: start a game vs bots, make a few moves, then "die".
        using (var first = new WebApplicationFactory<Program>())
        {
            var host = await Probe.ConnectAsync(first);
            await host.SendAsync(new { type = "create_room", name = "Survivor" });
            var room = await host.NextOfTypeAsync("room");
            code = (string)room["code"]!;
            token = (string)room["token"]!;

            for (int i = 0; i < 3; i++)
            {
                await host.SendAsync(new { type = "add_bot" });
                await host.NextOfTypeAsync("room");
            }
            await host.SendAsync(new { type = "start_game" });

            // Submit three human moves, then walk away mid-game.
            int submitted = 0;
            JObject lastUpdate = null!;
            while (submitted < 3)
            {
                var update = await host.NextOfTypeAsync("update");
                lastUpdate = update;
                if (update["moves"] is JArray moves && moves.Count > 0)
                {
                    await host.SendAsync(new { type = "action", action = moves[0]!["action"] });
                    submitted++;
                }
            }
            moneyBefore = lastUpdate["view"]!["players"]!;
            await host.DisposeAsync();
        }

        // ---- Second server lifetime: same DATA_DIR, fresh process.
        Assert.True(File.Exists(Path.Combine(dataDir, code + ".jsonl")));
        using (var second = new WebApplicationFactory<Program>())
        {
            var reconnected = await Probe.ConnectAsync(second);
            await reconnected.SendAsync(new { type = "join_room", code, name = "ignored", token });

            var room = await reconnected.NextOfTypeAsync("room");
            Assert.True((bool)room["started"]!);
            Assert.Equal(0, (int)room["yourSeat"]!);

            var update = await reconnected.NextOfTypeAsync("update");
            var players = (JArray)update["view"]!["players"]!;
            Assert.Equal(4, players.Count);
            // The replayed state is not a fresh game: setup draft money spend must be visible.
            Assert.NotEqual("choosingLemonLords", (string)update["view"]!["stage"]!);
            await reconnected.DisposeAsync();
        }
    }

    /// <summary>Minimal socket probe (mirrors ServerTests.TestClient).</summary>
    private sealed class Probe : IAsyncDisposable
    {
        private readonly WebSocket _socket;
        private readonly Channel<JObject> _inbox = Channel.CreateUnbounded<JObject>();
        private readonly CancellationTokenSource _cts = new();

        private Probe(WebSocket socket)
        {
            _socket = socket;
            _ = ReceiveLoopAsync();
        }

        public static async Task<Probe> ConnectAsync(WebApplicationFactory<Program> factory)
        {
            var client = factory.Server.CreateWebSocketClient();
            var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
            return new Probe(socket);
        }

        public async Task SendAsync(object message)
        {
            byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            await _socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task<JObject> NextOfTypeAsync(string type, int timeoutSeconds = 30)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            while (true)
            {
                var message = await _inbox.Reader.ReadAsync(timeout.Token);
                if ((string?)message["type"] == type)
                {
                    return message;
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
}
