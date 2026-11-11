using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Edits a game's state directly, moving card instances between zones, so a scenario
    /// never depends on what the seed dealt.
    /// </summary>
    internal static class StateRigging
    {
        // --------------------------------------------------------- lemon cards

        public static int GiveCard(Game game, int playerId, string defId)
        {
            int card = TakeLemonCard(game, defId);
            game.State.Players[playerId].Hand.Add(card);
            return card;
        }

        public static int PutInLemonDiscard(Game game, string defId)
        {
            int card = TakeLemonCard(game, defId);
            game.State.LemonDiscard.Add(card);
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

        public static void PutTantrumsInPile(Game game, int playerId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                PutTantrumInPile(game, playerId);
            }
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

        /// <summary>Tantrum, Tag and Rubber-Glue: nobody can answer a play except with cards the test then deals.</summary>
        public static void StripStackReactions(Game game) =>
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue");

        /// <summary>The stack reactions plus Profit Share, the answer to a theft.</summary>
        public static void StripAttackReactions(Game game) =>
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");

        /// <summary>The hand goes to the bottom of the deck.</summary>
        public static void BuryHand(Game game, int playerId)
        {
            var hand = game.State.Players[playerId].Hand;
            game.State.LemonDeck.AddRange(hand);
            hand.Clear();
        }

        public static void StripToOneCard(Game game, int playerId, string defId)
        {
            BuryHand(game, playerId);
            GiveCard(game, playerId, defId);
        }

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

        // -------------------------------------------------- black market cards

        /// <summary>Takes the card from the deck or the shelf; equips the Turf when no stand is given.</summary>
        public static int RigEquip(Game game, int playerId, string defId, int? standInstanceId = null)
        {
            var state = game.State;
            int found = state.BlackMarketDeck.Concat(state.Market)
                .First(id => state.BlackMarketInstances[id].DefId == defId);
            state.BlackMarketDeck.Remove(found);
            state.Market.Remove(found);
            var player = state.Players[playerId];
            if (standInstanceId is int standId)
            {
                player.Stands.First(stand => stand.InstanceId == standId).Equipped.Add(found);
            }
            else
            {
                player.Turf.Equipped.Add(found);
            }
            return found;
        }

        /// <summary>Equips whichever card tops the Black Market deck.</summary>
        public static int RigTurfEquipFromDeckTop(Game game, int playerId)
        {
            var state = game.State;
            int card = state.BlackMarketDeck[0];
            state.BlackMarketDeck.RemoveAt(0);
            state.Players[playerId].Turf.Equipped.Add(card);
            return card;
        }

        public static BlackMarketCardDef BlackMarketDefOf(Game game, int instanceId) =>
            TestData.Db.BlackMarket(game.State.BlackMarketInstances[instanceId].DefId);

        // ------------------------------------------------------------- players

        public static void SetEveryonesMoney(Game game, int dollars)
        {
            foreach (var player in game.State.Players)
            {
                player.Money = dollars;
            }
        }

        /// <summary>Secret titles (Friendly Fran, Expert Whiner...) sway scores and bot choices.</summary>
        public static void ClearLemonLords(Game game)
        {
            foreach (var player in game.State.Players)
            {
                player.LemonLordKept.Clear();
            }
        }
    }
}
