using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>Runs before the card leaves the hand, so a throw here changes nothing.</summary>
    public sealed partial class Game
    {
        private void ValidatePlayParams(LemonCardDef def, PlayLemonCard action, PlayerState player)
        {
            switch (def.Id)
            {
                case LemonCardIds.Apologize:
                case LemonCardIds.BlameChanger:
                    ValidateTantrumToMove(def, action, player);
                    break;

                case LemonCardIds.Connections:
                    ValidateConnections(action, player);
                    break;

                case LemonCardIds.DoorbusterSale:
                    RequireOwnStand(def, action, player);
                    break;

                case LemonCardIds.Rebrand:
                    ValidateRebrand(action, RequireOwnStand(def, action, player));
                    break;

                case LemonCardIds.FindersKeepers:
                    ValidateFindersKeepers(action, player);
                    break;

                case LemonCardIds.ReduceAndReuse:
                    ValidateReduceAndReuse(action, player);
                    break;

                case LemonCardIds.ReverseEngineer:
                    ValidateReverseEngineer(action);
                    break;

                case LemonCardIds.RummageSale:
                    ValidateRummageSale(action, player);
                    break;

                case LemonCardIds.StealTheCashbox:
                    ValidateStealTheCashbox(action, player);
                    break;

                case LemonCardIds.ThatsNotFair:
                    ValidateThatsNotFair(action, player);
                    break;

                case LemonCardIds.HoaViolation:
                case LemonCardIds.SharingIsCaring:
                case LemonCardIds.SmearCampaign:
                case LemonCardIds.Taxes:
                case LemonCardIds.TrashPandas:
                    RequireOtherPlayer(action.TargetPlayerId, player.PlayerId);
                    break;

                case LemonCardIds.Automation:
                case LemonCardIds.MarketForecasting:
                case LemonCardIds.NightShifts:
                    break;

                default:
                    throw new InvalidActionException($"No handler for lemon card '{def.Id}'.");
            }
        }

        // ---------------------------------------------------- per-card rules

        /// <summary>
        /// Apologize / Blame Changer. Tantrum copies are interchangeable, so the engine
        /// picks one; an explicit id (older persisted games recorded one) must be the player's.
        /// </summary>
        private static void ValidateTantrumToMove(LemonCardDef def, PlayLemonCard action, PlayerState player)
        {
            if (player.TantrumPile.Count == 0)
            {
                throw new InvalidActionException($"{def.Name}: you have no tantrums.");
            }
            if (action.TantrumInstanceId is int chosenTantrumId &&
                player.TantrumPile.All(t => t.InstanceId != chosenTantrumId))
            {
                throw new InvalidActionException($"{def.Name}: choose a tantrum from your own pile.");
            }
        }

        private void ValidateConnections(PlayLemonCard action, PlayerState player)
        {
            if (!(action.MarketIndex is int marketIndex) || marketIndex < 0 || marketIndex >= State.Market.Count)
            {
                throw new InvalidActionException("Connections: choose a face-up market card.");
            }
            ValidateEquipDestination(player, EquippedDef(State.Market[marketIndex]),
                action.EquipStandInstanceId, action.EquipReplaceInstanceId);
        }

        private void ValidateFindersKeepers(PlayLemonCard action, PlayerState player)
        {
            var victim = RequireOtherPlayer(action.TargetPlayerId, player.PlayerId);
            if (!(action.TargetEquippedInstanceId is int equippedId) ||
                FindEquipOwner(equippedId).PlayerId != victim.PlayerId)
            {
                throw new InvalidActionException("Finders Keepers: choose a card equipped by the target.");
            }
            ValidateEquipDestination(player, EquippedDef(equippedId),
                action.EquipStandInstanceId, action.EquipReplaceInstanceId);
        }

        private void ValidateReduceAndReuse(PlayLemonCard action, PlayerState player)
        {
            if (!(action.DiscardedBmInstanceId is int discardedBmId) ||
                !State.BlackMarketDiscard.Contains(discardedBmId))
            {
                throw new InvalidActionException("Reduce and Reuse: choose a card from the Black Market discard.");
            }
            ValidateEquipDestination(player, EquippedDef(discardedBmId),
                action.EquipStandInstanceId, action.EquipReplaceInstanceId);
        }

        private void ValidateReverseEngineer(PlayLemonCard action)
        {
            if (action.DrawInstead)
            {
                return;
            }
            if (!(action.DiscardedLemonInstanceId is int discardedLemonId) ||
                !State.LemonDiscard.Contains(discardedLemonId))
            {
                throw new InvalidActionException("Reverse Engineer: choose a card from the Lemon discard (or draw 2).");
            }
            var recovered = Db.Lemon(State.LemonInstances[discardedLemonId].DefId);
            if (!IsReverseEngineerable(recovered))
            {
                throw new InvalidActionException(
                    "Reverse Engineer: only plans and attacks (other than Reverse Engineer) can be played immediately.");
            }
        }

        private void ValidateRummageSale(PlayLemonCard action, PlayerState player)
        {
            var soldIds = action.SelectedInstanceIds;
            if (soldIds.Count < 1 || soldIds.Count > RummageSaleMaxCards ||
                soldIds.Distinct().Count() != soldIds.Count)
            {
                throw new InvalidActionException(
                    $"Rummage Sale: choose 1-{RummageSaleMaxCards} of your equipped cards.");
            }
            foreach (int soldId in soldIds)
            {
                if (FindEquipOwner(soldId).PlayerId != player.PlayerId)
                {
                    throw new InvalidActionException("Rummage Sale: you can only sell your own cards.");
                }
            }
        }

        private void ValidateStealTheCashbox(PlayLemonCard action, PlayerState player)
        {
            var victim = RequireOtherPlayer(action.TargetPlayerId, player.PlayerId);
            if (victim.Turf.TrapInstanceId != null)
            {
                throw new InvalidActionException("That Turf already has a trap on it.");
            }
        }

        private void ValidateThatsNotFair(PlayLemonCard action, PlayerState player)
        {
            if (!(action.TargetEquippedInstanceId is int equippedId))
            {
                throw new InvalidActionException("That's Not Fair!: choose an equipped Black Market card.");
            }
            if (FindEquipOwner(equippedId).PlayerId == player.PlayerId)
            {
                throw new InvalidActionException("That's Not Fair!: target another player's card.");
            }
        }

        private void ValidateRebrand(PlayLemonCard action, StandInstance stand)
        {
            if (string.IsNullOrEmpty(action.NewStandTypeId) || action.NewStandTypeId == stand.StandTypeId)
            {
                throw new InvalidActionException("Rebrand: choose a different Stand type.");
            }
            var newType = Db.StandType(action.NewStandTypeId); // throws on unknown id
            if (State.StandSupply[newType.Id].Count == 0)
            {
                throw new InvalidActionException($"Rebrand: no {newType.Name}s left in the supply.");
            }
            int overflow = Math.Max(0, stand.Equipped.Count - newType.UpgradeSlots);
            var discards = action.SelectedInstanceIds;
            if (discards.Count != overflow || discards.Distinct().Count() != discards.Count ||
                discards.Any(id => !stand.Equipped.Contains(id)))
            {
                throw new InvalidActionException(
                    $"Rebrand: discard exactly {overflow} card(s) from that Stand to fit the new limit.");
            }
        }

        // --------------------------------------------------- shared checks

        private static StandInstance RequireOwnStand(LemonCardDef def, PlayLemonCard action, PlayerState player) =>
            player.Stands.FirstOrDefault(s => s.InstanceId == action.TargetStandInstanceId)
                ?? throw new InvalidActionException($"{def.Name}: choose one of your own Stands.");

        private PlayerState RequireOtherPlayer(int? targetId, int selfId)
        {
            if (!(targetId is int target) || target < 0 || target >= State.Players.Count || target == selfId)
            {
                throw new InvalidActionException("Choose another player as the target.");
            }
            return State.Players[target];
        }

        private void ValidateEquipDestination(
            PlayerState player, BlackMarketCardDef def, int? standInstanceId, int? replaceId)
        {
            if (def.Target == EquipTarget.Stand)
            {
                var stand = player.Stands.FirstOrDefault(s => s.InstanceId == standInstanceId)
                    ?? throw new InvalidActionException($"{def.Name} equips to one of your Stands.");
                bool isFull = stand.Equipped.Count >= Db.StandType(stand.StandTypeId).UpgradeSlots;
                if (isFull && !IsReplaceable(stand.Equipped, replaceId))
                {
                    throw new InvalidActionException(
                        "That Stand is at its limit; choose an equipped card to discard.");
                }
                return;
            }

            if (standInstanceId != null)
            {
                throw new InvalidActionException($"{def.Name} is a Turf upgrade.");
            }
            bool isTurfFull = player.Turf.Equipped.Count >= Db.Turf.UpgradeSlots;
            if (isTurfFull && !IsReplaceable(player.Turf.Equipped, replaceId))
            {
                throw new InvalidActionException(
                    "Your Turf is at its limit; choose an equipped card to discard.");
            }
        }

        private static bool IsReplaceable(List<int> equipped, int? replaceId) =>
            replaceId is int replacedId && equipped.Contains(replacedId);
    }
}
