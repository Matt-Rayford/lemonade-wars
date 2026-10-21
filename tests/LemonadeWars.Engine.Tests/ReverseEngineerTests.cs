using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.BotScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Reverse Engineer recovering the other Reverse Engineer is a forced-play chain
    /// with no exit (arena game 100115 ran 20,000 actions on it), so the second copy
    /// is never on the menu.
    /// </summary>
    public class ReverseEngineerTests
    {
        [Fact]
        public void ReverseEngineerCannotRecoverAReverseEngineer()
        {
            var game = ReadyToAct(61, 3);
            var s = game.State;
            int actor = s.ActivePlayer;
            StripToOneCard(game, actor, "reverse-engineer");
            int held = s.Players[actor].Hand[0];

            // The discard holds the other copy plus an ordinary plan.
            int otherCopy = s.LemonDeck.First(id => s.LemonInstances[id].DefId == "reverse-engineer");
            int plan = s.LemonDeck.First(id => s.LemonInstances[id].DefId == "market-forecasting");
            s.LemonDeck.Remove(otherCopy);
            s.LemonDeck.Remove(plan);
            s.LemonDiscard.Add(otherCopy);
            s.LemonDiscard.Add(plan);

            var recoveries = game.LegalMovesFor(actor).OfType<PlayLemonCard>()
                .Where(p => p.CardInstanceId == held && !p.DrawInstead)
                .Select(p => p.DiscardedLemonInstanceId)
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
