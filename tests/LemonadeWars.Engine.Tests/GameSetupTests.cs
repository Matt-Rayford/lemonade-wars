using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.GameFlowSetup;

namespace LemonadeWars.Engine.Tests
{
    public class GameSetupTests
    {
        [Fact]
        public void SetupDealsHandsMoneyTurfsAndMarkets()
        {
            var game = NewGame();
            var state = game.State;

            Assert.Equal(GameStage.ChoosingLemonLords, state.Stage);
            Assert.All(state.Players, p => Assert.Equal(5, p.Hand.Count));
            Assert.All(state.Players, p => Assert.Equal(3, p.LemonLordDealt.Count));

            // Starting hands never contain a Timeout (rulebook p5).
            Assert.All(state.Players, p => Assert.All(p.Hand, id =>
                Assert.NotEqual("timeout", state.LemonInstances[id].DefId)));

            var pourNumbers = state.Players.Select(p => p.Turf.PowerPourNumber).ToList();
            Assert.Equal(pourNumbers.Count, pourNumbers.Distinct().Count());

            // First-player bonus: $15 + (n-1-position) going clockwise from first.
            int playerCount = state.Players.Count;
            for (int offset = 0; offset < playerCount; offset++)
            {
                var player = state.Players[(state.FirstPlayer + offset) % playerCount];
                Assert.Equal(15 + (playerCount - 1 - offset), player.Money);
            }

            Assert.Equal(4, state.Market.Count);                     // 4 face-up BM cards (4p game)
            Assert.Equal(playerCount + 1, state.FirstDibsRow.Count); // players + 1 titles
        }

        [Fact]
        public void TwoPlayerGameDealsFiveMarketCards()
        {
            var game = NewGame(names: new[] { "Ana", "Ben" });
            Assert.Equal(5, game.State.Market.Count);
        }

        [Fact]
        public void LemonLordChoiceValidatesAndAdvances()
        {
            var game = NewGame();
            var firstSeat = game.State.Players[0];

            Assert.Throws<InvalidActionException>(() => game.Apply(new ChooseLemonLords
            {
                PlayerId = 0,
                KeepTitleIds = new List<string> { firstSeat.LemonLordDealt[0] }, // only 1
            }));
            Assert.Throws<InvalidActionException>(() => game.Apply(new ChooseLemonLords
            {
                PlayerId = 0,
                KeepTitleIds = new List<string> { firstSeat.LemonLordDealt[0], "not-yours" },
            }));

            KeepFirstTwoLemonLords(game);
            Assert.Equal(GameStage.InitialBuys, game.State.Stage);
        }

        [Fact]
        public void InitialBuysSnakeAndRequireAStand()
        {
            var game = NewGame();
            KeepFirstTwoLemonLords(game);

            var state = game.State;
            int playerCount = state.Players.Count;
            // Queue is first player clockwise, then reversed (snake).
            var expected = Enumerable.Range(0, playerCount)
                .Select(i => (state.FirstPlayer + i) % playerCount).ToList();
            expected.AddRange(Enumerable.Reverse(expected).ToList());
            Assert.Equal(expected, state.InitialBuyQueue);

            int first = state.InitialBuyQueue[0];
            // Cannot pass without the mandatory Stand.
            Assert.Throws<InvalidActionException>(() => game.Apply(new InitialBuyEnd { PlayerId = first }));
            // Wrong player cannot jump the queue.
            int notFirst = (first + 1) % playerCount;
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new InitialBuyStand { PlayerId = notFirst, StandTypeId = "bargain" }));

            game.Apply(new InitialBuyStand { PlayerId = first, StandTypeId = "classic" });
            Assert.Single(state.Players[first].Stands);
            game.Apply(new InitialBuyEnd { PlayerId = first });
            Assert.Equal(2 * playerCount - 1, state.InitialBuyQueue.Count);
        }

        [Fact]
        public void DraftVisitAllowsOneMarketRefreshAfterStand()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben", "Cai", "Dee" }, 5);
            KeepFirstTwoLemonLords(game);
            var state = game.State;
            Assert.Equal(GameStage.InitialBuys, state.Stage);
            int buyer = state.InitialBuyQueue[0];

            // Not offered before the mandatory Stand.
            Assert.DoesNotContain(game.LegalMovesFor(buyer), m => m is RefreshMarket);

            game.Apply(game.LegalMovesFor(buyer).OfType<InitialBuyStand>().First());

            // Offered now; costs $1 and deals a fresh market row.
            Assert.Contains(game.LegalMovesFor(buyer), m => m is RefreshMarket);
            var marketBefore = state.Market.ToList();
            int moneyBefore = state.Players[buyer].Money;
            game.Apply(new RefreshMarket { PlayerId = buyer });
            Assert.Equal(moneyBefore - TestData.Db.Config.BlackMarketRefreshCost,
                state.Players[buyer].Money);
            Assert.NotEqual(marketBefore, state.Market);

            // Once per visit.
            Assert.DoesNotContain(game.LegalMovesFor(buyer), m => m is RefreshMarket);
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new RefreshMarket { PlayerId = buyer }));

            // The next visitor gets their own refresh after their Stand.
            game.Apply(new InitialBuyEnd { PlayerId = buyer });
            int next = state.InitialBuyQueue[0];
            game.Apply(game.LegalMovesFor(next).OfType<InitialBuyStand>().First());
            Assert.Contains(game.LegalMovesFor(next), m => m is RefreshMarket);
        }

        [Fact]
        public void FinishingInitialBuysStartsFirstTurnWithADraw()
        {
            var game = ReadyToPlay();
            var state = game.State;
            Assert.Equal(GameStage.Playing, state.Stage);
            Assert.Equal(state.FirstPlayer, state.ActivePlayer);
            Assert.Equal(TurnPhase.Play, state.Phase);
            Assert.Equal(2, state.ActionsRemaining);
            // The 5 dealt cards plus the first player's turn-start draw.
            Assert.Equal(6, state.Players[state.FirstPlayer].Hand.Count);
        }
    }
}
