using System.Security.Cryptography;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Protocol;

namespace LemonadeWars.Server;

public sealed partial class Room
{
    private const int SlowBotDelayMs = 2200;
    private const int FastBotDelayMs = 500;
    private const int NoBotDelayOverride = -1;

    private static readonly string[] BotNames = { "Benny", "Cleo", "Dex", "Squeezy" };

    /// <summary>Room-wide bot pacing: "slow" / "medium" / "fast".</summary>
    public string BotSpeed { get; private set; } = "medium";
    private int _botDelayOverrideMs = NoBotDelayOverride;

    /// <summary>A <see cref="FirstDibsModes"/> value: dealt at random, or hand-picked by the host as they start the game.</summary>
    public string FirstDibsMode { get; private set; } = FirstDibsModes.Random;

    public Seat? Join(string name, string? token, string? playerId, Connection connection,
        out string error)
    {
        List<Seat> toNotify;
        Seat seat;
        lock (_sync)
        {
            if (_retired)
            {
                error = "That room has expired.";
                return null;
            }
            var existing = FindReclaimableSeat(token, playerId);
            if (existing != null)
            {
                existing.Client = connection;
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
                seat = new Seat
                {
                    Index = _seats.Count,
                    Name = SanitizeName(name),
                    PlayerId = playerId,
                    Client = connection,
                };
                _seats.Add(seat);
            }
            LastActivityUtc = DateTime.UtcNow;
            toNotify = _seats.ToList();
        }

        error = "";
        BroadcastRoom(toNotify);
        if (Started)
        {
            // A rejoining player needs a fresh view and move list.
            _ = BroadcastGameAsync(Array.Empty<GameEvent>());
        }
        return seat;
    }

    /// <summary>
    /// A token always reclaims its seat. An identity reclaims one only if the seat is unheld
    /// or the game is running: two live clients sharing an identity in a lobby get two seats.
    /// </summary>
    private Seat? FindReclaimableSeat(string? token, string? playerId) =>
        _seats.FirstOrDefault(s =>
            (!string.IsNullOrEmpty(token) && s.Token == token) ||
            (playerId != null && s.PlayerId == playerId &&
             (_game != null || !s.Connected)));

    public string AddBot(int requestingSeat, string? level = null)
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
            _seats.Add(new Seat
            {
                Index = _seats.Count,
                Name = BotNames[_seats.Count % BotNames.Length],
                IsBot = true,
                Ready = true,
                BotLevel = BotFactory.Normalize(level),
            });
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
        return "";
    }

    /// <summary>
    /// Any seated human may change the pace, before or during the game: the player
    /// drowning in bot actions is rarely the host. Last write wins.
    /// </summary>
    public string SetSpeed(int requestingSeat, string? speed)
    {
        List<Seat> toNotify;
        lock (_sync)
        {
            if (requestingSeat < 0)
            {
                return "You are not seated in this room.";
            }
            switch ((speed ?? "").Trim().ToLowerInvariant())
            {
                case "slow":
                    BotSpeed = "slow";
                    _botDelayOverrideMs = SlowBotDelayMs;
                    break;
                case "fast":
                    BotSpeed = "fast";
                    _botDelayOverrideMs = FastBotDelayMs;
                    break;
                case "medium":
                    BotSpeed = "medium";
                    _botDelayOverrideMs = NoBotDelayOverride;
                    break;
                default:
                    return "Speed must be slow, medium, or fast.";
            }
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
        return "";
    }

    public string SetFirstDibsMode(int requestingSeat, string? mode)
    {
        List<Seat> toNotify;
        lock (_sync)
        {
            if (requestingSeat != 0)
            {
                return "Only the host can change how First Dibs are chosen.";
            }
            if (_game != null)
            {
                return "The game already started.";
            }
            if (mode != FirstDibsModes.Random && mode != FirstDibsModes.Manual)
            {
                return "First Dibs must be random or manual.";
            }
            FirstDibsMode = mode;
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
        return "";
    }

    public string SetBotLevel(int requestingSeat, int botSeat, string? level)
    {
        List<Seat> toNotify;
        lock (_sync)
        {
            if (requestingSeat != 0)
            {
                return "Only the host can change bot difficulty.";
            }
            if (_game != null)
            {
                return "The game already started.";
            }
            var seat = _seats.FirstOrDefault(s => s.Index == botSeat);
            if (seat == null || !seat.IsBot)
            {
                return "That seat is not a bot.";
            }
            seat.BotLevel = BotFactory.Normalize(level);
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
        return "";
    }

    public string RemoveBot(int requestingSeat, int botSeat)
    {
        List<Seat> toNotify;
        lock (_sync)
        {
            if (requestingSeat != 0)
            {
                return "Only the host can remove bots.";
            }
            if (_game != null)
            {
                return "The game already started.";
            }
            var seat = _seats.FirstOrDefault(s => s.Index == botSeat);
            if (seat == null || !seat.IsBot)
            {
                return "That seat is not a bot.";
            }
            RemoveSeat(seat);
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
        return "";
    }

    /// <summary>
    /// Lobby only. Seat index is turn order, hosthood (seat 0) and, at start, PlayerId, so
    /// the remaining seats renumber to stay contiguous; a departed host hands on to the next seat.
    /// </summary>
    private void RemoveSeat(Seat seat)
    {
        _seats.Remove(seat);
        for (int i = 0; i < _seats.Count; i++)
        {
            _seats[i].Index = i;
        }
    }

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

    /// <param name="handPickedFirstDibs">The host's First Dibs row; only read, and then required, in manual mode.</param>
    public string Start(int requestingSeat, IReadOnlyList<string>? handPickedFirstDibs = null)
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
            // The host's Start click implies their own readiness.
            bool isEveryoneElseReady = _seats.All(s => s.Ready || s.Index == requestingSeat);
            if (!isEveryoneElseReady)
            {
                return "Not All Players Are Ready";
            }

            bool isHandPicked = FirstDibsMode == FirstDibsModes.Manual;
            if (isHandPicked && handPickedFirstDibs == null)
            {
                return "Choose the First Dibs cards first.";
            }
            ulong seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
            try
            {
                _game = Game.Create(_db, _seats.Select(s => s.Name).ToArray(), seed,
                    isHandPicked ? handPickedFirstDibs : null);
            }
            catch (ArgumentException e)
            {
                return e.Message; // the picked row does not fit this table
            }
            foreach (var seat in _seats.Where(s => s.IsBot))
            {
                _bots[seat.Index] = BotFactory.Create(seat.BotLevel,
                    BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));
            }
            PersistHeader(seed);
        }

        BroadcastRoom(SeatsSnapshot());
        _ = BroadcastThenPumpAsync(new List<GameEvent>());
        return "";
    }

    /// <summary>A dropped socket is not a departure: the seat stays, offline, for the player to reclaim.</summary>
    public void Detach(Connection connection)
    {
        List<Seat> toNotify;
        lock (_sync)
        {
            var seat = _seats.FirstOrDefault(s => s.Client == connection);
            if (seat == null)
            {
                return;
            }
            seat.Client = null;
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
    }

    /// <summary>A deliberate exit frees a lobby seat outright; mid-game the seat only goes offline, like a drop.</summary>
    public void Leave(Connection connection)
    {
        List<Seat> toNotify;
        lock (_sync)
        {
            var seat = _seats.FirstOrDefault(s => s.Client == connection);
            if (seat == null)
            {
                return;
            }
            if (_game == null)
            {
                RemoveSeat(seat);
            }
            else
            {
                seat.Client = null;
            }
            LastActivityUtc = DateTime.UtcNow;
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
    }
}
