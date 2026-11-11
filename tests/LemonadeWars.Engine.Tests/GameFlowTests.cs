using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;

namespace LemonadeWars.Engine.Tests
{
    public class GameFlowTests
    {
        // ---------------------------------------------------------- purchases

        [Fact]
        public void StandPricesEscalateWithOwnership()
        {
            var game = DraftedGame(seed: 42);
            int active = game.State.ActivePlayer;
            var player = game.State.Players[active];

            // Everyone owns 2 bargain stands from the draft: classic costs $3 + 2×$1 = $5.
            Assert.Equal(5, game.StandPrice(active, "classic"));
            int before = player.Money;
            game.Apply(new BuyStand { PlayerId = active, StandTypeId = "classic" });
            Assert.Equal(before - 5, player.Money);
            Assert.Equal(1, game.State.ActionsRemaining);

            // Third stand owned: gourmet now costs $4 + 3×$1 = $7.
            Assert.Equal(7, game.StandPrice(active, "gourmet"));
        }

        [Fact]
        public void ActionsRunOutAfterTwo()
        {
            var game = DraftedGame(seed: 42);
            int active = game.State.ActivePlayer;
            game.Apply(new DrawLemonCard { PlayerId = active });
            game.Apply(new DrawLemonCard { PlayerId = active });
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new DrawLemonCard { PlayerId = active }));
        }

        /// <summary>The action log has to say what each action was spent on, so the events carry the cause.</summary>
        [Fact]
        public void DrawsAndMarketRefreshCarryTheirCause()
        {
            var game = DraftedGame(seed: 42);
            int active = game.State.ActivePlayer;

            // The paid sweep names the player; the refill after a purchase stays anonymous.
            var refreshed = game.Apply(new RefreshMarket { PlayerId = active })
                .OfType<MarketRefilled>().Single();
            Assert.Equal(active, refreshed.RefreshedByPlayerId);

            // Without the stamp, a turn spent drawing is indistinguishable from a passed turn.
            var byAction = game.Apply(new DrawLemonCard { PlayerId = active })
                .OfType<CardDrawn>().Single();
            Assert.Equal(DrawReason.Action, byAction.Reason);

            // The automatic top-of-turn draw has its own reason, so it reads as the freebie it is.
            var events = new List<GameEvent>(game.Apply(new EndTurn { PlayerId = active }));
            PassWindowsOnly(game, collectEventsInto: events);
            Assert.Contains(events.OfType<CardDrawn>(), d => d.Reason == DrawReason.TurnStart);
        }

        [Fact]
        public void BraggingRightsEscalateAndLimitOncePerTurn()
        {
            var game = DraftedGame(seed: 42);
            int active = game.State.ActivePlayer;
            var player = game.State.Players[active];
            player.Money = 100;

            game.Apply(new BuyBraggingRights { PlayerId = active });
            Assert.Equal(100 - 16, player.Money);
            Assert.Equal(1, player.InGameVictoryPoints);

            // Second one this turn is illegal even with actions left.
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new BuyBraggingRights { PlayerId = active }));

            ApplyAndPass(game, new EndTurn { PlayerId = active });

            // Next player pays the escalated price.
            int next = game.State.ActivePlayer;
            var nextPlayer = game.State.Players[next];
            nextPlayer.Money = 100;
            game.Apply(new BuyBraggingRights { PlayerId = next });
            Assert.Equal(100 - 18, nextPlayer.Money);
        }

        [Fact]
        public void MarketRefreshIsOncePerTurnAndCostsOneDollar()
        {
            var game = DraftedGame(seed: 42);
            int active = game.State.ActivePlayer;
            var player = game.State.Players[active];
            int before = player.Money;
            var beforeMarket = game.State.Market.ToList();

            game.Apply(new RefreshMarket { PlayerId = active });
            Assert.Equal(before - 1, player.Money);
            Assert.Equal(2, game.State.ActionsRemaining); // free action
            Assert.Equal(4, game.State.Market.Count);
            Assert.NotEqual(beforeMarket, game.State.Market);

            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new RefreshMarket { PlayerId = active }));
        }

        // ------------------------------------------------- selling and the end

        [Fact]
        public void SaleRollPaysMatchingStandsAndPowerPours()
        {
            var game = DraftedGame(seed: 42);
            var state = game.State;
            var moneyBefore = state.Players.Select(p => p.Money).ToList();

            var events = game.Apply(new EndTurn { PlayerId = state.ActivePlayer });
            int roll = events.OfType<SaleRolled>().Single().Value;
            PassAll(game); // decline any Out of Stock windows so payouts land

            foreach (var player in state.Players)
            {
                int expected = moneyBefore[player.PlayerId];
                // Everyone drafted 2 bargain stands (sell on 1-3 for $1).
                if (roll <= 3)
                {
                    expected += 2;
                }
                if (player.Turf.PowerPourNumber == roll)
                {
                    expected += 1;
                }
                Assert.Equal(expected, player.Money);
            }
        }

        [Fact]
        public void ThreeVictoryPointsTriggerFinalRoundThenGameEnds()
        {
            var game = DraftedGame(seed: 42);
            var state = game.State;
            int playerCount = state.Players.Count;

            // Give the first player 3 bragging rights over 3 turns (cheating money in for speed).
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < playerCount; i++)
                {
                    var active = state.Players[state.ActivePlayer];
                    if (active.PlayerId == state.FirstPlayer)
                    {
                        active.Money = 100;
                        game.Apply(new BuyBraggingRights { PlayerId = active.PlayerId });
                    }
                    if (state.Stage == GameStage.Finished)
                    {
                        break;
                    }
                    ApplyAndPass(game, new EndTurn { PlayerId = active.PlayerId });
                }
            }

            // First player hit 3 VP at end of their 3rd turn -> final round for the rest.
            Assert.Equal(GameStage.Finished, state.Stage);
            Assert.Equal(state.FirstPlayer, state.EndTriggeredBy);
            Assert.Contains(state.FirstPlayer, state.Winners);

            // Everyone got the same number of turns: game ended exactly before wrapping to first player.
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new EndTurn { PlayerId = state.ActivePlayer }));
        }

        // ------------------------------------------------------- determinism

        [Fact]
        public void SameSeedSameActionsSameState()
        {
            var firstRun = PlayScriptedGame(123);
            var secondRun = PlayScriptedGame(123);
            Assert.Equal(firstRun, secondRun);
        }

        [Fact]
        public void DifferentSeedsDiverge()
        {
            Assert.NotEqual(PlayScriptedGame(1), PlayScriptedGame(2));
        }

        private static string PlayScriptedGame(ulong seed)
        {
            var game = DraftedGame(seed);
            var state = game.State;
            for (int turns = 0; turns < 12 && state.Stage != GameStage.Finished; turns++)
            {
                int active = state.ActivePlayer;
                ApplyAndPass(game, new DrawLemonCard { PlayerId = active });
                if (state.Players[active].Money >= game.StandPrice(active, "bargain") &&
                    state.StandSupply["bargain"].Count > 0)
                {
                    ApplyAndPass(game, new BuyStand { PlayerId = active, StandTypeId = "bargain" });
                }
                ApplyAndPass(game, new EndTurn { PlayerId = active });
            }
            return game.SnapshotJson();
        }
    }
}
