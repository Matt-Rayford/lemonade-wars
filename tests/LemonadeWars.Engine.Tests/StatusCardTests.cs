using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;
using static LemonadeWars.Engine.Tests.StateRigging;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>The Whiniest Baby and Spoiled Rotten status cards, and the Timeout that fines the Baby.</summary>
    public class StatusCardTests
    {
        [Fact]
        public void TimeoutFinesTheBabyAndPassesTheCard()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum");
            int drawer = ActivePlayer(game);
            int baby = SeatAfterActive(game, 1);
            int runnerUp = SeatAfterActive(game, 2);
            var state = game.State;
            // The Baby holds 2 Tantrums ($3 each) but only $4, so the $6 fine forces a sale.
            // The runner-up holds 1.
            PutTantrumInPile(game, baby);
            PutTantrumInPile(game, baby);
            PutTantrumInPile(game, runnerUp);
            state.WhiniestBabyHolder = baby;
            state.Players[baby].Money = 4;
            int timeout = state.LemonDeck.First(id => state.LemonInstances[id].DefId == "timeout");
            state.LemonDeck.Remove(timeout);
            state.LemonDeck.Insert(0, timeout);

            game.Apply(new DrawLemonCard { PlayerId = drawer });

            var fine = Assert.Single(state.PendingDecisions);
            Assert.Equal(DecisionKind.TimeoutFine, fine.Kind);
            Assert.Equal(6, fine.RequiredMoney);

            // Paying with nothing sold cannot cover it.
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new SubmitTimeoutPayment { PlayerId = baby }));

            // Selling a Bargain stand (+$2) does.
            game.Apply(new SubmitTimeoutPayment
            {
                PlayerId = baby,
                SellStandInstanceIds = new List<int> { state.Players[baby].Stands[0].InstanceId },
            });

            Assert.Equal(0, state.Players[baby].Money);           // 4 + 2 - 6
            Assert.Single(state.Players[baby].Stands);            // sold one of two
            Assert.Empty(state.Players[baby].TantrumPile);        // tantrums discarded
            Assert.Equal(runnerUp, state.WhiniestBabyHolder);     // passed to next-most tantrums
            Assert.Contains(timeout, state.LemonDiscard);
        }

        [Fact]
        public void WhiniestBabyDiscardsOneOfTheTwoDrawnCards()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum", "out-of-stock");
            var state = game.State;
            // No Timeouts, so the turn-start draws are exactly two ordinary cards.
            state.LemonDeck.RemoveAll(id => state.LemonInstances[id].DefId == "timeout");
            int previous = ActivePlayer(game);
            int baby = SeatAfterActive(game, 1);
            state.WhiniestBabyHolder = baby;
            var handBefore = state.Players[baby].Hand.ToList();

            game.Apply(new EndTurn { PlayerId = previous });
            PassWindowsOnly(game);

            // The discard is restricted to exactly the 2 drawn cards, not the whole hand.
            var decision = Assert.Single(state.PendingDecisions);
            Assert.Equal(DecisionKind.WhiniestBabyDiscard, decision.Kind);
            var drawn = state.Players[baby].Hand.Except(handBefore).ToList();
            Assert.Equal(2, drawn.Count);
            Assert.Equal(drawn.OrderBy(x => x), decision.EligibleCardIds!.OrderBy(x => x));

            // Legal moves only offer the drawn pair; an old hand card is rejected.
            var offered = game.LegalMovesFor(baby).OfType<SubmitDiscard>().ToList();
            Assert.Equal(2, offered.Count);
            Assert.All(offered, m => Assert.Contains(Assert.Single(m.InstanceIds), drawn));
            Assert.Throws<InvalidActionException>(() => game.Apply(new SubmitDiscard
            {
                PlayerId = baby,
                InstanceIds = new List<int> { handBefore[0] },
            }));

            game.Apply(new SubmitDiscard
            {
                PlayerId = baby,
                InstanceIds = new List<int> { drawn[0] },
            });
            Assert.Equal(TurnPhase.Play, state.Phase);
            Assert.Contains(drawn[1], state.Players[baby].Hand);
            Assert.Contains(drawn[0], state.LemonDiscard);
            // Net effect matches a normal turn: hand grew by exactly 1.
            Assert.Equal(handBefore.Count + 1, state.Players[baby].Hand.Count);
        }

        [Fact]
        public void SpoiledRottenGoesToSoleLastAndGrantsAFreeRoll()
        {
            var game = ReadyToPlay(seed: 7, names: TwoPlayers);
            StripHands(game, "tantrum", "out-of-stock");
            int leader = ActivePlayer(game);
            int trailer = SeatAfterActive(game, 1);
            game.State.Players[leader].Money = 50;

            game.Apply(new BuyBraggingRights { PlayerId = leader });
            // With two players, the other one is now sole last and takes Spoiled Rotten.
            Assert.Equal(trailer, game.State.SpoiledRottenHolder);

            var events = new List<GameEvent>();
            events.AddRange(game.Apply(new EndTurn { PlayerId = leader }));
            PassWindowsOnly(game, events);

            // The holder's turn began with a free personal sale roll, before their Play phase.
            var rolls = events.OfType<SaleRolled>().ToList();
            Assert.Equal(2, rolls.Count);
            Assert.Equal(leader, rolls[0].PlayerId);
            Assert.Equal(trailer, rolls[1].PlayerId);
            Assert.Equal(trailer, game.State.ActivePlayer);
            Assert.Equal(TurnPhase.Play, game.State.Phase);
        }
    }
}
