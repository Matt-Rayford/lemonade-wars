using System;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Easy difficulty: the greedy heuristic made consistently SOFT rather than mostly
    /// perfect. It picks among the greedy top two, sometimes lets response windows
    /// slide, and makes a share of decisions at random.
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
            bool isResponseWindow = moves.Any(move => move is PassWindow || move is RespondToWindow);
            if (isResponseWindow)
            {
                // Windows blunder only by passing: a random Tantrum on a play that never
                // touched the bot reads as malice, not as an easy opponent.
                var pass = moves.FirstOrDefault(move => move is PassWindow);
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
