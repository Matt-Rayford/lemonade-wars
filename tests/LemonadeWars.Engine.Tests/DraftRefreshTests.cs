using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    public class DraftRefreshTests
    {
        [Fact]
        public void DraftVisitAllowsOneMarketRefreshAfterStand()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben", "Cai", "Dee" }, 5);
            foreach (var p in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = p.PlayerId,
                    KeepTitleIds = p.LemonLordDealt.Take(2).ToList(),
                });
            }
            var s = game.State;
            Assert.Equal(GameStage.InitialBuys, s.Stage);
            int buyer = s.InitialBuyQueue[0];

            // Not offered before the mandatory Stand.
            Assert.DoesNotContain(game.LegalMovesFor(buyer), m => m is RefreshMarket);

            game.Apply(game.LegalMovesFor(buyer).OfType<InitialBuyStand>().First());

            // Offered now; costs $1 and deals a fresh market row.
            Assert.Contains(game.LegalMovesFor(buyer), m => m is RefreshMarket);
            var marketBefore = s.Market.ToList();
            int moneyBefore = s.Players[buyer].Money;
            game.Apply(new RefreshMarket { PlayerId = buyer });
            Assert.Equal(moneyBefore - TestData.Db.Config.BlackMarketRefreshCost,
                s.Players[buyer].Money);
            Assert.NotEqual(marketBefore, s.Market);

            // Once per visit.
            Assert.DoesNotContain(game.LegalMovesFor(buyer), m => m is RefreshMarket);
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new RefreshMarket { PlayerId = buyer }));

            // The next visitor gets their own refresh after their Stand.
            game.Apply(new InitialBuyEnd { PlayerId = buyer });
            int next = s.InitialBuyQueue[0];
            game.Apply(game.LegalMovesFor(next).OfType<InitialBuyStand>().First());
            Assert.Contains(game.LegalMovesFor(next), m => m is RefreshMarket);
        }
    }
}
