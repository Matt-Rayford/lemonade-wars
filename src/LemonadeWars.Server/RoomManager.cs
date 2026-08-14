using System.Collections.Concurrent;
using System.Security.Cryptography;
using LemonadeWars.Engine.Data;
using LemonadeWars.Protocol;

namespace LemonadeWars.Server;

public sealed class RoomManager
{
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no 0/O/1/I/L

    private readonly CardDatabase _db;
    private readonly string _dataDir;
    private readonly int _botDelayMs;
    private readonly ConcurrentDictionary<string, Room> _rooms = new();

    private readonly Action<string, string>? _turnNotifier;

    public RoomManager(CardDatabase db, string dataDir, int botDelayMs,
        Action<string, string>? turnNotifier = null)
    {
        _db = db;
        _dataDir = dataDir;
        _botDelayMs = botDelayMs;
        _turnNotifier = turnNotifier;
        Directory.CreateDirectory(dataDir);
        LoadPersistedRooms();
        _sweepTimer = new Timer(_ => Sweep(), null,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public int RoomCount => _rooms.Count;

    /// <summary>
    /// Retire dead rooms: expired empty lobbies, finished games past their grace, and
    /// abandoned running games past the stale window. Retirement is atomic with the
    /// room lock, so a player joining in the same instant gets a clean "expired" error
    /// instead of a seat in a ghost room. Returns how many rooms were removed.
    /// </summary>
    public int Sweep(TimeSpan? lobbyTtl = null, TimeSpan? finishedTtl = null,
        TimeSpan? staleTtl = null)
    {
        int removed = 0;
        foreach (var pair in _rooms)
        {
            if (!pair.Value.TryRetire(lobbyTtl ?? LobbyTtl, finishedTtl ?? FinishedRetention,
                    staleTtl ?? StaleRetention))
            {
                continue;
            }
            if (_rooms.TryRemove(pair.Key, out var room))
            {
                removed++;
                if (room.StorePath != null)
                {
                    TryDelete(room.StorePath); // no-op when the game never started
                }
            }
        }
        return removed;
    }

    /// <summary>Grace before deleting finished/corrupt logs — post-game score viewing.</summary>
    private static readonly TimeSpan FinishedRetention = TimeSpan.FromHours(12);
    /// <summary>Unfinished but abandoned games eventually stop counting or loading —
    /// the async play-by-turn window: make a move within 3 days or the game expires.</summary>
    private static readonly TimeSpan StaleRetention = TimeSpan.FromDays(3);
    /// <summary>Never-started lobbies live only in memory; empty ones expire fast.</summary>
    private static readonly TimeSpan LobbyTtl = TimeSpan.FromHours(1);

    // Long-lived servers sweep periodically, not just at boot. Held so it isn't collected.
    private readonly Timer _sweepTimer;

    /// <summary>
    /// Rebuild every unfinished game from its action log (e.g. after a redeploy), and
    /// sweep the graveyard: finished/corrupt logs past their grace period and games
    /// nobody has touched in a month are deleted instead of loaded.
    /// </summary>
    private void LoadPersistedRooms()
    {
        foreach (string path in Directory.GetFiles(_dataDir, "*.jsonl"))
        {
            if (Path.GetFileName(path) == "players.jsonl")
            {
                continue; // identity registry, not a room log
            }
            string code = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            var room = age <= StaleRetention
                ? Room.LoadFromFile(code, _db, path, _botDelayMs, _turnNotifier)
                : null;
            if (room != null)
            {
                _rooms.TryAdd(code, room);
                room.PumpBots(); // in case a bot was mid-turn when the server died
            }
            else if (age > FinishedRetention)
            {
                TryDelete(path);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A locked or vanished file is not worth failing boot over.
        }
    }

    public Room Create()
    {
        while (true)
        {
            var code = new string(Enumerable.Range(0, 5)
                .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
                .ToArray());
            var room = new Room(code, _db, Path.Combine(_dataDir, code + ".jsonl"),
                _botDelayMs, _turnNotifier);
            if (_rooms.TryAdd(code, room))
            {
                return room;
            }
        }
    }

    public Room? Find(string code) =>
        _rooms.TryGetValue((code ?? "").Trim().ToUpperInvariant(), out var room) ? room : null;

    /// <summary>My Games: needs-you first, then running, then lobbies, then finished.</summary>
    public List<GameSummary> GamesFor(string playerId)
    {
        return _rooms.Values
            .Select(room => (Room: room, Summary: room.SummaryFor(playerId)))
            .Where(pair => pair.Summary != null)
            .OrderByDescending(pair => pair.Summary!.YourTurn)
            .ThenBy(pair => pair.Summary!.Finished)
            .ThenByDescending(pair => pair.Room.LastActivityUtc)
            .Select(pair => pair.Summary!)
            .ToList();
    }

    /// <summary>Unfinished rooms this player occupies — the create-room spam guard.</summary>
    public int ActiveGameCount(string playerId) =>
        _rooms.Values.Count(room => room.IsActiveFor(playerId));
}
