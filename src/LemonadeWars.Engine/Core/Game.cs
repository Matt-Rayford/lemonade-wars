using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;
using Newtonsoft.Json;

namespace LemonadeWars.Engine.Core
{
    /// <summary>Thrown when a submitted action is illegal in the current state.</summary>
    public sealed class InvalidActionException : Exception
    {
        public InvalidActionException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// The rules engine. Owns a <see cref="GameState"/> and mutates it exclusively through
    /// <see cref="Apply"/>, emitting <see cref="GameEvent"/>s that presentation layers animate from.
    /// Fully deterministic: same seed + same action sequence = same state on every platform.
    ///
    /// The complete ruleset is implemented; remaining engine work is hidden-information
    /// views for clients and AI opponents.
    /// </summary>
    public sealed partial class Game
    {
        public CardDatabase Db { get; }
        public GameState State { get; }

        private readonly DeterministicRng _rng;

        private Game(CardDatabase db, GameState state, DeterministicRng rng)
        {
            Db = db;
            State = state;
            _rng = rng;
        }

        // ------------------------------------------------------------ setup

        /// <summary>Create a game and run automatic setup up to the Lemon Lord keep-2 choice.</summary>
        public static Game Create(CardDatabase db, IReadOnlyList<string> playerNames, ulong seed)
        {
            var cfg = db.Config;
            if (playerNames.Count < cfg.MinPlayers || playerNames.Count > cfg.MaxPlayers)
            {
                throw new ArgumentException(
                    $"Player count must be {cfg.MinPlayers}-{cfg.MaxPlayers}, got {playerNames.Count}.");
            }

            var state = new GameState();
            var rng = new DeterministicRng(seed);
            var game = new Game(db, state, rng);

            for (int i = 0; i < playerNames.Count; i++)
            {
                state.Players.Add(new PlayerState
                {
                    PlayerId = i,
                    Name = playerNames[i],
                    Money = cfg.StartingMoney,
                });
            }

            game.BuildLemonDeck();
            game.BuildBlackMarketDeck();
            game.BuildStandSupply();
            game.DealTurfs();
            game.DealFirstDibsRow();
            game.RefillMarket();

            // Random first player; players after them in turn order get less bonus money
            // (rulebook p5: 1st gets +$1 per other player, descending to +$0 for the last).
            state.FirstPlayer = rng.Next(state.Players.Count);
            int n = state.Players.Count;
            for (int offset = 0; offset < n; offset++)
            {
                var player = state.Players[(state.FirstPlayer + offset) % n];
                player.Money += n - 1 - offset;
            }

            // Deal starting hands, bouncing Timeout cards back into the deck (rulebook p5).
            foreach (var player in state.Players)
            {
                for (int c = 0; c < cfg.StartingHandSize; c++)
                {
                    game.SetupDraw(player);
                }
            }

            // Deal 3 Lemon Lord candidates each; players choose 2 to keep via ChooseLemonLords.
            var lordIds = db.LemonLordTitles.Select(t => t.Id).ToList();
            rng.Shuffle(lordIds);
            int next = 0;
            foreach (var player in state.Players)
            {
                for (int c = 0; c < cfg.LemonLordDealt; c++)
                {
                    player.LemonLordDealt.Add(lordIds[next++]);
                }
            }

            state.RngState = rng.State;
            return game;
        }

        private void BuildLemonDeck()
        {
            foreach (var def in Db.LemonCards)
            {
                for (int i = 0; i < def.Count; i++)
                {
                    var instance = new LemonCardInstance
                    {
                        InstanceId = State.NextInstanceId++,
                        DefId = def.Id,
                    };
                    State.LemonInstances[instance.InstanceId] = instance;
                    State.LemonDeck.Add(instance.InstanceId);
                }
            }
            _rng.Shuffle(State.LemonDeck);
        }

        private void BuildBlackMarketDeck()
        {
            foreach (var def in Db.BlackMarketCards)
            {
                foreach (var shape in def.Shapes)
                {
                    var instance = new BlackMarketCardInstance
                    {
                        InstanceId = State.NextInstanceId++,
                        DefId = def.Id,
                        Shape = shape,
                    };
                    State.BlackMarketInstances[instance.InstanceId] = instance;
                    State.BlackMarketDeck.Add(instance.InstanceId);
                }
            }
            _rng.Shuffle(State.BlackMarketDeck);
        }

