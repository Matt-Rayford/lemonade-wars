using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    public class GameFlowTests
    {
        private static readonly string[] FourPlayers = { "Ana", "Ben", "Cal", "Dee" };

        private static Game NewGame(ulong seed = 42, string[]? names = null) =>
            Game.Create(TestData.Db, names ?? FourPlayers, seed);

        /// <summary>Run every player through Lemon Lord keep-2 and a minimal snake draft (bargain stand, no BM).</summary>
        private static Game ReadyToPlay(ulong seed = 42)
        {
            var game = NewGame(seed);
            foreach (var p in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = p.PlayerId,
                    KeepTitleIds = p.LemonLordDealt.Take(2).ToList(),
                });
            }
            while (game.State.Stage == GameStage.InitialBuys)
            {
                int buyer = game.State.InitialBuyQueue[0];
                game.Apply(new InitialBuyStand { PlayerId = buyer, StandTypeId = "bargain" });
                game.Apply(new InitialBuyEnd { PlayerId = buyer });
            }
            return game;
        }

        // -------------------------------------------------------------- setup

        [Fact]
        public void SetupDealsHandsMoneyTurfsAndMarkets()
        {
            var game = NewGame();
            var s = game.State;

            Assert.Equal(GameStage.ChoosingLemonLords, s.Stage);
            Assert.All(s.Players, p => Assert.Equal(5, p.Hand.Count));
            Assert.All(s.Players, p => Assert.Equal(3, p.LemonLordDealt.Count));

            // Starting hands never contain a Timeout (rulebook p5).
            Assert.All(s.Players, p => Assert.All(p.Hand, id =>
                Assert.NotEqual("timeout", s.LemonInstances[id].DefId)));

            // Turf power pour numbers are unique.
            var pours = s.Players.Select(p => p.Turf.PowerPourNumber).ToList();
            Assert.Equal(pours.Count, pours.Distinct().Count());

            // First-player bonus: $15 + (n-1-position) going clockwise from first.
            int n = s.Players.Count;
            for (int offset = 0; offset < n; offset++)
            {
                var p = s.Players[(s.FirstPlayer + offset) % n];
                Assert.Equal(15 + (n - 1 - offset), p.Money);
            }

            Assert.Equal(4, s.Market.Count);           // 4 face-up BM cards (4p game)
            Assert.Equal(n + 1, s.FirstDibsRow.Count); // players + 1 titles
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
            var p0 = game.State.Players[0];

            Assert.Throws<InvalidActionException>(() => game.Apply(new ChooseLemonLords
            {
                PlayerId = 0,
                KeepTitleIds = new List<string> { p0.LemonLordDealt[0] }, // only 1
            }));
            Assert.Throws<InvalidActionException>(() => game.Apply(new ChooseLemonLords
            {
                PlayerId = 0,
                KeepTitleIds = new List<string> { p0.LemonLordDealt[0], "not-yours" },
            }));

            foreach (var p in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = p.PlayerId,
                    KeepTitleIds = p.LemonLordDealt.Take(2).ToList(),
                });
            }
            Assert.Equal(GameStage.InitialBuys, game.State.Stage);
        }

        [Fact]
        public void InitialBuysSnakeAndRequireAStand()
        {
            var game = NewGame();
            foreach (var p in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = p.PlayerId,
                    KeepTitleIds = p.LemonLordDealt.Take(2).ToList(),
                });
            }

            var s = game.State;
            int n = s.Players.Count;
            // Queue is first player clockwise, then reversed (snake).
            var expected = Enumerable.Range(0, n).Select(i => (s.FirstPlayer + i) % n).ToList();
            expected.AddRange(Enumerable.Reverse(expected).ToList());
            Assert.Equal(expected, s.InitialBuyQueue);

            int first = s.InitialBuyQueue[0];
            // Cannot pass without the mandatory Stand.
            Assert.Throws<InvalidActionException>(() => game.Apply(new InitialBuyEnd { PlayerId = first }));
            // Wrong player cannot jump the queue.
            int notFirst = (first + 1) % n;
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new InitialBuyStand { PlayerId = notFirst, StandTypeId = "bargain" }));

            game.Apply(new InitialBuyStand { PlayerId = first, StandTypeId = "classic" });
            Assert.Single(s.Players[first].Stands);
            game.Apply(new InitialBuyEnd { PlayerId = first });
            Assert.Equal(2 * n - 1, s.InitialBuyQueue.Count);
        }

        [Fact]
        public void FinishingInitialBuysStartsFirstTurnWithADraw()
        {
            var game = ReadyToPlay();
            var s = game.State;
            Assert.Equal(GameStage.Playing, s.Stage);
            Assert.Equal(s.FirstPlayer, s.ActivePlayer);
            Assert.Equal(TurnPhase.Play, s.Phase);
            Assert.Equal(2, s.ActionsRemaining);
            // First player drew their turn-start card: 5 + 1... plus 2 stands bought each.
            Assert.Equal(6, s.Players[s.FirstPlayer].Hand.Count);
        }

        // ---------------------------------------------------------- purchases

        [Fact]
        public void StandPricesEscalateWithOwnership()
        {
            var game = ReadyToPlay();
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
            var game = ReadyToPlay();
            int active = game.State.ActivePlayer;
            game.Apply(new DrawLemonCard { PlayerId = active });
            game.Apply(new DrawLemonCard { PlayerId = active });
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new DrawLemonCard { PlayerId = active }));
        }

        [Fact]
        public void BraggingRightsEscalateAndLimitOncePerTurn()
        {
            var game = ReadyToPlay();
            int active = game.State.ActivePlayer;
            var player = game.State.Players[active];
            player.Money = 100; // test money

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
            var game = ReadyToPlay();
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

        // ------------------------------------------------------------ selling

        [Fact]
        public void SaleRollPaysMatchingStandsAndPowerPours()
        {
            var game = ReadyToPlay();
            var s = game.State;
            var moneyBefore = s.Players.Select(p => p.Money).ToList();

            var events = game.Apply(new EndTurn { PlayerId = s.ActivePlayer });
            int roll = events.OfType<SaleRolled>().Single().Value;
            PassAll(game); // decline any Out of Stock windows so payouts land

            foreach (var p in s.Players)
            {
                int expected = moneyBefore[p.PlayerId];
                // Everyone drafted 2 bargain stands (sell on 1-3 for $1).
                if (roll <= 3)
                {
                    expected += 2;
                }
                if (p.Turf.PowerPourNumber == roll)
                {
                    expected += 1;
                }
                Assert.Equal(expected, p.Money);
            }
        }

        [Fact]
        public void ThreeVictoryPointsTriggerFinalRoundThenGameEnds()
        {
            var game = ReadyToPlay();
            var s = game.State;
            int n = s.Players.Count;

            // Give the first player 3 bragging rights over 3 turns (cheating money in for speed).
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < n; i++)
                {
                    var active = s.Players[s.ActivePlayer];
                    if (active.PlayerId == s.FirstPlayer)
                    {
                        active.Money = 100;
                        game.Apply(new BuyBraggingRights { PlayerId = active.PlayerId });
                    }
                    if (s.Stage == GameStage.Finished)
                    {
                        break;
                    }
                    ApplyAndPass(game, new EndTurn { PlayerId = active.PlayerId });
                }
            }

            // First player hit 3 VP at end of their 3rd turn -> final round for the rest.
            Assert.Equal(GameStage.Finished, s.Stage);
            Assert.Equal(s.FirstPlayer, s.EndTriggeredBy);
            Assert.Contains(s.FirstPlayer, s.Winners);

            // Everyone got the same number of turns: game ended exactly before wrapping to first player.
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new EndTurn { PlayerId = s.ActivePlayer }));
        }

        // ------------------------------------------------------- determinism

        [Fact]
        public void SameSeedSameActionsSameState()
        {
            var a = PlayScriptedGame(123);
            var b = PlayScriptedGame(123);
            Assert.Equal(a, b);
        }

        [Fact]
        public void DifferentSeedsDiverge()
        {
            Assert.NotEqual(PlayScriptedGame(1), PlayScriptedGame(2));
        }

        /// <summary>Apply an action, then settle all windows/decisions with default choices.</summary>
        internal static void ApplyAndPass(Game game, GameAction action)
        {
            game.Apply(action);
            PassAll(game);
        }

        /// <summary>
        /// Everyone declines to respond, and mandatory decisions get deterministic default
        /// answers (discard newest), until the game is quiet.
        /// </summary>
        internal static void PassAll(Game game)
        {
            var s = game.State;
            while (s.AwaitingResponse.Count > 0 || s.PendingDecisions.Count > 0)
            {
                if (s.AwaitingResponse.Count > 0)
                {
                    game.Apply(new PassWindow { PlayerId = s.AwaitingResponse[0] });
                    continue;
                }
                var decision = s.PendingDecisions[0];
                var player = s.Players[decision.PlayerId];
                switch (decision.Kind)
                {
                    case DecisionKind.DiscardToHandLimit:
                    case DecisionKind.WhiniestBabyDiscard:
                        game.Apply(new SubmitDiscard
                        {
                            PlayerId = player.PlayerId,
                            InstanceIds = player.Hand
                                .Skip(player.Hand.Count - decision.RequiredCount).ToList(),
                        });
                        break;
                    case DecisionKind.TimeoutFine:
                        game.Apply(new SubmitTimeoutPayment { PlayerId = player.PlayerId });
                        break;
                    case DecisionKind.FreePlayOffer:
                        game.Apply(new SkipFreePlay { PlayerId = player.PlayerId });
                        break;
                    default:
                        throw new System.InvalidOperationException(
                            $"PassAll cannot default {decision.Kind}; answer it explicitly in the test.");
                }
            }
        }

        /// <summary>Plays a fixed action script over a seeded game and returns the final snapshot.</summary>
        private static string PlayScriptedGame(ulong seed)
        {
            var game = ReadyToPlay(seed);
            var s = game.State;
            for (int turns = 0; turns < 12 && s.Stage != GameStage.Finished; turns++)
            {
                int active = s.ActivePlayer;
                ApplyAndPass(game, new DrawLemonCard { PlayerId = active });
                if (s.Players[active].Money >= game.StandPrice(active, "bargain") &&
                    s.StandSupply["bargain"].Count > 0)
                {
                    ApplyAndPass(game, new BuyStand { PlayerId = active, StandTypeId = "bargain" });
                }
                ApplyAndPass(game, new EndTurn { PlayerId = active });
            }
            return game.SnapshotJson();
        }
    }
}
