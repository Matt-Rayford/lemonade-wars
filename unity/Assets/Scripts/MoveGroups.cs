using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Buckets a player's legal moves by the on-table thing they belong to, so the UI can
    /// present click-to-act instead of a flat button list. When the engine is blocked on a
    /// window/decision/setup choice, everything routes to the modal bucket instead.
    /// </summary>
    public sealed class MoveGroups
    {
        public readonly Dictionary<int, List<GameAction>> HandMoves = new Dictionary<int, List<GameAction>>();
        public readonly Dictionary<int, List<GameAction>> MarketMoves = new Dictionary<int, List<GameAction>>();
        public readonly Dictionary<string, List<GameAction>> SupplyMoves = new Dictionary<string, List<GameAction>>();
        public readonly List<GameAction> BarMoves = new List<GameAction>();
        public readonly List<GameAction> ModalMoves = new List<GameAction>();
        /// <summary>True when the player must answer through the modal (window/decision/setup).</summary>
        public bool IsModal { get; private set; }

        public static MoveGroups For(Game game, int playerId)
        {
            var groups = new MoveGroups();
            var moves = game.LegalMovesFor(playerId);
            if (moves.Count == 0)
            {
                return groups;
            }

            var s = game.State;
            groups.IsModal =
                s.Stage == GameStage.ChoosingLemonLords ||
                s.AwaitingResponse.Contains(playerId) ||
                s.PendingDecisions.Any(d => d.PlayerId == playerId);

            if (groups.IsModal)
            {
                groups.ModalMoves.AddRange(moves);
                return groups;
            }

            foreach (var move in moves)
            {
                switch (move)
                {
                    case PlayLemonCard play:
                        Add(groups.HandMoves, play.CardInstanceId, move);
                        break;
                    case BuyBlackMarket buy:
                        Add(groups.MarketMoves, buy.MarketIndex, move);
                        break;
                    case BuyStand buyStand:
                        Add(groups.SupplyMoves, buyStand.StandTypeId, move);
                        break;
                    case InitialBuyStand initial:
                        Add(groups.SupplyMoves, initial.StandTypeId, move);
                        break;
                    default:
                        groups.BarMoves.Add(move);
                        break;
                }
            }
            return groups;
        }

        private static void Add<TKey>(Dictionary<TKey, List<GameAction>> map, TKey key, GameAction move)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<GameAction>();
                map[key] = list;
            }
            list.Add(move);
        }
    }
}
