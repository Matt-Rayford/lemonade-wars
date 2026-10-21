using System;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Easy difficulty: the greedy heuristic made consistently SOFT rather than mostly
    /// perfect — like a distracted eight-year-old entrepreneur. Three handicaps:
    /// it picks among the greedy top two instead of the single best, it sometimes lets
    /// response windows slide, and a share of decisions are made at random. It never
    /// throws a random Tantrum: passing is the harmless blunder.
    /// </summary>
    public sealed class EasyBot : IBot
    {
        private readonly DeterministicRng _rng;
        private readonly GreedyBot _greedy = new GreedyBot();

        /// <summary>Share of non-window decisions made uniformly at random.</summary>
        private const int BlunderPercent = 25;
        /// <summary>Otherwise it picks uniformly among this many greedy-best moves.</summary>
        private const int TopChoices = 2;
        /// <summary>Share of response windows it simply passes on instead of reacting.</summary>
        private const int PassWindowPercent = 25;

        public EasyBot(ulong seed)
        {
            _rng = new DeterministicRng(seed);
        }

        public GameAction Choose(Game game, int playerId)
        {
            var moves = game.LegalMovesFor(playerId);
            if (moves.Any(m => m is PassWindow || m is RespondToWindow))
            {
                // A randomly thrown Tantrum against a play that never touched the bot
                // reads as malice, not as an easy opponent — so windows blunder only
                // by passing, never by reacting at random.
                var pass = moves.FirstOrDefault(m => m is PassWindow);
                if (pass != null && _rng.Next(100) < PassWindowPercent)
                {
                    return pass;
                }
                return _greedy.Choose(game, playerId);
            }
            if (moves.Count > 1 && _rng.Next(100) < BlunderPercent)
            {
                return moves[_rng.Next(moves.Count)];
            }
            var ranked = _greedy.Ranked(game, playerId);
            return ranked[_rng.Next(Math.Min(TopChoices, ranked.Count))];
        }
    }
}
