using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;
using Newtonsoft.Json;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// The rules engine. Owns a <see cref="GameState"/> and mutates it exclusively through
    /// <see cref="Apply"/>, emitting <see cref="GameEvent"/>s that presentation layers animate from.
    /// Fully deterministic: same seed + same action sequence = same state on every platform.
    /// </summary>
    public sealed partial class Game
    {
        public CardDatabase Db { get; }
        public GameState State { get; }

        private readonly DeterministicRng _rng;

        private Game(CardDatabase db, GameState state, DeterministicRng rng)
        {
            Db = db;
            State = state;
            _rng = rng;
        }

        /// <summary>
        /// Wrap an existing state snapshot (e.g. a deep clone for AI search). The RNG
        /// resumes from the snapshot's RngState, so determinism carries across copies.
        /// </summary>
        public static Game FromState(CardDatabase db, GameState state) =>
            new Game(db, state, new DeterministicRng(state.RngState));

        public string SnapshotJson() => JsonConvert.SerializeObject(State, Formatting.Indented);

        // ----------------------------------------------------- action entry

        /// <summary>Validate and execute a player action, returning the events it produced.</summary>
        public IReadOnlyList<GameEvent> Apply(GameAction action)
        {
            RequireInteractionGate(action);

            var events = new List<GameEvent>();
            Dispatch(action, events);
            State.RngState = _rng.State;

            // First Dibs titles are claimed the moment their condition is met (not tantrummable).
            if (State.Stage == GameStage.Playing || State.Stage == GameStage.FinalRound)
            {
                CheckFirstDibs(events);
            }
            return events;
        }

        private void Dispatch(GameAction action, List<GameEvent> events)
        {
            switch (action)
            {
                case ChooseLemonLords choose:
                    ApplyChooseLemonLords(choose, events);
                    break;
                case InitialBuyStand buyStand:
                    ApplyInitialBuyStand(buyStand, events);
                    break;
                case InitialBuyEnd end:
                    ApplyInitialBuyEnd(end, events);
                    break;
                case DrawLemonCard draw:
                    ApplyDrawLemonCard(draw, events);
                    break;
                case BuyStand buy:
                    RequireTurnAction(buy);
                    ApplyBuyStand(buy.PlayerId, buy.StandTypeId, buy.InsertIndex, events, spendAction: true);
                    break;
                case BuyBlackMarket buyBm:
                    ApplyBuyBlackMarket(buyBm, events);
                    break;
                case BuyBraggingRights brag:
                    RequireTurnAction(brag);
                    ApplyBuyBraggingRights(brag, events);
                    break;
                case RefreshMarket refresh:
                    ApplyRefreshMarket(refresh, events);
                    break;
                case EndTurn endTurn:
                    ApplyEndTurn(endTurn, events);
                    break;
                case PlayLemonCard play:
                    ApplyPlayLemonCard(play, events);
                    break;
                case RespondToWindow respond:
                    ApplyRespondToWindow(respond, events);
                    break;
                case PassWindow pass:
                    ApplyPassWindow(pass, events);
                    break;
                case SubmitDiscard discard:
                    ApplySubmitDiscard(discard, events);
                    break;
                case SubmitTimeoutPayment payment:
                    ApplySubmitTimeoutPayment(payment, events);
                    break;
                case SubmitRetarget retarget:
                    ApplySubmitRetarget(retarget, events);
                    break;
                case SkipFreePlay skip:
                    ApplySkipFreePlay(skip, events);
                    break;
                case UseTurnAbility ability:
                    ApplyUseTurnAbility(ability, events);
                    break;
                case SubmitAbilityChoice choice:
                    ApplySubmitAbilityChoice(choice, events);
                    break;
                default:
                    throw new InvalidActionException($"Unknown action type {action.GetType().Name}.");
            }
        }

        /// <summary>
        /// While windows or decisions are pending, only the matching interaction actions are
        /// legal; conversely interaction actions need an open window/decision to respond to.
        /// </summary>
        private void RequireInteractionGate(GameAction action)
        {
            bool interactionPending =
                State.PendingDecisions.Count > 0 ||
                State.AwaitingResponse.Count > 0 ||
                State.ResponseStack.Count > 0 ||
                State.PendingRoll != null ||
                State.TheftQueue.Count > 0;

            bool isInteractionAction =
                action is RespondToWindow || action is PassWindow ||
                action is SubmitDiscard || action is SubmitTimeoutPayment ||
                action is SubmitRetarget || action is SkipFreePlay ||
                action is SubmitAbilityChoice ||
                (action is UseTurnAbility && State.PendingRoll != null) ||
                (action is PlayLemonCard && State.PendingDecisions.Any(d =>
                    d.PlayerId == action.PlayerId &&
                    (d.Kind == DecisionKind.FreePlayOffer || d.Kind == DecisionKind.ForcedPlay ||
                     d.Kind == DecisionKind.BouncerAttack)));

            if (interactionPending && !isInteractionAction)
            {
                throw new InvalidActionException(
                    "A response window or decision is pending; resolve it first.");
            }
        }

        // ----------------------------------------------------------- guards

        private void RequireStage(GameStage stage)
        {
            if (State.Stage != stage)
            {
                throw new InvalidActionException($"Expected stage {stage}, but game is in {State.Stage}.");
            }
        }

        private void RequirePlaying()
        {
            if (State.Stage != GameStage.Playing && State.Stage != GameStage.FinalRound)
            {
                throw new InvalidActionException($"Game is not in play (stage {State.Stage}).");
            }
        }

        private void RequireActivePlayerInPlayPhase(GameAction action)
        {
            RequirePlaying();
            if (action.PlayerId != State.ActivePlayer)
            {
                throw new InvalidActionException($"It is P{State.ActivePlayer}'s turn, not P{action.PlayerId}'s.");
            }
            if (State.Phase != TurnPhase.Play)
            {
                throw new InvalidActionException($"Actions are only legal in the Play phase (now {State.Phase}).");
            }
        }

        /// <summary>Common checks for anything that costs one of the turn's actions.</summary>
        private void RequireTurnAction(GameAction action)
        {
            RequireActivePlayerInPlayPhase(action);
            if (State.ActionsRemaining <= 0)
            {
                throw new InvalidActionException("No actions remaining this turn.");
            }
        }

        private void SpendAction() => State.ActionsRemaining--;

        // -------------------------------------------------- shared plumbing

        private PlayerState Player(int id)
        {
            if (id < 0 || id >= State.Players.Count)
            {
                throw new InvalidActionException($"No player {id}.");
            }
            return State.Players[id];
        }

        private void Pay(PlayerState player, int amount, string reason, List<GameEvent> events,
            bool countsAsSpending = true)
        {
            if (player.Money < amount)
            {
                throw new InvalidActionException(
                    $"P{player.PlayerId} cannot afford ${amount} for {reason} (has ${player.Money}).");
            }
            player.Money -= amount;
            // Shopaholic tracks the active player's spending, Bragging Rights excluded.
            if (countsAsSpending && player.PlayerId == State.ActivePlayer)
            {
                State.SpentThisTurn += amount;
            }
            events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = -amount, Reason = reason });
        }
    }
}
