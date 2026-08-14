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

        /// <summary>
        /// Full deep copy via JSON round-trip. GameState is pure serializable data by
        /// design, so this cannot silently miss a field the way a hand-written copy can.
        /// </summary>
        public static GameState CloneState(GameState state) =>
            JsonConvert.DeserializeObject<GameState>(
                JsonConvert.SerializeObject(state, Settings), Settings)!;

        /// <summary>
        /// Rewrite the clone's hidden information into ONE possible world consistent
        /// with what <paramref name="viewerId"/> can actually see: opponents' hands are
        /// redealt from the pool of unseen Lemon cards (their deck+hand union), deck
        /// orders are reshuffled, opponents' secret Lemon Lord picks are re-chosen from
        /// their deals, and the RNG is re-seeded so every determinization rolls its own
        /// dice. Public zones and the viewer's own cards are untouched — the search can
        /// only exploit knowledge a human in that seat would also have.
        /// </summary>
        public static void Determinize(GameState state, int viewerId, DeterministicRng rng)
        {
            // Pool of Lemon cards the viewer cannot see: every opponent hand + the deck.
            var pool = new List<int>(state.LemonDeck);
            foreach (var player in state.Players)
            {
                if (player.PlayerId != viewerId)
                {
                    pool.AddRange(player.Hand);
                }
            }
            rng.Shuffle(pool);

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
                    player.Hand.Add(pool[cursor++]);
                }
            }
            state.LemonDeck.Clear();
            while (cursor < pool.Count)
            {
                state.LemonDeck.Add(pool[cursor++]);
            }

            // Black Market deck: contents drawn face-down, so its order is hidden too.
            var bmDeck = new List<int>(state.BlackMarketDeck);
            rng.Shuffle(bmDeck);
            state.BlackMarketDeck.Clear();
            state.BlackMarketDeck.AddRange(bmDeck);

            // Opponents' kept Lemon Lords are secret; re-pick 2 from what they were dealt.
            foreach (var player in state.Players)
            {
                if (player.PlayerId == viewerId || player.LemonLordKept.Count == 0 ||
                    player.LemonLordDealt.Count < player.LemonLordKept.Count)
                {
                    continue;
                }
                int keep = player.LemonLordKept.Count;
                var dealt = new List<string>(player.LemonLordDealt);
                rng.Shuffle(dealt);
                player.LemonLordKept.Clear();
                for (int i = 0; i < keep; i++)
                {
                    player.LemonLordKept.Add(dealt[i]);
                }
            }

            // Fresh dice per world — otherwise every rollout replays identical rolls.
            state.RngState = ((ulong)rng.Next(int.MaxValue) << 32) ^ (ulong)rng.Next(int.MaxValue);
        }
    }
}