        private void BuildStandSupply()
        {
            foreach (var standType in Db.StandTypes)
            {
                var stack = new List<Shape>();
                foreach (var pair in standType.Shapes.OrderBy(p => p.Key))
                {
                    for (int i = 0; i < pair.Value; i++)
                    {
                        stack.Add(pair.Key);
                    }
                }
                _rng.Shuffle(stack);
                State.StandSupply[standType.Id] = stack;
            }
        }

        private void DealTurfs()
        {
            var numbers = Db.Turf.PowerPourNumbers.ToList();
            _rng.Shuffle(numbers);
            for (int i = 0; i < State.Players.Count; i++)
            {
                State.Players[i].Turf.PowerPourNumber = numbers[i];
            }
        }

        private void DealFirstDibsRow()
        {
            var ids = Db.FirstDibsTitles.Select(t => t.Id).ToList();
            _rng.Shuffle(ids);
            int count = State.Players.Count + 1;
            State.FirstDibsRow.AddRange(ids.Take(count));
        }

        private int MarketSize =>
            State.Players.Count == 2 ? Db.Config.BlackMarketFaceUp2Player : Db.Config.BlackMarketFaceUp;

        private void RefillMarket()
        {
            while (State.Market.Count < MarketSize && TryDrawBlackMarket(out int id))
            {
                State.Market.Add(id);
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

        // ----------------------------------------------------- action entry

        /// <summary>Validate and execute a player action, returning the events it produced.</summary>
        public IReadOnlyList<GameEvent> Apply(GameAction action)
        {
            RequireInteractionGate(action);

            var events = new List<GameEvent>();
            switch (action)
            {
                case ChooseLemonLords choose:
                    ApplyChooseLemonLords(choose, events);
                    break;
                case InitialBuyStand buyStand:
                    ApplyInitialBuyStand(buyStand, events);
                    break;
                case InitialBuyEnd end:
                    ApplyInitialBuyEnd(end, events);
                    break;
                case DrawLemonCard draw:
                    RequireTurnAction(draw);
                    SpendAction();
                    QueueDraws(draw.PlayerId, 1);
                    Pump(events);
                    break;
                case BuyStand buy:
                    RequireTurnAction(buy);
                    ApplyBuyStand(buy.PlayerId, buy.StandTypeId, buy.InsertIndex, events, spendAction: true);
                    break;
                case BuyBlackMarket buyBm:
                    ApplyBuyBlackMarket(buyBm, events);
                    break;
                case BuyBraggingRights brag:
                    RequireTurnAction(brag);
                    ApplyBuyBraggingRights(brag, events);
                    break;
                case RefreshMarket refresh:
                    ApplyRefreshMarket(refresh, events);
                    break;
                case EndTurn endTurn:
                    ApplyEndTurn(endTurn, events);
                    break;
                case PlayLemonCard play:
                    ApplyPlayLemonCard(play, events);
                    break;
                case RespondToWindow respond:
                    ApplyRespondToWindow(respond, events);
                    break;
                case PassWindow pass:
                    ApplyPassWindow(pass, events);
                    break;
                case SubmitDiscard discard:
                    ApplySubmitDiscard(discard, events);
                    break;
                case SubmitTimeoutPayment payment:
                    ApplySubmitTimeoutPayment(payment, events);
                    break;
                case SubmitRetarget retarget:
                    ApplySubmitRetarget(retarget, events);
                    break;
                case SkipFreePlay skip:
                    ApplySkipFreePlay(skip, events);
                    break;
                case UseTurnAbility ability:
                    ApplyUseTurnAbility(ability, events);
                    break;
                case SubmitAbilityChoice choice:
                    ApplySubmitAbilityChoice(choice, events);
                    break;
                default:
                    throw new InvalidActionException($"Unknown action type {action.GetType().Name}.");
            }
            State.RngState = _rng.State;

            // First Dibs titles are claimed the moment their condition is met (not tantrummable).
            if (State.Stage == GameStage.Playing || State.Stage == GameStage.FinalRound)
            {
                CheckFirstDibs(events);
            }
            return events;
        }

        /// <summary>
        /// While windows or decisions are pending, only the matching interaction actions are
        /// legal; conversely interaction actions need an open window/decision to respond to.
        /// </summary>
        private void RequireInteractionGate(GameAction action)
        {
            bool interactionPending =
                State.PendingDecisions.Count > 0 ||
                State.AwaitingResponse.Count > 0 ||
                State.ResponseStack.Count > 0 ||
                State.PendingRoll != null ||
                State.TheftQueue.Count > 0;

            bool isInteractionAction =
                action is RespondToWindow || action is PassWindow ||
                action is SubmitDiscard || action is SubmitTimeoutPayment ||
                action is SubmitRetarget || action is SkipFreePlay ||
                action is SubmitAbilityChoice ||
                (action is UseTurnAbility && State.PendingRoll != null) ||
                (action is PlayLemonCard && State.PendingDecisions.Any(d =>
                    d.PlayerId == action.PlayerId &&
                    (d.Kind == DecisionKind.FreePlayOffer || d.Kind == DecisionKind.ForcedPlay ||
                     d.Kind == DecisionKind.BouncerAttack)));

            if (interactionPending && !isInteractionAction)
            {
                throw new InvalidActionException(
                    "A response window or decision is pending; resolve it first.");
            }
        }

        // ---------------------------------------------------- setup actions

        private void ApplyChooseLemonLords(ChooseLemonLords action, List<GameEvent> events)
        {
            RequireStage(GameStage.ChoosingLemonLords);
            var player = Player(action.PlayerId);
            if (player.LemonLordKept.Count > 0)
            {
                throw new InvalidActionException($"P{action.PlayerId} already chose Lemon Lords.");
            }
            if (action.KeepTitleIds.Count != Db.Config.LemonLordKept ||
                action.KeepTitleIds.Distinct().Count() != Db.Config.LemonLordKept ||
                action.KeepTitleIds.Any(id => !player.LemonLordDealt.Contains(id)))
            {
                throw new InvalidActionException(
                    $"Must keep exactly {Db.Config.LemonLordKept} distinct titles from those dealt.");
            }

            player.LemonLordKept.AddRange(action.KeepTitleIds);

            if (State.Players.All(p => p.LemonLordKept.Count == Db.Config.LemonLordKept))
            {
                BeginInitialBuys(events);
            }
        }

        private void BeginInitialBuys(List<GameEvent> events)
        {
            State.Stage = GameStage.InitialBuys;
            events.Add(new StageChanged { Stage = State.Stage });

            // Snake draft: clockwise from the first player, then counter-clockwise (rulebook p5).
            int n = State.Players.Count;
            var forward = Enumerable.Range(0, n).Select(i => (State.FirstPlayer + i) % n).ToList();
            State.InitialBuyQueue.AddRange(forward);
            State.InitialBuyQueue.AddRange(Enumerable.Reverse(forward));
            State.InitialBuyStandDone = false;
        }

        private int CurrentInitialBuyer =>
            State.InitialBuyQueue.Count > 0
                ? State.InitialBuyQueue[0]
                : throw new InvalidActionException("Initial buys are over.");

        private void ApplyInitialBuyStand(InitialBuyStand action, List<GameEvent> events)
        {
            RequireStage(GameStage.InitialBuys);
            if (action.PlayerId != CurrentInitialBuyer)
            {
                throw new InvalidActionException($"It is P{CurrentInitialBuyer}'s initial buy, not P{action.PlayerId}'s.");
            }
            if (State.InitialBuyStandDone)
            {
                throw new InvalidActionException("Already bought the mandatory Stand this visit.");
            }
            ApplyBuyStand(action.PlayerId, action.StandTypeId, action.InsertIndex, events, spendAction: false);
            State.InitialBuyStandDone = true;
        }

        private void ApplyInitialBuyEnd(InitialBuyEnd action, List<GameEvent> events)
        {
            RequireStage(GameStage.InitialBuys);
            if (action.PlayerId != CurrentInitialBuyer)
            {
                throw new InvalidActionException($"It is P{CurrentInitialBuyer}'s initial buy, not P{action.PlayerId}'s.");
            }
            if (!State.InitialBuyStandDone)
            {
                throw new InvalidActionException("Must buy a Stand before passing (rulebook p5).");
            }
            AdvanceInitialBuyQueue(events);
        }

        private void AdvanceInitialBuyQueue(List<GameEvent> events)
        {
            State.InitialBuyQueue.RemoveAt(0);
            State.InitialBuyStandDone = false;
            if (State.InitialBuyQueue.Count == 0)
            {
                State.Stage = GameStage.Playing;
                events.Add(new StageChanged { Stage = State.Stage });
                State.ActivePlayer = State.FirstPlayer;
                StartTurn(events);
                Pump(events);
            }
        }

        // ----------------------------------------------------- turn actions

        private void RequireStage(GameStage stage)
        {
            if (State.Stage != stage)
            {
                throw new InvalidActionException($"Expected stage {stage}, but game is in {State.Stage}.");
            }
        }

        private void RequirePlaying()
        {
            if (State.Stage != GameStage.Playing && State.Stage != GameStage.FinalRound)
            {
                throw new InvalidActionException($"Game is not in play (stage {State.Stage}).");
            }
        }

        /// <summary>Common checks for the 2-action Play phase.</summary>
        private void RequireTurnAction(GameAction action)
        {
            RequirePlaying();
            if (action.PlayerId != State.ActivePlayer)
            {
                throw new InvalidActionException($"It is P{State.ActivePlayer}'s turn, not P{action.PlayerId}'s.");
            }
            if (State.Phase != TurnPhase.Play)
            {
                throw new InvalidActionException($"Actions are only legal in the Play phase (now {State.Phase}).");
            }
            if (State.ActionsRemaining <= 0)
            {
                throw new InvalidActionException("No actions remaining this turn.");
            }
        }

        private void SpendAction() => State.ActionsRemaining--;

        private PlayerState Player(int id)
        {
            if (id < 0 || id >= State.Players.Count)
            {
                throw new InvalidActionException($"No player {id}.");
            }
            return State.Players[id];
        }

        private void Pay(PlayerState player, int amount, string reason, List<GameEvent> events,
            bool countsAsSpending = true)
        {
            if (player.Money < amount)
            {
                throw new InvalidActionException(
                    $"P{player.PlayerId} cannot afford ${amount} for {reason} (has ${player.Money}).");
            }
            player.Money -= amount;
            // Shopaholic tracks the active player's spending, Bragging Rights excluded.
            if (countsAsSpending && player.PlayerId == State.ActivePlayer)
            {
                State.SpentThisTurn += amount;
            }
            events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = -amount, Reason = reason });
        }

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

