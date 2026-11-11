using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Legal move enumeration: every action a player could submit right now, fully
    /// parameterized so that each returned action Applies cleanly. This is the contract
    /// the UI (button states), AI opponents, and the fuzz tests all share — including
    /// the ORDER moves are listed in, which bots break ties by.
    /// </summary>
    public sealed partial class Game
    {
        /// <summary>Players the game is currently waiting on.</summary>
        public IReadOnlyList<int> ActingPlayers()
        {
            var acting = new List<int>();
            switch (State.Stage)
            {
                case GameStage.Finished:
                    return acting;
                case GameStage.ChoosingLemonLords:
                    acting.AddRange(State.Players
                        .Where(p => p.LemonLordKept.Count == 0)
                        .Select(p => p.PlayerId));
                    return acting;
            }

            if (State.PendingDecisions.Count > 0)
            {
                acting.AddRange(State.PendingDecisions.Select(d => d.PlayerId).Distinct());
                return acting;
            }
            if (State.AwaitingResponse.Count > 0)
            {
                acting.AddRange(State.AwaitingResponse);
                return acting;
            }
            if (State.Stage == GameStage.InitialBuys)
            {
                acting.Add(CurrentInitialBuyer);
                return acting;
            }
            acting.Add(State.ActivePlayer);
            return acting;
        }

        public List<GameAction> LegalMovesFor(int playerId)
        {
            var moves = new List<GameAction>();
            var player = Player(playerId);

            if (State.Stage == GameStage.Finished)
            {
                return moves;
            }
            if (State.Stage == GameStage.ChoosingLemonLords)
            {
                AddLemonLordChoices(moves, player);
                return moves;
            }

            // Blocking decisions come first — nothing else is legal while they pend.
            if (State.PendingDecisions.Count > 0)
            {
                AddPendingDecisionMoves(moves, player);
                return moves;
            }

            if (State.AwaitingResponse.Contains(playerId))
            {
                AddWindowMoves(moves, player);
                return moves;
            }
            bool isInteractionPendingOnOthers =
                State.AwaitingResponse.Count > 0 || State.ResponseStack.Count > 0 ||
                State.PendingRoll != null || State.TheftQueue.Count > 0;
            if (isInteractionPendingOnOthers)
            {
                return moves;
            }

            if (State.Stage == GameStage.InitialBuys)
            {
                AddInitialBuyMoves(moves, player);
                return moves;
            }

            if (playerId == State.ActivePlayer && State.Phase == TurnPhase.Play)
            {
                AddTurnMoves(moves, player);
            }
            return moves;
        }

        private static void AddLemonLordChoices(List<GameAction> moves, PlayerState player)
        {
            if (player.LemonLordKept.Count > 0)
            {
                return;
            }
            var dealt = player.LemonLordDealt;
            for (int first = 0; first < dealt.Count; first++)
            {
                for (int second = first + 1; second < dealt.Count; second++)
                {
                    moves.Add(new ChooseLemonLords
                    {
                        PlayerId = player.PlayerId,
                        KeepTitleIds = new List<string> { dealt[first], dealt[second] },
                    });
                }
            }
        }

        // ------------------------------------------------------ setup draft

        private void AddInitialBuyMoves(List<GameAction> moves, PlayerState player)
        {
            if (player.PlayerId != CurrentInitialBuyer)
            {
                return;
            }
            if (!State.InitialBuyStandDone)
            {
                AddInitialStandBuys(moves, player);
                return;
            }

            moves.Add(new InitialBuyEnd { PlayerId = player.PlayerId });
            // Optional draft purchase: capped so the later mandatory Stand stays affordable.
            AddBlackMarketBuys(moves, player, SetupSpendingRoom(player));
            if (!State.MarketRefreshUsedThisTurn &&
                Db.Config.BlackMarketRefreshCost <= SetupSpendingRoom(player))
            {
                moves.Add(new RefreshMarket { PlayerId = player.PlayerId });
            }
        }

        private void AddInitialStandBuys(List<GameAction> moves, PlayerState player)
        {
            foreach (var standType in Db.StandTypes)
            {
                if (!CanBuyStand(player, standType))
                {
                    continue;
                }
                for (int insertIndex = 0; insertIndex <= player.Stands.Count; insertIndex++)
                {
                    moves.Add(new InitialBuyStand
                    {
                        PlayerId = player.PlayerId,
                        StandTypeId = standType.Id,
                        InsertIndex = insertIndex,
                    });
                }
            }
        }

        private bool CanBuyStand(PlayerState player, StandTypeDef standType) =>
            State.StandSupply[standType.Id].Count > 0 &&
            StandPrice(player.PlayerId, standType.Id) <= player.Money;

        // ------------------------------------------------------- turn moves

        private void AddTurnMoves(List<GameAction> moves, PlayerState player)
        {
            int playerId = player.PlayerId;
            moves.Add(new EndTurn { PlayerId = playerId });

            if (!State.MarketRefreshUsedThisTurn && player.Money >= Db.Config.BlackMarketRefreshCost)
            {
                moves.Add(new RefreshMarket { PlayerId = playerId });
            }

            AddLiquidEnergyMoves(moves, player);

            if (State.ActionsRemaining > 0)
            {
                moves.Add(new DrawLemonCard { PlayerId = playerId });
                AddStandBuys(moves, player);
                if (CanBuyBraggingRights(player))
                {
                    moves.Add(new BuyBraggingRights { PlayerId = playerId });
                }
                AddHandPlays(moves, player);
            }

            if (State.ActionsRemaining > 0 || State.BmOnlyActionsRemaining > 0)
            {
                AddBlackMarketBuys(moves, player);
            }
        }

        /// <summary>Liquid Energy is a free activated ability, usable outside roll windows.</summary>
        private void AddLiquidEnergyMoves(List<GameAction> moves, PlayerState player)
        {
            foreach (int equippedId in player.Turf.Equipped)
            {
                if (EquippedDef(equippedId).Id == BlackMarketCardIds.LiquidEnergy && !State.UsedTurnAbilities.Contains(equippedId))
                {
                    moves.Add(new UseTurnAbility { PlayerId = player.PlayerId, EquippedInstanceId = equippedId });
                }
            }
        }

        private void AddStandBuys(List<GameAction> moves, PlayerState player)
        {
            foreach (var standType in Db.StandTypes)
            {
                if (!CanBuyStand(player, standType))
                {
                    continue;
                }
                for (int insertIndex = 0; insertIndex <= player.Stands.Count; insertIndex++)
                {
                    moves.Add(new BuyStand
                    {
                        PlayerId = player.PlayerId,
                        StandTypeId = standType.Id,
                        InsertIndex = insertIndex,
                    });
                }
            }
        }

        private bool CanBuyBraggingRights(PlayerState player) =>
            !State.BraggingRightsBoughtThisTurn &&
            State.BraggingRightsSold < Db.Supporting.BraggingRightsPrices.Count &&
            Db.Supporting.BraggingRightsPrices[State.BraggingRightsSold] <= player.Money;

        private void AddHandPlays(List<GameAction> moves, PlayerState player)
        {
            foreach (int cardId in player.Hand.ToList())
            {
                var def = Db.Lemon(State.LemonInstances[cardId].DefId);
                if (def.Type == LemonCardType.Plan || def.Type == LemonCardType.Attack)
                {
                    moves.AddRange(PlayParamCombos(player, cardId, def));
                }
            }
        }

        private void AddBlackMarketBuys(List<GameAction> moves, PlayerState player, int? budget = null)
        {
            int spendable = budget ?? player.Money;
            for (int marketIndex = 0; marketIndex < State.Market.Count; marketIndex++)
            {
                var def = EquippedDef(State.Market[marketIndex]);
                if (BlackMarketPrice(player.PlayerId, def) > spendable)
                {
                    continue;
                }
                foreach (var (standId, replaceId) in DestinationsFor(player, def))
                {
                    moves.Add(new BuyBlackMarket
                    {
                        PlayerId = player.PlayerId,
                        MarketIndex = marketIndex,
                        TargetStandInstanceId = standId,
                        ReplaceInstanceId = replaceId,
                    });
                }
            }
        }
    }
}
