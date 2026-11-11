using LemonadeWars.BotArena;

// Any first argument that is not a mode name is a tournament's hero level, so the
// modes and their arguments are documented in Usage.Text (printed by `help`).
string mode = args.Length > 0 ? args[0] : "";
switch (mode)
{
    case "help":
    case "--help":
    case "-h":
        Console.WriteLine(Usage.Text);
        break;
    case "learn":
        LearnMode.Run(games: IntArg(1), iterations: IntArg(2), outDir: TextArgOr(3, "learned-weights"));
        break;
    case "tune":
        TuneMode.Run(games: IntArg(1), players: IntArg(2), rounds: IntArg(3),
            budgetMs: IntArgOr(4, 30), outPath: TextArgOr(5, "tuned-weights.json"));
        break;
    case "probe":
        ProbeMode.Run(seed: ulong.Parse(args[1]), heroSeat: IntArg(2), budgetMs: IntArgOr(3, 250));
        break;
    case "fuzz":
        FuzzMode.Run(seeds: IntArg(1), players: IntArgOr(2, 4));
        break;
    case "replay":
        RunReplay();
        break;
    case "record":
        RecordMode.Run(outPath: args[1], seed: ulong.Parse(args[2]), players: IntArgOr(3, 4));
        break;
    case "strategy":
        StrategyMode.Run(games: IntArg(1), players: IntArg(2), level: args[3],
            budgetMs: IntArg(4), outPath: args[5]);
        break;
    default:
        TournamentMode.Run(hero: TextArgOr(0, "hard"), baseline: TextArgOr(1, "medium"),
            games: IntArgOr(2, 100), players: IntArgOr(3, 4), budgetMs: IntArgOr(4, 120));
        break;
}

void RunReplay()
{
    bool flagsOnly = args.Contains("--flags-only");
    var positional = args.Skip(1).Where(argument => !argument.StartsWith("--")).ToArray();
    ReplayMode.Run(positional[0], positional.Length > 1 ? positional[1] : null, flagsOnly);
}

int IntArg(int index) => int.Parse(args[index]);

int IntArgOr(int index, int fallback) => args.Length > index ? int.Parse(args[index]) : fallback;

string TextArgOr(int index, string fallback) => args.Length > index ? args[index] : fallback;
