using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using LemonadeWars.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// What the UI talks to. Local play wraps an in-process Game; remote play speaks the
    /// server protocol. Either way the UI renders from PlayerView + typed legal moves.
    /// </summary>
    public interface IGameSession : IDisposable
    {
        /// <summary>Latest view for our seat; null before the game starts.</summary>
        PlayerView View { get; }
        IReadOnlyList<GameAction> Moves { get; }
        IReadOnlyList<string> Log { get; }
        /// <summary>Bumped on every change; the UI re-renders when it moves.</summary>
        int Revision { get; }
        int Seat { get; }
        bool HumanAutoplay { get; set; }
        /// <summary>Typed engine events as they arrive, for presentation (dice, effects).</summary>
        event Action<GameEvent> EventEmitted;

        string LabelFor(GameAction move);
        void Submit(GameAction action);
        /// <summary>Advance bots / pump the socket. Call every frame.</summary>
        void Tick();
    }

    // ------------------------------------------------------------------ local

    /// <summary>Single-machine play: our Game, our bots, zero latency.</summary>
    public sealed class LocalGameSession : IGameSession
    {
        private const float BotStepSeconds = 0.35f;

        private readonly Game _game;
        private readonly Dictionary<int, IBot> _bots = new Dictionary<int, IBot>();
        private readonly GreedyBot _autopilot = new GreedyBot();
        private readonly List<string> _log = new List<string>();
        private float _nextBotStep;

        public PlayerView View { get; private set; }
        public IReadOnlyList<GameAction> Moves { get; private set; } = new List<GameAction>();
        public IReadOnlyList<string> Log => _log;
        public int Revision { get; private set; }
        public int Seat { get; }
        public bool HumanAutoplay { get; set; }
        public event Action<GameEvent> EventEmitted;

        public LocalGameSession(CardDatabase db, string[] names, int humanSeat, ulong seed)
        {
            Seat = humanSeat;
            _game = Game.Create(db, names, seed);
            for (int i = 0; i < names.Length; i++)
            {
                if (i != humanSeat)
                {
                    _bots[i] = new GreedyBot();
                }
            }
            AddLog($"New game — {names.Length} players, you are {names[humanSeat]}.");
            Refresh();
        }

        public string LabelFor(GameAction move) => MoveDescriber.Describe(_game, move);

        public void Submit(GameAction action)
        {
            action.PlayerId = Seat;
            foreach (var gameEvent in _game.Apply(action))
            {
                AddLog(gameEvent.ToString());
                EventEmitted?.Invoke(gameEvent);
            }
            Refresh();
        }

        public void Tick()
        {
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
                var bot = actor == Seat ? _autopilot : (GreedyBot)_bots[actor];
                foreach (var gameEvent in _game.Apply(bot.Choose(_game, actor)))
                {
                    AddLog(gameEvent.ToString());
                    EventEmitted?.Invoke(gameEvent);
                }
                Refresh();
                return;
            }
        }

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

    // ----------------------------------------------------------------- remote

    public sealed class RemoteRoomState
    {
        public string Code = "";
        public int YourSeat = -1;
        public string Token = "";
        public bool Started;
        public List<SeatInfo> Seats = new List<SeatInfo>();
    }

    /// <summary>Networked play against the Lemonade Wars server.</summary>
    public sealed class RemoteGameSession : IGameSession
    {
        private const float AutoplaySeconds = 0.4f;

        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly ConcurrentQueue<JObject> _inbox = new ConcurrentQueue<JObject>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly List<string> _log = new List<string>();
        private readonly Dictionary<GameAction, string> _labels = new Dictionary<GameAction, string>();
        private readonly JsonSerializer _serializer = JsonSerializer.Create(WireCodec.Settings);
        private float _nextAutoplay;

        public RemoteRoomState Room { get; } = new RemoteRoomState();
        public event Action RoomChanged;
        public string ConnectionError { get; private set; } = "";
        public bool Connected => _socket.State == WebSocketState.Open;

        /// <summary>Durable public id from the server's welcome; empty pre-hello.</summary>
        public string PlayerId { get; private set; } = "";
        /// <summary>My Games snapshot from the latest welcome/games message.</summary>
        public List<GameSummary> GamesList { get; private set; } = new List<GameSummary>();

        public PlayerView View { get; private set; }
        public IReadOnlyList<GameAction> Moves { get; private set; } = new List<GameAction>();
        public IReadOnlyList<string> Log => _log;
        public int Revision { get; private set; }
        public int Seat => Room.YourSeat;
        public bool HumanAutoplay { get; set; }
        public event Action<GameEvent> EventEmitted;

        public static RemoteGameSession Connect(string url)
        {
            var session = new RemoteGameSession();
            _ = session.RunAsync(url);
            return session;
        }

        private async Task RunAsync(string url)
        {
            try
            {
                await _socket.ConnectAsync(new Uri(url), _cts.Token);
                var buffer = new byte[64 * 1024];
                while (!_cts.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    using (var stream = new System.IO.MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _socket.ReceiveAsync(
                                new ArraySegment<byte>(buffer), _cts.Token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                return;
                            }
                            stream.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);
                        _inbox.Enqueue(JObject.Parse(Encoding.UTF8.GetString(stream.ToArray())));
                    }
                }
            }
            catch (Exception e)
            {
                ConnectionError = e.Message;
                _inbox.Enqueue(new JObject
                {
                    ["type"] = "error",
                    ["message"] = $"Connection lost: {e.Message}",
                });
            }
        }

        // ------------------------------------------------------- lobby verbs

        /// <summary>Identify first on every connection; the reply carries My Games.</summary>
        public void Hello(string playerKey, string name) =>
            Send(new { type = "hello", playerKey, name });
        public void ListGames() => Send(new { type = "list_games" });
        public void CreateRoom(string name) => Send(new { type = "create_room", name });
        public void JoinRoom(string code, string name, string token = null) =>
            Send(new { type = "join_room", code, name, token });
        public void AddBot() => Send(new { type = "add_bot" });
        public void RemoveBot(int seat) => Send(new { type = "remove_bot", seat });
        public void SetReady(bool ready) => Send(new { type = "ready", ready });
        public void StartGame() => Send(new { type = "start_game" });

        /// <summary>The viewer's own ready flag, from the latest room snapshot.</summary>
        public bool MyReady =>
            Room.Seats.FirstOrDefault(s => s.Seat == Room.YourSeat)?.Ready == true;

        // -------------------------------------------------------------- game

        public string LabelFor(GameAction move) =>
            _labels.TryGetValue(move, out var label) ? label : move.GetType().Name;

        public void Submit(GameAction action)
        {
            action.PlayerId = Seat;
            Send(new JObject
            {
                ["type"] = "action",
                ["action"] = WireCodec.EncodeAction(action),
            });
        }

        public void Tick()
        {
            while (_inbox.TryDequeue(out var message))
            {
                Handle(message);
            }
            if (HumanAutoplay && Moves.Count > 0 && UnityEngine.Time.time >= _nextAutoplay)
            {
                _nextAutoplay = UnityEngine.Time.time + AutoplaySeconds;
                Submit(Moves[UnityEngine.Random.Range(0, Moves.Count)]);
            }
        }

        private void Handle(JObject message)
        {
            switch ((string)message["type"])
            {
                case MessageType.Room:
                {
                    var room = message.ToObject<RoomMessage>(_serializer);
                    Room.Code = room.Code;
                    Room.YourSeat = room.YourSeat;
                    Room.Token = room.Token;
                    Room.Started = room.Started;
                    Room.Seats = room.Seats;
                    Revision++;
                    RoomChanged?.Invoke();
                    break;
                }
                case MessageType.Update:
                {
                    if (message["view"] is JObject viewJson)
                    {
                        View = viewJson.ToObject<PlayerView>(_serializer);
                    }
                    var moves = new List<GameAction>();
                    _labels.Clear();
                    if (message["moves"] is JArray movesJson)
                    {
                        foreach (var entry in movesJson.OfType<JObject>())
                        {
                            if (entry["action"] is JObject actionJson)
                            {
                                var action = WireCodec.DecodeAction(actionJson);
                                moves.Add(action);
                                _labels[action] = (string)entry["label"] ?? "";
                            }
                        }
                    }
                    Moves = moves;
                    if (message["events"] is JArray events)
                    {
                        foreach (var entry in events.OfType<JObject>())
                        {
                            AddLog((string)entry["label"] ?? "");
                            var decoded = WireCodec.DecodeEvent(entry);
                            if (decoded != null)
                            {
                                EventEmitted?.Invoke(decoded);
                            }
                        }
                    }
                    Revision++;
                    break;
                }
                case MessageType.Welcome:
                {
                    PlayerId = (string)message["playerId"] ?? "";
                    GamesList = ParseGames(message);
                    Revision++;
                    break;
                }
                case MessageType.Games:
                {
                    GamesList = ParseGames(message);
                    Revision++;
                    break;
                }
                case MessageType.Error:
                    AddLog("! " + (string)message["message"]);
                    Revision++;
                    break;
            }
        }

        private List<GameSummary> ParseGames(JObject message)
        {
            var games = new List<GameSummary>();
            if (message["gamesList"] is JArray array)
            {
                foreach (var entry in array.OfType<JObject>())
                {
                    var summary = entry.ToObject<GameSummary>(_serializer);
                    if (summary != null)
                    {
                        games.Add(summary);
                    }
                }
            }
            return games;
        }

        private void Send(object message)
        {
            byte[] payload = Encoding.UTF8.GetBytes(
                message is JObject json
                    ? json.ToString(Formatting.None)
                    : JsonConvert.SerializeObject(message, WireCodec.Settings));
            _ = SendPayloadAsync(payload);
        }

        private async Task SendPayloadAsync(byte[] payload)
        {
            await _sendLock.WaitAsync();
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    await _socket.SendAsync(new ArraySegment<byte>(payload),
                        WebSocketMessageType.Text, true, _cts.Token);
                }
            }
            catch (Exception e)
            {
                ConnectionError = e.Message;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void AddLog(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }
            _log.Add(line);
            if (_log.Count > 18)
            {
                _log.RemoveAt(0);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _socket.Dispose();
            }
            catch
            {
                // already gone
            }
        }
    }
}
