using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Buckets a seat's legal moves by the on-table thing they belong to, so the UI can
    /// present click/drag-to-act instead of a flat button list. When the engine is blocked
    /// on a window/decision/setup choice, everything routes to the modal bucket instead.
    /// Purely view-driven: works identically for local and remote sessions.
    /// </summary>
    public sealed class MoveGroups
    {
        public readonly Dictionary<int, List<GameAction>> HandMoves = new Dictionary<int, List<GameAction>>();
        public readonly Dictionary<int, List<GameAction>> MarketMoves = new Dictionary<int, List<GameAction>>();
        public readonly Dictionary<string, List<GameAction>> SupplyMoves = new Dictionary<string, List<GameAction>>();
        /// <summary>Buy Bragging Rights: dragged from the shelf onto the VP column.</summary>
        public readonly List<GameAction> BraggingMoves = new List<GameAction>();
        public readonly List<GameAction> BarMoves = new List<GameAction>();
        public readonly List<GameAction> ModalMoves = new List<GameAction>();
        /// <summary>True when the player must answer through the modal (window/decision/setup).</summary>
        public bool IsModal { get; private set; }

        public static MoveGroups From(PlayerView view, IReadOnlyList<GameAction> moves)
        {
            var groups = new MoveGroups();
            if (view == null || moves == null || moves.Count == 0)
            {
                return groups;
            }

            groups.IsModal =
                view.Stage == GameStage.ChoosingLemonLords ||
                view.AwaitingResponse.Contains(view.ViewerId) ||
                view.MyDecisions.Count > 0;

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
                    case BuyBraggingRights _:
                        groups.BraggingMoves.Add(move);
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
