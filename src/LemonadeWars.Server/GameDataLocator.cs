using LemonadeWars.Engine.Data;

namespace LemonadeWars.Server;

/// <summary>Finds game-data/ via env var (containers) or by walking up (dev/tests).</summary>
public static class GameDataLocator
{
    public static CardDatabase LoadDatabase()
    {
        string? configured = Environment.GetEnvironmentVariable("GAME_DATA_DIR");
        if (!string.IsNullOrEmpty(configured))
        {
            return CardDatabase.Load(configured);
        }
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "game-data");
            if (Directory.Exists(candidate))
            {
                return CardDatabase.Load(candidate);
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "game-data not found; set GAME_DATA_DIR or run from within the repo.");
    }
}
