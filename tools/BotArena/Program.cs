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
