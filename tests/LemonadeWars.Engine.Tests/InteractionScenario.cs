using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// A drafted game in its first Play phase, plus rigging that moves card instances
    /// between zones so a scenario never depends on what the seed dealt.
    /// </summary>
    internal static class InteractionScenario
    {
        public static readonly string[] FourPlayers = { "Ana", "Ben", "Cal", "Dee" };
        public static readonly string[] TwoPlayers = { "Ana", "Ben" };

        /// <summary>Lemon Lords kept and a Bargain stand bought on every draft visit.</summary>
        public static Game ReadyToPlay(ulong seed = 7, string[]? names = null)
        {
            var game = Game.Create(TestData.Db, names ?? FourPlayers, seed);
            KeepFirstTwoLemonLords(game);
            while (game.State.Stage == GameStage.InitialBuys)
            {
                int buyer = game.State.InitialBuyQueue[0];
                game.Apply(new InitialBuyStand { PlayerId = buyer, StandTypeId = "bargain" });
                game.Apply(new InitialBuyEnd { PlayerId = buyer });
            }
            GameFlowTests.PassAll(game);
            return game;
        }

        public static void KeepFirstTwoLemonLords(Game game)
        {
            foreach (var player in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = player.PlayerId,
                    KeepTitleIds = player.LemonLordDealt.Take(2).ToList(),
                });
            }
        }

        public static int ActivePlayer(Game game) => game.State.ActivePlayer;

        public static int SeatAfterActive(Game game, int offset) =>
            (game.State.ActivePlayer + offset) % game.State.Players.Count;

        // --------------------------------------------------------- lemon cards

        public static int GiveCard(Game game, int playerId, string defId)
        {
            int card = TakeLemonCard(game, defId);
            game.State.Players[playerId].Hand.Add(card);
            return card;
        }

        public static int PutTantrumInPile(Game game, int playerId)
        {
            int tantrum = TakeLemonCard(game, "tantrum");
            game.State.Players[playerId].TantrumPile.Add(new TantrumRecord
            {
                InstanceId = tantrum,
                GainSeq = game.State.NextTantrumGainSeq++,
            });
            return tantrum;
        }

        public static int PutInLemonDiscard(Game game, string defId)
        {
            int card = TakeLemonCard(game, defId);
            game.State.LemonDiscard.Add(card);
            return card;
        }

        /// <summary>Looks in the deck first, then the discard, then each hand in seat order.</summary>
        private static int TakeLemonCard(Game game, string defId)
        {
            var state = game.State;
            int FirstCopyIn(List<int> zone) =>
                zone.FirstOrDefault(id => state.LemonInstances[id].DefId == defId);

            int found = FirstCopyIn(state.LemonDeck);
            if (found != 0)
            {
                state.LemonDeck.Remove(found);
            }
            else if ((found = FirstCopyIn(state.LemonDiscard)) != 0)
            {
                state.LemonDiscard.Remove(found);
            }
            else
            {
                foreach (var player in state.Players)
                {
                    found = FirstCopyIn(player.Hand);
                    if (found != 0)
                    {
                        player.Hand.Remove(found);
                        break;
                    }
                }
            }
            Assert.NotEqual(0, found);
            return found;
        }

        /// <summary>The stripped cards go to the bottom of the deck.</summary>
        public static void StripHands(Game game, params string[] defIds)
        {
            var state = game.State;
            foreach (var player in state.Players)
            {
                var stripped = player.Hand
                    .Where(id => defIds.Contains(state.LemonInstances[id].DefId)).ToList();
                foreach (int id in stripped)
                {
                    player.Hand.Remove(id);
                    state.LemonDeck.Add(id);
                }
            }
        }

        public static void StripAttackReactions(Game game) =>
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");

        /// <summary>To the bottom of the deck, so a draw the test triggers cannot hit one.</summary>
        public static void BuryTimeouts(Game game)
        {
            var state = game.State;
            var timeouts = state.LemonDeck
                .Where(id => state.LemonInstances[id].DefId == "timeout").ToList();
            foreach (int id in timeouts)
            {
                state.LemonDeck.Remove(id);
                state.LemonDeck.Add(id);
            }
        }

        // ------------------------------------------------- black market cards

        /// <summary>Equips whichever card tops the Black Market deck.</summary>
        public static int RigTurfEquip(Game game, int playerId)
        {
            var state = game.State;
            int card = state.BlackMarketDeck[0];
            state.BlackMarketDeck.RemoveAt(0);
            state.Players[playerId].Turf.Equipped.Add(card);
            return card;
        }

        public static BlackMarketCardDef BlackMarketDefOf(Game game, int instanceId) =>
            TestData.Db.BlackMarket(game.State.BlackMarketInstances[instanceId].DefId);

        // -------------------------------------------------------------- table

        public static void SetEveryonesMoney(Game game, int dollars)
        {
            foreach (var player in game.State.Players)
            {
                player.Money = dollars;
            }
        }

        /// <summary>Leaves pending decisions for the test to answer; GameFlowTests.PassAll answers those too.</summary>
        public static void PassWindowsOnly(Game game, List<GameEvent>? collectEventsInto = null)
        {
            while (game.State.AwaitingResponse.Count > 0)
            {
                var events = game.Apply(new PassWindow { PlayerId = game.State.AwaitingResponse[0] });
                collectEventsInto?.AddRange(events);
            }
        }
    }
}
