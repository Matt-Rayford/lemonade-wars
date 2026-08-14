using System.Security.Cryptography;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using LemonadeWars.Protocol;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Server;

// Action log: one jsonl file per room — a header line, then every applied action.
public sealed partial class Room
{
    /// <summary>
    /// Rebuild a started room from its persisted seed + action log (deterministic replay).
    /// Returns null for finished or unreadable games.
    /// </summary>
    public static Room? LoadFromFile(string code, CardDatabase db, string path, int botDelayMs,
        Action<string, string>? turnNotifier = null)
    {
        try
        {
            var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
            if (lines.Length == 0)
            {
                return null;
            }
            var header = JObject.Parse(lines[0])["header"] as JObject;
            if (header == null)
            {
                return null;
            }

            var room = new Room(code, db, path, botDelayMs, turnNotifier);
            var names = header["names"]!.Select(n => (string)n!).ToArray();
            var bots = header["bots"]!.Select(b => (bool)b!).ToArray();
            var tokens = header["tokens"]!.Select(t => (string)t!).ToArray();
            // Pre-identity logs have no players array: those seats stay token-only.
            var players = (header["players"] as JArray)?.Select(p => (string?)p).ToArray();
            // Pre-difficulty logs have no botLevels: those bots rehydrate as medium.
            var levels = (header["botLevels"] as JArray)?.Select(l => (string?)l).ToArray();
            for (int i = 0; i < names.Length; i++)
            {
                string botLevel = BotFactory.Normalize(
                    levels != null && i < levels.Length ? levels[i] : null);
                room._seats.Add(new Seat
                {
                    Index = i,
                    Name = names[i],
                    IsBot = bots[i],
                    Ready = true,
                    Token = tokens[i],
                    PlayerId = players != null && i < players.Length &&
                               !string.IsNullOrEmpty(players[i]) ? players[i] : null,
                    BotLevel = botLevel,
                });
                if (bots[i])
                {
                    // Bot decisions are persisted as actions, so the bot itself does
                    // not need to be deterministic across rehydrations.
                    room._bots[i] = BotFactory.Create(botLevel,
                        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));
                }
            }

            room._game = Game.Create(db, names, ulong.Parse((string)header["seed"]!));
            for (int i = 1; i < lines.Length; i++)
            {
                room._game.Apply(WireCodec.DecodeAction(JObject.Parse(lines[i])));
            }
            room.LastActivityUtc = File.GetLastWriteTimeUtc(path);
            return room._game.State.Stage == GameStage.Finished ? null : room;
        }
        catch (Exception)
        {
            return null; // corrupt log: better to drop the room than crash the server
        }
    }

    private void PersistHeader(ulong seed)
    {
        if (_storePath == null)
        {
            return;
        }
        var header = new JObject
        {
            ["header"] = new JObject
            {
                ["seed"] = seed.ToString(),
                ["names"] = new JArray(_seats.Select(s => s.Name)),
                ["bots"] = new JArray(_seats.Select(s => s.IsBot)),
                ["tokens"] = new JArray(_seats.Select(s => s.Token)),
                ["players"] = new JArray(_seats.Select(s => s.PlayerId ?? "")),
                ["botLevels"] = new JArray(_seats.Select(s => s.BotLevel)),
            },
        };
        AppendLine(header.ToString(Newtonsoft.Json.Formatting.None));
    }

    private void PersistAction(GameAction action)
    {
        LastActivityUtc = DateTime.UtcNow;
        if (_storePath != null)
        {
            AppendLine(WireCodec.EncodeAction(action).ToString(Newtonsoft.Json.Formatting.None));
        }
    }

    private void AppendLine(string line)
    {
        try
        {
            File.AppendAllText(_storePath!, line + "\n");
        }
        catch (IOException)
        {
            // Persistence is best-effort; a full disk must not kill the game.
        }
    }
}
