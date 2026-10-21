using System.Collections.Generic;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Hand-written deep copy of <see cref="GameState"/>, about ten times faster than
    /// the JSON round-trip, which matters because search bots clone hundreds of times
    /// per decision. A field missed here is a silent bug, so the engine tests compare
    /// this copy against the JSON copy across whole fuzzed games.
    /// </summary>
    public static class GameStateCloner
    {
        public static GameState Clone(GameState source)
        {
            var copy = new GameState
            {
                Stage = source.Stage,
                Phase = source.Phase,
                FirstPlayer = source.FirstPlayer,
                ActivePlayer = source.ActivePlayer,
                ActionsRemaining = source.ActionsRemaining,
                MarketRefreshUsedThisTurn = source.MarketRefreshUsedThisTurn,
                BraggingRightsBoughtThisTurn = source.BraggingRightsBoughtThisTurn,
                BraggingRightsSold = source.BraggingRightsSold,
                NextInstanceId = source.NextInstanceId,
                InitialBuyStandDone = source.InitialBuyStandDone,
                NextStackItemId = source.NextStackItemId,
                PendingRoll = ClonePendingRoll(source.PendingRoll),
                BabyDiscardOwed = source.BabyDiscardOwed,
                TurnStartInProgress = source.TurnStartInProgress,
                TurnStartStep = source.TurnStartStep,
                InteractionRevision = source.InteractionRevision,
                LastWindowRevision = source.LastWindowRevision,
                PostRollContinuation = source.PostRollContinuation,
                TimeoutDrawerId = source.TimeoutDrawerId,
                EpisodeHadTantrums = source.EpisodeHadTantrums,
                NextTantrumGainSeq = source.NextTantrumGainSeq,
                BmOnlyActionsRemaining = source.BmOnlyActionsRemaining,
                TradeWindsBuilt = source.TradeWindsBuilt,
                SpentThisTurn = source.SpentThisTurn,
                WhiniestBabyHolder = source.WhiniestBabyHolder,
                SpoiledRottenHolder = source.SpoiledRottenHolder,
                EndTriggeredBy = source.EndTriggeredBy,
                RngState = source.RngState,
            };
            foreach (var player in source.Players)
            {
                copy.Players.Add(ClonePlayer(player));
            }
            copy.LemonDeck.AddRange(source.LemonDeck);
            copy.LemonDiscard.AddRange(source.LemonDiscard);
            copy.BlackMarketDeck.AddRange(source.BlackMarketDeck);
            copy.Market.AddRange(source.Market);
            copy.BlackMarketDiscard.AddRange(source.BlackMarketDiscard);
            copy.FirstDibsRow.AddRange(source.FirstDibsRow);
            foreach (var pile in source.StandSupply)
            {
                copy.StandSupply[pile.Key] = new List<Data.Shape>(pile.Value);
            }
            foreach (var entry in source.LemonInstances)
            {
                copy.LemonInstances[entry.Key] = new LemonCardInstance
                {
                    InstanceId = entry.Value.InstanceId,
                    DefId = entry.Value.DefId,
                };
            }
            foreach (var entry in source.BlackMarketInstances)
            {
                copy.BlackMarketInstances[entry.Key] = new BlackMarketCardInstance
                {
                    InstanceId = entry.Value.InstanceId,
                    DefId = entry.Value.DefId,
                    Shape = entry.Value.Shape,
                };
            }
            copy.InitialBuyQueue.AddRange(source.InitialBuyQueue);
            foreach (var item in source.ResponseStack)
            {
                copy.ResponseStack.Add(CloneStackItem(item));
            }
            copy.AwaitingResponse.AddRange(source.AwaitingResponse);
            foreach (var theft in source.TheftQueue)
            {
                copy.TheftQueue.Add(new PendingTheftResponse
                {
                    VictimId = theft.VictimId,
                    AttackerId = theft.AttackerId,
                    AmountStolen = theft.AmountStolen,
                });
            }
            foreach (var decision in source.PendingDecisions)
            {
                copy.PendingDecisions.Add(CloneDecision(decision));
            }
            foreach (var draw in source.PendingDraws)
            {
                copy.PendingDraws.Add(new PendingDraw
                {
                    PlayerId = draw.PlayerId,
                    Count = draw.Count,
                    CountsForRoll = draw.CountsForRoll,
                    TrackDrawnIds = draw.TrackDrawnIds,
                    Reason = draw.Reason,
                });
            }
            copy.TrackedDrawnCards.AddRange(source.TrackedDrawnCards);
            copy.UsedTurnAbilities.AddRange(source.UsedTurnAbilities);
            copy.TradeWindsQueue.AddRange(source.TradeWindsQueue);
            foreach (var stats in source.RollStats)
            {
                copy.RollStats[stats.Key] = new RollStats
                {
                    Earned = stats.Value.Earned,
                    MoneyStolen = stats.Value.MoneyStolen,
                    CardsStolen = stats.Value.CardsStolen,
                    CardsKept = stats.Value.CardsKept,
                };
            }
            copy.Winners.AddRange(source.Winners);
            return copy;
        }

        private static PlayerState ClonePlayer(PlayerState source)
        {
            var copy = new PlayerState
            {
                PlayerId = source.PlayerId,
                Name = source.Name,
                Money = source.Money,
                LastTantrumPlaySeq = source.LastTantrumPlaySeq,
                BraggingRights = source.BraggingRights,
                Turf = new TurfState
                {
                    PowerPourNumber = source.Turf.PowerPourNumber,
                    TrapInstanceId = source.Turf.TrapInstanceId,
                    TrapOwnerId = source.Turf.TrapOwnerId,
                },
            };
            copy.Turf.Equipped.AddRange(source.Turf.Equipped);
            copy.Hand.AddRange(source.Hand);
            foreach (var stand in source.Stands)
            {
                var standCopy = new StandInstance
                {
                    InstanceId = stand.InstanceId,
                    StandTypeId = stand.StandTypeId,
                    Shape = stand.Shape,
                };
                standCopy.Equipped.AddRange(stand.Equipped);
                copy.Stands.Add(standCopy);
            }
            foreach (var tantrum in source.TantrumPile)
            {
                copy.TantrumPile.Add(new TantrumRecord
                {
                    InstanceId = tantrum.InstanceId,
                    GainSeq = tantrum.GainSeq,
                });
            }
            copy.LemonLordDealt.AddRange(source.LemonLordDealt);
            copy.LemonLordKept.AddRange(source.LemonLordKept);
            copy.FirstDibsClaimed.AddRange(source.FirstDibsClaimed);
            return copy;
        }

        private static StackItem CloneStackItem(StackItem source) =>
            new StackItem
            {
                ItemId = source.ItemId,
                Kind = source.Kind,
                OwnerId = source.OwnerId,
                Cancelled = source.Cancelled,
                FreePlay = source.FreePlay,
                LemonInstanceId = source.LemonInstanceId,
                LemonDefId = source.LemonDefId,
                RespondingToItemId = source.RespondingToItemId,
                AttackTargetId = source.AttackTargetId,
                OriginalTargetId = source.OriginalTargetId,
                TargetStandInstanceId = source.TargetStandInstanceId,
                TargetEquippedInstanceId = source.TargetEquippedInstanceId,
                TantrumInstanceId = source.TantrumInstanceId,
                MarketIndex = source.MarketIndex,
                DiscardedLemonInstanceId = source.DiscardedLemonInstanceId,
                DiscardedBmInstanceId = source.DiscardedBmInstanceId,
                NewStandTypeId = source.NewStandTypeId,
                SelectedInstanceIds = new List<int>(source.SelectedInstanceIds),
                DrawInstead = source.DrawInstead,
                RedirectTargetId = source.RedirectTargetId,
                EquipStandInstanceId = source.EquipStandInstanceId,
                EquipReplaceInstanceId = source.EquipReplaceInstanceId,
                BmInstanceId = source.BmInstanceId,
                PaidCost = source.PaidCost,
            };

        private static PendingDecision CloneDecision(PendingDecision source) =>
            new PendingDecision
            {
                PlayerId = source.PlayerId,
                Kind = source.Kind,
                RequiredCount = source.RequiredCount,
                RequiredMoney = source.RequiredMoney,
                StackItemId = source.StackItemId,
                CardInstanceId = source.CardInstanceId,
                SourceInstanceId = source.SourceInstanceId,
                ContextDefId = source.ContextDefId,
                ContextPlayerId = source.ContextPlayerId,
                ChosenPlayerId = source.ChosenPlayerId,
                StolenCardId = source.StolenCardId,
                EligibleCardIds = source.EligibleCardIds == null
                    ? null
                    : new List<int>(source.EligibleCardIds),
            };

        private static PendingRoll? ClonePendingRoll(PendingRoll? source) =>
            source == null
                ? null
                : new PendingRoll
                {
                    Value = source.Value,
                    Purpose = source.Purpose,
                    RollerId = source.RollerId,
                    StandInstanceId = source.StandInstanceId,
                };
    }
}
