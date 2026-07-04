using System;
using System.IO;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>Locates game-data/ from the test output directory and caches one shared database.</summary>
    public static class TestData
    {
        private static readonly Lazy<CardDatabase> Cached = new(() => CardDatabase.Load(GameDataDir()));

        public static CardDatabase Db => Cached.Value;

        public static string GameDataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "game-data");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not find game-data/ above the test directory.");
        }
    }
}
