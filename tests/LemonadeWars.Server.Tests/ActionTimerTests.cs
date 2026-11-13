using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Server.Tests;

/// <summary>
/// The action timer end to end. Each test boots its own server with the timer's second cut
/// short, so a 15s setting runs out in a fraction of a real second.
/// </summary>
public class ActionTimerTests
{
    private const int FastSecondMs = 20;

    [Fact]
    public async Task TheHostSetsTheActionTimerAndTheRoomHearsIt()
    {
        using var factory = StartServer();
        await using var host = await TestClient.ConnectAsync(factory);
        var room = await host.CreateRoomAsync("Host");
        Assert.Equal(60, (int)room["actionTimerSeconds"]!);
        await using var guest = await TestClient.ConnectAsync(factory);
        await guest.JoinRoomAsync((string)room["code"]!, "Guest");

        await host.SendAsync(new { type = "set_action_timer", seconds = 30 });
        var heardByGuest = await guest.NextAsync(m =>
            (string?)m["type"] == "room" && (int)m["actionTimerSeconds"]! == 30);
        Assert.Equal(30, (int)heardByGuest["actionTimerSeconds"]!);

        await host.SendAsync(new { type = "set_action_timer", seconds = 45 });
        var refusedLength = await host.NextOfTypeAsync("error");
        Assert.Contains("15, 30, or 60", (string)refusedLength["message"]!);

        await guest.SendAsync(new { type = "set_action_timer", seconds = 15 });
        var refusedGuest = await guest.NextOfTypeAsync("error");
        Assert.Contains("Only the host", (string)refusedGuest["message"]!);
    }

    [Fact]
    public async Task ASeatThatLetsItsTimerRunOutIsPlayedFor()
    {
        using var factory = StartServer();
        await using var host = await TestClient.ConnectAsync(factory);
        await using var guest = await TestClient.ConnectAsync(factory);
        await StartTwoHumanGameAsync(host, guest, timerSeconds: 15);

        // Neither player ever answers: the Lemon Lord choice is made for both, and the game moves on.
        var expiry = await host.NextAsync(m => (string?)m["type"] == "update" &&
            m["events"]!.Any(e => (string?)e["$type"] == "ActionTimerExpired"));
        Assert.NotEqual("choosingLemonLords", (string)expiry["view"]!["stage"]!);

        // Left alone it keeps going: the setup draft, then turn after skipped turn.
        var playing = await host.NextAsync(m => (string?)m["type"] == "update" &&
            (string?)m["view"]!["stage"] == "playing" &&
            m["events"]!.Any(e => (string?)e["$type"] == "SaleRolled"), timeoutSeconds: 30);
        Assert.Equal(2, ((JArray)playing["view"]!["players"]!).Count);
    }

    [Fact]
    public async Task ACountdownStartsWhenItsSeatReportsTheTableSettled()
    {
        // A slower second than the other tests: the settle grace (15 of them) must not beat the report.
        using var factory = StartServer(msPerTimerSecond: 200);
        await using var host = await TestClient.ConnectAsync(factory);
        await using var guest = await TestClient.ConnectAsync(factory);
        var firstUpdate = await StartTwoHumanGameAsync(host, guest, timerSeconds: 60);

        var armed = firstUpdate["clocks"]!.Single(clock => (int)clock["seat"]! == 0);
        Assert.False((bool)armed["running"]!);
        Assert.Equal(60, (double)armed["secondsLeft"]!, precision: 1);

        await host.SendAsync(new { type = "table_settled", stateVersion = (int)firstUpdate["stateVersion"]! });
        var started = await guest.NextOfTypeAsync("action_clocks");
        Assert.True((bool)started["clocks"]!.Single(clock => (int)clock["seat"]! == 0)["running"]!);
        Assert.False((bool)started["clocks"]!.Single(clock => (int)clock["seat"]! == 1)["running"]!);
    }

