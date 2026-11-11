using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Server;

/// <summary>
/// Durable player identity. Clients present a random secret key in the hello message;
/// the server stores only its SHA-256 and hands back a short public player id. The store
/// is append-only jsonl where the last line per key wins, so a rename is a one-line append.
/// Steam auth would slot in here by deriving the key from the SteamID ticket.
/// </summary>
public sealed partial class PlayerRegistry
{
    /// <summary>Anything shorter is too guessable to act as a bearer secret.</summary>
    public const int MinKeyLength = 16;
    private const int MaxNameLength = 20;

    private readonly object _sync = new();
    private readonly string? _storePath;
    private readonly Dictionary<string, Player> _byKeyHash = new();

    public PlayerRegistry(string? dataDir)
    {
        if (dataDir == null)
        {
            return;
        }
        Directory.CreateDirectory(dataDir);
        _storePath = Path.Combine(dataDir, "players.jsonl");
        if (File.Exists(_storePath))
        {
            LoadStore(_storePath);
        }
    }

    /// <summary>
    /// Find-or-create the player behind this secret key; a changed non-empty name is
    /// persisted as a rename. Returns null for keys too short to be secrets.
    /// </summary>
    public Player? Identify(string? playerKey, string? name)
    {
        if (playerKey == null || playerKey.Length < MinKeyLength)
        {
            return null;
        }
        string cleanedName = Sanitize(name);
        string keyHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(playerKey)));
        lock (_sync)
        {
            if (_byKeyHash.TryGetValue(keyHash, out var existing))
            {
                if (cleanedName.Length == 0 || cleanedName == existing.Name)
                {
                    return existing;
                }
                var renamed = existing with { Name = cleanedName };
                _byKeyHash[keyHash] = renamed;
                Persist(keyHash, renamed);
                return renamed;
            }
            var player = new Player(
                "p" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant(),
                cleanedName.Length == 0 ? "Player" : cleanedName);
            _byKeyHash[keyHash] = player;
            Persist(keyHash, player);
            return player;
        }
    }

    private void LoadStore(string storePath)
    {
        foreach (string line in File.ReadAllLines(storePath))
        {
            if (line.Length == 0)
            {
                continue;
            }
            try
            {
                var record = JObject.Parse(line);
                _byKeyHash[(string)record["keyHash"]!] = new Player(
                    (string)record["playerId"]!, (string)record["name"]!);
            }
            catch (Exception)
            {
                // Skip a corrupt line rather than refuse to boot.
            }
        }
    }

    private void Persist(string keyHash, Player player)
    {
        if (_storePath == null)
        {
            return;
        }
        var record = new JObject
        {
            ["keyHash"] = keyHash,
            ["playerId"] = player.PlayerId,
            ["name"] = player.Name,
        };
        try
        {
            File.AppendAllText(_storePath, record.ToString(Newtonsoft.Json.Formatting.None) + "\n");
        }
        catch (IOException)
        {
            // Best-effort, like the room logs: identity still works for this boot.
        }
    }

    private static string Sanitize(string? name)
    {
        name = (name ?? "").Trim();
        return name[..Math.Min(MaxNameLength, name.Length)];
    }
}
