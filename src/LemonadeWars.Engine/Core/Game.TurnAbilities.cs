using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Activated "On Your Turn" Black Market abilities: the die changers (Downsell,
    /// Sugared Up, Take Two) and Liquid Energy's extra sale roll. Each is usable once a turn.
    /// </summary>
    public sealed partial class Game
    {
        private static readonly HashSet<string> RollAbilityIds = new HashSet<string>
        {
            BlackMarketCardIds.Downsell, BlackMarketCardIds.SugaredUp, BlackMarketCardIds.TakeTwo,
        };

        private bool HasUnusedRollAbility(PlayerState player) =>
            player.Turf.Equipped.Any(id =>
                RollAbilityIds.Contains(EquippedDef(id).Id) &&
                !State.UsedTurnAbilities.Contains(id) &&
                RollAbilityUsable(EquippedDef(id).Id));

        /// <summary>Downsell cannot lower a 1; Sugared Up cannot raise a 6 (card text "max 6").</summary>
        private bool RollAbilityUsable(string defId)
        {
            if (State.PendingRoll == null)
            {
                return false;
            }
            if (defId == BlackMarketCardIds.Downsell && State.PendingRoll.Value <= 1)
            {
                return false;
            }
            if (defId == BlackMarketCardIds.SugaredUp && State.PendingRoll.Value >= Db.Config.SaleDieSides)
            {
                return false;
            }
            return true;
        }

        private void ApplyUseTurnAbility(UseTurnAbility action, List<GameEvent> events)
        {
            RequirePlaying();
            if (action.PlayerId != State.ActivePlayer)
            {
                throw new InvalidActionException("On Your Turn abilities only work on your own turn.");
            }
            var player = Player(action.PlayerId);
            if (!player.Turf.Equipped.Contains(action.EquippedInstanceId))
            {
                throw new InvalidActionException("That card is not equipped on your Turf.");
            }
            if (State.UsedTurnAbilities.Contains(action.EquippedInstanceId))
            {
                throw new InvalidActionException("That ability was already used this turn.");
            }

            var def = EquippedDef(action.EquippedInstanceId);
            switch (def.Id)
            {
                case BlackMarketCardIds.Downsell:
                case BlackMarketCardIds.SugaredUp:
                    NudgePendingRoll(player, action.EquippedInstanceId, def, events);
                    break;
                case BlackMarketCardIds.TakeTwo:
                    RerollPendingRoll(player, action.EquippedInstanceId, def, events);
                    break;
                case BlackMarketCardIds.LiquidEnergy:
                    StartExtraSaleRoll(player, action.EquippedInstanceId, def, events);
                    break;
                default:
                    throw new InvalidActionException($"{def.Name} has no activated ability.");
            }
            Pump(events);
        }

        private void NudgePendingRoll(PlayerState player, int instanceId, BlackMarketCardDef def, List<GameEvent> events)
        {
            var roll = State.PendingRoll
                ?? throw new InvalidActionException($"{def.Name} modifies a die that was just rolled.");
            if (!RollAbilityUsable(def.Id))
            {
                throw new InvalidActionException(def.Id == BlackMarketCardIds.Downsell
                    ? "Downsell cannot lower a roll of 1."
                    : "Sugared Up cannot raise a roll of 6.");
            }
            roll.Value = def.Id == BlackMarketCardIds.Downsell
                ? Math.Max(1, roll.Value - 1)
                : Math.Min(Db.Config.SaleDieSides, roll.Value + 1);
            RecordRollModification(player, instanceId, def, roll, events);
        }

        private void RerollPendingRoll(PlayerState player, int instanceId, BlackMarketCardDef def, List<GameEvent> events)
        {
            var roll = State.PendingRoll
                ?? throw new InvalidActionException("Take Two rerolls a die that was just rolled.");
            roll.Value = _rng.Roll(Db.Config.SaleDieSides);
            RecordRollModification(player, instanceId, def, roll, events);
        }

        private void RecordRollModification(PlayerState player, int instanceId, BlackMarketCardDef def, PendingRoll roll, List<GameEvent> events)
        {
            State.UsedTurnAbilities.Add(instanceId);
            BumpRevision();
            events.Add(new RollModified { PlayerId = player.PlayerId, SourceDefId = def.Id, NewValue = roll.Value });
        }

        private void StartExtraSaleRoll(PlayerState player, int instanceId, BlackMarketCardDef def, List<GameEvent> events)
        {
            if (State.PendingRoll != null || State.ResponseStack.Count > 0 ||
                State.PendingDecisions.Count > 0 || State.Phase != TurnPhase.Play)
            {
                throw new InvalidActionException("Liquid Energy needs a quiet moment in your Play phase.");
            }
            State.UsedTurnAbilities.Add(instanceId);
            events.Add(new AbilityTriggered { PlayerId = player.PlayerId, DefId = def.Id });
            OpenRollWindow(RollPurpose.ExtraSale, player.PlayerId, events);
        }
    }
}
