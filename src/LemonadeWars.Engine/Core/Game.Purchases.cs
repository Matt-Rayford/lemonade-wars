using System;
using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    public sealed partial class Game
    {
        // ----------------------------------------------------------- stands

        /// <summary>Stand price: base + $1 per Stand already owned (rulebook p8).</summary>
        public int StandPrice(int playerId, string standTypeId) =>
            Db.StandType(standTypeId).BaseCost +
            Player(playerId).Stands.Count * Db.Config.StandCostEscalationPerOwned;

        private void ApplyBuyStand(int playerId, string standTypeId, int? insertIndex, List<GameEvent> events, bool spendAction)
        {
            var player = Player(playerId);
            var standType = Db.StandType(standTypeId);
            var supply = State.StandSupply[standTypeId];
            if (supply.Count == 0)
            {
                throw new InvalidActionException($"No {standType.Name}s left in the supply.");
            }

            int price = StandPrice(playerId, standTypeId);
            Pay(player, price, standType.Name, events);
            if (spendAction)
            {
                SpendAction();
            }

            var stand = new StandInstance
            {
                InstanceId = State.NextInstanceId++,
                StandTypeId = standTypeId,
                Shape = supply[0],
            };
            supply.RemoveAt(0);
            // A Stand's position is chosen at purchase and never changes (rulebook p8):
            // Lefty Loosey / Righty Tighty read neighbors from it.
            int rowIndex = insertIndex is int requested
                ? Math.Clamp(requested, 0, player.Stands.Count)
                : player.Stands.Count;
            player.Stands.Insert(rowIndex, stand);
            events.Add(new StandPurchased
            {
                PlayerId = playerId,
                StandInstanceId = stand.InstanceId,
                StandTypeId = standTypeId,
                Price = price,
            });
        }

        // ----------------------------------------------------- black market

        private void ApplyBuyBlackMarket(BuyBlackMarket action, List<GameEvent> events)
        {
            // Legal both as a normal turn action and as the optional pick in the setup draft.
            bool duringSetup = State.Stage == GameStage.InitialBuys;
            if (duringSetup)
            {
                RequireSetupMarketVisit(action);
            }
            else
            {
                RequireBlackMarketBuyAction(action);
            }

            if (action.MarketIndex < 0 || action.MarketIndex >= State.Market.Count)
            {
                throw new InvalidActionException($"No market card at index {action.MarketIndex}.");
            }

            var player = Player(action.PlayerId);
            int instanceId = State.Market[action.MarketIndex];
            var def = Db.BlackMarket(State.BlackMarketInstances[instanceId].DefId);

            ValidateEquipDestination(player, def, action.TargetStandInstanceId, action.ReplaceInstanceId);

            int price = BlackMarketPrice(player.PlayerId, def);
            if (duringSetup && price > SetupSpendingRoom(player))
            {
                throw new InvalidActionException(
                    "You must keep enough money for your remaining mandatory Stand (rulebook p5).");
            }
            Pay(player, price, def.Name, events);
            if (!duringSetup)
            {
                SpendBlackMarketBuyAction();
            }

            State.Market.RemoveAt(action.MarketIndex);

            if (duringSetup)
            {
                EquipSetupPurchase(player, action, instanceId, price, events);
            }
            else
            {
                ContestPurchase(player, action, instanceId, price, events);
            }
        }

        private void RequireSetupMarketVisit(BuyBlackMarket action)
        {
            if (action.PlayerId != CurrentInitialBuyer)
            {
                throw new InvalidActionException($"It is P{CurrentInitialBuyer}'s initial buy.");
            }
            if (!State.InitialBuyStandDone)
            {
                throw new InvalidActionException("Buy the mandatory Stand first (rulebook p5).");
            }
        }

        /// <summary>Like <see cref="RequireTurnAction"/>, but a Shopping Spree buy can stand in for a normal action.</summary>
        private void RequireBlackMarketBuyAction(BuyBlackMarket action)
        {
            RequireActivePlayerInPlayPhase(action);
            if (State.ActionsRemaining <= 0 && State.BmOnlyActionsRemaining <= 0)
            {
                throw new InvalidActionException("No actions remaining this turn.");
            }
        }

        /// <summary>Shopping Spree's Black-Market-only buys are used up before normal actions.</summary>
        private void SpendBlackMarketBuyAction()
        {
            if (State.BmOnlyActionsRemaining > 0)
            {
                State.BmOnlyActionsRemaining--;
            }
            else
            {
                SpendAction();
            }
        }

        /// <summary>Pre-game purchases cannot be tantrummed: equip immediately and end the visit.</summary>
        private void EquipSetupPurchase(PlayerState player, BuyBlackMarket action, int instanceId, int price,
            List<GameEvent> events)
        {
            RefillMarket(action.MarketIndex);
            events.Add(new MarketRefilled { Market = State.Market.ToList() });
            TryEquip(player, instanceId, action.TargetStandInstanceId, action.ReplaceInstanceId, events,
                pricePaid: price);
            AdvanceInitialBuyQueue(events);
        }

        /// <summary>
        /// In-game purchases are contestable (rulebook p11): a cancel refunds the money, not the
        /// action. The slot stays empty meanwhile, so nobody sees the next card before deciding.
        /// </summary>
        private void ContestPurchase(PlayerState player, BuyBlackMarket action, int instanceId, int price,
            List<GameEvent> events)
        {
            var bought = State.BlackMarketInstances[instanceId];
            events.Add(new BlackMarketPurchaseStarted
            {
                PlayerId = player.PlayerId,
                InstanceId = instanceId,
                DefId = bought.DefId,
                Shape = bought.Shape,
                MarketIndex = action.MarketIndex,
                Price = price,
            });
            PushStackItem(new StackItem
            {
                Kind = StackItemKind.BlackMarketPurchase,
                OwnerId = player.PlayerId,
                BmInstanceId = instanceId,
                PaidCost = price,
                VacatedMarketIndex = action.MarketIndex,
                EquipStandInstanceId = action.TargetStandInstanceId,
                EquipReplaceInstanceId = action.ReplaceInstanceId,
            }, events);
            Pump(events);
        }

        private void CompleteBlackMarketPurchase(StackItem item, List<GameEvent> events)
        {
            var player = Player(item.OwnerId);
            if (!TryEquip(player, item.BmInstanceId!.Value,
                    item.EquipStandInstanceId, item.EquipReplaceInstanceId, events, pricePaid: item.PaidCost))
            {
                // Destination vanished mid-contest (cannot normally happen); card is lost.
                State.BlackMarketDiscard.Add(item.BmInstanceId.Value);
            }
            RefillAfterPurchase(item, events);
        }

        /// <summary>A contested purchase is over (equipped or cancelled): only now is the vacated slot refilled.</summary>
        private void RefillAfterPurchase(StackItem item, List<GameEvent> events)
        {
            RefillMarket(item.VacatedMarketIndex ?? -1);
            events.Add(new MarketRefilled { Market = State.Market.ToList() });
        }

        // ------------------------------------------------- black market row

        private int MarketSize =>
            State.Players.Count == 2 ? Db.Config.BlackMarketFaceUp2Player : Db.Config.BlackMarketFaceUp;

        /// <summary>
        /// A single taken card is replaced in its own slot (<paramref name="vacatedIndex"/>) so
        /// the shelf keeps its order; any further shortfall appends.
        /// </summary>
        private void RefillMarket(int vacatedIndex = -1)
        {
            while (State.Market.Count < MarketSize && TryDrawBlackMarket(out int id))
            {
                if (vacatedIndex >= 0 && vacatedIndex <= State.Market.Count)
                {
                    State.Market.Insert(vacatedIndex, id);
                    vacatedIndex = -1;
                }
                else
                {
                    State.Market.Add(id);
                }
            }
        }

        private bool TryDrawBlackMarket(out int instanceId)
        {
            if (State.BlackMarketDeck.Count == 0 && State.BlackMarketDiscard.Count > 0)
            {
                State.BlackMarketDeck.AddRange(State.BlackMarketDiscard);
                State.BlackMarketDiscard.Clear();
                _rng.Shuffle(State.BlackMarketDeck);
            }
            if (State.BlackMarketDeck.Count == 0)
            {
                instanceId = 0;
                return false;
            }
            instanceId = State.BlackMarketDeck[0];
            State.BlackMarketDeck.RemoveAt(0);
            return true;
        }

        private void ApplyRefreshMarket(RefreshMarket action, List<GameEvent> events)
        {
            RequireMarketRefreshAllowed(action);

            var player = Player(action.PlayerId);
            Pay(player, Db.Config.BlackMarketRefreshCost, "market refresh", events);
            State.MarketRefreshUsedThisTurn = true;

            State.BlackMarketDiscard.AddRange(State.Market);
            State.Market.Clear();
            RefillMarket();
            events.Add(new MarketRefilled
            {
                Market = State.Market.ToList(),
                RefreshedByPlayerId = player.PlayerId,
            });
        }

        private void RequireMarketRefreshAllowed(RefreshMarket action)
        {
            if (State.Stage == GameStage.InitialBuys)
            {
                // Draft visits may refresh too: after the mandatory Stand, and only with
                // money the setup reserve doesn't need for later Stand purchases.
                if (action.PlayerId != CurrentInitialBuyer || !State.InitialBuyStandDone)
                {
                    throw new InvalidActionException(
                        "Draft market refresh comes after your Stand purchase, on your own visit.");
                }
                if (Db.Config.BlackMarketRefreshCost > SetupSpendingRoom(Player(action.PlayerId)))
                {
                    throw new InvalidActionException(
                        "Refreshing would leave too little for your later mandatory Stand.");
                }
            }
            else
            {
                RequirePlaying();
                if (action.PlayerId != State.ActivePlayer || State.Phase != TurnPhase.Play)
                {
                    throw new InvalidActionException("Market refresh is a free action on your own Play phase.");
                }
            }
            if (State.MarketRefreshUsedThisTurn)
            {
                throw new InvalidActionException("Market refresh is once per turn (rulebook p6).");
            }
        }

        // -------------------------------------------------- bragging rights

        private void ApplyBuyBraggingRights(BuyBraggingRights action, List<GameEvent> events)
        {
            if (State.BraggingRightsBoughtThisTurn)
            {
                throw new InvalidActionException("Bragging Rights limit is 1 per turn (rulebook p6).");
            }
            var prices = Db.Supporting.BraggingRightsPrices;
            if (State.BraggingRightsSold >= prices.Count)
            {
                throw new InvalidActionException("All Bragging Rights have been sold.");
            }

            var player = Player(action.PlayerId);
            int price = prices[State.BraggingRightsSold];
            Pay(player, price, "Bragging Rights", events, countsAsSpending: false);
            SpendAction();

            State.BraggingRightsSold++;
            player.BraggingRights++;
            State.BraggingRightsBoughtThisTurn = true;
            events.Add(new BraggingRightsPurchased { PlayerId = player.PlayerId, Price = price });

            // Victory point purchases cannot be tantrummed (rulebook p11).
            AssignSpoiledRotten(events);
        }
    }
}
