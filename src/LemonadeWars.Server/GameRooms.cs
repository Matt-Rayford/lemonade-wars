using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using LemonadeWars.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Server;

public sealed class Seat
{
    public int Index { get; init; }
    public string Name { get; set; } = "";
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    public bool IsBot { get; set; }
    /// <summary>Bots are always ready; humans toggle in the lobby.</summary>
    public bool Ready { get; set; }
    public WebSocket? Socket { get; set; }
    public SemaphoreSlim SendLock { get; } = new(1, 1);

    public bool Connected => Socket is { State: WebSocketState.Open };

    public async Task SendAsync(object message)
    {
        var socket = Socket;
        if (socket is not { State: WebSocketState.Open })
        {
            return;
        }
        byte[] payload = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(message, WireCodec.Settings));
        await SendLock.WaitAsync();
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // Connection died mid-send; the receive loop will detach the seat.
        }
        finally
        {
            SendLock.Release();
        }
    }
}

/// <summary>
/// One table: seats, an optional running Game, and serialized action processing.
/// All game/seat mutations happen under <see cref="_sync"/>; sends happen outside it.
/// </summary>
public sealed class Room
{
    private const int MaxBotSteps = 20000;

    public string Code { get; }

    private readonly CardDatabase _db;
    private readonly object _sync = new();
    private readonly List<Seat> _seats = new();
    private readonly Dictionary<int, GreedyBot> _bots = new();
    private Game? _game;

    public Room(string code, CardDatabase db)
    {
        Code = code;
        _db = db;
    }

    public bool Started
    {
        get
        {
            lock (_sync)
            {
                return _game != null;
            }
        }
    }

    // ------------------------------------------------------------- lobby

    public Seat? Join(string name, string? token, WebSocket socket, out string error)
    {
        List<Seat> toNotify;
        Seat seat;
        lock (_sync)
        {
            // Reconnect by token takes the seat back over, even mid-game.
            var existing = _seats.FirstOrDefault(s => !string.IsNullOrEmpty(token) && s.Token == token);
            if (existing != null)
            {
                existing.Socket = socket;
                seat = existing;
            }
            else
            {
                if (_game != null)
                {
                    error = "That game already started.";
                    return null;
                }
                if (_seats.Count >= _db.Config.MaxPlayers)
                {
                    error = "That room is full.";
                    return null;
                }
                seat = new Seat { Index = _seats.Count, Name = SanitizeName(name) };
                seat.Socket = socket;
                _seats.Add(seat);
            }
            toNotify = _seats.ToList();
        }

        error = "";
        BroadcastRoom(toNotify);
        if (Started)
        {
            _ = BroadcastGameAsync(Array.Empty<GameEvent>()); // rejoin: fresh view + moves
        }
        return seat;
    }

