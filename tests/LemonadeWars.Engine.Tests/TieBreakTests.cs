using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.BlackMarketScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>The rulebook p14 chain for equal victory points: most money, then fewest played Tantrums.</summary>
    public class TieBreakTests
    {
        [Fact]
        public void EqualVictoryPointsAreBrokenByMostMoney()
        {
            var game = FinishTiedOnVictoryPoints((_, firstSeat, secondSeat) =>
            {
                firstSeat.Money = 12;
                secondSeat.Money = 40;
            });
            var state = game.State;

            Assert.Equal(new List<int> { (state.FirstPlayer + 1) % state.Players.Count }, state.Winners);
        }

        [Fact]
        public void EqualMoneyIsBrokenByFewestPlayedTantrums()
        {
            var game = FinishTiedOnVictoryPoints((tiedGame, firstSeat, secondSeat) =>
            {
                firstSeat.Money = 25;
                secondSeat.Money = 25;
                RigTantrums(tiedGame, secondSeat.PlayerId, 2);
            });
            var state = game.State;

            Assert.Equal(new List<int> { state.FirstPlayer }, state.Winners);
        }

        [Fact]
        public void ExhaustingTheTieBreakChainLeavesJointWinners()
        {
            // Rock paper scissors (rulebook p14) happens at the table, not in the engine.
            var game = FinishTiedOnVictoryPoints((tiedGame, firstSeat, secondSeat) =>
            {
                firstSeat.Money = 25;
                secondSeat.Money = 25;
                RigTantrums(tiedGame, firstSeat.PlayerId, 1);
                RigTantrums(tiedGame, secondSeat.PlayerId, 1);
            });
            var state = game.State;
            int first = state.FirstPlayer;

            var tied = new List<int> { first, (first + 1) % state.Players.Count };
            Assert.Equal(tied.OrderBy(x => x).ToList(), state.Winners.OrderBy(x => x).ToList());
        }

        /// <summary>
        /// Finishes a game where the first two seats hold identical victory points, so only the
        /// tie-break chain can separate them. The rig sets their money and Tantrums.
        /// </summary>
        private static Game FinishTiedOnVictoryPoints(Action<Game, PlayerState, PlayerState> rig)
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            var state = game.State;
            int first = state.FirstPlayer;
            int second = (first + 1) % state.Players.Count;
            Assert.Equal(first, state.ActivePlayer);

            // A Timeout drawn during the final round would move money around behind the rig.
            foreach (int id in state.LemonDeck.Where(id => state.LemonInstances[id].DefId == "timeout").ToList())
            {
                state.LemonDeck.Remove(id);
            }
            state.FirstDibsRow.Clear();
            foreach (var player in state.Players)
            {
                player.LemonLordKept.Clear();
            }

            var firstSeat = state.Players[first];
            var secondSeat = state.Players[second];
            firstSeat.BraggingRights = 3;
            secondSeat.BraggingRights = 3;
            // No stands and a shared power pour number: the final round pays both seats equally,
            // so whatever money the rig sets is still tied (or still apart) when scoring runs.
            firstSeat.Stands.Clear();
            secondSeat.Stands.Clear();
            secondSeat.Turf.PowerPourNumber = firstSeat.Turf.PowerPourNumber;
            rig(game, firstSeat, secondSeat);

            // First player already sits on 3 VP, so their EndTurn starts the final round.
            for (int i = 0; i < state.Players.Count; i++)
            {
                GameFlowTests.ApplyAndPass(game, new EndTurn { PlayerId = state.ActivePlayer });
            }

            Assert.Equal(GameStage.Finished, state.Stage);
            return game;
        }
    }
}
