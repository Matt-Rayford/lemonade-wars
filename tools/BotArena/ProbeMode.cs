using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.BotArena;

/// <summary>
/// Replays one stalled four-player tournament game (a pimc hero against greedy bots,
/// seeded exactly as <see cref="Matchup"/> seeds it) and prints its final action cycle.
/// </summary>
internal static class ProbeMode
{
    private const int Players = 4;
    private const int MaxSteps = 6000;
    private const int FirstPrintedStep = 5000;

    public static void Run(ulong seed, int heroSeat, int budgetMs)
    {
        var db = ArenaTools.LoadDatabase();
        var game = Game.Create(db, Matchup.SeatNames(Players, heroSeat), seed);
        var bots = new Dictionary<int, IBot>();
        ulong gameIndex = seed - Matchup.FirstGameSeed;
        for (int seat = 0; seat < Players; seat++)
        {
            bots[seat] = seat == heroSeat
                ? new SearchBot(Matchup.BotSeed(gameIndex, seat), budgetMs)
                : (IBot)new GreedyBot();
        }
        for (int step = 0; step < MaxSteps; step++)
        {
            var acting = game.ActingPlayers();
            int actor = acting[0];
            var action = bots[actor].Choose(game, actor);
            if (step >= FirstPrintedStep)
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
