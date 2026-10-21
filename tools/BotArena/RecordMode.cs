using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.BotArena;

/// <summary>
/// Plays one all-greedy game to completion and writes it in the server's room-log
/// format (header line + one encoded action per line), producing fixtures for
/// <see cref="ReplayMode"/>. Greedy is seedless and deterministic, so a replay at the
/// "greedy" level reproduces every move.
/// </summary>
internal static class RecordMode
{
    private const int MaxActions = 20_000;

    public static void Run(string outPath, ulong seed, int players)
    {
        var db = ArenaTools.LoadDatabase();
        var names = Enumerable.Range(0, players).Select(s => $"Bot{s}").ToArray();
        var game = Game.Create(db, names, seed);
        var bot = new GreedyBot();

        using var writer = new StreamWriter(outPath);
        writer.WriteLine(Header(seed, names).ToString(Formatting.None));
        int actions = 0;
        while (game.State.Stage != GameStage.Finished)
        {
            if (actions++ > MaxActions)
            {
                throw new InvalidOperationException("runaway game");
            }
            int actor = game.ActingPlayers()[0];
            var action = bot.Choose(game, actor);
            writer.WriteLine(WireCodec.EncodeAction(action).ToString(Formatting.None));
            game.Apply(action);
        }
        Console.WriteLine($"recorded {actions} actions (seed {seed}, {players} players) -> {outPath}");
    }

    private static JObject Header(ulong seed, string[] names) => new JObject
    {
        ["header"] = new JObject
        {
            ["seed"] = seed.ToString(),
            ["names"] = new JArray(names),
            ["bots"] = new JArray(names.Select(_ => true)),
            ["tokens"] = new JArray(names.Select(_ => "")),
            ["players"] = new JArray(names.Select(_ => "")),
            ["botLevels"] = new JArray(names.Select(_ => BotFactory.Medium)),
        },
    };
}