            Pay(player, StandPrice(playerId, standTypeId), standType.Name, events);
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
            // Stands live in a row: position is chosen at purchase and never changes
            // (rulebook p8) — Lefty Loosey / Righty Tighty read neighbors from it.
            int index = insertIndex is int i ? Math.Clamp(i, 0, player.Stands.Count) : player.Stands.Count;
            player.Stands.Insert(index, stand);
            events.Add(new StandPurchased
            {
                PlayerId = playerId,
                StandInstanceId = stand.InstanceId,
                StandTypeId = standTypeId,
            });
        }

        private void ApplyBuyBlackMarket(BuyBlackMarket action, List<GameEvent> events)
        {
            // Legal both as a normal turn action and as the optional pick in the initial draft.
            bool duringSetup = State.Stage == GameStage.InitialBuys;
            if (duringSetup)
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
            else
            {
                // Like RequireTurnAction, but a Shopping Spree BM-only action can stand in
                // for a normal action.
                RequirePlaying();
                if (action.PlayerId != State.ActivePlayer)
                {
                    throw new InvalidActionException($"It is P{State.ActivePlayer}'s turn, not P{action.PlayerId}'s.");
                }
                if (State.Phase != TurnPhase.Play)
                {
                    throw new InvalidActionException($"Actions are only legal in the Play phase (now {State.Phase}).");
                }
                if (State.ActionsRemaining <= 0 && State.BmOnlyActionsRemaining <= 0)
                {
                    throw new InvalidActionException("No actions remaining this turn.");
                }
            }

            if (action.MarketIndex < 0 || action.MarketIndex >= State.Market.Count)
            {
                throw new InvalidActionException($"No market card at index {action.MarketIndex}.");
            }

            var player = Player(action.PlayerId);
            int instanceId = State.Market[action.MarketIndex];
            var def = Db.BlackMarket(State.BlackMarketInstances[instanceId].DefId);

            ValidateEquipDestination(player, def, action.TargetStandInstanceId, action.ReplaceInstanceId);

            int price = BlackMarketPrice(player.PlayerId, def); // Peddlin' Pete discount
            Pay(player, price, def.Name, events);
            if (!duringSetup)
            {
                // Shopping Spree grants extra Black-Market-only buy actions.
                if (State.BmOnlyActionsRemaining > 0)
                {
                    State.BmOnlyActionsRemaining--;
                }
                else
                {
                    SpendAction();
                }
            }

            State.Market.RemoveAt(action.MarketIndex);
            RefillMarket();
            events.Add(new MarketRefilled { Market = State.Market.ToList() });

            if (duringSetup)
            {
                // Pre-game purchases cannot be tantrummed — equip immediately.
                TryEquip(player, instanceId, action.TargetStandInstanceId, action.ReplaceInstanceId, events);
                AdvanceInitialBuyQueue(events);
                return;
            }

            // In-game purchases are contestable (rulebook p11): money is paid up front and
            // refunded (action is not) if a tantrum chain cancels the purchase.
            PushStackItem(new StackItem
            {
                Kind = StackItemKind.BlackMarketPurchase,
                OwnerId = player.PlayerId,
                BmInstanceId = instanceId,
                PaidCost = price,
                EquipStandInstanceId = action.TargetStandInstanceId,
                EquipReplaceInstanceId = action.ReplaceInstanceId,
            }, events);
            Pump(events);
        }

