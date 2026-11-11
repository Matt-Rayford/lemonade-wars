using System.Net.WebSockets;

namespace LemonadeWars.Server.Tests;

/// <summary>Periodic room retirement, without the web host.</summary>
public class RoomSweepTests
{
    private static RoomManager FreshManager()
    {
        var db = GameDataLocator.LoadDatabase();
        string dir = Path.Combine(Path.GetTempPath(), "lw-sweep-" + Guid.NewGuid().ToString("N"));
        return new RoomManager(db, dir, 0);
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
        // A join racing the sweep must error, not take a seat in a room nobody can list or resume.
        var seat = room.Join("Late", null, null, null!, out string error);
        Assert.Null(seat);
        Assert.Contains("expired", error);
    }

    [Fact]
    public void LeavingALobbyFreesTheSeatAndRenumbersTheRest()
    {
        var manager = FreshManager();
        var room = manager.Create();
        var host = new Connection(new ClientWebSocket());
        var guest = new Connection(new ClientWebSocket());
        Assert.NotNull(room.Join("Host", null, null, host, out _));
        Assert.NotNull(room.Join("Guest", null, null, guest, out _));

        room.Leave(host);

        // No ghost seat: the host is gone entirely and the guest inherits seat 0.
        Assert.Equal(-1, room.SeatIndexOf(host));
        Assert.Equal(0, room.SeatIndexOf(guest));
    }
}
