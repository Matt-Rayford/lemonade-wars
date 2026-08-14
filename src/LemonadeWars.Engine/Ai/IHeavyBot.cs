namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// A bot whose Choose burns a real time budget. Sessions must never run one on the
    /// UI thread or against the live game — think on a snapshot clone.
    /// </summary>
    public interface IHeavyBot
    {
    }
}
