using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;
using static LemonadeWars.Engine.Tests.BotScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>Moves no player would want are dropped before a search spends time on them.</summary>
    public class MoveFilterTests
    {
        [Fact]
        public void WastedNumberPlacementsAreFilteredFromSearch()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben" }, seed: 5);
            var me = game.State.Players[0];
            me.Stands.Add(new StandInstance
                { InstanceId = 9001, StandTypeId = "classic", Shape = Shape.Square }); // sells 4,5
            me.Stands.Add(new StandInstance
                { InstanceId = 9002, StandTypeId = "bargain", Shape = Shape.Square }); // sells 1,2,3

            int pushySalesman4 = game.State.BlackMarketDeck
                .First(id => game.State.BlackMarketInstances[id].DefId == "pushy-salesman-4");
            game.State.Market.Insert(0, pushySalesman4);

            var ontoClassic = new BuyBlackMarket { MarketIndex = 0, TargetStandInstanceId = 9001 };
            var ontoBargain = new BuyBlackMarket { MarketIndex = 0, TargetStandInstanceId = 9002 };

            // A 4 onto a stand already selling on 4 is objectively wasted; the same
            // card onto the bargain stand is a real upgrade.
            Assert.True(MoveFilters.ObjectivelyWasted(game, 0, ontoClassic));
            Assert.False(MoveFilters.ObjectivelyWasted(game, 0, ontoBargain));

            var kept = MoveFilters.DropWasted(game, 0, new GameAction[] { ontoClassic, ontoBargain });
            Assert.Same(ontoBargain, Assert.Single(kept));
            // With only wasted options, they stay.
            Assert.Single(MoveFilters.DropWasted(game, 0, new GameAction[] { ontoClassic }));

            // Spiked Lemonade duplicating a pour number we already cover is wasted too,
            // unless the secret Pour Master lord collects duplicates.
            int coveredPour = game.PourNumbersOf(me).First();
            int spikedLemonade = game.State.BlackMarketDeck
                .First(id => game.State.BlackMarketInstances[id].DefId == $"spiked-lemonade-{coveredPour}");
            game.State.Market.Insert(0, spikedLemonade);
            var duplicatePour = new BuyBlackMarket { MarketIndex = 0 };
            Assert.True(MoveFilters.ObjectivelyWasted(game, 0, duplicatePour));
            me.LemonLordKept.Add("pour-master");
            Assert.False(MoveFilters.ObjectivelyWasted(game, 0, duplicatePour));
        }

        [Fact]
        public void MoneyAttacksOnABrokeVictimAreFilteredFromSearch()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben" }, seed: 5);
            var victim = game.State.Players[1];
            AddStand(victim, "bargain");

            int CardId(string defId) =>
                game.State.LemonInstances.First(instance => instance.Value.DefId == defId).Key;

            var taxes = new PlayLemonCard { CardInstanceId = CardId("taxes"), TargetPlayerId = 1 };
            victim.Money = 0;
            Assert.True(MoveFilters.ObjectivelyWasted(game, 0, taxes));   // $0 recovered = no-op
            victim.Money = 1;
            Assert.False(MoveFilters.ObjectivelyWasted(game, 0, taxes));  // bad target, not a wasted one

            // The other two cash grabs follow the same rule; card steals are untouched.
            foreach (string defId in new[] { "hoa-violation", "sharing-is-caring" })
            {
                var grab = new PlayLemonCard { CardInstanceId = CardId(defId), TargetPlayerId = 1 };
                victim.Money = 0;
                Assert.True(MoveFilters.ObjectivelyWasted(game, 0, grab));
                victim.Money = 3;
                Assert.False(MoveFilters.ObjectivelyWasted(game, 0, grab));
            }
            victim.Money = 0;
            Assert.False(MoveFilters.ObjectivelyWasted(game, 0,
                new PlayLemonCard { CardInstanceId = CardId("smear-campaign"), TargetPlayerId = 1 }));

            // The broke victim drops out of the shortlist; the paying one survives.
            var taxesOnPayer = new PlayLemonCard { CardInstanceId = CardId("taxes"), TargetPlayerId = 0 };
            game.State.Players[0].Money = 8;
            AddStand(game.State.Players[0], "bargain");
            var kept = MoveFilters.DropWasted(game, 1, new GameAction[] { taxes, taxesOnPayer });
            Assert.Same(taxesOnPayer, Assert.Single(kept));
        }
    }
}
