using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Forced reproductions of the reported frozen state: the Whiniest Baby's
    /// turn-start draw-2 colliding with Timeout draws. The invariant under test is the
    /// one whose violation freezes the client: every awaited player always has at
    /// least one legal move, and the baby's turn always reaches the Play phase.
    /// </summary>
    public class WhiniestBabyTimeoutTests
    {
        private static Game NewPlayingGame(ulong seed)
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben", "Cal" }, seed);
            Drive(game, () => game.State.Stage == GameStage.Playing &&
                              game.State.Phase == TurnPhase.Play, seed);
            return game;
        }

        /// <summary>Play random legal moves until done, asserting no one is ever starved.</summary>
        private static void Drive(Game game, System.Func<bool> done, ulong seed, int cap = 800)
        {
            var random = new RandomBot(seed * 17 + 5);
            DriveWith(game, done, (g, p) => random.Choose(g, p), cap);
        }

        /// <summary>
        /// Drive with a policy that never draws voluntarily: end the turn / pass the
        /// window as soon as possible, so a hand-stacked deck survives untouched until
        /// the player under test draws it.
        /// </summary>
        private static void DriveQuietly(Game game, System.Func<bool> done, int cap = 800)
        {
            DriveWith(game, done, (g, p) =>
            {
                var moves = g.LegalMovesFor(p);
                return moves.FirstOrDefault(m => m is EndTurn)
                    ?? moves.FirstOrDefault(m => m is PassWindow)
                    ?? moves[0];
            }, cap);
        }

        private static void DriveWith(Game game, System.Func<bool> done,
            System.Func<Game, int, GameAction> policy, int cap)
        {
            for (int i = 0; i < cap; i++)
            {
                if (done())
                {
                    return;
                }
                var acting = game.ActingPlayers();
                Assert.True(acting.Count > 0,
                    "No acting players but the game is not at the target state: " +
                    GameRunner.Describe(game));
                foreach (int player in acting)
                {
                    Assert.True(game.LegalMovesFor(player).Count > 0,
                        $"P{player} is awaited with ZERO legal moves — the frozen state: " +
                        GameRunner.Describe(game));
                }
                game.Apply(policy(game, acting[0]));
            }
            Assert.True(false, "Target state not reached in time: " + GameRunner.Describe(game));
        }

        /// <summary>Pull a card instance out of every zone so we can place it by hand.</summary>
        private static void Extract(GameState state, int instanceId)
        {
            state.LemonDeck.Remove(instanceId);
            state.LemonDiscard.Remove(instanceId);
            state.TrackedDrawnCards.Remove(instanceId);
            foreach (var player in state.Players)
            {
                player.Hand.Remove(instanceId);
            }
        }

        private static void MakeNextPlayerTheBaby(Game game, out int babyId)
        {
            var state = game.State;
            babyId = (state.ActivePlayer + 1) % state.Players.Count;
            int tantrumId = state.LemonInstances
                .First(kv => kv.Value.DefId == "tantrum").Key;
            Extract(state, tantrumId);
            state.WhiniestBabyHolder = babyId;
            state.Players[babyId].TantrumPile.Add(new TantrumRecord
            {
                InstanceId = tantrumId,
                GainSeq = state.NextTantrumGainSeq++,
            });
        }

        private static List<int> TimeoutIds(Game game) =>
            game.State.LemonInstances
                .Where(kv => game.Db.Lemon(kv.Value.DefId).Type == LemonCardType.Timeout)
                .Select(kv => kv.Key)
                .ToList();

        private static void StackDeck(Game game, params int[] topCards)
        {
            foreach (int id in topCards)
            {
                Extract(game.State, id);
            }
            for (int i = topCards.Length - 1; i >= 0; i--)
            {
                game.State.LemonDeck.Insert(0, topCards[i]);
            }
        }

        private static void AssertBabyTurnSurvives(ulong seed, bool timeoutFirst, bool bothTimeouts)
        {
            var game = NewPlayingGame(seed);
            MakeNextPlayerTheBaby(game, out int babyId);

            // Strip every equipped ability: sale-trigger and Power-Pour draws must not
            // eat the hand-stacked deck during the turn BEFORE the baby's.
            foreach (var player in game.State.Players)
            {
                player.Turf.Equipped.Clear();
                foreach (var stand in player.Stands)
                {
                    stand.Equipped.Clear();
                }
            }

            var timeouts = TimeoutIds(game);
            Assert.True(timeouts.Count >= 2, "expected the two rulebook Timeout cards");
            int normal = game.State.LemonInstances.Keys.First(id =>
                !timeouts.Contains(id) &&
                game.State.LemonDeck.Contains(id));

            if (bothTimeouts)
            {
                StackDeck(game, timeouts[0], timeouts[1]);
            }
            else if (timeoutFirst)
            {
                StackDeck(game, timeouts[0], normal);
            }
            else
            {
                StackDeck(game, normal, timeouts[0]);
            }

            // Drive until the baby's keep-choice appears (or their Play phase, or a
            // game end that beat us to it) — quietly, so nothing else draws first.
            DriveQuietly(game, () =>
                    game.State.Stage == GameStage.Finished ||
                    game.State.PendingDecisions.Any(d =>
                        d.Kind == DecisionKind.WhiniestBabyDiscard) ||
                    (game.State.ActivePlayer == babyId &&
                     game.State.Phase == TurnPhase.Play &&
                     !game.State.TurnStartInProgress));

            // The new guarantee: Timeouts are replaced, so the keep-choice always
            // offers the full two REAL cards, both actually in the baby's hand —
            // even when the Timeout fine passed the Baby card on mid-draw.
            var decision = game.State.PendingDecisions
                .FirstOrDefault(d => d.Kind == DecisionKind.WhiniestBabyDiscard);
            if (game.State.Stage != GameStage.Finished)
            {
                Assert.NotNull(decision);
                Assert.Equal(babyId, decision!.PlayerId);
                Assert.NotNull(decision.EligibleCardIds);
                Assert.Equal(2, decision.EligibleCardIds!.Count);
                Assert.All(decision.EligibleCardIds,
                    id => Assert.Contains(id, game.State.Players[babyId].Hand));
            }

            // And the turn still lands cleanly in the baby's Play phase.
            DriveQuietly(game, () =>
                    game.State.Stage == GameStage.Finished ||
                    (game.State.ActivePlayer == babyId &&
                     game.State.Phase == TurnPhase.Play &&
                     !game.State.TurnStartInProgress));
        }

        [Theory]
        [InlineData(101UL)]
        [InlineData(202UL)]
        [InlineData(303UL)]
        public void BabyDrawsTimeoutSecond(ulong seed) =>
            AssertBabyTurnSurvives(seed, timeoutFirst: false, bothTimeouts: false);

        [Theory]
        [InlineData(404UL)]
        [InlineData(505UL)]
        [InlineData(606UL)]
        public void BabyDrawsTimeoutFirst(ulong seed) =>
            AssertBabyTurnSurvives(seed, timeoutFirst: true, bothTimeouts: false);

        [Theory]
        [InlineData(707UL)]
        [InlineData(808UL)]
        [InlineData(909UL)]
        public void BabyDrawsBothTimeouts(ulong seed) =>
            AssertBabyTurnSurvives(seed, timeoutFirst: false, bothTimeouts: true);
    }
}
