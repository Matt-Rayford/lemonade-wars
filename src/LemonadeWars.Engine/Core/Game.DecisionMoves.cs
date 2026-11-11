using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    public sealed partial class Game
    {
        /// <summary>
        /// Each Apply handler answers the FIRST pending decision of its group, so moves
        /// are listed for the first decision per group only, keeping move shapes aligned.
        /// </summary>
        private void AddPendingDecisionMoves(List<GameAction> moves, PlayerState player)
        {
            var seenGroups = new HashSet<string>();
            foreach (var decision in State.PendingDecisions.Where(d => d.PlayerId == player.PlayerId))
            {
                if (seenGroups.Add(DecisionGroup(decision.Kind)))
                {
                    AddDecisionMoves(moves, player, decision);
                }
            }
        }

        /// <summary>Decisions answered by the same action type share a group (FIFO within it).</summary>
        private static string DecisionGroup(DecisionKind kind)
        {
            switch (kind)
            {
                case DecisionKind.DiscardToHandLimit:
                case DecisionKind.WhiniestBabyDiscard:
                    return "discard";
                case DecisionKind.AbilityVictim:
                case DecisionKind.AbilityPickCard:
                case DecisionKind.AbilityGiveBack:
                case DecisionKind.AbilityDiscard:
                case DecisionKind.InnovationCopy:
                case DecisionKind.WordOfMouthStand:
                    return "ability";
                case DecisionKind.FreePlayOffer:
                case DecisionKind.ForcedPlay:
                case DecisionKind.BouncerAttack:
                    return "freeplay";
                default:
                    return kind.ToString();
            }
        }

        private void AddDecisionMoves(List<GameAction> moves, PlayerState player, PendingDecision decision)
        {
            switch (decision.Kind)
            {
                case DecisionKind.DiscardToHandLimit:
                case DecisionKind.WhiniestBabyDiscard:
                    foreach (var discards in CardCombos(
                        decision.EligibleCardIds ?? player.Hand, decision.RequiredCount))
                    {
                        moves.Add(new SubmitDiscard { PlayerId = player.PlayerId, InstanceIds = discards });
                    }
                    break;

                case DecisionKind.TimeoutFine:
                    moves.Add(BuildTimeoutPayment(player, decision.RequiredMoney));
                    break;

                case DecisionKind.AttackRetarget:
                    AddRetargetMoves(moves, player, decision);
                    break;

                case DecisionKind.FreePlayOffer:
                case DecisionKind.BouncerAttack:
                case DecisionKind.ForcedPlay:
                    AddFreePlayMoves(moves, player, decision);
                    break;

                case DecisionKind.AbilityVictim:
                    AddAbilityVictimMoves(moves, player);
                    break;

                case DecisionKind.AbilityPickCard:
                    AddAbilityCardPicks(moves, player, Player(decision.ChosenPlayerId!.Value).Hand);
                    break;

                case DecisionKind.AbilityGiveBack:
                    AddAbilityCardPicks(moves, player, player.Hand.Where(id => id != decision.StolenCardId));
                    break;

                case DecisionKind.AbilityDiscard:
                    foreach (var discards in CardCombos(
                        decision.EligibleCardIds ?? player.Hand, decision.RequiredCount))
                    {
                        moves.Add(new SubmitAbilityChoice { PlayerId = player.PlayerId, CardInstanceIds = discards });
                    }
                    break;

                case DecisionKind.InnovationCopy:
                    AddInnovationCopyMoves(moves, player, decision);
                    break;

                case DecisionKind.WordOfMouthStand:
                    foreach (var stand in player.Stands)
                    {
                        moves.Add(new SubmitAbilityChoice
                        {
                            PlayerId = player.PlayerId,
                            StandInstanceId = stand.InstanceId,
                        });
                    }
                    break;
            }
        }

        /// <summary>Canonical Timeout payment: sell cheapest assets until the fine is covered.</summary>
        private SubmitTimeoutPayment BuildTimeoutPayment(PlayerState player, int fine)
        {
            var payment = new SubmitTimeoutPayment { PlayerId = player.PlayerId };
            int cash = player.Money;
            if (cash >= fine)
            {
                return payment;
            }

            var sellables = AllEquipped(player)
                .Select(id => (Id: id, Price: EquippedDef(id).Cost, IsStand: false))
                .Concat(player.Stands.Select(s =>
                    (Id: s.InstanceId, Price: Db.StandType(s.StandTypeId).BaseCost, IsStand: true)))
                .OrderBy(x => x.Price)
                .ToList();
            foreach (var asset in sellables)
            {
                if (cash >= fine)
                {
                    break;
                }
                cash += asset.Price;
                if (asset.IsStand)
                {
                    payment.SellStandInstanceIds.Add(asset.Id);
                }
                else
                {
                    payment.SellBmInstanceIds.Add(asset.Id);
                }
            }
            return payment; // if still short, the engine takes everything (bankrupt path)
        }

        private void AddRetargetMoves(List<GameAction> moves, PlayerState player, PendingDecision decision)
        {
            var item = FindStackItem(decision.StackItemId);
            if (item == null)
            {
                return;
            }
            bool needsEquipDestination = item.LemonDefId == LemonCardIds.FindersKeepers;
            foreach (int equippedId in AllEquipped(Player(item.AttackTargetId!.Value)))
            {
                if (!needsEquipDestination)
                {
                    moves.Add(new SubmitRetarget
                    {
                        PlayerId = player.PlayerId,
                        StackItemId = item.ItemId,
                        TargetEquippedInstanceId = equippedId,
                    });
                    continue;
                }
                foreach (var (standId, replaceId) in DestinationsFor(player, EquippedDef(equippedId)))
                {
                    moves.Add(new SubmitRetarget
                    {
                        PlayerId = player.PlayerId,
                        StackItemId = item.ItemId,
                        TargetEquippedInstanceId = equippedId,
                        EquipStandInstanceId = standId,
                        EquipReplaceInstanceId = replaceId,
                    });
                }
            }
        }

        private void AddFreePlayMoves(List<GameAction> moves, PlayerState player, PendingDecision decision)
        {
            bool isForcedPlay = decision.Kind == DecisionKind.ForcedPlay;
            bool attacksOnly = decision.Kind == DecisionKind.BouncerAttack;
            var candidates = isForcedPlay
                ? new List<int> { decision.CardInstanceId!.Value }
                : player.Hand.ToList();
            bool hasAnyPlay = false;
            foreach (int cardId in candidates)
            {
                var def = Db.Lemon(State.LemonInstances[cardId].DefId);
                bool isPlayable = def.Type == LemonCardType.Attack ||
                                  (def.Type == LemonCardType.Plan && !attacksOnly);
                if (!isPlayable)
                {
                    continue;
                }
                foreach (var play in PlayParamCombos(player, cardId, def))
                {
                    moves.Add(play);
                    hasAnyPlay = true;
                }
            }
            // Optional offers are always skippable; a forced play is skippable only
            // when the recovered card has no legal parameterization.
            if (!isForcedPlay || !hasAnyPlay)
            {
                moves.Add(new SkipFreePlay { PlayerId = player.PlayerId });
            }
        }

        private void AddAbilityVictimMoves(List<GameAction> moves, PlayerState player)
        {
            foreach (var other in OpponentsOf(player))
            {
                moves.Add(new SubmitAbilityChoice
                {
                    PlayerId = player.PlayerId,
                    TargetPlayerId = other.PlayerId,
                });
            }
            // The steal-and-swap abilities are optional: giving a card back
            // can cost more than the steal is worth, so the owner may pass.
            moves.Add(new SubmitAbilityChoice { PlayerId = player.PlayerId, Decline = true });
        }

        private static void AddAbilityCardPicks(List<GameAction> moves, PlayerState player, IEnumerable<int> cardIds)
        {
            foreach (int cardId in cardIds)
            {
                moves.Add(new SubmitAbilityChoice
                {
                    PlayerId = player.PlayerId,
                    CardInstanceIds = new List<int> { cardId },
                });
            }
        }

        /// <summary>Innovation never copies itself or a second Innovation.</summary>
        private void AddInnovationCopyMoves(List<GameAction> moves, PlayerState player, PendingDecision decision)
        {
            foreach (int equippedId in player.Turf.Equipped)
            {
                var def = EquippedDef(equippedId);
                if (equippedId != decision.SourceInstanceId &&
                    def.Timing == EffectTiming.PowerPour && def.Id != BlackMarketCardIds.Innovation)
                {
                    moves.Add(new SubmitAbilityChoice { PlayerId = player.PlayerId, EquippedInstanceId = equippedId });
                }
            }
        }
    }
}
