using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;

namespace LemonadeWars.Engine.Tests
{
    public class AbsentPlayerPolicyTests
    {
        private static readonly IBot StandIn = new GreedyBot();

        [Fact]
        public void AnAbsentPlayersTurnEndsWithoutSpendingAnAction()
        {
            var game = ReadyToPlay(seed: 9001);
            int active = game.State.ActivePlayer;

            var choice = AbsentPlayerPolicy.Choose(game, active, StandIn);

            Assert.IsType<EndTurn>(choice);
        }

        [Fact]
        public void ATableOfAbsentPlayersKeepsTakingTurns()
        {
            var game = NewGame(seed: 31337);
            var turnsStarted = new List<int>();

            for (int step = 0; step < 600; step++)
            {
                int actor = game.ActingPlayers()[0];
                var events = game.Apply(AbsentPlayerPolicy.Choose(game, actor, StandIn));
                turnsStarted.AddRange(events.OfType<TurnStarted>().Select(turn => turn.PlayerId));
            }

            // Setup (titles, the mandatory Stands) was answered by the stand-in, and from then
            // on every seat keeps being skipped in order.
            Assert.True(turnsStarted.Count > 40, $"only {turnsStarted.Count} turns started");
            Assert.Equal(game.State.Players.Count, turnsStarted.Distinct().Count());
        }

        [Fact]
        public void AnAbsentPlayerNeverAnswersAWindowWithACard()
        {
            var game = NewGame(seed: 2024);
            var bots = Enumerable.Range(0, game.State.Players.Count)
                .ToDictionary(seat => seat, seat => (IBot)new GreedyBot());
            int windowsSeen = 0;

            for (int step = 0; step < 3000 && game.State.Stage != GameStage.Finished; step++)
            {
                int actor = game.ActingPlayers()[0];
                if (game.State.AwaitingResponse.Contains(actor))
                {
                    windowsSeen++;
                    Assert.IsType<PassWindow>(AbsentPlayerPolicy.Choose(game, actor, StandIn));
                }
                game.Apply(bots[actor].Choose(game, actor));
            }

            Assert.True(windowsSeen > 0, "the seed never opened a response window");
        }
    }
}