    [Fact]
    public async Task AnotherPlayersActionLeavesYourCountdownRunning()
    {
        using var factory = StartServer();
        await using var host = await TestClient.ConnectAsync(factory);
        await using var guest = await TestClient.ConnectAsync(factory);
        var firstUpdate = await StartTwoHumanGameAsync(host, guest, timerSeconds: 60);
        var hostsChoice = (JObject)firstUpdate["moves"]![0]!;

        // The guest's clock starts when the settle grace lapses, then runs a while longer.
        await host.NextAsync(m => (string?)m["type"] == "action_clocks" &&
            (bool)m["clocks"]!.Single(clock => (int)clock["seat"]! == 1)["running"]!);
        await Task.Delay(200);
        await host.SendAsync(new { type = "action", action = hostsChoice["action"] });

        // The host chose their titles; the guest still faces the same choice on the same clock.
        var afterHostActed = await host.NextAsync(m => (string?)m["type"] == "update" &&
            ((JArray)m["moves"]!).Count == 0);
        var guestClock = afterHostActed["clocks"]!.Single();
        Assert.Equal(1, (int)guestClock["seat"]!);
        Assert.True((bool)guestClock["running"]!);
        Assert.True((double)guestClock["secondsLeft"]! < 55, "the guest's countdown was restarted");
    }

    [Fact]
    public async Task ALoneHumanAmongBotsPlaysUntimed()
    {
        using var factory = StartServer();
        await using var host = await TestClient.ConnectAsync(factory);
        await host.CreateRoomAsync("Host");
        await host.SendAsync(new { type = "add_bot" });
        await host.NextOfTypeAsync("room");
        await host.SendAsync(new { type = "start_game" });

        var update = await host.NextAsync(m => (string?)m["type"] == "update" &&
            m["moves"] is JArray moves && moves.Count > 0);
        Assert.Empty((JArray)update["clocks"]!);
    }

    /// <summary>Returns the host's first update of the game, which awaits both seats' Lemon Lord choices.</summary>
    private static async Task<JObject> StartTwoHumanGameAsync(TestClient host, TestClient guest,
        int timerSeconds)
    {
        var room = await host.CreateRoomAsync("Host");
        await guest.JoinRoomAsync((string)room["code"]!, "Guest");
        await guest.SendAsync(new { type = "ready", ready = true });
        await host.NextAsync(m => (string?)m["type"] == "room" &&
            m["seats"]!.Any(seat => (bool)seat["ready"]!));
        await host.SendAsync(new { type = "set_action_timer", seconds = timerSeconds });
        await host.NextAsync(m => (string?)m["type"] == "room" &&
            (int)m["actionTimerSeconds"]! == timerSeconds);
        await host.SendAsync(new { type = "start_game" });
        return await host.NextAsync(m => (string?)m["type"] == "update" &&
            m["moves"] is JArray moves && moves.Count > 0);
    }

    /// <summary>
    /// The server reads its environment once, as it boots, so the overrides are lifted again
    /// before any other test class can start a server of its own under them.
    /// </summary>
    private static WebApplicationFactory<Program> StartServer(int msPerTimerSecond = FastSecondMs)
    {
        string? previousDataDir = Environment.GetEnvironmentVariable("DATA_DIR");
        Environment.SetEnvironmentVariable("ACTION_TIMER_SECOND_MS", msPerTimerSecond.ToString());
        Environment.SetEnvironmentVariable("BOT_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("RATE_LIMIT_PER_SEC", "0");
        Environment.SetEnvironmentVariable("DATA_DIR",
            Path.Combine(Path.GetTempPath(), "lw-timer-" + Guid.NewGuid().ToString("N")));
        try
        {
            var factory = new WebApplicationFactory<Program>();
            _ = factory.Server;
            return factory;
        }
        finally
        {
            Environment.SetEnvironmentVariable("ACTION_TIMER_SECOND_MS", null);
            Environment.SetEnvironmentVariable("DATA_DIR", previousDataDir);
        }
    }
}
