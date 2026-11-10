using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Unity
{
    /// <summary>Single-machine play: our Game, our bots, zero latency.</summary>
    public sealed class LocalGameSession : IGameSession
    {
        // Play-tester verdict: 0.35s reads as a blur — a human-ish beat between bot
        // actions lets the log/floaters land. Adjustable via the in-game speed chip.
        public float BotStepSeconds = 1.0f;

        private readonly Game _game;
        private readonly Dictionary<int, IBot> _bots = new Dictionary<int, IBot>();
        private readonly Dictionary<int, string> _botLevels = new Dictionary<int, string>();
        private readonly GreedyBot _autopilot = new GreedyBot();
        private readonly List<string> _log = new List<string>();
        private float _nextBotStep;
        private Task<GameAction> _pendingChoice;
        private float _pendingChoiceSince;

        public PlayerView View { get; private set; }
        public IReadOnlyList<GameAction> Moves { get; private set; } = new List<GameAction>();
        public IReadOnlyList<string> Log => _log;
        public int Revision { get; private set; }
        public int Seat { get; }
        public bool HumanAutoplay { get; set; }
        /// <summary>
        /// While true, bots do not act: the app points this at its presentation
        /// state (die tumbling, effects playing, turn banner up) so a bot never
        /// starts its turn over a sale roll the human is still watching.
        /// </summary>
        public Func<bool> HoldBots;
        public event Action<GameEvent> EventEmitted;

        public LocalGameSession(CardDatabase db, string[] names, int humanSeat, ulong seed,
            IReadOnlyList<string> botLevels = null)
        {
            Seat = humanSeat;
            _game = Game.Create(db, names, seed);
            int botIndex = 0;
            for (int i = 0; i < names.Length; i++)
            {
                if (i != humanSeat)
                {
                    string level = botLevels != null && botIndex < botLevels.Count
                        ? botLevels[botIndex]
                        : BotFactory.Medium;
                    level = BotFactory.Normalize(level);
                    _bots[i] = BotFactory.Create(level, seed * 77UL + (ulong)i);
                    _botLevels[i] = level;
                    botIndex++;
                }
            }
            SeedDebugHandCard(humanSeat, "rummage-sale");
            AddLog($"New game — {names.Length} players, you are {names[humanSeat]}.");
            Refresh();
        }

        /// <summary>
        /// TEMPORARY (testing a card's table flow): swap one copy of a card from the
        /// deck into the human's opening hand, trading out their first card so the
        /// hand size stays honest. Solo games only; delete the call when done.
        /// </summary>
        private void SeedDebugHandCard(int seat, string defId)
        {
            var state = _game.State;
            var hand = state.Players[seat].Hand;
            if (hand.Any(id => state.LemonInstances[id].DefId == defId))
            {
                return;
            }
            int deckIndex = state.LemonDeck.FindIndex(id => state.LemonInstances[id].DefId == defId);
            if (deckIndex < 0 || hand.Count == 0)
            {
                return;
            }
            int swappedOut = hand[0];
            hand[0] = state.LemonDeck[deckIndex];
            state.LemonDeck[deckIndex] = swappedOut;
        }

        public string BotLevelOf(int playerId) =>
            _botLevels.TryGetValue(playerId, out var level) ? level : null;

        public string LabelFor(GameAction move) => MoveDescriber.Describe(_game, move);

        public void Submit(GameAction action)
        {
            action.PlayerId = Seat;
            Emit(_game.Apply(action));
        }

        /// <summary>
        /// Events go out with the View already caught up to them — the same order the
        /// server's updates arrive in, so handlers can read the post-action state
        /// (an open window, the stack top) while they play the events.
        /// </summary>
        private void Emit(IEnumerable<GameEvent> events)
        {
            var burst = events.ToList();
            Refresh();
            foreach (var gameEvent in burst)
            {
                AddLog(gameEvent.ToString());
                EventEmitted?.Invoke(gameEvent);
            }
        }

        public void Tick()
        {
            if (HoldBots != null && HoldBots())
            {
                return;
            }
            // A search bot thinks off the main thread on a SNAPSHOT; its move applies
            // here once ready — no frame hitches, no shared-state races.
            if (_pendingChoice != null)
            {
                if (!_pendingChoice.IsCompleted)
                {
                    if (UnityEngine.Time.time - _pendingChoiceSince < 5f)
                    {
                        return;
                    }
                    // Watchdog: a think should take ~a quarter second. Abandon a
                    // wedged one and re-decide rather than freeze the table forever.
                    UnityEngine.Debug.LogWarning("Bot think timed out; re-deciding.");
                    _pendingChoice = null;
                    return;
                }
                var thought = _pendingChoice;
                _pendingChoice = null;
                try
                {
                    ApplyBotAction(thought.Result);
                }
                catch (System.Exception e)
                {
                    // Stale think (state moved on) or a faulted search: log it and
                    // let the next tick re-decide from the current state.
                    UnityEngine.Debug.LogException(e);
                }
                return;
            }

            if (_game.State.Stage == GameStage.Finished ||
                UnityEngine.Time.time < _nextBotStep)
            {
                return;
            }
            var acting = _game.ActingPlayers();
            // The table waits for YOU: while you are among the players being asked
            // (response window, discard, ...), bots hold their own responses. Without
            // this they answer within a bot-step and cards resolve mid-thought — the
            // pause never feels real even though the engine is blocked on you.
            if (!HumanAutoplay && acting.Contains(Seat))
            {
                return;
            }
            foreach (int actor in acting)
            {
                bool isBot = actor != Seat || HumanAutoplay;
                if (!isBot)
                {
                    continue;
                }
                _nextBotStep = UnityEngine.Time.time + BotStepSeconds;
                IBot bot = actor == Seat ? _autopilot : _bots[actor];
                if (bot is IHeavyBot)
                {
                    // The background thread must NEVER touch the live game: a human
                    // Apply (response window) racing a Dictionary read can spin the
                    // search into an infinite loop and freeze the whole table. Clone
                    // on the main thread; think on the copy. The chosen action still
                    // applies to the live game — if the state moved on meanwhile, the
                    // Apply above throws and the bot simply re-decides.
                    var snapshot = Game.FromState(_game.Db, SearchTools.CloneState(_game.State));
                    int seat = actor;
                    _pendingChoiceSince = UnityEngine.Time.time;
                    _pendingChoice = Task.Run(() => bot.Choose(snapshot, seat));
                    return;
                }
                ApplyBotAction(bot.Choose(_game, actor));
                return;
            }
        }

        private void ApplyBotAction(GameAction action) => Emit(_game.Apply(action));

        private void Refresh()
        {
            View = _game.ViewFor(Seat);
            Moves = HumanAutoplay || !_game.ActingPlayers().Contains(Seat)
                ? new List<GameAction>()
                : _game.LegalMovesFor(Seat);
            Revision++;
        }

        private void AddLog(string line)
        {
            _log.Add(line);
            if (_log.Count > 18)
            {
                _log.RemoveAt(0);
            }
        }

        public void Dispose()
        {
        }
    }
}
