using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Turn lifecycle and sale rolls: the stepped turn start (Spoiled Rotten roll, draws,
    /// Whiniest Baby discard), roll windows and their finalization into payouts, and the
    /// end-of-turn sequence (Trade Winds rolls, VP trigger, turn pass). Every step advances
    /// through the pump so response windows can interleave anywhere.
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
            var active = Player(State.ActivePlayer);
            switch (State.TurnStartStep)
            {
                case 0: // Spoiled Rotten: free sale roll for yourself (Supporting Cards p80).
                    State.TurnStartStep = 1;
                    if (State.SpoiledRottenHolder == State.ActivePlayer)
                    {
                        OpenRollWindow(RollPurpose.SpoiledRotten, State.ActivePlayer, events);
                    }
                    break;
                case 1: // Draw: 1 normally, 2 for the Whiniest Baby (who discards 1 below).
                    State.TurnStartStep = 2;
                    bool isBaby = State.WhiniestBabyHolder == State.ActivePlayer;
                    if (isBaby)
                    {
                        State.TrackedDrawnCards.Clear();
                        // The keep-choice is owed because the turn STARTED as the
                        // baby — a Timeout fine clearing the tantrums (and passing
                        // the Baby card on) mid-draw doesn't cancel it.
                        State.BabyDiscardOwed = true;
                    }
                    QueueDraws(State.ActivePlayer,
                        isBaby ? Db.Config.TurnStartDraw + 1 : Db.Config.TurnStartDraw,
                        trackDrawnIds: isBaby, reason: DrawReason.TurnStart);
                    break;
                case 2: // Whiniest Baby: discard 1 — of the cards just drawn, not the whole hand.
                    State.TurnStartStep = 3;
                    if (State.BabyDiscardOwed)
                    {
                        State.BabyDiscardOwed = false;
                        var drawn = State.TrackedDrawnCards
                            .Where(active.Hand.Contains).ToList();
                        if (drawn.Count > 0)
                        {
                            State.PendingDecisions.Add(new PendingDecision
                            {
                                PlayerId = State.ActivePlayer,
                                Kind = DecisionKind.WhiniestBabyDiscard,
                                RequiredCount = 1,
                                EligibleCardIds = drawn,
                            });
                            events.Add(new DecisionRequired
                            {
                                PlayerId = State.ActivePlayer,
                                Kind = DecisionKind.WhiniestBabyDiscard,
                            });
                        }
                    }
                    break;
                default:
                    State.TurnStartInProgress = false;
                    State.Phase = TurnPhase.Play;
                    break;
            }
        }

        // ------------------------------------------------------ sale rolls

        private void OpenRollWindow(RollPurpose purpose, int rollerId, List<GameEvent> events, int? standInstanceId = null)
        {
            // A fresh roll starts a fresh "single roll" episode for title tracking.
            State.RollStats.Clear();
            State.PendingRoll = new PendingRoll
            {
                Value = _rng.Roll(Db.Config.SaleDieSides),
                Purpose = purpose,
                RollerId = rollerId,
                StandInstanceId = standInstanceId,
            };
            BumpRevision();
            events.Add(new SaleRolled
            {
                PlayerId = rollerId,
                Value = State.PendingRoll.Value,
                Purpose = purpose,
                StandInstanceId = standInstanceId,
            });
        }

        private void FinalizePendingRoll(List<GameEvent> events)
        {
            var roll = State.PendingRoll!;
            State.PendingRoll = null;
            BumpRevision();
            FinalizeSale(roll, events);
            State.PostRollContinuation = roll.Purpose;
        }

        /// <summary>
        /// Apply a final die value. All players sell on every table roll — the roller just
        /// throws the die; Night Shifts / Spoiled Rotten rolls apply to the roller only, and
        /// Trade Winds rolls to a single stand. Base payouts land first, then interactive
        /// triggers queue decisions (rulebook p13 ordering).
        /// </summary>
        private void FinalizeSale(PendingRoll roll, List<GameEvent> events)
        {
            bool allPlayers = roll.Purpose == RollPurpose.TurnSale || roll.Purpose == RollPurpose.ExtraSale;
            int n = State.Players.Count;

            for (int offset = 0; offset < n; offset++)
            {
                var player = State.Players[(roll.RollerId + offset) % n];
                if (!allPlayers && player.PlayerId != roll.RollerId)
                {
                    continue;
                }

                foreach (var stand in player.Stands.ToList())
                {
                    if (roll.StandInstanceId is int only && stand.InstanceId != only)
                    {
                        continue; // Trade Winds: this roll is for one specific stand
                    }
                    if (SaleNumbersOf(stand).Contains(roll.Value))
                    {
                        SellStand(player, stand, events);
                    }
                }

                if (roll.StandInstanceId == null && PourNumbersOf(player).Contains(roll.Value))
                {
                    TriggerPowerPour(player, events);
                }
            }

            // Tip Jars (once, on your turn): +$1 per own stand that sold on the turn sale.
            if (roll.Purpose == RollPurpose.TurnSale)
            {
                var roller = Player(roll.RollerId);
                int jars = CountOnTurf(roller, "tip-jars");
                if (jars > 0)
                {
                    int sold = roller.Stands.Count(s => SaleNumbersOf(s).Contains(roll.Value));
                    if (sold > 0)
                    {
                        events.Add(new AbilityTriggered { PlayerId = roller.PlayerId, DefId = "tip-jars" });
                        GainFromRoll(roller, jars * sold, "tip jars", events);
                    }
                }
            }

            // Steal the Cashbox traps: skim up to $10 of what the trapped player just earned.
            foreach (var player in State.Players)
            {
                if (!(player.Turf.TrapInstanceId is int trapId) ||
                    !(player.Turf.TrapOwnerId is int thief))
                {
                    continue;
                }
                if (!allPlayers && player.PlayerId != roll.RollerId)
                {
                    continue; // trapped player was not part of this roll
                }
                int gain = State.RollStats.TryGetValue(player.PlayerId, out var stats) ? stats.Earned : 0;
                int stolen = System.Math.Min(gain, 10);
                if (stolen > 0)
                {
                    player.Money -= stolen;
                    Player(thief).Money += stolen;
                    StatsFor(thief).MoneyStolen += stolen;
                    events.Add(new MoneyStolen
                    {
                        FromPlayerId = player.PlayerId,
                        ToPlayerId = thief,
                        Amount = stolen,
                        Reason = "steal the cashbox",
                    });
                    OpenTheftWindow(player.PlayerId, thief, stolen);
                }
                // "Discard after rolling" — the trap goes whether or not it caught anything.
                player.Turf.TrapInstanceId = null;
                player.Turf.TrapOwnerId = null;
                State.LemonDiscard.Add(trapId);
            }
        }

        private void OpenTheftWindow(int victimId, int attackerId, int amount)
        {
            State.TheftQueue.Add(new PendingTheftResponse
            {
                VictimId = victimId,
                AttackerId = attackerId,
                AmountStolen = amount,
            });
            BumpRevision();
        }

        // -------------------------------------------------------- turn end

        /// <summary>After the end-of-turn sale settles: Trade Winds rolls, then the VP trigger and turn pass.</summary>
        private void FinishEndTurn(List<GameEvent> events)
        {
            var active = Player(State.ActivePlayer);

            // Trade Winds: "End of Your Turn: +1 sale roll for this Stand only" — one roll
            // per equipped copy, resolved before the turn passes.
            if (!State.TradeWindsBuilt)
            {
                State.TradeWindsBuilt = true;
                foreach (var stand in active.Stands)
                {
                    for (int i = 0; i < stand.Equipped.Count(id => EquippedDef(id).Id == "trade-winds"); i++)
                    {
                        State.TradeWindsQueue.Add(stand.InstanceId);
                    }
                }
            }
            if (State.TradeWindsQueue.Count > 0)
            {
                int standId = State.TradeWindsQueue[0];
                State.TradeWindsQueue.RemoveAt(0);
                if (active.Stands.Any(s => s.InstanceId == standId))
                {
                    events.Add(new AbilityTriggered { PlayerId = active.PlayerId, DefId = "trade-winds" });
                    OpenRollWindow(RollPurpose.TradeWinds, active.PlayerId, events, standId);
                    return; // pump finalizes the roll, then re-enters FinishEndTurn
                }
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
                FinishGame(events);
                return;
            }

            State.ActivePlayer = nextPlayer;
            StartTurn(events);
        }
    }
}
