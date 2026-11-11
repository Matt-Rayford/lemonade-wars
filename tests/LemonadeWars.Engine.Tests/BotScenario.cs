using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Scripted positions for bot tests: a game warmed up to a Play phase, then rigged
    /// (here and through <see cref="StateRigging"/>) so a single decision is isolated from
    /// everything else the bot could do.
    /// </summary>
    internal static class BotScenario
    {
        private static int _nextRigStandId = 9100;

        /// <summary>A drafted game, played by a greedy bot until someone is in their Play phase.</summary>
        public static Game ReadyToAct(ulong seed, int players)
        {
            var game = GameScenario.DraftedGame(seed, GameScenario.FourPlayers.Take(players).ToArray());
            var warmup = new GreedyBot();
            while (game.State.Phase != TurnPhase.Play && game.State.Stage != GameStage.Finished)
            {
                game.Apply(warmup.Choose(game, game.ActingPlayers()[0]));
            }
            return game;
        }

        /// <summary>A stand that never came from the supply, so a position can hold any stands it needs.</summary>
        public static StandInstance AddStand(PlayerState player, string typeId)
        {
            var stand = new StandInstance
            {
                InstanceId = _nextRigStandId++,
                StandTypeId = typeId,
                Shape = Shape.Circle,
            };
            player.Stands.Add(stand);
            return stand;
        }

        /// <summary>From the draw pile only, never the shelf; equips the player's Turf when no stand is given.</summary>
        public static int EquipFromDeck(Game game, PlayerState player, string defId, StandInstance? stand = null)
        {
            var state = game.State;
            int found = state.BlackMarketDeck.First(id => state.BlackMarketInstances[id].DefId == defId);
            state.BlackMarketDeck.Remove(found);
            (stand?.Equipped ?? player.Turf.Equipped).Add(found);
            return found;
        }
    }
}
