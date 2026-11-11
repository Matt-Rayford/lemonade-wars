using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.BotScenario;
using static LemonadeWars.Engine.Tests.StateRigging;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Reverse Engineer recovering the other Reverse Engineer is a forced-play chain with no
    /// exit (arena game 100115 ran 20,000 actions on it), so the second copy is never on the menu.
    /// </summary>
    public class ReverseEngineerTests
    {
        [Fact]
        public void ReverseEngineerCannotRecoverAReverseEngineer()
        {
            var game = ReadyToAct(61, 3);
            var state = game.State;
            int actor = state.ActivePlayer;
            StripToOneCard(game, actor, "reverse-engineer");
            int held = state.Players[actor].Hand[0];

            // The discard holds the other copy plus an ordinary plan.
            int otherCopy = state.LemonDeck.First(id => state.LemonInstances[id].DefId == "reverse-engineer");
            int plan = state.LemonDeck.First(id => state.LemonInstances[id].DefId == "market-forecasting");
            state.LemonDeck.Remove(otherCopy);
            state.LemonDeck.Remove(plan);
            state.LemonDiscard.Add(otherCopy);
            state.LemonDiscard.Add(plan);

            var recoveries = game.LegalMovesFor(actor).OfType<PlayLemonCard>()
                .Where(play => play.CardInstanceId == held && !play.DrawInstead)
                .Select(play => play.DiscardedLemonInstanceId)
                .ToList();
            Assert.Contains(plan, recoveries);
            Assert.DoesNotContain(otherCopy, recoveries);

            Assert.Throws<InvalidActionException>(() => game.Apply(new PlayLemonCard
            {
                PlayerId = actor,
                CardInstanceId = held,
                DiscardedLemonInstanceId = otherCopy,
            }));
        }
    }
}
