namespace LemonadeWars.Server.Tests;

/// <summary>Boot-time graveyard sweep, without the web host.</summary>
public class RoomGcTests
{
    [Fact]
    public void OldCorruptOrFinishedLogsAreSweptAtBoot()
    {
        var db = GameDataLocator.LoadDatabase();
        string dir = Path.Combine(Path.GetTempPath(), "lw-gc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        string oldGarbage = Path.Combine(dir, "AAAAA.jsonl");
        File.WriteAllText(oldGarbage, "not json\n");
        File.SetLastWriteTimeUtc(oldGarbage, DateTime.UtcNow.AddDays(-10));

        string freshGarbage = Path.Combine(dir, "BBBBB.jsonl");
        File.WriteAllText(freshGarbage, "not json\n");

        string registry = Path.Combine(dir, "players.jsonl");
        File.WriteAllText(registry, "{\"keyHash\":\"x\",\"playerId\":\"p1\",\"name\":\"N\"}\n");

        _ = new RoomManager(db, dir, 0);

        Assert.False(File.Exists(oldGarbage));   // past grace: deleted
        Assert.True(File.Exists(freshGarbage));  // unreadable but recent: kept for debugging
        Assert.True(File.Exists(registry));      // identity store is never a room log
    }
}
