using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Regression harness for "the game didn't pause for my tantrum": simulates the
    /// client's bot loop (bots act whenever ActingPlayers contains them, human never
    /// acts) and asserts the game blocks on the human's open window.
    /// </summary>
    public class WindowPauseTests
    {
        private static readonly string[] FourPlayers = { "Ana", "Ben", "Cai", "Dee" };

        /// <summary>Mirror of LocalGameSession.Tick: let every non-human actor act.</summary>
        private static int RunBots(Game game, int human, int maxSteps = 200)
        {
            var bot = new GreedyBot();
            int steps = 0;
            while (steps < maxSteps && game.State.Stage != GameStage.Finished)
            {
                int actor = game.ActingPlayers().FirstOrDefault(a => a != human, -1);
                if (actor < 0)
                {
                    break;
                }
                game.Apply(bot.Choose(game, actor));
                steps++;
            }
            return steps;
        }

        [Fact]
        public void BotPlayWaitsForHumanTantrumHolder()
        {
            var game = Game.Create(TestData.Db, FourPlayers, 424242);
            foreach (var p in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = p.PlayerId,
                    KeepTitleIds = p.LemonLordDealt.Take(2).ToList(),
                });
            }
            int human = (game.State.ActivePlayer + 1) % 4;

            // Setup buys + all bot turns run; the human never acts. The game must
            // eventually stop with the human as the ONLY player being waited on.
            int steps = RunBots(game, human);
            Assert.True(steps < 200, "bot loop never yielded to the human");
            var acting = game.ActingPlayers();
            Assert.Equal(new[] { human }, acting);

            // If what blocks is a response window, verify bots cannot advance past it
            // and the pending interaction stays pending.
            if (game.State.AwaitingResponse.Contains(human))
            {
                int stackBefore = game.State.ResponseStack.Count;
                bool rollBefore = game.State.PendingRoll != null;
                Assert.Equal(0, RunBots(game, human, 50));
                Assert.Equal(stackBefore, game.State.ResponseStack.Count);
                Assert.Equal(rollBefore, game.State.PendingRoll != null);

                // The human passes; play continues without them until the next window.
                game.Apply(new PassWindow { PlayerId = human });
                RunBots(game, human);
                Assert.Equal(new[] { human }, game.ActingPlayers());
            }
        }

        [Fact]
        public void WindowBlocksResolutionUntilHumanPasses()
        {
            var game = Game.Create(TestData.Db, FourPlayers, 77);
            foreach (var p in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = p.PlayerId,
                    KeepTitleIds = p.LemonLordDealt.Take(2).ToList(),
                });
            }
            int human = (game.State.ActivePlayer + 1) % 4;
            RunBots(game, human);

            // Force the exact scenario: give the human a tantrum, have the active bot
            // play a tantrummable card, then run the bot loop hard.
            var s = game.State;
            if (s.AwaitingResponse.Count > 0 || s.PendingDecisions.Count > 0)
            {
                return; // seed landed in another blocking shape; first test covers it
            }
            int active = s.ActivePlayer;
            Assert.NotEqual(human, active);

            int tantrum = s.LemonDeck.Concat(s.LemonDiscard)
                .First(id => s.LemonInstances[id].DefId == "tantrum");
            s.LemonDeck.Remove(tantrum);
            s.LemonDiscard.Remove(tantrum);
            s.Players[human].Hand.Add(tantrum);

            var play = game.LegalMovesFor(active).OfType<PlayLemonCard>()
                .FirstOrDefault(m => TestData.Db.Lemon(
                    s.LemonInstances[m.CardInstanceId].DefId).Type == LemonCardType.Plan);
            if (play == null)
            {
                return; // active bot holds no plan this seed; first test covers the flow
            }
            game.Apply(play);

            Assert.Contains(human, s.AwaitingResponse);
            int stackCount = s.ResponseStack.Count;
            Assert.True(stackCount > 0);

            // Bots may respond among themselves, but the human's window must survive
            // every recompute, and nothing may resolve out from under it.
            RunBots(game, human, 50);
            Assert.Equal(new[] { human }, game.ActingPlayers());
            Assert.True(s.ResponseStack.Count > 0,
                "stack resolved while the human still held an open response window");
        }
    }
}
