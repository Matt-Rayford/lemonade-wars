using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LemonadeWars.Engine.Core;
using Newtonsoft.Json;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// A bot whose Choose burns a real time budget. Sessions must never run one on the
    /// UI thread or against the live game — think on a snapshot clone.
    /// </summary>
    public interface IHeavyBot
    {
    }

    /// <summary>
    /// Single-observer Information Set MCTS (Cowling, Powley &amp; Whitehouse 2012) —
    /// the step up from flat determinized Monte-Carlo: ONE search tree grown from the
    /// searcher's perspective across many sampled worlds. Each iteration determinizes
    /// the hidden information afresh, descends the tree using only actions legal in
    /// that world (UCB1 adjusted by availability counts, the subset-armed-bandit
    /// correction), expands one untried action, and evaluates with a greedy rollout.
    /// Nodes carry a reward per PLAYER and selection maximizes the acting player's own
    /// reward (max^n-style), so simulated opponents play to win rather than to spite.
    /// </summary>
    public sealed class IsmctsBot : IBot, IHeavyBot
    {
        private sealed class Node
        {
            /// <summary>Canonical action identity — stable across determinized worlds.</summary>
            public string Key = "";
            public int Visits;
            /// <summary>How often this action was legal when its parent was selected from.</summary>
            public int Availability;
            public double[] Reward = Array.Empty<double>();
            public readonly List<Node> Children = new List<Node>();
        }

        private const double Exploration = 0.7;
        private const int RolloutActionCap = 1200;
        private const int MaxNodeBreadth = 16;
        private const int MinIterations = 2;

        private readonly DeterministicRng _rng;
        private readonly GreedyBot _policy = new GreedyBot();
        private readonly int _budgetMs;
        private readonly int _maxIterations;
        private readonly int _rolloutDepth;

        /// <param name="rolloutDepth">
        /// Actions per rollout before cutting to the static evaluation. Full-game
        /// rollouts (the default) give the cleanest signal but only a few hundred
        /// iterations fit a budget; truncating buys the tree an order of magnitude
        /// more iterations at the cost of leaning on the heuristic eval.
        /// </param>
        public IsmctsBot(ulong seed, int budgetMs = 300, int maxIterations = int.MaxValue,
            int rolloutDepth = int.MaxValue)
        {
            _rng = new DeterministicRng(seed);
            _budgetMs = budgetMs;
            _maxIterations = maxIterations;
            _rolloutDepth = rolloutDepth;
        }

        public GameAction Choose(Game game, int playerId)
        {
            var moves = game.LegalMovesFor(playerId);
            if (moves.Count == 0)
            {
                throw new InvalidOperationException(
                    $"P{playerId} has no legal moves — engine deadlock. {GameRunner.Describe(game)}");
            }
            if (moves.Count == 1)
            {
                return moves[0];
            }

            // Root actions from the REAL state, so the returned move is always valid
            // on it (worlds resample hidden info but never the searcher's own options).
            var rootActions = new Dictionary<string, GameAction>();
            foreach (var move in moves)
            {
                rootActions[KeyOf(move)] = move;
            }

            int players = game.State.Players.Count;
            var root = new Node { Reward = new double[players] };
            var stopwatch = Stopwatch.StartNew();
            int iterations = 0;
            while (iterations < _maxIterations &&
                   (iterations < MinIterations || stopwatch.ElapsedMilliseconds < _budgetMs))
            {
                iterations++;
                try
                {
                    var state = SearchTools.CloneState(game.State);
                    SearchTools.Determinize(state, playerId, _rng);
                    Iterate(root, Game.FromState(game.Db, state), players);
                }
                catch (Exception)
                {
                    // One broken world must not sink the turn; the next sample differs.
                }
            }

            Node? best = null;
            foreach (var child in root.Children)
            {
                if (rootActions.ContainsKey(child.Key) &&
                    (best == null || child.Visits > best.Visits))
                {
                    best = child; // robust child: most visited
                }
            }
            return best != null ? rootActions[best.Key] : _policy.Choose(game, playerId);
        }

        /// <summary>One ISMCTS iteration in one determinized world.</summary>
        private void Iterate(Node root, Game world, int players)
        {
            var path = new List<Node> { root };
            var node = root;
            int steps = 0;

            while (world.State.Stage != GameStage.Finished && steps < RolloutActionCap)
            {
                var acting = world.ActingPlayers();
                if (acting.Count == 0)
                {
                    break;
                }
                int actor = acting[0];
                var legal = Shortlist(world, actor);

                // Match this world's legal actions to the node's children.
                var legalChildren = new List<Node>();
                var legalActions = new List<GameAction>();
                GameAction? untried = null;
                foreach (var move in legal)
                {
                    string key = KeyOf(move);
                    Node? child = null;
                    foreach (var candidate in node.Children)
                    {
                        if (candidate.Key == key)
                        {
                            child = candidate;
                            break;
                        }
                    }
                    if (child == null)
                    {
                        if (untried == null)
                        {
                            untried = move;
                        }
                    }
                    else
                    {
                        legalChildren.Add(child);
                        legalActions.Add(move);
                    }
                }

                // Every action considered in this selection was "available" once.
                foreach (var child in legalChildren)
                {
                    child.Availability++;
                }

                if (untried != null)
                {
                    var fresh = new Node
                    {
                        Key = KeyOf(untried),
                        Availability = 1,
                        Reward = new double[players],
                    };
                    node.Children.Add(fresh);
                    world.Apply(untried);
                    steps++;
                    path.Add(fresh);
                    Backpropagate(path, Rollout(world, players, steps));
                    return;
                }

                // All tried: UCB over this world's children on the ACTOR's own reward.
                Node? chosen = null;
                GameAction? chosenAction = null;
                double bestScore = double.MinValue;
                for (int i = 0; i < legalChildren.Count; i++)
                {
                    var child = legalChildren[i];
                    double exploit = child.Visits > 0 ? child.Reward[actor] / child.Visits : 0.0;
                    double explore = Math.Sqrt(
                        Math.Log(Math.Max(2, child.Availability)) / Math.Max(1, child.Visits));
                    double score = exploit + Exploration * explore;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        chosen = child;
                        chosenAction = legalActions[i];
                    }
                }
                if (chosen == null)
                {
                    break; // this world offers no action the tree knows — evaluate here
                }
                world.Apply(chosenAction!);
                steps++;
                node = chosen;
                path.Add(chosen);
            }

            Backpropagate(path, TerminalRewards(world, players, steps));
        }

        private double[] Rollout(Game world, int players, int stepsSoFar)
        {
            int steps = stepsSoFar;
            int horizon = _rolloutDepth == int.MaxValue
                ? RolloutActionCap
                : Math.Min(RolloutActionCap, stepsSoFar + _rolloutDepth);
            while (world.State.Stage != GameStage.Finished && steps < horizon)
            {
                var acting = world.ActingPlayers();
                if (acting.Count == 0)
                {
                    break;
                }
                steps++;
                world.Apply(_policy.Choose(world, acting[0]));
            }
            return TerminalRewards(world, players, steps);
        }

        /// <summary>
        /// Finished games: per-player win share, impatience-discounted. Truncated
        /// games: the standing evaluation carries the whole reward — full score
        /// (in-game VP + met Lemon Lords), money, and board income as a proxy for
        /// the future, squashed to [0,1] against the strongest rival.
        /// </summary>
        private static double[] TerminalRewards(Game world, int players, int steps)
        {
            var state = world.State;
            bool finished = state.Stage == GameStage.Finished;
            double decay = Math.Pow(0.999, steps);
            var scores = new double[players];
            for (int p = 0; p < players; p++)
            {
                var player = state.Players[p];
                double income = 0;
                foreach (var stand in player.Stands)
                {
                    income += world.StandEarnings(player, stand);
                }
                scores[p] = (player.InGameVictoryPoints +
                    player.LemonLordKept.Count(id => world.MeetsLemonLord(player, id))) * 10
                    + player.Money * 0.5
                    + income * 1.2;
            }
            var rewards = new double[players];
            for (int p = 0; p < players; p++)
            {
                double bestOther = double.MinValue;
                for (int other = 0; other < players; other++)
                {
                    if (other != p && scores[other] > bestOther)
                    {
                        bestOther = scores[other];
                    }
                }
                double standing = Math.Max(0.0, Math.Min(1.0, 0.5 + (scores[p] - bestOther) * 0.02));
                if (finished)
                {
                    double win = state.Winners.Contains(p) ? 1.0 / state.Winners.Count : 0.0;
                    rewards[p] = win * decay + standing * 0.05;
                }
                else
                {
                    rewards[p] = standing; // heuristic proxy for win probability
                }
            }
            return rewards;
        }

        private static void Backpropagate(List<Node> path, double[] rewards)
        {
            foreach (var node in path)
            {
                node.Visits++;
                for (int p = 0; p < rewards.Length; p++)
                {
                    node.Reward[p] += rewards[p];
                }
            }
        }

        /// <summary>Greedy-anchored bounded breadth, mirroring the flat search's pruning.</summary>
        private List<GameAction> Shortlist(Game world, int actor)
        {
            var moves = world.LegalMovesFor(actor);
            if (moves.Count <= MaxNodeBreadth)
            {
                return moves.ToList();
            }
            var shortlist = new List<GameAction> { _policy.Choose(world, actor) };
            double stride = (double)moves.Count / (MaxNodeBreadth - 1);
            for (double at = 0; at < moves.Count && shortlist.Count < MaxNodeBreadth; at += stride)
            {
                var move = moves[(int)at];
                if (!shortlist.Contains(move))
                {
                    shortlist.Add(move);
                }
            }
            return shortlist;
        }

        /// <summary>
        /// Canonical identity across worlds: instance ids are global (only card LOCATION
        /// is hidden), so a serialized action names the same physical move everywhere.
        /// </summary>
        private static string KeyOf(GameAction action) =>
            action.GetType().Name + JsonConvert.SerializeObject(action);
    }
}
