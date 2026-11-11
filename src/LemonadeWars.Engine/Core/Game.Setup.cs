using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    public sealed partial class Game
    {
        // ------------------------------------------------------ table setup

        /// <summary>Runs automatic setup up to the Lemon Lord keep-2 choice.</summary>
        public static Game Create(CardDatabase db, IReadOnlyList<string> playerNames, ulong seed)
        {
            var config = db.Config;
            if (playerNames.Count < config.MinPlayers || playerNames.Count > config.MaxPlayers)
            {
                throw new ArgumentException(
                    $"Player count must be {config.MinPlayers}-{config.MaxPlayers}, got {playerNames.Count}.");
            }

            var state = new GameState();
            var rng = new DeterministicRng(seed);
            var game = new Game(db, state, rng);

            // Every step below draws from the one RNG: their order is part of the seed's meaning.
            game.SeatPlayers(playerNames);
            game.BuildLemonDeck();
            game.BuildBlackMarketDeck();
            game.BuildStandSupply();
            game.DealTurfs();
            game.DealFirstDibsRow();
            game.RefillMarket();
            game.ChooseFirstPlayer();
            game.DealStartingHands();
            game.DealLemonLordCandidates();

            state.RngState = rng.State;
            return game;
        }

        private void SeatPlayers(IReadOnlyList<string> playerNames)
        {
            for (int seat = 0; seat < playerNames.Count; seat++)
            {
                State.Players.Add(new PlayerState
                {
                    PlayerId = seat,
                    Name = playerNames[seat],
                    Money = Db.Config.StartingMoney,
                });
            }
        }

        private void BuildLemonDeck()
        {
            foreach (var def in Db.LemonCards)
            {
                for (int copy = 0; copy < def.Count; copy++)
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
                foreach (var shapeCount in standType.Shapes.OrderBy(pair => pair.Key))
                {
                    for (int copy = 0; copy < shapeCount.Value; copy++)
                    {
                        stack.Add(shapeCount.Key);
                    }
                }
                _rng.Shuffle(stack);
                State.StandSupply[standType.Id] = stack;
            }
        }

        private void DealTurfs()
        {
            var pourNumbers = Db.Turf.PowerPourNumbers.ToList();
            _rng.Shuffle(pourNumbers);
            for (int seat = 0; seat < State.Players.Count; seat++)
            {
                State.Players[seat].Turf.PowerPourNumber = pourNumbers[seat];
            }
        }

        private void DealFirstDibsRow()
        {
            var titleIds = Db.FirstDibsTitles.Select(t => t.Id).ToList();
            _rng.Shuffle(titleIds);
            int rowSize = State.Players.Count + 1;
            State.FirstDibsRow.AddRange(titleIds.Take(rowSize));
        }

        /// <summary>
        /// Random first player; those after them in turn order get less bonus money
        /// (rulebook p5: 1st gets +$1 per other player, descending to +$0 for the last).
        /// </summary>
        private void ChooseFirstPlayer()
        {
            State.FirstPlayer = _rng.Next(State.Players.Count);
            int playerCount = State.Players.Count;
            for (int offset = 0; offset < playerCount; offset++)
            {
                var player = State.Players[(State.FirstPlayer + offset) % playerCount];
                player.Money += playerCount - 1 - offset;
            }
        }

        private void DealStartingHands()
        {
            foreach (var player in State.Players)
            {
                for (int card = 0; card < Db.Config.StartingHandSize; card++)
                {
                    SetupDraw(player);
                }
            }
        }

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

        private void DealLemonLordCandidates()
        {
            var lordIds = Db.LemonLordTitles.Select(t => t.Id).ToList();
            _rng.Shuffle(lordIds);
            int nextLord = 0;
            foreach (var player in State.Players)
            {
                for (int dealt = 0; dealt < Db.Config.LemonLordDealt; dealt++)
                {
                    player.LemonLordDealt.Add(lordIds[nextLord++]);
                }
            }
        }

        // ------------------------------------------------ lemon lord choice

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

        // ----------------------------------------------------- initial buys

        private void BeginInitialBuys(List<GameEvent> events)
        {
            State.Stage = GameStage.InitialBuys;
            events.Add(new StageChanged { Stage = State.Stage });

            // Snake draft: clockwise from the first player, then counter-clockwise (rulebook p5).
            int playerCount = State.Players.Count;
            var clockwise = Enumerable.Range(0, playerCount)
                .Select(offset => (State.FirstPlayer + offset) % playerCount).ToList();
            State.InitialBuyQueue.AddRange(clockwise);
            State.InitialBuyQueue.AddRange(Enumerable.Reverse(clockwise));
            State.InitialBuyStandDone = false;
        }

        private int CurrentInitialBuyer =>
            State.InitialBuyQueue.Count > 0
                ? State.InitialBuyQueue[0]
                : throw new InvalidActionException("Initial buys are over.");

        private void RequireInitialBuyer(int playerId)
        {
            if (playerId != CurrentInitialBuyer)
            {
                throw new InvalidActionException($"It is P{CurrentInitialBuyer}'s initial buy, not P{playerId}'s.");
            }
        }

        private void ApplyInitialBuyStand(InitialBuyStand action, List<GameEvent> events)
        {
            RequireStage(GameStage.InitialBuys);
            RequireInitialBuyer(action.PlayerId);
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
            RequireInitialBuyer(action.PlayerId);
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
            State.MarketRefreshUsedThisTurn = false; // refresh is once per draft visit
            if (State.InitialBuyQueue.Count == 0)
            {
                State.Stage = GameStage.Playing;
                events.Add(new StageChanged { Stage = State.Stage });
                State.ActivePlayer = State.FirstPlayer;
                StartTurn(events);
                Pump(events);
            }
        }

        /// <summary>
        /// During the setup draft, money that may be spent on optional Black Market cards:
        /// enough must stay reserved to afford the mandatory Stand of any later visit.
        /// </summary>
        private int SetupSpendingRoom(PlayerState player)
        {
            bool hasLaterVisit = State.InitialBuyQueue.Skip(1).Contains(player.PlayerId);
            if (!hasLaterVisit)
            {
                return player.Money;
            }
            int cheapestBaseCost = Db.StandTypes
                .Where(t => State.StandSupply[t.Id].Count > 0)
                .Min(t => t.BaseCost);
            int reserve = cheapestBaseCost + player.Stands.Count * Db.Config.StandCostEscalationPerOwned;
            return player.Money - reserve;
        }
    }
}
