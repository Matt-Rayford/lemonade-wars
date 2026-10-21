using LemonadeWars.BotArena;

// Bot arena CLI. Default mode is the tournament:
//   dotnet run -c Release --project tools/BotArena -- hard medium 100 4 [budgetMs]
//
// Probe mode: replay one stalled game and print the action cycle.
//   dotnet run -c Release --project tools/BotArena -- probe <seed> <heroSeat> [budgetMs]
//
// Fuzz mode: hunt engine deadlocks (an awaited player with zero legal moves).
//   dotnet run -c Release --project tools/BotArena -- fuzz <seeds> [players]
//
// Strategy telemetry: all seats the SAME level, per-player behavior + final-state
// metrics dumped as JSONL for offline analysis (win-rate by strategy).
//   dotnet run -c Release --project tools/BotArena -- strategy <games> <players> <level> <budgetMs> <out.jsonl>
//
// Replay audit: rebuild a persisted room log (server jsonl) and, before every logged
// bot move, ask a fresh bot of <level> (default: the seat's recorded level) what it
// would do now; prints DIFF on disagreement and flags dubious plays by anyone.
// --flags-only keeps just the flagged lines and the per-turn standings.
//   dotnet run -c Release --project tools/BotArena -- replay <path.jsonl> [level] [--flags-only]
//
// Learn: fit the evaluator's weights to self-play outcomes (conditional logit), iterating
// so the next batch of games is played with the weights just learned.
//   dotnet run -c Release --project tools/BotArena -- learn <games> <iterations> [out-dir]
//
// Tune: self-play coordinate descent over the planner's evaluation weights.
//   dotnet run -c Release --project tools/BotArena -- tune <games> <players> <rounds> [budgetMs] [out.json]
//
// Record: play one all-greedy game and write it in the room-log format (a replay fixture;
// replay it at level "greedy" for a zero-DIFF baseline).
//   dotnet run -c Release --project tools/BotArena -- record <out.jsonl> <seed> [players]

if (args.Length > 0 && args[0] == "learn")
{
    LearnMode.Run(
        games: int.Parse(args[1]),
        iterations: int.Parse(args[2]),
        outDir: args.Length > 3 ? args[3] : "learned-weights");
    return;
}

if (args.Length > 0 && args[0] == "tune")
{
    TuneMode.Run(
        games: int.Parse(args[1]),
        players: int.Parse(args[2]),
        rounds: int.Parse(args[3]),
        budgetMs: args.Length > 4 ? int.Parse(args[4]) : 30,
        outPath: args.Length > 5 ? args[5] : "tuned-weights.json");
    return;
}

if (args.Length > 0 && args[0] == "probe")
{
    ProbeMode.Run(ulong.Parse(args[1]), int.Parse(args[2]),
        args.Length > 3 ? int.Parse(args[3]) : 250);
    return;
}

if (args.Length > 0 && args[0] == "fuzz")
{
    FuzzMode.Run(int.Parse(args[1]), args.Length > 2 ? int.Parse(args[2]) : 4);
    return;
}

if (args.Length > 0 && args[0] == "replay")
{
    bool flagsOnly = args.Contains("--flags-only");
    var positional = args.Skip(1).Where(a => !a.StartsWith("--")).ToArray();
    ReplayMode.Run(positional[0], positional.Length > 1 ? positional[1] : null, flagsOnly);
    return;
}

if (args.Length > 0 && args[0] == "record")
{
    RecordMode.Run(args[1], ulong.Parse(args[2]), args.Length > 3 ? int.Parse(args[3]) : 4);
    return;
}

if (args.Length > 0 && args[0] == "strategy")
{
    StrategyMode.Run(int.Parse(args[1]), int.Parse(args[2]), args[3],
        int.Parse(args[4]), args[5]);
    return;
}

TournamentMode.Run(
    hero: args.Length > 0 ? args[0] : "hard",
    baseline: args.Length > 1 ? args[1] : "medium",
    games: args.Length > 2 ? int.Parse(args[2]) : 100,
    players: args.Length > 3 ? int.Parse(args[3]) : 4,
    budgetMs: args.Length > 4 ? int.Parse(args[4]) : 120);
