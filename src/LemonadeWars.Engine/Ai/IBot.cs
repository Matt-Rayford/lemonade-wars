using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>A computer opponent: given the game and its seat, pick one legal action.</summary>
    public interface IBot
    {
        GameAction Choose(Game game, int playerId);
    }
}
