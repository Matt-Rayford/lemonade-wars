using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.BotArena;

/// <summary>Replays one stalled tournament game and prints its final action cycle.</summary>
internal static class ProbeMode
{
    public static void Run(ulong seed, int heroSeat, int budgetMs)
    {
        var db = ArenaTools.LoadDatabase();
        var names = Enumerable.Range(0, 4).Select(s => s == heroSeat ? "Hero" : $"Base{s}").ToArray();
        var game = Game.Create(db, names, seed);
        var bots = new Dictionary<int, IBot>();
        ulong index = seed - 100_000UL;
        for (int seat = 0; seat < 4; seat++)
        {
            ulong botSeed = 555UL * (index + 1) + (ulong)seat;
            bots[seat] = seat == heroSeat
                ? new SearchBot(botSeed, budgetMs)
                : (IBot)new GreedyBot();
        }
        for (int step = 0; step < 6000; step++)
        {
            var acting = game.ActingPlayers();
            int actor = acting[0];
            var action = bots[actor].Choose(game, actor);
            if (step >= 5000)
            {
                Console.WriteLine($"{step}: P{actor} {MoveDescriber.Describe(game, action)} " +
                    $"| VP {string.Join("/", game.State.Players.Select(p => p.InGameVictoryPoints))}" +
                    $" $ {string.Join("/", game.State.Players.Select(p => p.Money))}");
            }
            game.Apply(action);
            if (game.State.Stage == GameStage.Finished)
            {
                Console.WriteLine($"finished at {step}");
                return;
            }
        }
    }
}
