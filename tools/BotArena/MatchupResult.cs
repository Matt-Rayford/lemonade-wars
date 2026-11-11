namespace LemonadeWars.BotArena;

internal sealed record MatchupResult(double WinShare, double AverageActions, double HeroLordsMet, int Stalled)
{
    /// <summary>Half-width of the Wilson 95% interval around the win share.</summary>
    public double Margin(int games)
    {
        const double z = 1.96;
        double n = games;
        return z * Math.Sqrt(WinShare * (1 - WinShare) / n + z * z / (4 * n * n)) / (1 + z * z / n);
    }
}
