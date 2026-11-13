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
    /// Rebuild a started room by replaying its action log from the persisted seed.
    /// Returns null for finished or unreadable games.
    /// </summary>
    public static Room? LoadFromFile(string code, CardDatabase db, string path, int botDelayMs,
        Action<string, string>? turnNotifier = null, int msPerTimerSecond = 1000)
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

            var room = new Room(code, db, path, botDelayMs, turnNotifier, msPerTimerSecond);
            string[] names = room.RestoreSeats(header);
            // Logs from before the action timer belong to games that started without one.
            room.ActionTimerSeconds = (int?)header["actionTimerSeconds"] ?? NoActionTimer;
            // Only a hand-picked row is logged; a dealt one comes back out of the seed.
            var handPickedFirstDibs = (header["firstDibs"] as JArray)?.Select(id => (string)id!).ToList();
            if (handPickedFirstDibs != null)
            {
                room.FirstDibsMode = FirstDibsModes.Manual;
            }
            room._game = Game.Create(db, names, ulong.Parse((string)header["seed"]!), handPickedFirstDibs);
            foreach (string actionLine in lines.Skip(1))
            {
                room._game.Apply(WireCodec.DecodeAction(JObject.Parse(actionLine)));
            }
            room.LastActivityUtc = File.GetLastWriteTimeUtc(path);
            return room._game.State.Stage == GameStage.Finished ? null : room;
        }
        catch (Exception)
        {
            // A corrupt log drops the room rather than crashing the server.
            return null;
        }
    }

    private string[] RestoreSeats(JObject header)
    {
        var names = header["names"]!.Select(n => (string)n!).ToArray();
        var isBot = header["bots"]!.Select(b => (bool)b!).ToArray();
        var tokens = header["tokens"]!.Select(t => (string)t!).ToArray();
        // Pre-identity logs have no players array: those seats stay token-only.
        var playerIds = (header["players"] as JArray)?.Select(p => (string?)p).ToArray();
        // Pre-difficulty logs have no botLevels: those bots rehydrate as medium.
        var botLevels = (header["botLevels"] as JArray)?.Select(l => (string?)l).ToArray();
        for (int i = 0; i < names.Length; i++)
        {
            string botLevel = BotFactory.Normalize(
                botLevels != null && i < botLevels.Length ? botLevels[i] : null);
            _seats.Add(new Seat
            {
                Index = i,
                Name = names[i],
                IsBot = isBot[i],
                Ready = true,
                Token = tokens[i],
                PlayerId = playerIds != null && i < playerIds.Length &&
                           !string.IsNullOrEmpty(playerIds[i]) ? playerIds[i] : null,
                BotLevel = botLevel,
            });
            if (isBot[i])
            {
                // Bot moves are replayed from the log, so a rehydrated bot needs no stable seed.
                _bots[i] = BotFactory.Create(botLevel,
                    BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));
            }
        }
        return names;
    }

    private void PersistHeader(ulong seed)
    {
        if (_storePath == null)
        {
            return;
        }
        var fields = new JObject
        {
            ["seed"] = seed.ToString(),
            ["names"] = new JArray(_seats.Select(s => s.Name)),
            ["bots"] = new JArray(_seats.Select(s => s.IsBot)),
            ["tokens"] = new JArray(_seats.Select(s => s.Token)),
            ["players"] = new JArray(_seats.Select(s => s.PlayerId ?? "")),
            ["botLevels"] = new JArray(_seats.Select(s => s.BotLevel)),
            ["actionTimerSeconds"] = ActionTimerSeconds,
        };
        if (FirstDibsMode == FirstDibsModes.Manual)
        {
            fields["firstDibs"] = new JArray(_game!.State.FirstDibsRow);
        }
        var header = new JObject { ["header"] = fields };
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
