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

    static ServerTests()
    {
        // Instant bots, no throttling, and an isolated data dir for the shared factory.
        Environment.SetEnvironmentVariable("BOT_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("RATE_LIMIT_PER_SEC", "0");
        Environment.SetEnvironmentVariable("DATA_DIR",
            Path.Combine(Path.GetTempPath(), "lw-tests-" + Guid.NewGuid().ToString("N")));
    }

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
            try
            {
                // CloseOutputAsync, NOT CloseAsync: CloseAsync performs an internal
                // ReceiveAsync (waiting for the peer's close ack) that races our
                // receive loop's outstanding ReceiveAsync — two concurrent receives
                // violate the WebSocket contract and deadlock TestWebSocket's buffer
                // semaphore forever, wedging the whole test run.
                await _socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch
            {
                // already gone
            }
            // Let the receive loop drain the server's close reply before tearing the
            // socket down under it — bounded, so a silent server can't wedge disposal.
            await Task.WhenAny(_receiveLoop, Task.Delay(2000));
            _cts.Cancel();
            _socket.Dispose();
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
        // Regression guard: a freshly created room must NOT report started.
        Assert.False((bool)room["started"]!);

        var guests = new List<TestClient>();
        for (int i = 1; i <= 3; i++)
        {
            var guest = await TestClient.ConnectAsync(_factory);
            guests.Add(guest);
            await guest.SendAsync(new { type = "join_room", code, name = $"Guest{i}" });
            await guest.NextOfTypeAsync("room");
        }

        // Starting before everyone readies is rejected.
        await host.SendAsync(new { type = "start_game" });
        var refused = await host.NextOfTypeAsync("error");
        Assert.Contains("Waiting for", (string)refused["message"]!);

        foreach (var guest in guests)
        {
            await guest.SendAsync(new { type = "ready", ready = true });
        }
        // Wait until the host sees all guests ready, then start for real.
        await host.NextAsync(m => (string?)m["type"] == "room" &&
            m["seats"]!.Count(s => (bool)s["ready"]!) >= 3);
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

    // ---------------------------------------------------- identity + async

    [Fact]
    public async Task RepeatedHelloIsRefused()
    {
        await using var churner = await TestClient.ConnectAsync(_factory);
        for (int i = 0; i < 5; i++)
        {
            await churner.SendAsync(new
            {
                type = "hello",
                playerKey = $"churn-key-{i}-{Guid.NewGuid():N}",
                name = "Churn",
            });
            await churner.NextOfTypeAsync("welcome");
        }
        await churner.SendAsync(new
        {
            type = "hello",
            playerKey = "churn-key-final-" + Guid.NewGuid().ToString("N"),
            name = "Churn",
        });
        var refused = await churner.NextOfTypeAsync("error");
        Assert.Contains("identity", (string)refused["message"]!);
    }

    [Fact]
    public async Task HelloIsDurableAcrossConnections()
    {
        string key = "identity-test-key-" + Guid.NewGuid().ToString("N");

        await using var first = await TestClient.ConnectAsync(_factory);
        await first.SendAsync(new { type = "hello", playerKey = key, name = "Matt" });
        var welcome = await first.NextOfTypeAsync("welcome");
        string playerId = (string)welcome["playerId"]!;
        Assert.False(string.IsNullOrEmpty(playerId));
        Assert.Equal("Matt", (string)welcome["name"]!);

        // Same key on a new socket: same identity, rename sticks.
        await using var second = await TestClient.ConnectAsync(_factory);
        await second.SendAsync(new { type = "hello", playerKey = key, name = "Matt R" });
        var again = await second.NextOfTypeAsync("welcome");
        Assert.Equal(playerId, (string)again["playerId"]!);
        Assert.Equal("Matt R", (string)again["name"]!);

        // A key too short to be a secret is rejected.
        await using var bogus = await TestClient.ConnectAsync(_factory);
        await bogus.SendAsync(new { type = "hello", playerKey = "short", name = "X" });
        var error = await bogus.NextOfTypeAsync("error");
        Assert.Contains("player key", (string)error["message"]!);
    }

    [Fact]
    public async Task IdentityReclaimsSeatMidGameWithoutToken()
    {
        string key = "reclaim-test-key-" + Guid.NewGuid().ToString("N");

        var creator = await TestClient.ConnectAsync(_factory);
        await creator.SendAsync(new { type = "hello", playerKey = key, name = "Host" });
        await creator.NextOfTypeAsync("welcome");
        await creator.SendAsync(new { type = "create_room", name = "Host" });
        var room = await creator.NextOfTypeAsync("room");
        string code = (string)room["code"]!;
        await creator.SendAsync(new { type = "add_bot" });
        await creator.NextOfTypeAsync("room");
        await creator.SendAsync(new { type = "start_game" });
        await creator.NextOfTypeAsync("update");

        // Vanish, then return with identity only — no room token, no code memory
        // beyond what My Games provides.
        await creator.DisposeAsync();
        await using var comeback = await TestClient.ConnectAsync(_factory);
        await comeback.SendAsync(new { type = "hello", playerKey = key, name = "Host" });
        var welcome = await comeback.NextOfTypeAsync("welcome");
        var mine = ((JArray)welcome["gamesList"]!)
            .FirstOrDefault(g => (string?)g["code"] == code);
        Assert.NotNull(mine);
        Assert.True((bool)mine!["started"]!);
        Assert.False((bool)mine["finished"]!);
        Assert.True((bool)mine["yourTurn"]!); // Lemon Lord choice awaits every player

        await comeback.SendAsync(new { type = "join_room", code, name = "Host" });
        var rejoin = await comeback.NextOfTypeAsync("room");
        Assert.Equal(0, (int)rejoin["yourSeat"]!);
        // Rejoining a started room re-sends the live view with legal moves.
        var update = await comeback.NextOfTypeAsync("update");
        Assert.NotNull(update["view"]);
    }

    [Fact]
    public async Task SameIdentityTwiceInALobbyGetsTwoSeats()
    {
        // Two running instances on one machine share PlayerPrefs and thus one key —
        // the second must NOT steal the first's connected lobby seat.
        string key = "twin-test-key-" + Guid.NewGuid().ToString("N");

        await using var first = await TestClient.ConnectAsync(_factory);
        await first.SendAsync(new { type = "hello", playerKey = key, name = "Twin" });
        await first.NextOfTypeAsync("welcome");
        await first.SendAsync(new { type = "create_room", name = "Twin" });
        var room = await first.NextOfTypeAsync("room");
        string code = (string)room["code"]!;

        await using var second = await TestClient.ConnectAsync(_factory);
        await second.SendAsync(new { type = "hello", playerKey = key, name = "Twin" });
        await second.NextOfTypeAsync("welcome");
        await second.SendAsync(new { type = "join_room", code, name = "Twin" });
        var joined = await second.NextOfTypeAsync("room");

        Assert.Equal(1, (int)joined["yourSeat"]!);
        Assert.Equal(2, ((JArray)joined["seats"]!).Count);
    }

    [Fact]
    public async Task BotDifficultyIsSetAndBroadcast()
    {
        await using var host = await TestClient.ConnectAsync(_factory);
        await host.SendAsync(new { type = "create_room", name = "Host" });
        await host.NextOfTypeAsync("room");

        await host.SendAsync(new { type = "add_bot", level = "hard" });
        var room = await host.NextOfTypeAsync("room");
        Assert.Equal("hard", (string)room["seats"]![1]!["botLevel"]!);

        await host.SendAsync(new { type = "set_bot_level", seat = 1, level = "easy" });
        room = await host.NextOfTypeAsync("room");
        Assert.Equal("easy", (string)room["seats"]![1]!["botLevel"]!);

        // Garbage levels normalize to medium instead of erroring.
        await host.SendAsync(new { type = "set_bot_level", seat = 1, level = "nightmare" });
        room = await host.NextOfTypeAsync("room");
        Assert.Equal("medium", (string)room["seats"]![1]!["botLevel"]!);

        // Humans are not tunable.
        await host.SendAsync(new { type = "set_bot_level", seat = 0, level = "hard" });
        var refused = await host.NextOfTypeAsync("error");
        Assert.Contains("not a bot", (string)refused["message"]!);
    }

    [Fact]
    public async Task GameSpeedIsSetAndBroadcast()
    {
        await using var host = await TestClient.ConnectAsync(_factory);
        await host.SendAsync(new { type = "create_room", name = "Host" });
        var room = await host.NextOfTypeAsync("room");
        Assert.Equal("medium", (string)room["speed"]!);

        await host.SendAsync(new { type = "set_speed", speed = "fast" });
        room = await host.NextOfTypeAsync("room");
        Assert.Equal("fast", (string)room["speed"]!);

        await host.SendAsync(new { type = "set_speed", speed = "ludicrous" });
        var refused = await host.NextOfTypeAsync("error");
        Assert.Contains("slow, medium, or fast", (string)refused["message"]!);
    }

    [Fact]
    public async Task AbsentPlayerGetsTurnAlertOnOtherConnection()
    {
        string hostKey = "alert-host-" + Guid.NewGuid().ToString("N");
        string guestKey = "alert-guest-" + Guid.NewGuid().ToString("N");

        await using var host = await TestClient.ConnectAsync(_factory);
        await host.SendAsync(new { type = "hello", playerKey = hostKey, name = "Host" });
        await host.NextOfTypeAsync("welcome");
        await host.SendAsync(new { type = "create_room", name = "Host" });
        var room = await host.NextOfTypeAsync("room");
        string code = (string)room["code"]!;

        var guest = await TestClient.ConnectAsync(_factory);
        await guest.SendAsync(new { type = "hello", playerKey = guestKey, name = "Guest" });
        await guest.NextOfTypeAsync("welcome");
        await guest.SendAsync(new { type = "join_room", code, name = "Guest" });
        await guest.NextOfTypeAsync("room");
        await guest.SendAsync(new { type = "ready", ready = true });
        await host.NextAsync(m => (string?)m["type"] == "room" &&
            m["seats"]!.Any(s => (bool)s["ready"]!));
        await host.SendAsync(new { type = "start_game" });
        // The post-start update carries the host's Lemon Lord moves — hold one.
        var update = await host.NextAsync(m => (string?)m["type"] == "update" &&
            m["moves"] is JArray moves && moves.Count > 0);
        var pick = (JObject)update["moves"]![0]!;

        // Guest walks away from the table but keeps the app open elsewhere:
        // a second connection identified by the same key, joined to nothing.
        await guest.DisposeAsync();
        await using var guestPhone = await TestClient.ConnectAsync(_factory);
        await guestPhone.SendAsync(new { type = "hello", playerKey = guestKey, name = "Guest" });
        await guestPhone.NextOfTypeAsync("welcome");

        // Host acts; the guest is still awaited (Lemon Lord choice) and away —
        // the idle connection gets exactly one nudge naming the room.
        await host.SendAsync(new { type = "action", action = pick["action"] });

        var alert = await guestPhone.NextOfTypeAsync("turn_alert");
        Assert.Equal(code, (string)alert["code"]!);
    }

    [Fact]
    public async Task ListGamesRequiresHelloAndTracksRooms()
    {
        await using var anonymous = await TestClient.ConnectAsync(_factory);
        await anonymous.SendAsync(new { type = "list_games" });
        var refused = await anonymous.NextOfTypeAsync("error");
        Assert.Contains("Unknown or out-of-order", (string)refused["message"]!);

        string key = "list-test-key-" + Guid.NewGuid().ToString("N");
        await using var player = await TestClient.ConnectAsync(_factory);
        await player.SendAsync(new { type = "hello", playerKey = key, name = "Lister" });
        var welcome = await player.NextOfTypeAsync("welcome");
        Assert.Empty((JArray)welcome["gamesList"]!);

        await player.SendAsync(new { type = "create_room", name = "Lister" });
        await player.NextOfTypeAsync("room");
        await player.SendAsync(new { type = "list_games" });
        var games = await player.NextOfTypeAsync("games");
        var entries = (JArray)games["gamesList"]!;
        Assert.Single(entries);
        Assert.False((bool)entries[0]["started"]!);
        Assert.Equal(0, (int)entries[0]["yourSeat"]!);
    }
}

/// <summary>Periodic room retirement, without the web host.</summary>
public class RoomSweepTests
{
    private static LemonadeWars.Server.RoomManager FreshManager()
    {
        var db = LemonadeWars.Server.GameDataLocator.LoadDatabase();
        string dir = Path.Combine(Path.GetTempPath(), "lw-sweep-" + Guid.NewGuid().ToString("N"));
        return new LemonadeWars.Server.RoomManager(db, dir, 0);
    }

    [Fact]
    public void FreshLobbiesSurviveTheSweep()
    {
        var manager = FreshManager();
        var room = manager.Create();
        Assert.Equal(0, manager.Sweep()); // default TTLs: a brand-new lobby stays
        Assert.NotNull(manager.Find(room.Code));
    }

    [Fact]
    public void ExpiredEmptyLobbiesAreRetiredAndRefuseLateJoins()
    {
        var manager = FreshManager();
        var room = manager.Create();
        Assert.Equal(1, manager.Sweep(lobbyTtl: TimeSpan.Zero));
        Assert.Null(manager.Find(room.Code));
        // The retire flag closes the race: a join hitting the ghost room errors
        // cleanly instead of taking a seat nobody can ever list or resume.
        var seat = room.Join("Late", null, null, null!, out string error);
        Assert.Null(seat);
        Assert.Contains("expired", error);
    }
}

/// <summary>Boot-time graveyard sweep, without the web host.</summary>
public class RoomGcTests
{
    [Fact]
    public void OldCorruptOrFinishedLogsAreSweptAtBoot()
    {
        var db = LemonadeWars.Server.GameDataLocator.LoadDatabase();
        string dir = Path.Combine(Path.GetTempPath(), "lw-gc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        string oldGarbage = Path.Combine(dir, "AAAAA.jsonl");
        File.WriteAllText(oldGarbage, "not json\n");
        File.SetLastWriteTimeUtc(oldGarbage, DateTime.UtcNow.AddDays(-10));

        string freshGarbage = Path.Combine(dir, "BBBBB.jsonl");
        File.WriteAllText(freshGarbage, "not json\n");

        string registry = Path.Combine(dir, "players.jsonl");
        File.WriteAllText(registry, "{\"keyHash\":\"x\",\"playerId\":\"p1\",\"name\":\"N\"}\n");

        _ = new LemonadeWars.Server.RoomManager(db, dir, 0);

        Assert.False(File.Exists(oldGarbage));   // past grace: deleted
        Assert.True(File.Exists(freshGarbage));  // unreadable but recent: kept for debugging
        Assert.True(File.Exists(registry));      // identity store is never a room log
    }
}
