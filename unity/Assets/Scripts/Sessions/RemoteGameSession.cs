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
using LemonadeWars.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Unity
{
    public sealed class RemoteGameSession : IGameSession
    {
        private const float AutoplaySeconds = 0.4f;
        private const int MaxLogLines = 18;

        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly ConcurrentQueue<JObject> _inbox = new ConcurrentQueue<JObject>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly List<string> _log = new List<string>();
        private readonly Dictionary<GameAction, string> _labels = new Dictionary<GameAction, string>();
        private readonly JsonSerializer _serializer = JsonSerializer.Create(WireCodec.Settings);
        private readonly ActionClockBoard _actionClocks = new ActionClockBoard();
        /// <summary>The server update the viewer last reported their table settled for.</summary>
        private int _settledReportVersion = -1;
        private float _nextAutoplay;

        public RemoteRoomState Room { get; } = new RemoteRoomState();
        public event Action RoomChanged;
        public string ConnectionError { get; private set; } = "";
        public bool Connected => _socket.State == WebSocketState.Open;

        /// <summary>Durable public id from the server's welcome; empty before hello.</summary>
        public string PlayerId { get; private set; } = "";
        public List<GameSummary> GamesList { get; private set; } = new List<GameSummary>();
        /// <summary>A game you're not watching needs you; the payload is its room code.</summary>
        public event Action<string> TurnAlert;

        public PlayerView View { get; private set; }
        public IReadOnlyList<GameAction> Moves { get; private set; } = new List<GameAction>();
        public IReadOnlyList<string> Log => _log;
        public int Revision { get; private set; }
        public int Seat => Room.YourSeat;
        public bool HumanAutoplay { get; set; }
        public event Action<GameEvent> EventEmitted;

        public string BotLevelOf(int playerId)
        {
            var seat = Room.Seats.FirstOrDefault(s => s.Seat == playerId);
            return seat != null && seat.IsBot ? BotFactory.Normalize(seat.BotLevel) : null;
        }

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
        public void SetBotLevel(int seat, string level) =>
            Send(new { type = "set_bot_level", seat, level });
        public void SetSpeed(string speed) => Send(new { type = "set_speed", speed });
        public void SetActionTimer(int seconds) => Send(new { type = "set_action_timer", seconds });
        public void SetReady(bool ready) => Send(new { type = "ready", ready });
        public void StartGame() => Send(new { type = "start_game" });

        /// <summary>Tells the server to free the seat (a dropped socket keeps it for a token rejoin).
        /// Closes only once that message is on the wire: Dispose cancels in-flight sends.</summary>
        public void Leave()
        {
            byte[] payload = Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(new { type = "leave_room" }, WireCodec.Settings));
            _ = SendPayloadAsync(payload).ContinueWith(_ => Dispose());
        }

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

        // ------------------------------------------------------ action timer

        /// <summary>Null unless that seat's countdown is running.</summary>
        public float? ActionSecondsLeft(int seat) =>
            _actionClocks.SecondsLeft(seat, UnityEngine.Time.realtimeSinceStartup);

        /// <summary>The server holds the viewer's countdown until told their table has finished playing out this update.</summary>
        public bool OwesSettledReport =>
            _actionClocks.IsHeldForSettling(Seat) && _settledReportVersion != _actionClocks.StateVersion;

        public void ReportTableSettled()
        {
            _settledReportVersion = _actionClocks.StateVersion;
            Send(new { type = "table_settled", stateVersion = _actionClocks.StateVersion });
        }

        private void HearActionClocks(JObject message)
        {
            var clocks = message["clocks"]?.ToObject<List<ActionClockInfo>>(_serializer)
                ?? new List<ActionClockInfo>();
            _actionClocks.Hear((int?)message["stateVersion"] ?? 0, clocks,
                UnityEngine.Time.realtimeSinceStartup);
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
                    HandleRoom(message);
                    break;
                case MessageType.Update:
                    HandleUpdate(message);
                    break;
                case MessageType.Welcome:
                    PlayerId = (string)message["playerId"] ?? "";
                    GamesList = ParseGames(message);
                    Revision++;
                    break;
                case MessageType.Games:
                    GamesList = ParseGames(message);
                    Revision++;
                    break;
                case MessageType.TurnAlert:
                    HandleTurnAlert(message);
                    break;
                case MessageType.ActionClocks:
                    HearActionClocks(message);
                    Revision++; // the player bars wear the countdowns
                    break;
                case MessageType.Error:
                    AddLog("! " + (string)message["message"]);
                    Revision++;
                    break;
            }
        }

        private void HandleRoom(JObject message)
        {
            var room = message.ToObject<RoomMessage>(_serializer);
            if (Room.Code != room.Code)
            {
                // Each room counts its own updates: the last room's clocks would outrank this one's.
                _actionClocks.Clear();
                _settledReportVersion = -1;
            }
            Room.Code = room.Code;
            Room.YourSeat = room.YourSeat;
            Room.Token = room.Token;
            Room.Started = room.Started;
            Room.Speed = room.Speed;
            Room.ActionTimerSeconds = room.ActionTimerSeconds;
            Room.Seats = room.Seats;
            Revision++;
            RoomChanged?.Invoke();
        }

        /// <summary>The view and moves are in place before the events go out, so their handlers
        /// read the post-action state.</summary>
        private void HandleUpdate(JObject message)
        {
            if (message["view"] is JObject viewJson)
            {
                View = viewJson.ToObject<PlayerView>(_serializer);
            }
            Moves = DecodeLabelledMoves(message);
            HearActionClocks(message);
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
        }

        private List<GameAction> DecodeLabelledMoves(JObject message)
        {
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
            return moves;
        }

        private void HandleTurnAlert(JObject message)
        {
            string code = (string)message["code"] ?? "";
            if (code.Length > 0)
            {
                TurnAlert?.Invoke(code);
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
            if (_log.Count > MaxLogLines)
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
