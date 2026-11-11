using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>Human-readable labels for engine actions, used by the move buttons.</summary>
    public static class MoveDescriber
    {
        public static string Describe(Game game, GameAction action)
        {
            var state = game.State;
            switch (action)
            {
                case ChooseLemonLords choose:
                    return "Keep: " + string.Join(" + ", choose.KeepTitleIds.Select(id => game.Db.Title(id).Name));
                case InitialBuyStand draftStand:
                    return $"Buy {game.Db.StandType(draftStand.StandTypeId).Name} (slot {draftStand.InsertIndex})";
                case InitialBuyEnd _:
                    return "Finish this draft visit";
                case DrawLemonCard _:
                    return "Draw a Lemon card";
                case BuyStand buyStand:
                    return $"Buy {game.Db.StandType(buyStand.StandTypeId).Name} " +
                           $"(${game.StandPrice(action.PlayerId, buyStand.StandTypeId)}, slot {buyStand.InsertIndex})";
                case BuyBlackMarket purchase:
                    return DescribeBlackMarketPurchase(game, purchase);
                case BuyBraggingRights _:
                    return $"Buy Bragging Rights (${game.Db.Supporting.BraggingRightsPrices[state.BraggingRightsSold]})";
                case RefreshMarket _:
                    return "Refresh the Black Market ($1)";
                case EndTurn _:
                    return "End turn (roll the sale die)";
                case PlayLemonCard play:
                    return $"Play {LemonName(game, play.CardInstanceId)}{PlayDetail(game, play)}";
                case RespondToWindow respond:
                    return DescribeResponse(game, respond);
                case PassWindow _:
                    return "Pass";
                case UseTurnAbility ability:
                    return $"Use {BlackMarketName(game, ability.EquippedInstanceId)}";
                case SubmitDiscard discard:
                    return "Discard " + string.Join(", ", discard.InstanceIds.Select(id => LemonName(game, id)));
                case SubmitTimeoutPayment payment:
                    return DescribeTimeoutPayment(game, payment);
                case SubmitRetarget retarget:
                    return DescribeRetarget(game, retarget);
                case SkipFreePlay _:
                    return "Skip";
                case SubmitAbilityChoice choice:
                    return DescribeAbilityChoice(game, choice);
                default:
                    return action.GetType().Name;
            }
        }

        private static string DescribeBlackMarketPurchase(Game game, BuyBlackMarket purchase)
        {
            var state = game.State;
            var def = game.Db.BlackMarket(state.BlackMarketInstances[state.Market[purchase.MarketIndex]].DefId);
            string destination = def.Target == EquipTarget.Turf
                ? "Turf"
                : $"stand #{StandNumber(game, purchase.PlayerId, purchase.TargetStandInstanceId)}";
            string replacing = purchase.ReplaceInstanceId is int replacedId
                ? $", replacing {BlackMarketName(game, replacedId)}"
                : "";
            return $"Buy {def.Name} (${game.BlackMarketPrice(purchase.PlayerId, def)}) onto {destination}{replacing}";
        }

        private static string PlayDetail(Game game, PlayLemonCard play) =>
            PlayTarget(game, play) +
            EquipDestination(game, play.PlayerId, play.EquipStandInstanceId, play.EquipReplaceInstanceId);

        private static string PlayTarget(Game game, PlayLemonCard play)
        {
            var state = game.State;
            if (play.TargetPlayerId is int lootedPlayerId && play.TargetEquippedInstanceId is int lootId)
            {
                // Finders Keepers takes the card, That's Not Fair! only trashes it.
                string verb = state.LemonInstances[play.CardInstanceId].DefId == LemonCardIds.ThatsNotFair
                    ? "discard"
                    : "steal";
                return $" -> {PlayerName(game, lootedPlayerId)}: {verb} {BlackMarketName(game, lootId)}";
            }
            if (play.TargetPlayerId is int victimId)
            {
                return $" -> {PlayerName(game, victimId)}";
            }
            if (play.TargetEquippedInstanceId is int equippedId)
            {
                return $" -> {BlackMarketName(game, equippedId)}";
            }
            if (play.DrawInstead)
            {
                return " (draw 2 instead)";
            }
            if (play.DiscardedLemonInstanceId is int recoveredLemonId)
            {
                return $" -> recover {LemonName(game, recoveredLemonId)}";
            }
            if (play.DiscardedBmInstanceId is int recoveredBlackMarketId)
            {
                return $" -> {BlackMarketName(game, recoveredBlackMarketId)}";
            }
            if (play.MarketIndex is int marketIndex && marketIndex < state.Market.Count)
            {
                return $" -> take {BlackMarketName(game, state.Market[marketIndex])}";
            }
            if (!string.IsNullOrEmpty(play.NewStandTypeId))
            {
                return $" -> {game.Db.StandType(play.NewStandTypeId).Name}";
            }
            if (play.TargetStandInstanceId is int standId)
            {
                return $" -> stand #{StandNumber(game, play.PlayerId, standId)}";
            }
            if (play.SelectedInstanceIds.Count > 0)
            {
                return " -> " + string.Join(", ", play.SelectedInstanceIds.Select(id => BlackMarketName(game, id)));
            }
            return "";
        }

        /// <summary>
        /// Stand-target equips can land on different stands or replace a full slot:
        /// saying where keeps those variants from reading as duplicates.
        /// </summary>
        private static string EquipDestination(Game game, int playerId, int? standId, int? replacedId)
        {
            if (standId is int destinationStandId)
            {
                return $" (onto stand #{StandNumber(game, playerId, destinationStandId)}" +
                       (replacedId is int replaced ? $", replacing {BlackMarketName(game, replaced)})" : ")");
            }
            return replacedId is int replacedOnTurf ? $" (replacing {BlackMarketName(game, replacedOnTurf)})" : "";
        }

        private static string DescribeResponse(Game game, RespondToWindow respond)
        {
            if (respond.EquippedInstanceId is int decoyId)
            {
                return $"Use {BlackMarketName(game, decoyId)}";
            }
            string cardName = LemonName(game, respond.CardInstanceId);
            return respond.RedirectTargetId is int newVictimId
                ? $"Play {cardName} -> {PlayerName(game, newVictimId)}"
                : $"Play {cardName}";
        }

        private static string DescribeTimeoutPayment(Game game, SubmitTimeoutPayment payment)
        {
            if (payment.SellStandInstanceIds.Count + payment.SellBmInstanceIds.Count == 0)
            {
                return "Pay the Timeout fine";
            }
            var payer = game.State.Players[payment.PlayerId];
            var soldNames = payment.SellBmInstanceIds.Select(id => BlackMarketName(game, id))
                .Concat(payment.SellStandInstanceIds.Select(id =>
                    game.Db.StandType(payer.Stands.First(stand => stand.InstanceId == id).StandTypeId).Name));
            return "Pay the fine (selling " + string.Join(", ", soldNames) + ")";
        }

        private static string DescribeRetarget(Game game, SubmitRetarget retarget)
        {
            var retargeted = game.State.ResponseStack.FirstOrDefault(item => item.ItemId == retarget.StackItemId);
            string lootName = BlackMarketName(game, retarget.TargetEquippedInstanceId ?? 0);
            string verb = retargeted?.LemonDefId == LemonCardIds.ThatsNotFair ? "Discard" : "Steal";
            return $"{verb} {lootName}" + EquipDestination(
                game, retarget.PlayerId, retarget.EquipStandInstanceId, retarget.EquipReplaceInstanceId);
        }

        private static string DescribeAbilityChoice(Game game, SubmitAbilityChoice choice)
        {
            if (choice.Decline)
            {
                return "Pass";
            }
            if (choice.TargetPlayerId is int victimId)
            {
                return $"Steal from {PlayerName(game, victimId)}";
            }
            if (choice.CardInstanceIds.Count > 0)
            {
                return "Choose " + string.Join(", ", choice.CardInstanceIds.Select(id => LemonName(game, id)));
            }
            if (choice.EquippedInstanceId is int copiedId)
            {
                return $"Copy {BlackMarketName(game, copiedId)}";
            }
            if (choice.StandInstanceId is int standId)
            {
                return $"Sell with stand #{StandNumber(game, choice.PlayerId, standId)}";
            }
            return "Choose";
        }

        private static string PlayerName(Game game, int playerId) => game.State.Players[playerId].Name;

        private static string LemonName(Game game, int instanceId) =>
            game.Db.Lemon(game.State.LemonInstances[instanceId].DefId).Name;

        private static string BlackMarketName(Game game, int instanceId) =>
            game.Db.BlackMarket(game.State.BlackMarketInstances[instanceId].DefId).Name;

        /// <summary>1-based position on the owner's board; 0 when the stand is not theirs.</summary>
        private static int StandNumber(Game game, int playerId, int? standInstanceId) =>
            game.State.Players[playerId].Stands.FindIndex(stand => stand.InstanceId == standInstanceId) + 1;
    }
}
