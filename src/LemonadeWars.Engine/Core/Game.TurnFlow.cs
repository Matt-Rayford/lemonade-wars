using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Turn lifecycle: the stepped turn start (Spoiled Rotten roll, draws, Whiniest Baby
    /// discard) and the end-of-turn sequence (sale roll, Trade Winds rolls, VP trigger,
    /// turn pass). Every step advances through the pump so response windows can
    /// interleave anywhere. The rolls themselves resolve in Game.SaleRolls.cs.
    /// </summary>
    public sealed partial class Game
    {
        // ------------------------------------------------------ turn start

        /// <summary>Begin the active player's turn; steps advance through the pump.</summary>
        private void StartTurn(List<GameEvent> events)
        {
            State.Phase = TurnPhase.Start;
            State.ActionsRemaining = Db.Config.ActionsPerTurn;
            State.MarketRefreshUsedThisTurn = false;
            State.BraggingRightsBoughtThisTurn = false;
            State.UsedTurnAbilities.Clear();
            State.SpentThisTurn = 0;
            State.TradeWindsBuilt = false;
            State.TradeWindsQueue.Clear();
            // Shopping Spree: +1 Black-Market-only buy action per equipped copy.
            State.BmOnlyActionsRemaining = CountOnTurf(Player(State.ActivePlayer), "shopping-spree");
            State.TurnStartInProgress = true;
            State.TurnStartStep = 0;
            events.Add(new TurnStarted { PlayerId = State.ActivePlayer });
        }

        private void AdvanceTurnStart(List<GameEvent> events)
        {
            switch (State.TurnStartStep)
            {
                case 0:
                    State.TurnStartStep = 1;
                    RollForSpoiledRotten(events);
                    break;
                case 1:
                    State.TurnStartStep = 2;
                    QueueTurnStartDraws();
                    break;
                case 2:
                    State.TurnStartStep = 3;
                    RaiseWhiniestBabyDiscard(events);
                    break;
                default:
                    State.TurnStartInProgress = false;
                    State.Phase = TurnPhase.Play;
                    break;
            }
        }

        /// <summary>Spoiled Rotten: a free sale roll for yourself (Supporting Cards p80).</summary>
        private void RollForSpoiledRotten(List<GameEvent> events)
        {
            if (State.SpoiledRottenHolder == State.ActivePlayer)
            {
                OpenRollWindow(RollPurpose.SpoiledRotten, State.ActivePlayer, events);
            }
        }

        /// <summary>The Whiniest Baby draws one extra card, then discards one of those drawn.</summary>
        private void QueueTurnStartDraws()
        {
            bool isBaby = State.WhiniestBabyHolder == State.ActivePlayer;
            if (isBaby)
            {
                State.TrackedDrawnCards.Clear();
                // Owed because the turn STARTED as the baby: a Timeout fine passing the
                // Baby card on mid-draw doesn't cancel the discard.
                State.BabyDiscardOwed = true;
            }
            QueueDraws(State.ActivePlayer,
                isBaby ? Db.Config.TurnStartDraw + 1 : Db.Config.TurnStartDraw,
                trackDrawnIds: isBaby, reason: DrawReason.TurnStart);
        }

        private void RaiseWhiniestBabyDiscard(List<GameEvent> events)
        {
            if (!State.BabyDiscardOwed)
            {
                return;
            }
            State.BabyDiscardOwed = false;
            var drawnStillInHand = State.TrackedDrawnCards
                .Where(Player(State.ActivePlayer).Hand.Contains).ToList();
            if (drawnStillInHand.Count == 0)
            {
                return;
            }
            State.PendingDecisions.Add(new PendingDecision
            {
                PlayerId = State.ActivePlayer,
                Kind = DecisionKind.WhiniestBabyDiscard,
                RequiredCount = 1,
                EligibleCardIds = drawnStillInHand,
            });
            events.Add(new DecisionRequired
            {
                PlayerId = State.ActivePlayer,
                Kind = DecisionKind.WhiniestBabyDiscard,
            });
        }

        // -------------------------------------------------------- turn end

        private void ApplyEndTurn(EndTurn action, List<GameEvent> events)
        {
            RequirePlaying();
            if (action.PlayerId != State.ActivePlayer)
            {
                throw new InvalidActionException($"It is P{State.ActivePlayer}'s turn.");
            }
            if (State.Phase != TurnPhase.Play)
            {
                throw new InvalidActionException("Turn can only end from the Play phase.");
            }

            State.Phase = TurnPhase.Sell;
            OpenRollWindow(RollPurpose.TurnSale, State.ActivePlayer, events);
            Pump(events);
        }

        /// <summary>After the end-of-turn sale settles: Trade Winds rolls, then the VP trigger and turn pass.</summary>
        private void FinishEndTurn(List<GameEvent> events)
        {
            var active = Player(State.ActivePlayer);

            QueueTradeWindsRolls(active);
            if (TryOpenNextTradeWindsRoll(active, events))
            {
                return; // the pump finalizes the roll, then re-enters FinishEndTurn
            }

            if (State.Stage == GameStage.Playing &&
                active.InGameVictoryPoints >= Db.Config.VictoryPointsToTriggerEnd)
            {
                State.Stage = GameStage.FinalRound;
                State.EndTriggeredBy = active.PlayerId;
                events.Add(new StageChanged { Stage = State.Stage });
            }

            int nextPlayer = (State.ActivePlayer + 1) % State.Players.Count;
            if (State.Stage == GameStage.FinalRound && nextPlayer == State.FirstPlayer)
            {
                FinishGameWithScores(events);
                return;
            }

            State.ActivePlayer = nextPlayer;
            StartTurn(events);
        }

        /// <summary>Trade Winds: "End of Your Turn: +1 sale roll for this Stand only", one roll per equipped copy.</summary>
        private void QueueTradeWindsRolls(PlayerState active)
        {
            if (State.TradeWindsBuilt)
            {
                return;
            }
            State.TradeWindsBuilt = true;
            foreach (var stand in active.Stands)
            {
                int copies = stand.Equipped.Count(id => EquippedDef(id).Id == "trade-winds");
                for (int copy = 0; copy < copies; copy++)
                {
                    State.TradeWindsQueue.Add(stand.InstanceId);
                }
            }
        }

        private bool TryOpenNextTradeWindsRoll(PlayerState active, List<GameEvent> events)
        {
            if (State.TradeWindsQueue.Count == 0)
            {
                return false;
            }
            int standId = State.TradeWindsQueue[0];
            State.TradeWindsQueue.RemoveAt(0);
            if (!active.Stands.Any(s => s.InstanceId == standId))
            {
                return false;
            }
            events.Add(new AbilityTriggered { PlayerId = active.PlayerId, DefId = "trade-winds" });
            OpenRollWindow(RollPurpose.TradeWinds, active.PlayerId, events, standId);
            return true;
        }
    }
}