        /// <summary>A purchase survived its tantrum window: equip it.</summary>
        private void CompleteBlackMarketPurchase(StackItem item, List<GameEvent> events)
        {
            var player = Player(item.OwnerId);
            if (!TryEquip(player, item.BmInstanceId!.Value,
                    item.EquipStandInstanceId, item.EquipReplaceInstanceId, events))
            {
                // Destination vanished mid-contest (cannot normally happen); card is lost.
                State.BlackMarketDiscard.Add(item.BmInstanceId.Value);
            }
        }

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
            Pay(player, price, "Bragging Rights", events, countsAsSpending: false); // Shopaholic excludes these
            SpendAction();

            State.BraggingRightsSold++;
            player.BraggingRights++;
            State.BraggingRightsBoughtThisTurn = true;
            events.Add(new BraggingRightsPurchased { PlayerId = player.PlayerId, Price = price });

            // Victory point purchases cannot be tantrummed (rulebook p11).
            AssignSpoiledRotten(events);
        }

        /// <summary>Each VP gain re-checks Spoiled Rotten: sole last place holds it (rulebook p12).</summary>
        private void AssignSpoiledRotten(List<GameEvent> events)
        {
            int min = State.Players.Min(p => p.InGameVictoryPoints);
            var last = State.Players.Where(p => p.InGameVictoryPoints == min).ToList();
            int? newHolder = last.Count == 1 ? last[0].PlayerId : (int?)null;
            if (newHolder != State.SpoiledRottenHolder)
            {
                events.Add(new SpoiledRottenMoved
                {
                    FromPlayerId = State.SpoiledRottenHolder,
                    ToPlayerId = newHolder,
                });
                State.SpoiledRottenHolder = newHolder;
            }
        }

