using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;
using static LemonadeWars.Engine.Tests.StateRigging;

namespace LemonadeWars.Engine.Tests
{
    public class BlackMarketDiscountTests
    {
        [Fact]
        public void PeddlinPeteDiscountsDuringTheSetupDraft()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben", "Cal" }, 4242);
            KeepFirstTwoLemonLords(game);
            Assert.Equal(GameStage.InitialBuys, game.State.Stage);

            int buyer = game.State.InitialBuyQueue[0];
            var def = TestData.Db.BlackMarket("early-worm");
            Assert.Equal(def.Cost, game.BlackMarketPrice(buyer, def));

            // Pete on the Turf discounts the very next draft purchase (designer ruling).
            RigEquip(game, buyer, "peddlin-pete");
            Assert.Equal(def.Cost - 1, game.BlackMarketPrice(buyer, def));
        }

        [Fact]
        public void ShoppingSpreeAndPeddlinPeteDiscountPurchases()
        {
            var game = ReadyToPlay(seed: 11);
            StripHands(game, "tantrum");
            int next = SeatAfterActive(game, 1);
            RigEquip(game, next, "shopping-spree");
            RigEquip(game, next, "peddlin-pete");

            ApplyAndPass(game, new EndTurn { PlayerId = ActivePlayer(game) });
            Assert.Equal(next, ActivePlayer(game));
            Assert.Equal(1, game.State.BmOnlyActionsRemaining);

            var player = game.State.Players[next];
            player.Money = 50;
            // Burn both normal actions first: the buy must still work via Shopping Spree.
            ApplyAndPass(game, new DrawLemonCard { PlayerId = next });
            ApplyAndPass(game, new DrawLemonCard { PlayerId = next });

            int marketCardId = game.State.Market[0];
            var def = TestData.Db.BlackMarket(game.State.BlackMarketInstances[marketCardId].DefId);
            var buy = new BuyBlackMarket { PlayerId = next, MarketIndex = 0 };
            if (def.Target == EquipTarget.Stand)
            {
                buy.TargetStandInstanceId = player.Stands[0].InstanceId;
            }
            ApplyAndPass(game, buy);

            Assert.Equal(50 - (def.Cost - 1), player.Money); // Pete's $1 discount
            Assert.Equal(0, game.State.BmOnlyActionsRemaining);
        }
    }
}
