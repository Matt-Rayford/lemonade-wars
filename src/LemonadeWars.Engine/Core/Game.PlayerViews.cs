using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Projects the full state into the <see cref="PlayerView"/> one seat may see.
    /// Read-only: building a view never changes the game.
    /// </summary>
    public sealed partial class Game
    {
        public PlayerView ViewFor(int viewerId)
        {
            var viewer = Player(viewerId);
            var view = new PlayerView
            {
                ViewerId = viewerId,
                Stage = State.Stage,
                Phase = State.Phase,
                ActivePlayer = State.ActivePlayer,
                FirstPlayer = State.FirstPlayer,
                ActionsRemaining = State.ActionsRemaining,
                BmOnlyActionsRemaining = State.BmOnlyActionsRemaining,
                ActingPlayers = ActingPlayers().ToList(),
                CurrentInitialBuyer =
                    State.Stage == GameStage.InitialBuys && State.InitialBuyQueue.Count > 0
                        ? State.InitialBuyQueue[0]
                        : (int?)null,
                Players = State.Players.Select(PlayerPanelFor).ToList(),
                WhiniestBabyHolder = State.WhiniestBabyHolder,
                SpoiledRottenHolder = State.SpoiledRottenHolder,
                Winners = State.Winners.ToList(),
            };
            ShowViewerSecrets(view, viewer);
            ShowSharedZones(view, viewerId);
            ShowInteractionSurface(view, viewerId);
            return view;
        }

        // ------------------------------------------------------------ cards

        private PlayerView.CardInfo CardInfoForLemon(int instanceId) => new PlayerView.CardInfo
        {
            InstanceId = instanceId,
            DefId = State.LemonInstances[instanceId].DefId,
        };

        private PlayerView.CardInfo CardInfoForBlackMarket(int instanceId) => new PlayerView.CardInfo
        {
            InstanceId = instanceId,
            DefId = State.BlackMarketInstances[instanceId].DefId,
            Shape = State.BlackMarketInstances[instanceId].Shape,
        };

        private List<PlayerView.LordStatus> LordStatusesOf(PlayerState player) =>
            player.LemonLordKept
                .Select(titleId => new PlayerView.LordStatus
                {
                    TitleId = titleId,
                    Met = MeetsLemonLord(player, titleId),
                }).ToList();

        // ----------------------------------------------------------- boards

        private PlayerView.PlayerPanel PlayerPanelFor(PlayerState player) => new PlayerView.PlayerPanel
        {
            PlayerId = player.PlayerId,
            Name = player.Name,
            Money = player.Money,
            HandCount = player.Hand.Count,
            TantrumCount = player.TantrumPile.Count,
            TurfPowerPourNumber = player.Turf.PowerPourNumber,
            PourNumbers = PourNumbersOf(player).OrderBy(number => number).ToList(),
            TurfEquipped = player.Turf.Equipped.Select(CardInfoForBlackMarket).ToList(),
            TurfTrapped = player.Turf.TrapInstanceId != null,
            Stands = player.Stands.Select(stand => StandPanelFor(player, stand)).ToList(),
            FirstDibsClaimed = player.FirstDibsClaimed.ToList(),
            BraggingRights = player.BraggingRights,
            InGameVictoryPoints = player.InGameVictoryPoints,
            // Kept Lemon Lords are secret until the game is over.
            FinishedLords = State.Stage == GameStage.Finished
                ? LordStatusesOf(player)
                : new List<PlayerView.LordStatus>(),
        };

        private PlayerView.StandPanel StandPanelFor(PlayerState owner, StandInstance stand) => new PlayerView.StandPanel
        {
            InstanceId = stand.InstanceId,
            StandTypeId = stand.StandTypeId,
            Shape = stand.Shape,
            Equipped = stand.Equipped.Select(CardInfoForBlackMarket).ToList(),
            SaleNumbers = SaleNumbersOf(stand).OrderBy(number => number).ToList(),
            Earnings = StandEarnings(owner, stand),
        };

        // ---------------------------------------------------- viewer-private

        private void ShowViewerSecrets(PlayerView view, PlayerState viewer)
        {
            view.Hand = viewer.Hand.Select(CardInfoForLemon).ToList();
            view.LemonLordDealt = viewer.LemonLordDealt.ToList();
            view.LemonLordStatus = LordStatusesOf(viewer);

            // Don's Blessings: the ability owner looks at the victim's hand while picking.
            var handPeek = State.PendingDecisions.FirstOrDefault(d =>
                d.PlayerId == viewer.PlayerId && d.Kind == DecisionKind.AbilityPickCard);
            if (handPeek?.ChosenPlayerId is int victimId)
            {
                view.RevealedHand = Player(victimId).Hand.Select(CardInfoForLemon).ToList();
            }
        }

        // ----------------------------------------------------- shared zones

        private void ShowSharedZones(PlayerView view, int viewerId)
        {
            view.LemonDeckCount = State.LemonDeck.Count;
            view.LemonDiscard = State.LemonDiscard.Select(CardInfoForLemon).ToList();
            view.BlackMarketDeckCount = State.BlackMarketDeck.Count;
            view.Market = State.Market.Select(CardInfoForBlackMarket).ToList();
            view.MarketPrices = State.Market
                .Select(id => BlackMarketPrice(viewerId, Db.BlackMarket(State.BlackMarketInstances[id].DefId)))
                .ToList();
            view.MarketGapIndex = State.ResponseStack
                .FirstOrDefault(item => item.Kind == StackItemKind.BlackMarketPurchase)?.VacatedMarketIndex;
            view.BlackMarketDiscard = State.BlackMarketDiscard.Select(CardInfoForBlackMarket).ToList();
            view.FirstDibsRow = State.FirstDibsRow.ToList();
            view.StandSupplyCounts = State.StandSupply.ToDictionary(supply => supply.Key, supply => supply.Value.Count);
            view.SupplyTopShapes = State.StandSupply
                .Where(supply => supply.Value.Count > 0)
                .ToDictionary(supply => supply.Key, supply => supply.Value[0]);
            view.SupplyPrices = Db.StandTypes.ToDictionary(type => type.Id, type => StandPrice(viewerId, type.Id));
            view.NextBraggingRightsPrice =
                State.BraggingRightsSold < Db.Supporting.BraggingRightsPrices.Count
                    ? Db.Supporting.BraggingRightsPrices[State.BraggingRightsSold]
                    : (int?)null;
        }

        // ---------------------------------------------- interaction surface

        private void ShowInteractionSurface(PlayerView view, int viewerId)
        {
            view.AwaitingResponse = State.AwaitingResponse.ToList();
            view.PendingRollValue = State.PendingRoll?.Value;
            view.PendingRollPurpose = State.PendingRoll?.Purpose;
            view.PendingRollerId = State.PendingRoll?.RollerId;
            view.PendingRollStandInstanceId = State.PendingRoll?.StandInstanceId;
            view.StackTop = StackTopInfoFor();
            view.MyDecisions = State.PendingDecisions
                .Where(d => d.PlayerId == viewerId)
                .Select(DecisionInfoFor)
                .ToList();

            var timeoutFine = State.PendingDecisions.FirstOrDefault(d => d.Kind == DecisionKind.TimeoutFine);
            view.TimeoutFinePlayerId = timeoutFine?.PlayerId;
            view.TimeoutFineAmount = timeoutFine?.RequiredMoney ?? 0;

            ShowTheftOnViewer(view, viewerId);
        }

        private static PlayerView.DecisionInfo DecisionInfoFor(PendingDecision decision) => new PlayerView.DecisionInfo
        {
            Kind = decision.Kind,
            RequiredCount = decision.RequiredCount,
            RequiredMoney = decision.RequiredMoney,
            StolenCardId = decision.StolenCardId,
            ChosenPlayerId = decision.ChosenPlayerId,
            CardInstanceId = decision.CardInstanceId,
            StackItemId = decision.StackItemId,
            SourceInstanceId = decision.SourceInstanceId,
            ContextDefId = decision.ContextDefId,
            ContextPlayerId = decision.ContextPlayerId,
            EligibleCardIds = decision.EligibleCardIds?.ToList(),
        };

        private void ShowTheftOnViewer(PlayerView view, int viewerId)
        {
            if (State.TheftQueue.Count == 0 || State.TheftQueue[0].VictimId != viewerId)
            {
                return;
            }
            var theft = State.TheftQueue[0];
            view.TheftOnMe = true;
            view.TheftAttackerId = theft.AttackerId;
            view.TheftAmount = theft.AmountStolen;
            // Profit Share claws back half, rounded up, capped by what the attacker still holds.
            view.TheftRecoverable = Math.Min((theft.AmountStolen + 1) / 2, State.Players[theft.AttackerId].Money);
        }

        // ------------------------------------------------- response stack top

        private PlayerView.StackTopInfo? StackTopInfoFor()
        {
            if (State.ResponseStack.Count == 0)
            {
                return null;
            }
            var top = TopStackItem();
            bool isPurchase = top.Kind == StackItemKind.BlackMarketPurchase;
            var info = new PlayerView.StackTopInfo
            {
                OwnerId = top.OwnerId,
                IsPurchase = isPurchase,
                DefId = isPurchase
                    ? State.BlackMarketInstances[top.BmInstanceId!.Value].DefId
                    : top.LemonDefId,
                Shape = isPurchase
                    ? State.BlackMarketInstances[top.BmInstanceId!.Value].Shape
                    : (Shape?)null,
                AttackTargetId = top.AttackTargetId,
                RedirectTargetId = top.RedirectTargetId,
            };
            NameStolenEquip(info, top);
            NameTakenCard(info, top);
            NameAnsweredItem(info, top);
            return info;
        }

        private void NameStolenEquip(PlayerView.StackTopInfo info, StackItem top)
        {
            if (top.TargetEquippedInstanceId is int stolenId &&
                State.BlackMarketInstances.TryGetValue(stolenId, out var stolen))
            {
                info.StolenDefId = stolen.DefId;
                info.StolenShape = stolen.Shape;
            }
        }

        /// <summary>A take names its loot, so a response window can show what is about to leave the shelf or the discard.</summary>
        private void NameTakenCard(PlayerView.StackTopInfo info, StackItem top)
        {
            if (top.MarketIndex is int shelfIndex &&
                shelfIndex >= 0 && shelfIndex < State.Market.Count)
            {
                var shelfCard = State.BlackMarketInstances[State.Market[shelfIndex]];
                info.TakenDefId = shelfCard.DefId;
                info.TakenShape = shelfCard.Shape;
            }
            else if (top.DiscardedBmInstanceId is int discardedBmId &&
                     State.BlackMarketInstances.TryGetValue(discardedBmId, out var discardedBm))
            {
                info.TakenDefId = discardedBm.DefId;
                info.TakenShape = discardedBm.Shape;
            }
            else if (top.DiscardedLemonInstanceId is int discardedLemonId)
            {
                info.TakenDefId = State.LemonInstances[discardedLemonId].DefId;
                info.TakenIsLemon = true;
            }
        }

        private void NameAnsweredItem(PlayerView.StackTopInfo info, StackItem top)
        {
            var answered = top.RespondingToItemId is int answeredItemId
                ? State.ResponseStack.FirstOrDefault(item => item.ItemId == answeredItemId)
                : null;
            if (answered == null)
            {
                return;
            }
            info.RespondingToOwnerId = answered.OwnerId;
            if (answered.Kind == StackItemKind.BlackMarketPurchase)
            {
                var purchased = State.BlackMarketInstances[answered.BmInstanceId!.Value];
                info.RespondingToIsPurchase = true;
                info.RespondingToDefId = purchased.DefId;
                info.RespondingToShape = purchased.Shape;
            }
            else
            {
                info.RespondingToDefId = answered.LemonDefId;
            }
        }
    }
}
