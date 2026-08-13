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
                // Window responses ALSO map onto their hand cards: with the live-table
                // reaction panel, clicking the Tantrum in your hand plays it (equipped
                // responses like Inflatable Decoy stay panel-only).
                // Free-play decisions (Bouncer strike-back, Smear Campaign's offer)
                // enumerate full PlayLemonCard variants: bind those to their hand
                // cards too, so the card itself is clickable mid-window and the
                // normal aiming/menu flows do the target picking.
                foreach (var move in moves)
                {
                    if (move is PlayLemonCard play)
                    {
                        Add(groups.HandMoves, play.CardInstanceId, move);
                    }
                }
                if (view.AwaitingResponse.Contains(view.ViewerId))
                {
                    foreach (var move in moves)
                    {
                        if (!(move is RespondToWindow respond) || respond.EquippedInstanceId != null)
                        {
                            continue;
                        }
                        // The engine lists ONE copy of each response card (identical
                        // copies are interchangeable, and two "Play Tantrum" rows would
                        // be noise) — so bind the move to EVERY copy in hand, or the
                        // second Tantrum reads as dead.
                        string defId = view.Hand
                            .FirstOrDefault(c => c.InstanceId == respond.CardInstanceId)?.DefId;
                        if (defId == null)
                        {
                            Add(groups.HandMoves, respond.CardInstanceId, move);
                            continue;
                        }
                        foreach (var card in view.Hand)
                        {
                            if (card.DefId == defId)
                            {
                                Add(groups.HandMoves, card.InstanceId, move);
                            }
                        }
                    }
                }
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
