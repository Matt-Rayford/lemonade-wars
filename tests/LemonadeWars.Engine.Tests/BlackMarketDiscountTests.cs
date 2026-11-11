using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;
using static LemonadeWars.Engine.Tests.BlackMarketScenario;

namespace LemonadeWars.Engine.Tests
{
    public class BlackMarketDiscountTests
    {
        [Fact]
        public void PeddlinPeteDiscountsDuringTheSetupDraft()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben", "Cal" }, 4242);
            GameFlowSetup.KeepFirstTwoLemonLords(game);
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
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int next = Seat(game, 1);
            RigEquip(game, next, "shopping-spree");
            RigEquip(game, next, "peddlin-pete");

            GameFlowTests.ApplyAndPass(game, new EndTurn { PlayerId = Active(game) });
            Assert.Equal(next, Active(game));
            Assert.Equal(1, game.State.BmOnlyActionsRemaining);

            var player = game.State.Players[next];
            player.Money = 50;
            // Burn both normal actions first: the buy must still work via Shopping Spree.
            GameFlowTests.ApplyAndPass(game, new DrawLemonCard { PlayerId = next });
            GameFlowTests.ApplyAndPass(game, new DrawLemonCard { PlayerId = next });

            int marketCardId = game.State.Market[0];
            var def = TestData.Db.BlackMarket(game.State.BlackMarketInstances[marketCardId].DefId);
            var buy = new BuyBlackMarket { PlayerId = next, MarketIndex = 0 };
            if (def.Target == EquipTarget.Stand)
            {
                buy.TargetStandInstanceId = player.Stands[0].InstanceId;
            }
            GameFlowTests.ApplyAndPass(game, buy);

            Assert.Equal(50 - (def.Cost - 1), player.Money); // Pete's $1 discount
            Assert.Equal(0, game.State.BmOnlyActionsRemaining);
        }
    }
}
