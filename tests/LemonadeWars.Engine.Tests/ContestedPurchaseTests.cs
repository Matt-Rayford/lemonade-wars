using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;
using static LemonadeWars.Engine.Tests.StateRigging;

namespace LemonadeWars.Engine.Tests
{
    public class ContestedPurchaseTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ContestedPurchaseLeavesItsSlotEmptyUntilItResolves(bool tantrummed)
        {
            // Rule: the shelf is refilled only once the purchase is settled, so nobody sees
            // the next card while deciding whether to Tantrum.
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum");
            int buyer = ActivePlayer(game);
            int objector = SeatAfterActive(game, 1);
            int tantrum = GiveCard(game, objector, "tantrum");
            game.State.Players[buyer].Money = 50;
            var shelfBefore = game.State.Market.ToList();
            const int slot = 1;
            int bought = shelfBefore[slot];

            game.Apply(BuyFromShelf(game, buyer, slot));
            Assert.Equal(shelfBefore.Count - 1, game.State.Market.Count); // not refilled yet
            Assert.DoesNotContain(bought, game.State.Market);
            Assert.Equal(slot, game.ViewFor(objector).MarketGapIndex);

            game.Apply(tantrummed
                ? new RespondToWindow { PlayerId = objector, CardInstanceId = tantrum }
                : (GameAction)new PassWindow { PlayerId = objector });

            Assert.Equal(shelfBefore.Count, game.State.Market.Count);      // refilled on resolution
            Assert.Null(game.ViewFor(objector).MarketGapIndex);
            Assert.Equal(shelfBefore[0], game.State.Market[0]);            // neighbours never moved
            Assert.Equal(shelfBefore[2], game.State.Market[2]);
            Assert.DoesNotContain(game.State.Market[slot], shelfBefore);   // a new card, in the same slot
            Assert.Equal(tantrummed, game.State.BlackMarketDiscard.Contains(bought));
        }

        [Fact]
        public void CancelledPurchaseRefundsMoneyButNotAction()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum");
            int buyer = ActivePlayer(game);
            int objector = SeatAfterActive(game, 1);
            int tantrum = GiveCard(game, objector, "tantrum");
            var buyerState = game.State.Players[buyer];
            buyerState.Money = 50;
            int bought = game.State.Market[0];
            int cost = BlackMarketDefOf(game, bought).Cost;

            game.Apply(BuyFromShelf(game, buyer, marketIndex: 0));
            Assert.Equal(50 - cost, buyerState.Money); // paid up front
            game.Apply(new RespondToWindow { PlayerId = objector, CardInstanceId = tantrum });

            Assert.Equal(50, buyerState.Money);                 // refunded
            Assert.Equal(1, game.State.ActionsRemaining);       // action NOT refunded
            Assert.Contains(bought, game.State.BlackMarketDiscard);
            Assert.Empty(buyerState.Stands[0].Equipped);
            Assert.Empty(buyerState.Turf.Equipped);
        }

        /// <summary>A Stand card is aimed at the buyer's first Stand; a Turf card needs no target.</summary>
        private static BuyBlackMarket BuyFromShelf(Game game, int buyerId, int marketIndex)
        {
            var buy = new BuyBlackMarket { PlayerId = buyerId, MarketIndex = marketIndex };
            if (BlackMarketDefOf(game, game.State.Market[marketIndex]).Target == EquipTarget.Stand)
            {
                buy.TargetStandInstanceId = game.State.Players[buyerId].Stands[0].InstanceId;
            }
            return buy;
        }
    }
}
