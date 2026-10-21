using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using Newtonsoft.Json;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// The hand-written cloner is held to the JSON round-trip, field for field, at
    /// many points of fuzzed games — the only defense against a new state field that
    /// quietly never gets copied.
    /// </summary>
    public class GameStateClonerTests
    {
        private static string Fingerprint(GameState state) => JsonConvert.SerializeObject(state);

        [Theory]
        [InlineData(101UL, 3)]
        [InlineData(102UL, 4)]
        [InlineData(103UL, 5)]
        [InlineData(104UL, 2)]
        public void HandCloneMatchesJsonCloneThroughoutAFuzzedGame(ulong seed, int players)
        {
            var names = new[] { "Ana", "Ben", "Cal", "Dee", "Eve" }.Take(players).ToArray();
            var game = Game.Create(TestData.Db, names, seed);
            var bots = new Dictionary<int, IBot>();
            for (int i = 0; i < players; i++)
            {
                bots[i] = i == 0 ? new GreedyBot() : (IBot)new RandomBot(seed * 7 + (ulong)i);
            }

            int checkedStates = 0;
            for (int step = 0; step < 4000 && game.State.Stage != GameStage.Finished; step++)
            {
                if (step % 5 == 0)
                {
                    string expected = Fingerprint(SearchTools.CloneStateViaJson(game.State));
                    Assert.Equal(expected, Fingerprint(GameStateCloner.Clone(game.State)));
                    checkedStates++;
                }
                int actor = game.ActingPlayers()[0];
                game.Apply(bots[actor].Choose(game, actor));
            }
            Assert.Equal(Fingerprint(game.State), Fingerprint(GameStateCloner.Clone(game.State)));
            Assert.True(checkedStates > 10, $"only {checkedStates} states compared");
        }

        [Fact]
        public void HandCloneSharesNothingWithTheOriginal()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben", "Cal" }, 55);
            var bot = new GreedyBot();
            for (int i = 0; i < 80 && game.State.Stage != GameStage.Finished; i++)
            {
                int actor = game.ActingPlayers()[0];
                game.Apply(bot.Choose(game, actor));
            }
            string before = Fingerprint(game.State);

            var clone = Game.FromState(TestData.Db, GameStateCloner.Clone(game.State));
            for (int i = 0; i < 120 && clone.State.Stage != GameStage.Finished; i++)
            {
                int actor = clone.ActingPlayers()[0];
                clone.Apply(bot.Choose(clone, actor));
            }
            Assert.Equal(before, Fingerprint(game.State));
            Assert.NotEqual(before, Fingerprint(clone.State));
        }
    }
}
