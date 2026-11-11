using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Whoever a response window is waiting on must be able to SEE what they are
    /// answering: a card on the stack, a die, or money just stolen from them. A wait
    /// with none of those reaches the table as a blank "Your choice — Pass" screen.
    /// </summary>
    public class WindowContextTests
    {
        public static IEnumerable<object[]> Seeds() =>
            Enumerable.Range(1, 400).Select(i => new object[] { (ulong)i, 2 + i % 4 });

        [Theory]
        [MemberData(nameof(Seeds))]
        public void EveryAwaitedPlayerHasSomethingToAnswer(ulong seed, int playerCount)
        {
            var names = new[] { "Ana", "Ben", "Cal", "Dee", "Eve" }.Take(playerCount).ToArray();
            var game = Game.Create(TestData.Db, names, seed);
            var bots = Enumerable.Range(0, playerCount)
                .ToDictionary(i => i, i => new RandomBot(seed * 131 + (ulong)i));
            for (int step = 0; step < 20000 && game.State.Stage != GameStage.Finished; step++)
            {
                int actor = game.ActingPlayers()[0];
                var move = bots[actor].Choose(game, actor);
                // Described before the move changes the table it is described against.
                string lastMove = $"P{actor}: {MoveDescriber.Describe(game, move)}";
                game.Apply(move);

                foreach (int awaited in game.State.AwaitingResponse)
                {
                    var view = game.ViewFor(awaited);
                    bool hasContext = view.StackTop != null || view.PendingRollValue != null ||
                                      view.TheftOnMe || view.MyDecisions.Count > 0;
                    Assert.True(hasContext,
                        $"P{awaited} is awaited with nothing to answer after {lastMove} " +
                        $"(step {step}). {GameRunner.Describe(game)}");
                    if (view.MyDecisions.Count == 0)
                    {
                        var options = game.LegalMovesFor(awaited);
                        Assert.True(options.Any(m => !(m is PassWindow)),
                            $"P{awaited} is asked to respond but can only Pass, after {lastMove}. " +
                            $"window={DescribeWindow(view)}");
                    }
                }
            }
        }

        private static string DescribeWindow(PlayerView view)
        {
            if (view.StackTop != null)
            {
                return "stack:" + view.StackTop.DefId;
            }
            return view.PendingRollValue != null ? "roll:" + view.PendingRollPurpose : "theft";
        }
    }
}