        private void ApplyRefreshMarket(RefreshMarket action, List<GameEvent> events)
        {
            RequirePlaying();
            if (action.PlayerId != State.ActivePlayer || State.Phase != TurnPhase.Play)
            {
                throw new InvalidActionException("Market refresh is a free action on your own Play phase.");
            }
            if (State.MarketRefreshUsedThisTurn)
            {
                throw new InvalidActionException("Market refresh is once per turn (rulebook p6).");
            }

            var player = Player(action.PlayerId);
            Pay(player, Db.Config.BlackMarketRefreshCost, "market refresh", events);
            State.MarketRefreshUsedThisTurn = true;

            State.BlackMarketDiscard.AddRange(State.Market);
            State.Market.Clear();
            RefillMarket();
            events.Add(new MarketRefilled { Market = State.Market.ToList() });
        }

        // ------------------------------------------------------- turn cycle
        // StartTurn, draws, Timeout handling, and sale resolution live in Game.Responses.cs;
        // everything below flows through the interaction pump.

        /// <summary>Setup-only draw: Timeout cards are shuffled back instead of resolving (rulebook p5).</summary>
        private void SetupDraw(PlayerState player)
        {
            while (State.LemonDeck.Count > 0)
            {
                int instanceId = State.LemonDeck[0];
                State.LemonDeck.RemoveAt(0);
                if (Db.Lemon(State.LemonInstances[instanceId].DefId).Type == LemonCardType.Timeout)
                {
                    State.LemonDeck.Add(instanceId);
                    _rng.Shuffle(State.LemonDeck);
                    continue;
                }
                player.Hand.Add(instanceId);
                return;
            }
        }

        private void ApplyEndTurn(EndTurn action, List<GameEvent> events)
        {
            RequirePlaying();
            if (action.PlayerId != State.ActivePlayer)
            {
                throw new InvalidActionException($"It is P{State.ActivePlayer}'s turn.");
            }
            if (State.Phase != TurnPhase.Play)
            {
                throw new InvalidActionException("Turn can only end from the Play phase.");
            }

            State.Phase = TurnPhase.Sell;
            OpenRollWindow(RollPurpose.TurnSale, State.ActivePlayer, events);
            Pump(events);
        }

        private void FinishGame(List<GameEvent> events) => FinishGameWithScores(events);

        // -------------------------------------------------------- utilities

        /// <summary>Full state snapshot as JSON — used by tests and (later) save games / network sync.</summary>
        public string SnapshotJson() => JsonConvert.SerializeObject(State, Formatting.Indented);
    }
}
