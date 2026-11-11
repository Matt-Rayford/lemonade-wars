using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.StateRigging;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Starts games and settles them between a test's own moves. Every start takes its seed:
    /// the engine is deterministic, so the seed is the deal a test's expected values rest on.
    /// </summary>
    internal static class GameScenario
    {
        public static readonly string[] FourPlayers = { "Ana", "Ben", "Cal", "Dee" };
        public static readonly string[] TwoPlayers = { "Ana", "Ben" };

        // ----------------------------------------------------- starting a game

        public static Game NewGame(ulong seed, string[]? names = null) =>
            Game.Create(TestData.Db, names ?? FourPlayers, seed);

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

        public static void DraftBargainStandsOnly(Game game)
        {
            while (game.State.Stage == GameStage.InitialBuys)
            {
                int buyer = game.State.InitialBuyQueue[0];
                game.Apply(new InitialBuyStand { PlayerId = buyer, StandTypeId = "bargain" });
                game.Apply(new InitialBuyEnd { PlayerId = buyer });
            }
        }

        /// <summary>Through the draft and no further: whatever the first turn's opening draw raises is still pending.</summary>
        public static Game DraftedGame(ulong seed, string[]? names = null)
        {
            var game = NewGame(seed, names);
            KeepFirstTwoLemonLords(game);
            DraftBargainStandsOnly(game);
            return game;
        }

        /// <summary>A drafted game settled into the first player's Play phase.</summary>
        public static Game ReadyToPlay(ulong seed, string[]? names = null)
        {
            var game = DraftedGame(seed, names);
            PassAll(game);
            return game;
        }

        // --------------------------------------------------------------- seats

        public static int ActivePlayer(Game game) => game.State.ActivePlayer;

        public static int SeatAfterActive(Game game, int offset) =>
            (game.State.ActivePlayer + offset) % game.State.Players.Count;

        // ----------------------------------------------------- settling a game

        public static void ApplyAndPass(Game game, GameAction action)
        {
            game.Apply(action);
            PassAll(game);
        }

        /// <summary>
        /// Everyone declines to respond, and mandatory decisions get deterministic default
        /// answers (discard newest), until the game is quiet.
        /// </summary>
        public static void PassAll(Game game)
        {
            var state = game.State;
            while (state.AwaitingResponse.Count > 0 || state.PendingDecisions.Count > 0)
            {
                if (state.AwaitingResponse.Count > 0)
                {
                    game.Apply(new PassWindow { PlayerId = state.AwaitingResponse[0] });
                    continue;
                }
                var decision = state.PendingDecisions[0];
                var player = state.Players[decision.PlayerId];
                switch (decision.Kind)
                {
                    case DecisionKind.DiscardToHandLimit:
                    case DecisionKind.WhiniestBabyDiscard:
                        var pool = decision.EligibleCardIds ?? player.Hand;
                        game.Apply(new SubmitDiscard
                        {
                            PlayerId = player.PlayerId,
                            InstanceIds = pool
                                .Skip(pool.Count - decision.RequiredCount).ToList(),
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

        /// <summary>Leaves pending decisions for the test to answer; <see cref="PassAll"/> answers those too.</summary>
        public static void PassWindowsOnly(Game game, List<GameEvent>? collectEventsInto = null)
        {
            while (game.State.AwaitingResponse.Count > 0)
            {
                var events = game.Apply(new PassWindow { PlayerId = game.State.AwaitingResponse[0] });
                collectEventsInto?.AddRange(events);
            }
        }

        // ------------------------------------------- sale rolls with a set die

        /// <summary>
        /// A roll nobody can answer resolves inside EndTurn; an Out of Stock in a rival's hand
        /// holds its window open, so a test can set the die first.
        /// </summary>
        public static int HoldRollWindowOpen(Game game, int holderSeatsAfterActive = 1) =>
            GiveCard(game, SeatAfterActive(game, holderSeatsAfterActive), "out-of-stock");

        public static void EndTurnWithRoll(Game game, int value)
        {
            int holder = SeatAfterActive(game, 1);
            int outOfStock = HoldRollWindowOpen(game);
            game.Apply(new EndTurn { PlayerId = ActivePlayer(game) });
            Assert.NotNull(game.State.PendingRoll);
            game.State.PendingRoll!.Value = value;
            PassAll(game);
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
            game.Apply(new EndTurn { PlayerId = ActivePlayer(game) });
            game.State.PendingRoll!.Value = value;
            PassWindowsOnly(game);
        }
    }
}
