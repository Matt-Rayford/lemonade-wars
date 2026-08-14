using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Scope of the non-table sale rolls: Trade Winds ("+1 sale roll for this Stand
    /// only", black-market-cards.json) and Spoiled Rotten ("a free sale roll for only
    /// themselves", rulebook p12). Neither may pay out any other stand or any other
    /// player.
    /// </summary>
    public class RollScopeTests
    {
        private static readonly string[] FourPlayers = { "Ana", "Ben", "Cal", "Dee" };

        private static Game ReadyToPlay(ulong seed = 17)
        {
            var game = Game.Create(TestData.Db, FourPlayers, seed);
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
            GameFlowTests.PassAll(game);
            return game;
        }

        private static int Seat(Game game, int offset) =>
            (game.State.ActivePlayer + offset) % game.State.Players.Count;

        /// <summary>Pull a specific Black Market def out of the deck/market and equip it directly.</summary>
        private static int RigEquip(Game game, int playerId, string defId, int standInstanceId)
        {
            var s = game.State;
            int found = s.BlackMarketDeck.Concat(s.Market)
                .First(id => s.BlackMarketInstances[id].DefId == defId);
            s.BlackMarketDeck.Remove(found);
            s.Market.Remove(found);
            s.Players[playerId].Stands.First(st => st.InstanceId == standInstanceId)
                .Equipped.Add(found);
            return found;
        }

        private static int GiveCard(Game game, int playerId, string defId)
        {
            var s = game.State;
            int found = s.LemonDeck.Concat(s.LemonDiscard)
                .Concat(s.Players.SelectMany(p => p.Hand))
                .First(id => s.LemonInstances[id].DefId == defId);
            s.LemonDeck.Remove(found);
            s.LemonDiscard.Remove(found);
            foreach (var p in s.Players)
            {
                p.Hand.Remove(found);
            }
            s.Players[playerId].Hand.Add(found);
            return found;
        }

        private static void StripHands(Game game, params string[] defIds)
        {
            var s = game.State;
            foreach (var p in s.Players)
            {
                var strip = p.Hand.Where(id => defIds.Contains(s.LemonInstances[id].DefId)).ToList();
                foreach (int id in strip)
                {
                    p.Hand.Remove(id);
                    s.LemonDeck.Add(id);
                }
            }
        }

        /// <summary>Keep every player's power pour away from the die values a test rigs.</summary>
        private static void ParkPourNumbers(Game game)
        {
            foreach (var p in game.State.Players)
            {
                p.Turf.PowerPourNumber = 6;
            }
        }

        /// <summary>Pass everyone out of the CURRENT window only (later windows stay open).</summary>
        private static void PassCurrentWindow(Game game)
        {
            int revision = game.State.LastWindowRevision;
            while (game.State.AwaitingResponse.Count > 0 &&
                   game.State.LastWindowRevision == revision)
            {
                game.Apply(new PassWindow { PlayerId = game.State.AwaitingResponse[0] });
            }
        }

        [Fact]
        public void TradeWindsRollSellsOnlyTheEquippedStand()
        {
            var game = ReadyToPlay();
            var s = game.State;
            StripHands(game, "tantrum", "out-of-stock");
            ParkPourNumbers(game);
            int a = s.ActivePlayer;
            var ana = s.Players[a];
            RigEquip(game, a, "trade-winds", ana.Stands[0].InstanceId);
            foreach (var p in s.Players)
            {
                p.Money = 20;
            }

            // Out of Stock in another hand keeps each roll window open for rigging.
            int holder = Seat(game, 1);
            GiveCard(game, holder, "out-of-stock");

            game.Apply(new EndTurn { PlayerId = a });
            Assert.Equal(RollPurpose.TurnSale, s.PendingRoll!.Purpose);
            s.PendingRoll.Value = 5; // bargains sell on 1-3, pours parked on 6: silent
            PassCurrentWindow(game);

            // Now the Trade Winds roll for the equipped stand only.
            Assert.NotNull(s.PendingRoll);
            Assert.Equal(RollPurpose.TradeWinds, s.PendingRoll!.Purpose);
            Assert.Equal(ana.Stands[0].InstanceId, s.PendingRoll.StandInstanceId);
            s.PendingRoll.Value = 2; // every bargain on the table would sell on a table roll
            PassCurrentWindow(game);

            // Only Ana's equipped stand sold: +$1. Her second bargain and every other
            // player's stands stay silent.
            Assert.Equal(21, ana.Money);
            foreach (var p in s.Players)
            {
                if (p.PlayerId != a)
                {
                    Assert.Equal(20, p.Money);
                }
            }
        }

        [Fact]
        public void SpoiledRottenRollSellsOnlyForTheHolder()
        {
            var game = ReadyToPlay();
            var s = game.State;
            StripHands(game, "tantrum", "out-of-stock");
            ParkPourNumbers(game);
            int a = s.ActivePlayer;
            int next = Seat(game, 1);
            s.SpoiledRottenHolder = next;
            foreach (var p in s.Players)
            {
                p.Money = 20;
            }

            int holder = Seat(game, 2);
            GiveCard(game, holder, "out-of-stock");

            game.Apply(new EndTurn { PlayerId = a });
            Assert.Equal(RollPurpose.TurnSale, s.PendingRoll!.Purpose);
            s.PendingRoll.Value = 5; // silent table roll
            PassCurrentWindow(game);

            // Next player's turn starts with the Spoiled Rotten free roll.
            Assert.Equal(next, s.ActivePlayer);
            Assert.NotNull(s.PendingRoll);
            Assert.Equal(RollPurpose.SpoiledRotten, s.PendingRoll!.Purpose);
            Assert.Equal(next, s.PendingRoll.RollerId);
            s.PendingRoll.Value = 2; // both of everyone's bargains match on a table roll
            PassCurrentWindow(game);

            // Only the holder's two bargains sold (+$2); everyone else is untouched.
            Assert.Equal(22, s.Players[next].Money);
            foreach (var p in s.Players)
            {
                if (p.PlayerId != next)
                {
                    Assert.Equal(20, p.Money);
                }
            }
        }
    }
}
