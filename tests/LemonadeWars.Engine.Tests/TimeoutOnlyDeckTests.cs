using System;
using System.Linq;
using System.Threading.Tasks;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// A deck (plus discard) holding nothing but Timeout cards must not wedge a draw:
    /// the Timeout is drawn and resolved, and the replacement it owes simply never
    /// arrives. The planner's deep lookahead reaches this state long before a real
    /// game does, and a wedged Apply there froze the bot's think forever.
    /// </summary>
    public class TimeoutOnlyDeckTests
    {
        [Fact]
        public void DrawingFromATimeoutOnlyDeckTerminates()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben", "Cal" }, 11);
            PlayRandomlyToAQuietPlayPhase(game);
            Assert.Equal(GameStage.Playing, game.State.Stage);

            var state = game.State;
            int timeoutId = state.LemonDeck.Concat(state.LemonDiscard)
                .First(id => TestData.Db.Lemon(state.LemonInstances[id].DefId).Type == LemonCardType.Timeout);
            state.LemonDeck.Clear();
            state.LemonDiscard.Clear();
            state.LemonDeck.Add(timeoutId);

            int drawer = state.ActivePlayer;
            var draw = game.LegalMovesFor(drawer).OfType<DrawLemonCard>().FirstOrDefault();
            Assert.NotNull(draw);

            // On a worker thread, so a wedged Apply fails the test instead of hanging the run.
            var apply = Task.Run(() => game.Apply(draw).ToList());
            Assert.True(apply.Wait(TimeSpan.FromSeconds(5)), "Apply wedged: the Timeout-only deck looped forever");
            Assert.Empty(state.PendingDraws);
        }

        private static void PlayRandomlyToAQuietPlayPhase(Game game)
        {
            var random = new RandomBot(99);
            int guard = 0;
            while (!(game.State.Stage == GameStage.Playing && game.State.Phase == TurnPhase.Play &&
                     game.State.PendingDecisions.Count == 0 && game.State.AwaitingResponse.Count == 0) &&
                   guard++ < 800)
            {
                int actor = game.ActingPlayers()[0];
                game.Apply(random.Choose(game, actor));
            }
        }
    }
}
