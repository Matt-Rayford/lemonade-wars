using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Easy difficulty: the greedy heuristic, but with a blunder rate — a share of
    /// decisions are made uniformly at random, like a distracted eight-year-old
    /// entrepreneur. Windows are exempt so it never randomly cancels its own plays.
    /// </summary>
    public sealed class EasyBot : IBot
    {
        private readonly DeterministicRng _rng;
        private readonly GreedyBot _greedy = new GreedyBot();
        private const double BlunderRate = 0.25;

        public EasyBot(ulong seed)
        {
            _rng = new DeterministicRng(seed);
        }

        public GameAction Choose(Game game, int playerId)
        {
            var moves = game.LegalMovesFor(playerId);
            // Response windows are exempt from blunders: a randomly thrown Tantrum
            // against a play that never touched the bot reads as malice, not as an
            // easy opponent — and it double-feeds the Whiniest Baby fine.
            bool isWindow = moves.Any(m => m is PassWindow || m is RespondToWindow);
            if (!isWindow && moves.Count > 1 && _rng.Next(100) < BlunderRate * 100)
            {
                return moves[_rng.Next(moves.Count)];
            }
            return _greedy.Choose(game, playerId);
        }
    }
}
