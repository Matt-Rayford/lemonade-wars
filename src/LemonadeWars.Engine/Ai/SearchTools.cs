using System.Collections.Generic;
using LemonadeWars.Engine.Core;
using Newtonsoft.Json;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>Deep-copy and hidden-information tools for search bots.</summary>
    public static class SearchTools
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
        };

        /// <summary>The fast path every search clones through.</summary>
        public static GameState CloneState(GameState state) => GameStateCloner.Clone(state);

        /// <summary>
        /// The reference copy: a JSON round-trip cannot miss a field, so the tests hold
        /// the hand-written cloner to it across whole games.
        /// </summary>
        public static GameState CloneStateViaJson(GameState state) =>
            JsonConvert.DeserializeObject<GameState>(
                JsonConvert.SerializeObject(state, Settings), Settings)!;

        /// <summary>
        /// Rewrites the clone's hidden information into ONE possible world consistent
        /// with what <paramref name="viewerId"/> can see. Public zones and the viewer's
        /// own cards are untouched, so a search can only exploit knowledge a human in
        /// that seat would also have.
        /// </summary>
        public static void Determinize(GameState state, int viewerId, DeterministicRng rng)
        {
            // The order of these steps fixes the order of RNG draws, and with it every
            // seeded bot's play.
            RedealUnseenLemonCards(state, viewerId, rng);
            ShuffleBlackMarketDeck(state, rng);
            RepickSecretLemonLords(state, viewerId, rng);
            ReseedDice(state, rng);
        }

        /// <summary>Opponents' hands and the deck are one pool the viewer cannot tell apart; hand sizes are kept.</summary>
        private static void RedealUnseenLemonCards(GameState state, int viewerId, DeterministicRng rng)
        {
            var unseen = new List<int>(state.LemonDeck);
            foreach (var player in state.Players)
            {
                if (player.PlayerId != viewerId)
                {
                    unseen.AddRange(player.Hand);
                }
            }
            rng.Shuffle(unseen);

            int cursor = 0;
            foreach (var player in state.Players)
            {
                if (player.PlayerId == viewerId)
                {
                    continue;
                }
                int handSize = player.Hand.Count;
                player.Hand.Clear();
                for (int i = 0; i < handSize; i++)
                {
                    player.Hand.Add(unseen[cursor++]);
                }
            }
            state.LemonDeck.Clear();
            while (cursor < unseen.Count)
            {
                state.LemonDeck.Add(unseen[cursor++]);
            }
        }

        private static void ShuffleBlackMarketDeck(GameState state, DeterministicRng rng)
        {
            var reordered = new List<int>(state.BlackMarketDeck);
            rng.Shuffle(reordered);
            state.BlackMarketDeck.Clear();
            state.BlackMarketDeck.AddRange(reordered);
        }

        /// <summary>Each opponent keeps as many lords as before, re-chosen from the ones they were dealt.</summary>
        private static void RepickSecretLemonLords(GameState state, int viewerId, DeterministicRng rng)
        {
            foreach (var player in state.Players)
            {
                if (player.PlayerId == viewerId || player.LemonLordKept.Count == 0 ||
                    player.LemonLordDealt.Count < player.LemonLordKept.Count)
                {
                    continue;
                }
                int keptCount = player.LemonLordKept.Count;
                var dealt = new List<string>(player.LemonLordDealt);
                rng.Shuffle(dealt);
                player.LemonLordKept.Clear();
                for (int i = 0; i < keptCount; i++)
                {
                    player.LemonLordKept.Add(dealt[i]);
                }
            }
        }

        /// <summary>Without fresh dice per world, every rollout would replay identical rolls.</summary>
        private static void ReseedDice(GameState state, DeterministicRng rng)
        {
            state.RngState = ((ulong)rng.Next(int.MaxValue) << 32) ^ (ulong)rng.Next(int.MaxValue);
        }
    }
}
