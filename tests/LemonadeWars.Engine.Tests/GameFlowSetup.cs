using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Tests
{
    internal static class GameFlowSetup
    {
        private static readonly string[] FourPlayers = { "Ana", "Ben", "Cal", "Dee" };

        public static Game NewGame(ulong seed = 42, string[]? names = null) =>
            Game.Create(TestData.Db, names ?? FourPlayers, seed);

        public static void KeepFirstTwoLemonLords(Game game)
        {
            foreach (var player in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = player.PlayerId,
                    KeepTitleIds = player.LemonLordDealt.Take(2).ToList(),
                });
            }
        }

        public static void DraftBargainStandsOnly(Game game)
        {
            while (game.State.Stage == GameStage.InitialBuys)
            {
                int buyer = game.State.InitialBuyQueue[0];
                game.Apply(new InitialBuyStand { PlayerId = buyer, StandTypeId = "bargain" });
                game.Apply(new InitialBuyEnd { PlayerId = buyer });
            }
        }

        /// <summary>Anything the first turn's opening draw raises is left pending.</summary>
        public static Game ReadyToPlay(ulong seed = 42)
        {
            var game = NewGame(seed);
            KeepFirstTwoLemonLords(game);
            DraftBargainStandsOnly(game);
            return game;
        }
    }
}
