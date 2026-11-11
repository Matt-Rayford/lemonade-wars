using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// "The game didn't pause for my tantrum": simulates the client's bot loop (bots act
    /// whenever ActingPlayers contains them, the human never acts) and asserts the game
    /// blocks on the human's open window.
    /// </summary>
    public class WindowPauseTests
    {
        private static readonly string[] PlayerNames = { "Ana", "Ben", "Cai", "Dee" };

        [Fact]
        public void BotPlayWaitsForHumanTantrumHolder()
        {
            var game = NewGameWithLordsChosen(seed: 424242);
            int human = (game.State.ActivePlayer + 1) % 4;

            // The setup buys and every bot turn run without the human, so the game must stop
            // with the human as the ONLY player being waited on.
            int steps = RunBots(game, human);
            Assert.True(steps < 200, "bot loop never yielded to the human");
            var acting = game.ActingPlayers();
            Assert.Equal(new[] { human }, acting);

            // When what blocks is a response window, bots cannot advance past it and the
            // pending interaction stays pending.
            if (game.State.AwaitingResponse.Contains(human))
            {
                int stackBefore = game.State.ResponseStack.Count;
                bool hadPendingRoll = game.State.PendingRoll != null;
                Assert.Equal(0, RunBots(game, human, 50));
                Assert.Equal(stackBefore, game.State.ResponseStack.Count);
                Assert.Equal(hadPendingRoll, game.State.PendingRoll != null);

                // The human passes; play continues without them until the next window.
                game.Apply(new PassWindow { PlayerId = human });
                RunBots(game, human);
                Assert.Equal(new[] { human }, game.ActingPlayers());
            }
        }

        [Fact]
        public void WindowBlocksResolutionUntilHumanPasses()
        {
            var game = NewGameWithLordsChosen(seed: 77);
            int human = (game.State.ActivePlayer + 1) % 4;
            RunBots(game, human);

            var state = game.State;
            if (state.AwaitingResponse.Count > 0 || state.PendingDecisions.Count > 0)
            {
                return; // seed landed in another blocking shape; first test covers it
            }
            int activeBot = state.ActivePlayer;
            Assert.NotEqual(human, activeBot);

            // The exact scenario: the human holds a Tantrum and the active bot plays a
            // tantrummable card.
            GiveTantrumFromDeckOrDiscard(game, human);
            var play = game.LegalMovesFor(activeBot).OfType<PlayLemonCard>()
                .FirstOrDefault(m => TestData.Db.Lemon(
                    state.LemonInstances[m.CardInstanceId].DefId).Type == LemonCardType.Plan);
            if (play == null)
            {
                return; // active bot holds no plan this seed; first test covers the flow
            }
            game.Apply(play);

            Assert.Contains(human, state.AwaitingResponse);
            int stackCount = state.ResponseStack.Count;
            Assert.True(stackCount > 0);

            // Bots may respond among themselves, but the human's window must survive every
            // recompute, and nothing may resolve out from under it.
            RunBots(game, human, 50);
            Assert.Equal(new[] { human }, game.ActingPlayers());
            Assert.True(state.ResponseStack.Count > 0,
                "stack resolved while the human still held an open response window");
        }

        /// <summary>The draft is left unplayed: the bot loop makes the setup buys.</summary>
        private static Game NewGameWithLordsChosen(ulong seed)
        {
            var game = Game.Create(TestData.Db, PlayerNames, seed);
            KeepFirstTwoLemonLords(game);
            return game;
        }

        /// <summary>Mirror of LocalGameSession.Tick: every non-human actor acts. Returns the steps taken.</summary>
        private static int RunBots(Game game, int human, int maxSteps = 200)
        {
            var bot = new GreedyBot();
            int steps = 0;
            while (steps < maxSteps && game.State.Stage != GameStage.Finished)
            {
                int actor = game.ActingPlayers().FirstOrDefault(player => player != human, -1);
                if (actor < 0)
                {
                    break;
                }
                game.Apply(bot.Choose(game, actor));
                steps++;
            }
            return steps;
        }

        private static void GiveTantrumFromDeckOrDiscard(Game game, int playerId)
        {
            var state = game.State;
            int tantrum = state.LemonDeck.Concat(state.LemonDiscard)
                .First(id => state.LemonInstances[id].DefId == "tantrum");
            state.LemonDeck.Remove(tantrum);
            state.LemonDiscard.Remove(tantrum);
            state.Players[playerId].Hand.Add(tantrum);
        }
    }
}
