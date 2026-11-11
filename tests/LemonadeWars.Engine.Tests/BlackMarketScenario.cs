using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>A drafted four-player game, rigged by editing its state directly.</summary>
    internal static class BlackMarketScenario
    {
        private const ulong Seed = 11;

        public static Game ReadyToPlay()
        {
            var game = GameFlowSetup.ReadyToPlay(Seed);
            GameFlowTests.PassAll(game);
            return game;
        }

        public static int Active(Game game) => game.State.ActivePlayer;

        public static int Seat(Game game, int seatsAfterActive) =>
            (game.State.ActivePlayer + seatsAfterActive) % game.State.Players.Count;

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

        /// <summary>The card may come out of another player's hand, not only the deck or discard.</summary>
        public static int GiveCard(Game game, int playerId, string defId)
        {
            var state = game.State;
            int found = state.LemonDeck.Concat(state.LemonDiscard)
                .Concat(state.Players.SelectMany(p => p.Hand))
                .First(id => state.LemonInstances[id].DefId == defId);
            state.LemonDeck.Remove(found);
            state.LemonDiscard.Remove(found);
            foreach (var p in state.Players)
            {
                p.Hand.Remove(found);
            }
            state.Players[playerId].Hand.Add(found);
            return found;
        }

        public static void StripHands(Game game, params string[] defIds)
        {
            var state = game.State;
            foreach (var player in state.Players)
            {
                var stripped = player.Hand.Where(id => defIds.Contains(state.LemonInstances[id].DefId)).ToList();
                foreach (int id in stripped)
                {
                    player.Hand.Remove(id);
                    state.LemonDeck.Add(id);
                }
            }
        }

        public static void RigTantrums(Game game, int playerId, int count)
        {
            var player = game.State.Players[playerId];
            for (int i = 0; i < count; i++)
            {
                int tantrum = GiveCard(game, playerId, "tantrum");
                player.Hand.Remove(tantrum);
                player.TantrumPile.Add(new TantrumRecord
                {
                    InstanceId = tantrum,
                    GainSeq = game.State.NextTantrumGainSeq++,
                });
            }
        }

        /// <summary>
        /// A roll nobody can answer resolves inside EndTurn; an Out of Stock in the next seat
        /// holds its window open, so a test can set the die first.
        /// </summary>
        public static int HoldRollWindowOpen(Game game) => GiveCard(game, Seat(game, 1), "out-of-stock");

        public static void EndTurnWithRoll(Game game, int value)
        {
            int holder = Seat(game, 1);
            int outOfStock = HoldRollWindowOpen(game);
            game.Apply(new EndTurn { PlayerId = Active(game) });
            Assert.NotNull(game.State.PendingRoll);
            game.State.PendingRoll!.Value = value;
            GameFlowTests.PassAll(game);
            // Back to the deck, so the helper card cannot affect later windows.
            var holderHand = game.State.Players[holder].Hand;
            if (holderHand.Remove(outOfStock))
            {
                game.State.LemonDeck.Add(outOfStock);
            }
        }

        /// <summary>Passes the roll's windows only. Needs <see cref="HoldRollWindowOpen"/> first.</summary>
        public static void EndTurnWithRollUntilDecision(Game game, int value)
        {
            game.Apply(new EndTurn { PlayerId = Active(game) });
            game.State.PendingRoll!.Value = value;
            while (game.State.AwaitingResponse.Count > 0)
            {
                game.Apply(new PassWindow { PlayerId = game.State.AwaitingResponse[0] });
            }
        }
    }
}
