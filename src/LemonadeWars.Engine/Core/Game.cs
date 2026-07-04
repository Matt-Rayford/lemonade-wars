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
    /// Not yet implemented (next passes): Lemon card play + the tantrum stack, Black Market
    /// effect resolution, First Dibs auto-claiming, Lemon Lord end-game scoring, Whiniest
    /// Baby / Spoiled Rotten mechanics.
    /// </summary>
    public sealed class Game
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
                    game.DrawIntoHand(player, allowTimeout: false);
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
                    DrawIntoHand(Player(draw.PlayerId), allowTimeout: true, events);
                    break;
                case BuyStand buy:
                    RequireTurnAction(buy);
                    ApplyBuyStand(buy.PlayerId, buy.StandTypeId, events, spendAction: true);
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
                default:
                    throw new InvalidActionException($"Unknown action type {action.GetType().Name}.");
            }
            State.RngState = _rng.State;
            return events;
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
            ApplyBuyStand(action.PlayerId, action.StandTypeId, events, spendAction: false);
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

        private void Pay(PlayerState player, int amount, string reason, List<GameEvent> events)
        {
            if (player.Money < amount)
            {
                throw new InvalidActionException(
                    $"P{player.PlayerId} cannot afford ${amount} for {reason} (has ${player.Money}).");
            }
            player.Money -= amount;
            events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = -amount, Reason = reason });
        }

        /// <summary>Stand price: base + $1 per Stand already owned (rulebook p8).</summary>
        public int StandPrice(int playerId, string standTypeId) =>
            Db.StandType(standTypeId).BaseCost +
            Player(playerId).Stands.Count * Db.Config.StandCostEscalationPerOwned;

        private void ApplyBuyStand(int playerId, string standTypeId, List<GameEvent> events, bool spendAction)
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
            player.Stands.Add(stand);
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
                RequireTurnAction(action);
            }

            if (action.MarketIndex < 0 || action.MarketIndex >= State.Market.Count)
            {
                throw new InvalidActionException($"No market card at index {action.MarketIndex}.");
            }

            var player = Player(action.PlayerId);
            int instanceId = State.Market[action.MarketIndex];
            var instance = State.BlackMarketInstances[instanceId];
            var def = Db.BlackMarket(instance.DefId);

            // Resolve the equip target and its slot limit.
            List<int> equipped;
            int slotLimit;
            if (def.Target == EquipTarget.Stand)
            {
                if (!(action.TargetStandInstanceId is int standId))
                {
                    throw new InvalidActionException($"{def.Name} is a Stand upgrade; choose a target Stand.");
                }
                var stand = player.Stands.FirstOrDefault(s => s.InstanceId == standId)
                    ?? throw new InvalidActionException($"P{player.PlayerId} has no Stand {standId}.");
                equipped = stand.Equipped;
                slotLimit = Db.StandType(stand.StandTypeId).UpgradeSlots;
            }
            else
            {
                if (action.TargetStandInstanceId != null)
                {
                    throw new InvalidActionException($"{def.Name} is a Turf upgrade; it cannot target a Stand.");
                }
                equipped = player.Turf.Equipped;
                slotLimit = Db.Turf.UpgradeSlots;
            }

            // At the limit, the buyer may discard an existing card to make room (rulebook p9).
            if (equipped.Count >= slotLimit)
            {
                if (!(action.ReplaceInstanceId is int replaceId) || !equipped.Contains(replaceId))
                {
                    throw new InvalidActionException(
                        "Target is at its upgrade limit; choose an equipped card to discard.");
                }
                equipped.Remove(replaceId);
                State.BlackMarketDiscard.Add(replaceId);
                events.Add(new CardsDiscarded
                {
                    PlayerId = player.PlayerId,
                    InstanceIds = new List<int> { replaceId },
                });
            }

            // TODO(effects): Peddlin' Pete's $1 discount on Black Market purchases.
            Pay(player, def.Cost, def.Name, events);
            if (!duringSetup)
            {
                SpendAction();
            }

            State.Market.RemoveAt(action.MarketIndex);
            equipped.Add(instanceId);
            events.Add(new BlackMarketPurchased
            {
                PlayerId = player.PlayerId,
                InstanceId = instanceId,
                DefId = def.Id,
                TargetStandInstanceId = def.Target == EquipTarget.Stand ? action.TargetStandInstanceId : null,
            });

            RefillMarket();
            events.Add(new MarketRefilled { Market = State.Market.ToList() });

            if (duringSetup)
            {
                // The optional Black Market pick ends this draft visit.
                AdvanceInitialBuyQueue(events);
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
            Pay(player, price, "Bragging Rights", events);
            SpendAction();

            State.BraggingRightsSold++;
            player.BraggingRights++;
            State.BraggingRightsBoughtThisTurn = true;
            events.Add(new BraggingRightsPurchased { PlayerId = player.PlayerId, Price = price });
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

        private void StartTurn(List<GameEvent> events)
        {
            State.Phase = TurnPhase.Start;
            State.ActionsRemaining = Db.Config.ActionsPerTurn;
            State.MarketRefreshUsedThisTurn = false;
            State.BraggingRightsBoughtThisTurn = false;
            events.Add(new TurnStarted { PlayerId = State.ActivePlayer });

            var player = Player(State.ActivePlayer);
            // TODO(whiniest-baby): draw 2 and discard 1 instead, once tantrums can assign the card.
            for (int i = 0; i < Db.Config.TurnStartDraw; i++)
            {
                DrawIntoHand(player, allowTimeout: true, events);
            }

            State.Phase = TurnPhase.Play;
        }

        /// <summary>
        /// Draw one Lemon card. With <paramref name="allowTimeout"/> false (setup), Timeouts are
        /// shuffled back; otherwise a drawn Timeout resolves immediately (rulebook p12).
        /// </summary>
        private void DrawIntoHand(PlayerState player, bool allowTimeout, List<GameEvent>? events = null)
        {
            while (true)
            {
                if (State.LemonDeck.Count == 0)
                {
                    State.LemonDeck.AddRange(State.LemonDiscard);
                    State.LemonDiscard.Clear();
                    _rng.Shuffle(State.LemonDeck);
                    if (State.LemonDeck.Count == 0)
                    {
                        return; // Nothing left to draw anywhere.
                    }
                }

                int instanceId = State.LemonDeck[0];
                State.LemonDeck.RemoveAt(0);
                var def = Db.Lemon(State.LemonInstances[instanceId].DefId);

                if (def.Type != LemonCardType.Timeout)
                {
                    player.Hand.Add(instanceId);
                    events?.Add(new CardDrawn
                    {
                        PlayerId = player.PlayerId,
                        InstanceId = instanceId,
                        DefId = def.Id,
                    });
                    return;
                }

                if (!allowTimeout)
                {
                    // Setup deal: shuffle the Timeout back and draw again (rulebook p5).
                    State.LemonDeck.Add(instanceId);
                    _rng.Shuffle(State.LemonDeck);
                    continue;
                }

                ResolveTimeout(player, instanceId, events);
                // The drawer then draws a replacement card (rulebook p12) — loop.
            }
        }

        private void ResolveTimeout(PlayerState drawer, int timeoutInstanceId, List<GameEvent>? events)
        {
            events?.Add(new TimeoutDrawn { PlayerId = drawer.PlayerId });

            // All players discard down to the hand limit.
            // TODO(decisions): let players choose which cards to discard; for now the newest go.
            foreach (var player in State.Players)
            {
                int excess = player.Hand.Count - Db.Config.TimeoutHandLimit;
                if (excess <= 0)
                {
                    continue;
                }
                var discarded = new List<int>();
                for (int i = 0; i < excess; i++)
                {
                    int id = player.Hand[player.Hand.Count - 1];
                    player.Hand.RemoveAt(player.Hand.Count - 1);
                    State.LemonDiscard.Add(id);
                    discarded.Add(id);
                }
                events?.Add(new CardsDiscarded { PlayerId = player.PlayerId, InstanceIds = discarded });
            }

            // TODO(tantrums): Whiniest Baby pays $3 per played tantrum and passes the card.
            State.LemonDiscard.Add(timeoutInstanceId);
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

            ResolveSellPhase(events);

            // Game-end trigger: a player ends their turn at the VP target (rulebook p2).
            var active = Player(State.ActivePlayer);
            if (State.Stage == GameStage.Playing &&
                active.InGameVictoryPoints >= Db.Config.VictoryPointsToTriggerEnd)
            {
                State.Stage = GameStage.FinalRound;
                State.EndTriggeredBy = active.PlayerId;
                events.Add(new StageChanged { Stage = State.Stage });
            }

            int nextPlayer = (State.ActivePlayer + 1) % State.Players.Count;
            if (State.Stage == GameStage.FinalRound && nextPlayer == State.FirstPlayer)
            {
                FinishGame(events);
                return;
            }

            State.ActivePlayer = nextPlayer;
            StartTurn(events);
        }

        private void ResolveSellPhase(List<GameEvent> events)
        {
            State.Phase = TurnPhase.Sell;

            int roll = _rng.Roll(Db.Config.SaleDieSides);
            events.Add(new SaleRolled { PlayerId = State.ActivePlayer, Value = roll });
            // TODO(instants): "Out of Stock" re-roll window goes here.
            // TODO(effects): roll modifiers (Downsell, Sugared Up, Pushy Salesman, ...).

            int n = State.Players.Count;
            for (int offset = 0; offset < n; offset++)
            {
                var player = State.Players[(State.ActivePlayer + offset) % n];

                foreach (var stand in player.Stands)
                {
                    var standType = Db.StandType(stand.StandTypeId);
                    if (!standType.SaleNumbers.Contains(roll))
                    {
                        continue;
                    }
                    // TODO(effects): equipped Black Market "On Sale" triggers and earning bonuses.
                    player.Money += standType.BaseEarnings;
                    events.Add(new StandSold
                    {
                        PlayerId = player.PlayerId,
                        StandInstanceId = stand.InstanceId,
                        Earnings = standType.BaseEarnings,
                    });
                }

                if (player.Turf.PowerPourNumber == roll)
                {
                    // Base Turf ability: take $1 from the bank (rulebook p7).
                    // TODO(effects): equipped "Power Pour" Black Market triggers.
                    events.Add(new PowerPourTriggered { PlayerId = player.PlayerId });
                    player.Money += Db.Turf.BasePowerPourMoney;
                    events.Add(new MoneyChanged
                    {
                        PlayerId = player.PlayerId,
                        Amount = Db.Turf.BasePowerPourMoney,
                        Reason = "power pour",
                    });
                }
            }
        }

        private void FinishGame(List<GameEvent> events)
        {
            State.Stage = GameStage.Finished;

            // TODO(titles): evaluate kept Lemon Lord conditions for their end-game VP.
            int best = State.Players.Max(p => p.InGameVictoryPoints);
            State.Winners.AddRange(
                State.Players.Where(p => p.InGameVictoryPoints == best).Select(p => p.PlayerId));

            events.Add(new StageChanged { Stage = State.Stage });
            events.Add(new GameEnded { Winners = State.Winners.ToList() });
        }

        // -------------------------------------------------------- utilities

        /// <summary>Full state snapshot as JSON — used by tests and (later) save games / network sync.</summary>
        public string SnapshotJson() => JsonConvert.SerializeObject(State, Formatting.Indented);
    }
}
