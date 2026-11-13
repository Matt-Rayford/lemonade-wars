using System.Collections.Concurrent;
using System.Security.Cryptography;
using LemonadeWars.Engine.Data;
using LemonadeWars.Protocol;

namespace LemonadeWars.Server;

public sealed class RoomManager
{
    /// <summary>No 0/O/1/I/L: room codes get read aloud and typed by hand.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 5;

    /// <summary>Finished and corrupt logs are kept this long, for post-game score viewing.</summary>
    private static readonly TimeSpan FinishedRetention = TimeSpan.FromHours(12);
    /// <summary>The async play-by-turn window: make a move within 3 days or the game expires.</summary>
    private static readonly TimeSpan StaleRetention = TimeSpan.FromDays(3);
    /// <summary>Never-started lobbies live only in memory; empty ones expire fast.</summary>
    private static readonly TimeSpan LobbyTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    private readonly CardDatabase _db;
    private readonly string _dataDir;
    private readonly int _botDelayMs;
    private readonly int _msPerTimerSecond;
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly Action<string, string>? _turnNotifier;
    /// <summary>Held in a field so the timer is not garbage-collected.</summary>
    private readonly Timer _sweepTimer;

    public RoomManager(CardDatabase db, string dataDir, int botDelayMs,
        Action<string, string>? turnNotifier = null, int msPerTimerSecond = 1000)
    {
        _db = db;
        _dataDir = dataDir;
        _botDelayMs = botDelayMs;
        _msPerTimerSecond = msPerTimerSecond;
        _turnNotifier = turnNotifier;
        Directory.CreateDirectory(dataDir);
        LoadPersistedRooms();
        _sweepTimer = new Timer(_ => Sweep(), null, SweepInterval, SweepInterval);
    }

    public int RoomCount => _rooms.Count;

    public Room Create()
    {
        while (true)
        {
            var code = new string(Enumerable.Range(0, CodeLength)
                .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
                .ToArray());
            var room = new Room(code, _db, Path.Combine(_dataDir, code + ".jsonl"),
                _botDelayMs, _turnNotifier, _msPerTimerSecond);
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

    public int ActiveGameCount(string playerId) =>
        _rooms.Values.Count(room => room.IsActiveFor(playerId));

    /// <summary>Removes every room that agrees to retire, with its log; returns how many.</summary>
    public int Sweep(TimeSpan? lobbyTtl = null, TimeSpan? finishedTtl = null,
        TimeSpan? staleTtl = null)
    {
        int removed = 0;
        foreach (var (code, candidate) in _rooms)
        {
            if (!candidate.TryRetire(lobbyTtl ?? LobbyTtl, finishedTtl ?? FinishedRetention,
                    staleTtl ?? StaleRetention))
            {
                continue;
            }
            if (_rooms.TryRemove(code, out var room))
            {
                removed++;
                // A room that never started has a path but no file; deleting it is a no-op.
                if (room.StorePath != null)
                {
                    TryDelete(room.StorePath);
                }
            }
        }
        return removed;
    }

    /// <summary>
    /// Rebuilds every unfinished game from its action log (after a redeploy, say). Logs that
    /// do not load — finished, corrupt or stale — are deleted once past the finished grace.
    /// </summary>
    private void LoadPersistedRooms()
    {
        foreach (string path in Directory.GetFiles(_dataDir, "*.jsonl"))
        {
            // The identity registry shares the folder and the extension.
            if (Path.GetFileName(path) == "players.jsonl")
            {
                continue;
            }
            string code = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            var room = age <= StaleRetention
                ? Room.LoadFromFile(code, _db, path, _botDelayMs, _turnNotifier, _msPerTimerSecond)
                : null;
            if (room != null)
            {
                _rooms.TryAdd(code, room);
                room.PumpBots();
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
            // A locked or vanished file is not worth failing over.
        }
    }
}
