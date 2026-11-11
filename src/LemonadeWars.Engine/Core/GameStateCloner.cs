using System.Collections.Generic;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Hand-written deep copy, about ten times faster than a JSON round-trip (search bots clone
    /// hundreds of times per decision). A missed field is a silent bug, so the engine tests
    /// compare this copy against the JSON copy across whole fuzzed games.
    /// </summary>
    public static class GameStateCloner
    {
        public static GameState Clone(GameState source)
        {
            var copy = CloneScalarFields(source);
            foreach (var player in source.Players)
            {
                copy.Players.Add(ClonePlayer(player));
            }
            CopyCardZones(source, copy);
            CopyInstanceTables(source, copy);
            copy.InitialBuyQueue.AddRange(source.InitialBuyQueue);
            CopyInteractionState(source, copy);
            copy.UsedTurnAbilities.AddRange(source.UsedTurnAbilities);
            copy.TradeWindsQueue.AddRange(source.TradeWindsQueue);
            foreach (var stats in source.RollStats)
            {
                copy.RollStats[stats.Key] = CloneRollStats(stats.Value);
            }
            copy.Winners.AddRange(source.Winners);
            return copy;
        }

        private static GameState CloneScalarFields(GameState source) =>
            new GameState
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

        private static void CopyCardZones(GameState source, GameState copy)
        {
            copy.LemonDeck.AddRange(source.LemonDeck);
            copy.LemonDiscard.AddRange(source.LemonDiscard);
            copy.BlackMarketDeck.AddRange(source.BlackMarketDeck);
            copy.Market.AddRange(source.Market);
            copy.BlackMarketDiscard.AddRange(source.BlackMarketDiscard);
            copy.FirstDibsRow.AddRange(source.FirstDibsRow);
            foreach (var pile in source.StandSupply)
            {
                copy.StandSupply[pile.Key] = new List<Shape>(pile.Value);
            }
        }

        private static void CopyInstanceTables(GameState source, GameState copy)
        {
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
        }

        private static void CopyInteractionState(GameState source, GameState copy)
        {
            foreach (var item in source.ResponseStack)
            {
                copy.ResponseStack.Add(CloneStackItem(item));
            }
            copy.AwaitingResponse.AddRange(source.AwaitingResponse);
            foreach (var theft in source.TheftQueue)
            {
                copy.TheftQueue.Add(CloneTheftResponse(theft));
            }
            foreach (var decision in source.PendingDecisions)
            {
                copy.PendingDecisions.Add(CloneDecision(decision));
            }
            foreach (var draw in source.PendingDraws)
            {
                copy.PendingDraws.Add(ClonePendingDraw(draw));
            }
            copy.TrackedDrawnCards.AddRange(source.TrackedDrawnCards);
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
                copy.Stands.Add(CloneStand(stand));
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

        private static StandInstance CloneStand(StandInstance source)
        {
            var copy = new StandInstance
            {
                InstanceId = source.InstanceId,
                StandTypeId = source.StandTypeId,
                Shape = source.Shape,
            };
            copy.Equipped.AddRange(source.Equipped);
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
                WindowOffered = source.WindowOffered,
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
                VacatedMarketIndex = source.VacatedMarketIndex,
            };

        private static PendingTheftResponse CloneTheftResponse(PendingTheftResponse source) =>
            new PendingTheftResponse
            {
                VictimId = source.VictimId,
                AttackerId = source.AttackerId,
                AmountStolen = source.AmountStolen,
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

        private static PendingDraw ClonePendingDraw(PendingDraw source)
        {
            var copy = new PendingDraw
            {
                PlayerId = source.PlayerId,
                Count = source.Count,
                CountsForRoll = source.CountsForRoll,
                TrackDrawnIds = source.TrackDrawnIds,
                Reason = source.Reason,
                DiscardAfterCount = source.DiscardAfterCount,
                DiscardAfterSourceInstanceId = source.DiscardAfterSourceInstanceId,
            };
            copy.DrawnIds.AddRange(source.DrawnIds);
            return copy;
        }

        private static RollStats CloneRollStats(RollStats source) =>
            new RollStats
            {
                Earned = source.Earned,
                MoneyStolen = source.MoneyStolen,
                CardsStolen = source.CardsStolen,
                CardsKept = source.CardsKept,
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
