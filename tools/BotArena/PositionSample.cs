namespace LemonadeWars.BotArena;

/// <summary>
/// One training example for the evaluator: a position seen from one seat, with a
/// feature row per player and each player's share of the eventual win.
/// </summary>
internal sealed record PositionSample(double[][] Rows, double[] WinShares);
