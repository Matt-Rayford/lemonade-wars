using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.BotArena;

/// <summary>
/// Audits a persisted room log (see Room.Persistence.cs): rebuilds the game action by
/// action and, before each logged bot move, asks a fresh bot what it would do from the
/// same state. Prints a DIFF when the two disagree and flags suspicious plays by anyone,
/// so "dumb bot moves" from real games can be found without re-playing them by hand.
/// </summary>
internal static class ReplayMode
{
    private const int BotBudgetMs = 300;
    private const double LeaderThreatThreshold = 0.5;
    private const string AttackedNonLeader = "ATTACKED-NON-LEADER";
    private const string SelfCancel = "SELF-CANCEL";
    private static readonly string[] ReactionDefIds = { "tantrum", "tag-youre-it", "im-rubber-youre-glue" };

    private sealed record Header(ulong Seed, string[] Names, bool[] Bots, string[] BotLevels);

    private sealed class Totals
    {
        public int Actions;
        public int BotActions;
        public int[] DiffsPerSeat;
        public Dictionary<string, int> FlagsPerKind = new();

        public Totals(int seats) => DiffsPerSeat = new int[seats];
    }

    public static void Run(string path, string? level, bool flagsOnly)
    {
        var db = ArenaTools.LoadDatabase();
        var lines = File.ReadAllLines(path);
        var header = ReadHeader(lines[0]);
        var game = Game.Create(db, header.Names, header.Seed);
        var bots = MakeBots(header, level);
        var totals = new Totals(header.Names.Length);

        int turn = 1;
        int lastActivePlayer = game.State.ActivePlayer;
        Console.WriteLine(Standings(game, turn));
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (lines[lineIndex].Length == 0)
            {
                continue;
            }
            var logged = WireCodec.DecodeAction(JObject.Parse(lines[lineIndex]));
            try
            {
                Audit(game, logged, turn, bots, totals, flagsOnly);
                game.Apply(logged);
            }
            // Describing a logged action can also fail once the engine has drifted from
            // the game as played (a card instance or seat the rebuilt state never had).
            catch (Exception ex) when (ex is InvalidActionException
                                       or KeyNotFoundException
                                       or ArgumentOutOfRangeException)
            {
                Console.WriteLine($"replay diverged at line {lineIndex + 1}: {ex.Message}");
                break;
            }
            if (game.State.ActivePlayer != lastActivePlayer)
            {
                lastActivePlayer = game.State.ActivePlayer;
                turn++;
                Console.WriteLine(Standings(game, turn));
            }
        }
        PrintTotals(header, totals);
    }

    private static Header ReadHeader(string firstLine)
    {
        var header = JObject.Parse(firstLine)["header"] as JObject
            ?? throw new InvalidDataException("line 1 is not a room header");
        var names = header["names"]!.Select(n => (string)n!).ToArray();
        var bots = header["bots"]!.Select(b => (bool)b!).ToArray();
        // Pre-difficulty logs have no botLevels: those bots were medium (see Room.LoadFromFile).
        var levels = (header["botLevels"] as JArray)?.Select(l => (string?)l).ToArray();
        var botLevels = Enumerable.Range(0, names.Length)
            .Select(i => BotFactory.Normalize(levels != null && i < levels.Length ? levels[i] : null))
            .ToArray();
        return new Header(ulong.Parse((string)header["seed"]!), names, bots, botLevels);
    }

    private static Dictionary<int, (IBot Bot, string Level)> MakeBots(Header header, string? levelOverride)
    {
        var bots = new Dictionary<int, (IBot, string)>();
        for (int seat = 0; seat < header.Names.Length; seat++)
        {
            if (!header.Bots[seat])
            {
                continue;
            }
            string level = levelOverride ?? header.BotLevels[seat];
            bots[seat] = (ArenaTools.MakeBot(level, 9001UL + (ulong)seat, BotBudgetMs), level);
        }
        return bots;
    }

    private static void Audit(Game game, GameAction logged, int turn,
        Dictionary<int, (IBot Bot, string Level)> bots, Totals totals, bool flagsOnly)
    {
        totals.Actions++;
        int actor = logged.PlayerId;
        string name = game.State.Players[actor].Name;
        string line = $"#{totals.Actions} turn {turn} P{actor} {name}";
        bool hasMarker = false;

        if (bots.TryGetValue(actor, out var seat))
        {
            totals.BotActions++;
            var chosen = ChooseFresh(game, seat.Bot, actor);
            line += $" [{seat.Level}] did: {MoveDescriber.Describe(game, logged)}" +
                    $" | now: {MoveDescriber.Describe(game, chosen)}";
            if (JsonConvert.SerializeObject(logged) != JsonConvert.SerializeObject(chosen))
            {
                totals.DiffsPerSeat[actor]++;
                line += " !! DIFF";
                hasMarker = true;
            }
        }
        else
        {
            line += $" [human] did: {MoveDescriber.Describe(game, logged)}";
        }

        foreach (string flag in Flags(game, logged))
        {
            totals.FlagsPerKind[flag] = totals.FlagsPerKind.GetValueOrDefault(flag) + 1;
            line += $" !! {flag}";
            hasMarker = true;
        }
        if (hasMarker || !flagsOnly)
        {
            Console.WriteLine(line);
        }
    }

    /// <summary>Search bots mutate the state they explore, so they think on a clone.</summary>
    private static GameAction ChooseFresh(Game game, IBot bot, int actor) =>
        bot is IHeavyBot
            ? bot.Choose(Game.FromState(game.Db, SearchTools.CloneState(game.State)), actor)
            : bot.Choose(game, actor);

    private static IEnumerable<string> Flags(Game game, GameAction action)
    {
        var s = game.State;
        int actor = action.PlayerId;
        switch (action)
        {
            case PlayLemonCard play when play.TargetPlayerId is int victim && victim != actor:
                if (ThreatAssessment.ThreatOf(game, actor, s.Players[victim]) < LeaderThreatThreshold)
                {
                    yield return AttackedNonLeader;
                }
                break;
            case RespondToWindow respond when s.ResponseStack.Count > 0:
                if (IsSelfCancel(game, respond))
                {
                    yield return SelfCancel;
                }
                break;
        }
    }

    /// <summary>
    /// Two shapes of shooting yourself in the foot: cancelling your own pending play,
    /// or spending a tantrum on an unrelated top item while your own earlier play still
    /// waits beneath it. The second burns the card that could have answered the next
    /// counter against your own play; countering a reaction (tantrum/tag/rubber-glue)
    /// is excluded because that is exactly how you rescue your own play.
    /// </summary>
    private static bool IsSelfCancel(Game game, RespondToWindow respond)
    {
        var s = game.State;
        int actor = respond.PlayerId;
        var top = s.ResponseStack[s.ResponseStack.Count - 1];
        if (top.OwnerId == actor)
        {
            return true;
        }
        bool isTantrum = s.LemonInstances.TryGetValue(respond.CardInstanceId, out var card) &&
                         card.DefId == "tantrum";
        bool topIsReaction = ReactionDefIds.Contains(top.LemonDefId);
        return isTantrum && !topIsReaction && s.ResponseStack.Any(it => it.OwnerId == actor);
    }

    private static string Standings(Game game, int turn) =>
        $"-- turn {turn}: " + string.Join(" | ", game.State.Players.Select(p =>
            $"{p.Name} VP{p.InGameVictoryPoints} ${p.Money} hand{p.Hand.Count} stands{p.Stands.Count}"));

    private static void PrintTotals(Header header, Totals totals)
    {
        Console.WriteLine();
        Console.WriteLine($"replayed {totals.Actions} actions ({totals.BotActions} by bots)");
        for (int seat = 0; seat < header.Names.Length; seat++)
        {
            if (header.Bots[seat])
            {
                Console.WriteLine($"  P{seat} {header.Names[seat]}: {totals.DiffsPerSeat[seat]} DIFF");
            }
        }
        foreach (var (flag, count) in totals.FlagsPerKind.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"  {flag}: {count}");
        }
    }
}
