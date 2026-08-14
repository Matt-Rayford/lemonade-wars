using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using LemonadeWars.Protocol;

namespace LemonadeWars.Server;

/// <summary>
/// One table: seats, an optional running Game, and serialized action processing.
/// All game/seat mutations happen under <see cref="_sync"/>; sends happen outside it.
/// Subsystems: <c>Room.Lobby</c> (seating and start), <c>Room.Play</c> (actions, bot
/// pump, broadcasts), <c>Room.Persistence</c> (action log), <c>Room.Lifecycle</c>
/// (summaries and retirement).
/// </summary>
public sealed partial class Room
{
    public string Code { get; }

    private readonly CardDatabase _db;
    private readonly object _sync = new();
    private readonly List<Seat> _seats = new();
    private readonly Dictionary<int, IBot> _bots = new();
    private readonly string? _storePath;
    private readonly int _botDelayMs;
    /// <summary>Cross-game turn alerts: (playerId, roomCode) when a seat needs input.</summary>
    private readonly Action<string, string>? _turnNotifier;
    private bool _retired;
    private Game? _game;

    /// <summary>Last join/action, for stale-room GC and My Games ordering.</summary>
    public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>Room log file, so the sweeper can delete it after retirement.</summary>
    public string? StorePath => _storePath;

    public Room(string code, CardDatabase db, string? storePath, int botDelayMs,
        Action<string, string>? turnNotifier = null)
    {
        Code = code;
        _db = db;
        _storePath = storePath;
        _botDelayMs = botDelayMs;
        _turnNotifier = turnNotifier;
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

    public int SeatIndexOf(Connection connection)
    {
        lock (_sync)
        {
            return _seats.FirstOrDefault(s => s.Client == connection)?.Index ?? -1;
        }
    }

    private List<Seat> SeatsSnapshot()
    {
        lock (_sync)
        {
            return _seats.ToList();
        }
    }

    private static string SanitizeName(string name)
    {
        name = (name ?? "").Trim();
        return name.Length == 0 ? "Player" : name[..Math.Min(20, name.Length)];
    }
}
