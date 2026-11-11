using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Response windows: who is asked about the current context (the top stack item, a
    /// resolved theft, or a pending roll), and the two answers, pass or respond.
    /// </summary>
    public sealed partial class Game
    {
        private static readonly HashSet<string> Untantrummable = new HashSet<string>
        {
            LemonCardIds.Apologize, // card text: "This card cannot be tantrummed"
        };

        // ------------------------------------------------------------ opening

        /// <summary>Returns true when the window is already settled (nobody left to ask).</summary>
        private bool EnsureWindow(List<GameEvent> events)
        {
            if (State.LastWindowRevision != State.InteractionRevision)
            {
                State.LastWindowRevision = State.InteractionRevision;
                State.AwaitingResponse.Clear();
                var top = State.ResponseStack.Count > 0 ? TopStackItem() : null;
                // Each stack item is offered to the table once: resurfacing as the stack unwinds, it
                // resolves unasked unless a re-aim or a countered Tantrum cleared WindowOffered.
                if (top == null || !top.WindowOffered)
                {
                    if (top != null)
                    {
                        top.WindowOffered = true;
                    }
                    OpenWindow(events);
                }
            }
            return State.AwaitingResponse.Count == 0;
        }

        private void OpenWindow(List<GameEvent> events)
        {
            State.AwaitingResponse.AddRange(EligibleResponders());
            if (State.AwaitingResponse.Count > 0)
            {
                events.Add(new ResponseWindowOpened
                {
                    AwaitingPlayers = State.AwaitingResponse.ToList(),
                    Context = CurrentWindowContext(),
                });
            }
        }

        private string CurrentWindowContext()
        {
            if (State.ResponseStack.Count > 0)
            {
                var top = TopStackItem();
                return top.Kind == StackItemKind.BlackMarketPurchase
                    ? $"purchase:{StackItemDefId(top)}"
                    : $"play:{top.LemonDefId}";
            }
            if (State.TheftQueue.Count > 0)
            {
                return "theft";
            }
            if (State.PendingRoll != null)
            {
                return $"roll:{State.PendingRoll.Value}";
            }
            return "none";
        }

        // ------------------------------------------------------- who is asked

        private IEnumerable<int> EligibleResponders()
        {
            if (State.ResponseStack.Count > 0)
            {
                return StackResponders(TopStackItem());
            }
            if (State.TheftQueue.Count > 0)
            {
                return TheftResponders(State.TheftQueue[0]);
            }
            if (State.PendingRoll != null)
            {
                return RollResponders();
            }
            return Enumerable.Empty<int>();
        }

        private IEnumerable<int> StackResponders(StackItem top)
        {
            bool isTantrummable = IsTantrummable(top);
            bool isAttack = IsAttackItem(top);
            foreach (var player in State.Players)
            {
                bool isOwnPlay = player.PlayerId == top.OwnerId;
                if (isOwnPlay)
                {
                    continue;
                }
                // Inflatable Decoy defends its owner only: you must BE the target.
                bool hasDecoy = isAttack && top.AttackTargetId == player.PlayerId &&
                    player.Turf.Equipped.Any(id => EquippedDef(id).Id == BlackMarketCardIds.InflatableDecoy);
                if ((isTantrummable && HoldsLemonCard(player, LemonCardIds.Tantrum)) ||
                    (isAttack && (HoldsLemonCard(player, LemonCardIds.TagYoureIt) || HoldsLemonCard(player, LemonCardIds.ImRubberYoureGlue))) ||
                    hasDecoy)
                {
                    yield return player.PlayerId;
                }
            }
        }

        /// <summary>Profit Share belongs to the robbed player alone.</summary>
        private IEnumerable<int> TheftResponders(PendingTheftResponse theft)
        {
            var victim = State.Players[theft.VictimId];
            if (HoldsLemonCard(victim, LemonCardIds.ProfitShare))
            {
                yield return victim.PlayerId;
            }
        }

        /// <summary>
        /// Out of Stock from any hand, or the active player's unused die abilities
        /// (Downsell / Sugared Up / Take Two).
        /// </summary>
        private IEnumerable<int> RollResponders()
        {
            foreach (var player in State.Players)
            {
                if (HoldsLemonCard(player, LemonCardIds.OutOfStock) ||
                    (player.PlayerId == State.ActivePlayer && HasUnusedRollAbility(player)))
                {
                    yield return player.PlayerId;
                }
            }
        }

        private bool HoldsLemonCard(PlayerState player, string defId) =>
            player.Hand.Any(id => State.LemonInstances[id].DefId == defId);

        private bool IsTantrummable(StackItem item) =>
            item.Kind == StackItemKind.BlackMarketPurchase || !Untantrummable.Contains(item.LemonDefId);

        private bool IsAttackItem(StackItem item) =>
            item.Kind == StackItemKind.LemonPlay &&
            Db.Lemon(item.LemonDefId).Type == LemonCardType.Attack &&
            !item.Cancelled;

        // ------------------------------------------------------------ answers

        private void ApplyPassWindow(PassWindow action, List<GameEvent> events)
        {
            if (!State.AwaitingResponse.Remove(action.PlayerId))
            {
                throw new InvalidActionException($"P{action.PlayerId} is not being awaited by any window.");
            }
            Pump(events);
        }

        private void ApplyRespondToWindow(RespondToWindow action, List<GameEvent> events)
        {
            if (!State.AwaitingResponse.Contains(action.PlayerId))
            {
                throw new InvalidActionException($"P{action.PlayerId} is not being awaited by any window.");
            }
            var player = Player(action.PlayerId);

            // An Inflatable Decoy answers from the Turf, not from the hand.
            if (action.EquippedInstanceId is int decoyId)
            {
                RespondWithDecoy(player, decoyId, events);
            }
            else
            {
                RespondWithHandCard(player, action, events);
            }
            Pump(events);
        }

        private void RespondWithHandCard(PlayerState player, RespondToWindow action, List<GameEvent> events)
        {
            if (!player.Hand.Contains(action.CardInstanceId))
            {
                throw new InvalidActionException("That card is not in your hand.");
            }
            string defId = State.LemonInstances[action.CardInstanceId].DefId;
            switch (defId)
            {
                case LemonCardIds.Tantrum:
                    RespondWithTantrum(player, action, events);
                    break;
                case LemonCardIds.TagYoureIt:
                    RespondWithTag(player, action, events);
                    break;
                case LemonCardIds.ImRubberYoureGlue:
                    RespondWithRubberGlue(player, action, events);
                    break;
                case LemonCardIds.OutOfStock:
                    RespondWithOutOfStock(player, action, events);
                    break;
                case LemonCardIds.ProfitShare:
                    RespondWithProfitShare(player, action, events);
                    break;
                default:
                    throw new InvalidActionException($"{defId} is not a window response card.");
            }
        }

        private StackItem RequireStackTop(string responseName)
        {
            if (State.ResponseStack.Count == 0)
            {
                throw new InvalidActionException($"{responseName} needs a pending play or purchase to respond to.");
            }
            return TopStackItem();
        }
    }
}