    public string AddBot(int requestingSeat)
    {
        List<Seat> toNotify;
        lock (_sync)
        {
            if (requestingSeat != 0)
            {
                return "Only the host can add bots.";
            }
            if (_game != null)
            {
                return "The game already started.";
            }
            if (_seats.Count >= _db.Config.MaxPlayers)
            {
                return "The room is full.";
            }
            string[] botNames = { "Benny", "Cleo", "Dex", "Squeezy" };
            _seats.Add(new Seat
            {
                Index = _seats.Count,
                Name = botNames[_seats.Count % botNames.Length] + " (bot)",
                IsBot = true,
                Ready = true,
            });
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
        return "";
    }

    /// <summary>Toggle a human seat's ready flag in the lobby.</summary>
    public void SetReady(int seatIndex, bool ready)
    {
        List<Seat> toNotify;
        lock (_sync)
        {
            var seat = _seats.FirstOrDefault(s => s.Index == seatIndex && !s.IsBot);
            if (seat == null || _game != null)
            {
                return;
            }
            seat.Ready = ready;
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
    }

    public string Start(int requestingSeat)
    {
        lock (_sync)
        {
            if (requestingSeat != 0)
            {
                return "Only the host can start the game.";
            }
            if (_game != null)
            {
                return "The game already started.";
            }
            if (_seats.Count < _db.Config.MinPlayers)
            {
                return $"Need at least {_db.Config.MinPlayers} players.";
            }
            // Everyone except the host (whose Start click implies readiness) must be ready.
            var notReady = _seats.Where(s => !s.Ready && s.Index != requestingSeat).ToList();
            if (notReady.Count > 0)
            {
                return "Waiting for: " + string.Join(", ", notReady.Select(s => s.Name));
            }

            ulong seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
            _game = Game.Create(_db, _seats.Select(s => s.Name).ToArray(), seed);
            foreach (var seat in _seats.Where(s => s.IsBot))
            {
                _bots[seat.Index] = new GreedyBot();
            }
        }

        BroadcastRoom(SeatsSnapshot());
        _ = RunBotsAndBroadcastAsync(new List<GameEvent>());
        return "";
    }

    public void Detach(WebSocket socket)
    {
        List<Seat> toNotify;
        lock (_sync)
        {
            var seat = _seats.FirstOrDefault(s => s.Socket == socket);
            if (seat == null)
            {
                return;
            }
            seat.Socket = null;
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
    }

    // ------------------------------------------------------------ actions

    public async Task HandleActionAsync(int seatIndex, JObject actionJson)
    {
        GameAction action;
        try
        {
            action = WireCodec.DecodeAction(actionJson);
        }
        catch (JsonSerializationException e)
        {
            await SendErrorAsync(seatIndex, $"Bad action: {e.Message}");
            return;
        }
        // Seats can only ever act as themselves.
        action.PlayerId = seatIndex;

        var events = new List<GameEvent>();
        lock (_sync)
        {
            if (_game == null)
            {
                _ = SendErrorAsync(seatIndex, "The game has not started.");
                return;
            }
            try
            {
                events.AddRange(_game.Apply(action));
            }
            catch (InvalidActionException e)
            {
                _ = SendErrorAsync(seatIndex, e.Message);
                return;
            }
        }

        await RunBotsAndBroadcastAsync(events);
    }

    /// <summary>Let server-side bots take their turns, then push updates to every human.</summary>
    private async Task RunBotsAndBroadcastAsync(List<GameEvent> events)
    {
        lock (_sync)
        {
            if (_game != null)
            {
                int steps = 0;
                while (_game.State.Stage != GameStage.Finished && steps++ < MaxBotSteps)
                {
                    int actor = _game.ActingPlayers().FirstOrDefault(a => _bots.ContainsKey(a), -1);
                    if (actor < 0)
                    {
                        break;
                    }
                    events.AddRange(_game.Apply(_bots[actor].Choose(_game, actor)));
                }
            }
        }
        await BroadcastGameAsync(events);
    }

    // ---------------------------------------------------------- broadcast

    private async Task BroadcastGameAsync(IReadOnlyList<GameEvent> events)
    {
        List<(Seat Seat, UpdateMessage Message)> outbox = new();
        lock (_sync)
        {
            if (_game == null)
            {
                return;
            }
            var acting = _game.ActingPlayers();
            foreach (var seat in _seats.Where(s => !s.IsBot))
            {
                var update = new UpdateMessage
                {
                    Events = events.Select(e => WireCodec.EncodeEventFor(e, seat.Index)).ToList(),
                    View = JObject.FromObject(_game.ViewFor(seat.Index),
                        JsonSerializer.Create(WireCodec.Settings)),
                };
                if (acting.Contains(seat.Index))
                {
                    update.Moves = _game.LegalMovesFor(seat.Index)
                        .Select(m => new MoveOption
                        {
                            Label = MoveDescriber.Describe(_game, m),
                            Action = WireCodec.EncodeAction(m),
                        })
                        .ToList();
                }
                outbox.Add((seat, update));
            }
        }
        await Task.WhenAll(outbox.Select(o => o.Seat.SendAsync(o.Message)));
    }

    private void BroadcastRoom(List<Seat> seats)
    {
        bool started = Started;
        foreach (var seat in seats.Where(s => !s.IsBot))
        {
            var message = new RoomMessage
            {
                Code = Code,
                YourSeat = seat.Index,
                Token = seat.Token,
                Started = started,
                Seats = seats.Select(s => new SeatInfo
                {
                    Seat = s.Index,
                    Name = s.Name,
                    IsBot = s.IsBot,
                    Connected = s.IsBot || s.Connected,
                    Ready = s.Ready,
                }).ToList(),
            };
            _ = seat.SendAsync(message);
        }
    }

    private Task SendErrorAsync(int seatIndex, string message)
    {
        Seat? seat;
        lock (_sync)
        {
            seat = _seats.FirstOrDefault(s => s.Index == seatIndex);
        }
        return seat?.SendAsync(new ErrorMessage { Message = message }) ?? Task.CompletedTask;
    }

    private List<Seat> SeatsSnapshot()
    {
        lock (_sync)
        {
            return _seats.ToList();
        }
    }

    public int SeatIndexOf(WebSocket socket)
    {
        lock (_sync)
        {
            return _seats.FirstOrDefault(s => s.Socket == socket)?.Index ?? -1;
        }
    }

    private static string SanitizeName(string name)
    {
        name = (name ?? "").Trim();
        return name.Length == 0 ? "Player" : name[..Math.Min(20, name.Length)];
    }
}

public sealed class RoomManager
{
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no 0/O/1/I/L

    private readonly CardDatabase _db;
    private readonly ConcurrentDictionary<string, Room> _rooms = new();

    public RoomManager(CardDatabase db)
    {
        _db = db;
    }

    public Room Create()
    {
        while (true)
        {
            var code = new string(Enumerable.Range(0, 5)
                .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
                .ToArray());
            var room = new Room(code, _db);
            if (_rooms.TryAdd(code, room))
            {
                return room;
            }
        }
    }

    public Room? Find(string code) =>
        _rooms.TryGetValue((code ?? "").Trim().ToUpperInvariant(), out var room) ? room : null;
}
