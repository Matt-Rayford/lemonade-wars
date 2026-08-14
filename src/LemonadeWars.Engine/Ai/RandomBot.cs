using System;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Uniform-random legal play. Terrible opponent, perfect fuzzer: it walks every corner
    /// of the rules engine and proves no reachable state deadlocks or throws.
    /// </summary>
    public sealed class RandomBot : IBot
    {
        private readonly DeterministicRng _rng;

        public RandomBot(ulong seed)
        {
            _rng = new DeterministicRng(seed);
        }

        public GameAction Choose(Game game, int playerId)
        {
            var moves = game.LegalMovesFor(playerId);
            if (moves.Count == 0)
            {
                throw new InvalidOperationException(
                    $"P{playerId} has no legal moves — engine deadlock. {GameRunner.Describe(game)}");
            }
            return moves[_rng.Next(moves.Count)];
        }
    }
}
