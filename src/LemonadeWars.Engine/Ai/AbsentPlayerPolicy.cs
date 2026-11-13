using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Acts for a player whose action timer ran out: whatever can be declined is declined
    /// (pass the window, skip the offer, end the turn), and a stand-in answers what cannot be
    /// (a forced discard, a fine, the setup draft's mandatory Stand).
    /// </summary>
    public static class AbsentPlayerPolicy
    {
        public static GameAction Choose(Game game, int playerId, IBot standIn)
        {
            var declining = game.LegalMovesFor(playerId).FirstOrDefault(IsDeclining);
            return declining ?? standIn.Choose(game, playerId);
        }

        private static bool IsDeclining(GameAction move) =>
            move is PassWindow || move is SkipFreePlay || move is EndTurn || move is InitialBuyEnd;
    }
}
