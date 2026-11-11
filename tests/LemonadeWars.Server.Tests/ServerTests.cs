using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Server.Tests;

/// <summary>
/// End-to-end over real WebSockets against one shared server: the full stack of protocol,
/// room locking, per-seat views and the bot pump.
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

    // ---------------------------------------------------------- full games

    [Fact]
    public async Task FourHumansPlayARandomGameToCompletion()
    {
        await using var host = await TestClient.ConnectAsync(_factory);
        var room = await host.CreateRoomAsync("Host");
        string code = (string)room["code"]!;
        Assert.Equal(5, code.Length);
        Assert.False((bool)room["started"]!);

        var guests = new List<TestClient>();
        for (int i = 1; i <= 3; i++)
        {
            var guest = await TestClient.ConnectAsync(_factory);
            guests.Add(guest);
            await guest.JoinRoomAsync(code, $"Guest{i}");
        }

        await host.SendAsync(new { type = "start_game" });
        var refused = await host.NextOfTypeAsync("error");
        Assert.Contains("Not All Players Are Ready", (string)refused["message"]!);

        foreach (var guest in guests)
        {
            await guest.SendAsync(new { type = "ready", ready = true });
        }
        // The host must have seen all three ready, or the second start is refused too.
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
        await host.CreateRoomAsync("Solo");

        for (int i = 0; i < 3; i++)
        {
            await host.SendAsync(new { type = "add_bot" });
            await host.NextOfTypeAsync("room");
        }
        await host.SendAsync(new { type = "start_game" });

        var final = await host.PlayRandomlyUntilGameOverAsync(seed: 7);
        Assert.NotEmpty((JArray)final["view"]!["winners"]!);
    }

    // --------------------------------------------------------------- lobby

    [Fact]
    public async Task LeaveRoomMessageFreesTheLobbySeat()
    {
        await using var host = await TestClient.ConnectAsync(_factory);
        var room = await host.CreateRoomAsync("Host");
        string code = (string)room["code"]!;

        await using var guest = await TestClient.ConnectAsync(_factory);
        await guest.JoinRoomAsync(code, "Guest");
        var withGuest = await host.NextOfTypeAsync("room");
        Assert.Equal(2, ((JArray)withGuest["seats"]!).Count);

        await guest.SendAsync(new { type = "leave_room" });
        var afterLeave = await host.NextOfTypeAsync("room");
        Assert.Single((JArray)afterLeave["seats"]!);
    }

    [Fact]
    public async Task CreatingARoomLeavesTheOneYouWereIn()
    {
        await using var host = await TestClient.ConnectAsync(_factory);
        var firstRoom = await host.CreateRoomAsync("Host");
        string firstCode = (string)firstRoom["code"]!;

        await using var wanderer = await TestClient.ConnectAsync(_factory);
        await wanderer.JoinRoomAsync(firstCode, "Wanderer");
        var withWanderer = await host.NextOfTypeAsync("room");
        Assert.Equal(2, ((JArray)withWanderer["seats"]!).Count);

        var secondRoom = await wanderer.CreateRoomAsync("Wanderer");
        Assert.NotEqual(firstCode, (string)secondRoom["code"]!);
        var afterMove = await host.NextOfTypeAsync("room", timeoutSeconds: 5);
        Assert.Single((JArray)afterMove["seats"]!);
    }

    [Fact]
    public async Task JoiningAnotherRoomLeavesTheOneYouWereIn()
    {
        await using var firstHost = await TestClient.ConnectAsync(_factory);
        string firstCode = (string)(await firstHost.CreateRoomAsync("First"))["code"]!;
        await using var secondHost = await TestClient.ConnectAsync(_factory);
        string secondCode = (string)(await secondHost.CreateRoomAsync("Second"))["code"]!;

        await using var wanderer = await TestClient.ConnectAsync(_factory);
        await wanderer.JoinRoomAsync(firstCode, "Wanderer");
        await firstHost.NextOfTypeAsync("room");

        await wanderer.JoinRoomAsync(secondCode, "Wanderer");
        var firstRoomAfterMove = await firstHost.NextOfTypeAsync("room", timeoutSeconds: 5);
        Assert.Single((JArray)firstRoomAfterMove["seats"]!);
    }

    [Fact]
    public async Task JoiningTheRoomYouAreAlreadyInIsRefused()
    {
        await using var host = await TestClient.ConnectAsync(_factory);
        string code = (string)(await host.CreateRoomAsync("Host"))["code"]!;

        await host.SendAsync(new { type = "join_room", code, name = "Host again" });
        var refused = await host.NextOfTypeAsync("error", timeoutSeconds: 5);
        Assert.Contains("already in that room", (string)refused["message"]!);
    }

    [Fact]
    public async Task JoiningAMissingRoomFails()
    {
        await using var client = await TestClient.ConnectAsync(_factory);
        await client.SendAsync(new { type = "join_room", code = "ZZZZZ", name = "Lost" });
        var error = await client.NextOfTypeAsync("error");
        Assert.Contains("No room", (string)error["message"]!);
    }

    [Fact]
    public async Task BotDifficultyIsSetAndBroadcast()
    {
        await using var host = await TestClient.ConnectAsync(_factory);
        await host.CreateRoomAsync("Host");

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
        var room = await host.CreateRoomAsync("Host");
        Assert.Equal("medium", (string)room["speed"]!);

        await host.SendAsync(new { type = "set_speed", speed = "fast" });
        room = await host.NextOfTypeAsync("room");
        Assert.Equal("fast", (string)room["speed"]!);

        await host.SendAsync(new { type = "set_speed", speed = "ludicrous" });
        var refused = await host.NextOfTypeAsync("error");
        Assert.Contains("slow, medium, or fast", (string)refused["message"]!);
    }

    // ------------------------------------------------- reclaiming a seat

    [Fact]
    public async Task ReconnectWithTokenReclaimsTheSeat()
    {
        var creator = await TestClient.ConnectAsync(_factory);
        var room = await creator.CreateRoomAsync("Flaky");
        string code = (string)room["code"]!;
        string token = (string)room["token"]!;
        Assert.False(string.IsNullOrEmpty(token));

        await using (var other = await TestClient.ConnectAsync(_factory))
        {
            await other.JoinRoomAsync(code, "Steady");

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
    public async Task IdentityReclaimsSeatMidGameWithoutToken()
    {
        string key = "reclaim-test-key-" + Guid.NewGuid().ToString("N");

        var creator = await TestClient.ConnectAsync(_factory);
        await creator.HelloAsync(key, "Host");
        var room = await creator.CreateRoomAsync("Host");
        string code = (string)room["code"]!;
        await creator.SendAsync(new { type = "add_bot" });
        await creator.NextOfTypeAsync("room");
        await creator.SendAsync(new { type = "start_game" });
        await creator.NextOfTypeAsync("update");

        // Back with identity only: no room token, and the code comes from My Games.
        await creator.DisposeAsync();
        await using var comeback = await TestClient.ConnectAsync(_factory);
        var welcome = await comeback.HelloAsync(key, "Host");
        var mine = ((JArray)welcome["gamesList"]!)
            .FirstOrDefault(g => (string?)g["code"] == code);
        Assert.NotNull(mine);
        Assert.True((bool)mine!["started"]!);
        Assert.False((bool)mine["finished"]!);
        Assert.True((bool)mine["yourTurn"]!); // Lemon Lord choice awaits every player

        var rejoin = await comeback.JoinRoomAsync(code, "Host");
        Assert.Equal(0, (int)rejoin["yourSeat"]!);
        // Rejoining a started room re-sends the live view with legal moves.
        var update = await comeback.NextOfTypeAsync("update");
        Assert.NotNull(update["view"]);
    }

    // ------------------------------------------------------------ identity

    [Fact]
    public async Task RepeatedHelloIsRefused()
    {
        await using var churner = await TestClient.ConnectAsync(_factory);
        for (int i = 0; i < 5; i++)
        {
            await churner.HelloAsync($"churn-key-{i}-{Guid.NewGuid():N}", "Churn");
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
    public async Task ARejectedHelloKeepsTheIdentityYouHad()
    {
        await using var player = await TestClient.ConnectAsync(_factory);
        await player.HelloAsync("kept-identity-key-" + Guid.NewGuid().ToString("N"), "Keeper");

        await player.SendAsync(new { type = "hello", playerKey = "short", name = "Keeper" });
        await player.NextOfTypeAsync("error");

        // list_games answers only a connection that has an identity.
        await player.SendAsync(new { type = "list_games" });
        var reply = await player.NextAsync(
            message => (string?)message["type"] == "games" || (string?)message["type"] == "error");
        Assert.Equal("games", (string)reply["type"]!);
    }

    [Fact]
    public async Task HelloIsDurableAcrossConnections()
    {
        string key = "identity-test-key-" + Guid.NewGuid().ToString("N");

        await using var first = await TestClient.ConnectAsync(_factory);
        var welcome = await first.HelloAsync(key, "Matt");
        string playerId = (string)welcome["playerId"]!;
        Assert.False(string.IsNullOrEmpty(playerId));
        Assert.Equal("Matt", (string)welcome["name"]!);

        // Same key on a new socket: same identity, rename sticks.
        await using var second = await TestClient.ConnectAsync(_factory);
        var again = await second.HelloAsync(key, "Matt R");
        Assert.Equal(playerId, (string)again["playerId"]!);
        Assert.Equal("Matt R", (string)again["name"]!);

        // A key too short to be a secret is rejected.
        await using var bogus = await TestClient.ConnectAsync(_factory);
        await bogus.SendAsync(new { type = "hello", playerKey = "short", name = "X" });
        var error = await bogus.NextOfTypeAsync("error");
        Assert.Contains("player key", (string)error["message"]!);
    }

    [Fact]
    public async Task SameIdentityTwiceInALobbyGetsTwoSeats()
    {
        // Two instances on one machine share PlayerPrefs and so one key: the second
        // must not steal the first's connected lobby seat.
        string key = "twin-test-key-" + Guid.NewGuid().ToString("N");

        await using var first = await TestClient.ConnectAsync(_factory);
        await first.HelloAsync(key, "Twin");
        var room = await first.CreateRoomAsync("Twin");
        string code = (string)room["code"]!;

        await using var second = await TestClient.ConnectAsync(_factory);
        await second.HelloAsync(key, "Twin");
        var joined = await second.JoinRoomAsync(code, "Twin");

        Assert.Equal(1, (int)joined["yourSeat"]!);
        Assert.Equal(2, ((JArray)joined["seats"]!).Count);
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
        var welcome = await player.HelloAsync(key, "Lister");
        Assert.Empty((JArray)welcome["gamesList"]!);

        await player.CreateRoomAsync("Lister");
        await player.SendAsync(new { type = "list_games" });
        var games = await player.NextOfTypeAsync("games");
        var entries = (JArray)games["gamesList"]!;
        Assert.Single(entries);
        Assert.False((bool)entries[0]["started"]!);
        Assert.Equal(0, (int)entries[0]["yourSeat"]!);
    }

    [Fact]
    public async Task AbsentPlayerGetsTurnAlertOnOtherConnection()
    {
        string hostKey = "alert-host-" + Guid.NewGuid().ToString("N");
        string guestKey = "alert-guest-" + Guid.NewGuid().ToString("N");

        await using var host = await TestClient.ConnectAsync(_factory);
        await host.HelloAsync(hostKey, "Host");
        var room = await host.CreateRoomAsync("Host");
        string code = (string)room["code"]!;

        var guest = await TestClient.ConnectAsync(_factory);
        await guest.HelloAsync(guestKey, "Guest");
        await guest.JoinRoomAsync(code, "Guest");
        await guest.SendAsync(new { type = "ready", ready = true });
        await host.NextAsync(m => (string?)m["type"] == "room" &&
            m["seats"]!.Any(s => (bool)s["ready"]!));
        await host.SendAsync(new { type = "start_game" });
        // The post-start update carries the host's Lemon Lord moves: hold one.
        var update = await host.NextAsync(m => (string?)m["type"] == "update" &&
            m["moves"] is JArray moves && moves.Count > 0);
        var pick = (JObject)update["moves"]![0]!;

        // The guest leaves the table but keeps the app open elsewhere: a second
        // connection with the same key, joined to nothing.
        await guest.DisposeAsync();
        await using var guestPhone = await TestClient.ConnectAsync(_factory);
        await guestPhone.HelloAsync(guestKey, "Guest");

        // The host acts while the guest is still awaited (Lemon Lord choice) and away:
        // the idle connection gets a nudge naming the room.
        await host.SendAsync(new { type = "action", action = pick["action"] });

        var alert = await guestPhone.NextOfTypeAsync("turn_alert");
        Assert.Equal(code, (string)alert["code"]!);
    }
}
