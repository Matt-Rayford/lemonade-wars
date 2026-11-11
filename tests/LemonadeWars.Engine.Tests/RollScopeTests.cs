using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;
using static LemonadeWars.Engine.Tests.StateRigging;

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
        [Fact]
        public void TradeWindsRollSellsOnlyTheEquippedStand()
        {
            var game = ReadyToPlay(seed: 17);
            var state = game.State;
            StripHands(game, "tantrum", "out-of-stock");
            ParkPourNumbers(game);
            int owner = state.ActivePlayer;
            var ownerState = state.Players[owner];
            RigEquip(game, owner, "trade-winds", ownerState.Stands[0].InstanceId);
            SetEveryonesMoney(game, 20);
            HoldRollWindowOpen(game);

            game.Apply(new EndTurn { PlayerId = owner });
            Assert.Equal(RollPurpose.TurnSale, state.PendingRoll!.Purpose);
            state.PendingRoll.Value = 5; // bargains sell on 1-3, pours parked on 6: silent
            PassCurrentWindow(game);

            Assert.NotNull(state.PendingRoll);
            Assert.Equal(RollPurpose.TradeWinds, state.PendingRoll!.Purpose);
            Assert.Equal(ownerState.Stands[0].InstanceId, state.PendingRoll.StandInstanceId);
            state.PendingRoll.Value = 2; // every bargain on the table would sell on a table roll
            PassCurrentWindow(game);

            // Only the equipped stand sold (+$1): the owner's second bargain stays silent too.
            Assert.Equal(21, ownerState.Money);
            foreach (var player in state.Players)
            {
                if (player.PlayerId != owner)
                {
                    Assert.Equal(20, player.Money);
                }
            }
        }

        [Fact]
        public void SpoiledRottenRollSellsOnlyForTheHolder()
        {
            var game = ReadyToPlay(seed: 17);
            var state = game.State;
            StripHands(game, "tantrum", "out-of-stock");
            ParkPourNumbers(game);
            int previous = state.ActivePlayer;
            int spoiled = SeatAfterActive(game, 1);
            state.SpoiledRottenHolder = spoiled;
            SetEveryonesMoney(game, 20);
            HoldRollWindowOpen(game, holderSeatsAfterActive: 2);

            game.Apply(new EndTurn { PlayerId = previous });
            Assert.Equal(RollPurpose.TurnSale, state.PendingRoll!.Purpose);
            state.PendingRoll.Value = 5; // silent table roll
            PassCurrentWindow(game);

            // The next turn starts with the Spoiled Rotten free roll.
            Assert.Equal(spoiled, state.ActivePlayer);
            Assert.NotNull(state.PendingRoll);
            Assert.Equal(RollPurpose.SpoiledRotten, state.PendingRoll!.Purpose);
            Assert.Equal(spoiled, state.PendingRoll.RollerId);
            state.PendingRoll.Value = 2; // both of everyone's bargains match on a table roll
            PassCurrentWindow(game);

            // Only the holder's two bargains sold (+$2).
            Assert.Equal(22, state.Players[spoiled].Money);
            foreach (var player in state.Players)
            {
                if (player.PlayerId != spoiled)
                {
                    Assert.Equal(20, player.Money);
                }
            }
        }

        /// <summary>Keeps every Power Pour off the die values these tests rig.</summary>
        private static void ParkPourNumbers(Game game)
        {
            foreach (var player in game.State.Players)
            {
                player.Turf.PowerPourNumber = 6;
            }
        }

        /// <summary>Passes everyone out of the CURRENT window only; a window opened by its resolution stays open.</summary>
        private static void PassCurrentWindow(Game game)
        {
            int revision = game.State.LastWindowRevision;
            while (game.State.AwaitingResponse.Count > 0 &&
                   game.State.LastWindowRevision == revision)
            {
                game.Apply(new PassWindow { PlayerId = game.State.AwaitingResponse[0] });
            }
        }
    }
}
