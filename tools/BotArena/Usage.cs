namespace LemonadeWars.BotArena;

internal static class Usage
{
    public const string Text = """
        Bot arena. Run a mode with:
          dotnet run -c Release --project tools/BotArena -- <mode and arguments>

          <hero> <baseline> [games] [players] [budgetMs]
              Tournament, the default mode: one hero level against a table of baseline
              bots. With no arguments: hard medium 100 4 120.

          probe <seed> <heroSeat> [budgetMs]
              Replay one stalled tournament game and print its final action cycle.

          fuzz <seeds> [players]
              Hunt engine deadlocks (an awaited player with zero legal moves).

          strategy <games> <players> <level> <budgetMs> <out.jsonl>
              Every seat plays the same level; per-player behavior and final-state
              metrics are dumped as JSONL for offline analysis (win rate by strategy).

          replay <path.jsonl> [level] [--flags-only]
              Rebuild a persisted server room log and, before every logged bot move, ask
              a fresh bot of <level> (default: the seat's recorded level) what it would do
              now. Prints DIFF on disagreement and flags dubious plays by anyone.
              --flags-only keeps just the flagged lines and the per-turn standings.

          learn <games> <iterations> [out-dir]
              Fit the evaluator's weights to self-play outcomes (conditional logit),
              iterating so the next batch of games is played with the weights just learned.

          tune <games> <players> <rounds> [budgetMs] [out.json]
              Self-play coordinate descent over the planner's evaluation weights.

          record <out.jsonl> <seed> [players]
              Play one all-greedy game and write it in the room-log format (a replay
              fixture; replay it at level "greedy" for a zero-DIFF baseline).

        Levels: easy, medium, hard and wambulance as shipped, plus the arena-only levels
        listed in ArenaTools.MakeBot.
        """;
}
