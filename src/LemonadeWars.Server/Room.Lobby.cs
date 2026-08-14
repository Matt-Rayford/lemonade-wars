using System.Security.Cryptography;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Server;

// Seating, readiness, bot roster, and game start.
public sealed partial class Room
{
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
            // Reconnect takes the seat back over: a token always (explicit credential);
            // identity only when the seat is unheld or the game is running — two live
            // clients sharing one identity in a lobby (same machine, twin instances)
            // should get two seats, not fight over one.
            var existing = _seats.FirstOrDefault(s =>
                (!string.IsNullOrEmpty(token) && s.Token == token) ||
                (playerId != null && s.PlayerId == playerId &&
                 (_game != null || !s.Connected)));
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
            _ = BroadcastGameAsync(Array.Empty<GameEvent>()); // rejoin: fresh view + moves
        }
        return seat;
    }

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
            string[] botNames = { "Benny", "Cleo", "Dex", "Squeezy" };
            _seats.Add(new Seat
            {
                Index = _seats.Count,
                Name = botNames[_seats.Count % botNames.Length] + " (bot)",
                IsBot = true,
                Ready = true,
                BotLevel = BotFactory.Normalize(level),
            });
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
        return "";
    }

    /// <summary>Room-wide bot pacing: "slow" / "medium" / "fast".</summary>
    public string BotSpeed { get; private set; } = "medium";
    private int _botDelayOverrideMs = -1;

    /// <summary>
    /// Any seated human may change the pace, before or during the game — the player
    /// drowning in bot actions is rarely the host. Last write wins; the broadcast
    /// keeps every client's speed chip in sync.
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
                    _botDelayOverrideMs = 2200;
                    break;
                case "fast":
                    BotSpeed = "fast";
                    _botDelayOverrideMs = 500;
                    break;
                case "medium":
                    BotSpeed = "medium";
                    _botDelayOverrideMs = -1; // server default (BOT_DELAY_MS)
                    break;
                default:
                    return "Speed must be slow, medium, or fast.";
            }
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
        return "";
    }

    /// <summary>Host retunes a bot seat's difficulty in the lobby.</summary>
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

    /// <summary>
    /// Remove a specific bot seat (host only, before the game starts). Remaining seats
    /// are re-indexed so seat numbers stay contiguous — required because seat index
    /// becomes PlayerId at start.
    /// </summary>
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
            _seats.Remove(seat);
            for (int i = 0; i < _seats.Count; i++)
            {
                _seats[i].Index = i;
            }
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
                _bots[seat.Index] = BotFactory.Create(seat.BotLevel,
                    BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));
            }
            PersistHeader(seed);
        }

        BroadcastRoom(SeatsSnapshot());
        _ = BroadcastThenPumpAsync(new List<GameEvent>());
        return "";
    }

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
}
