using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using Newtonsoft.Json;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// The search bots' foundations, each of which fails SILENTLY if wrong: cloning
    /// (a shallow copy corrupts the real game), determinization (a leak lets the bot
    /// cheat), and the search loop itself (must not throw across whole games).
    /// </summary>
    public class SearchBotTests
    {
        private static Game NewMidGame(ulong seed, int players, int warmupActions = 60)
        {
            var names = new[] { "Ana", "Ben", "Cal", "Dee" }.Take(players).ToArray();
            var game = Game.Create(TestData.Db, names, seed);
            PlayGreedily(game, warmupActions);
            return game;
        }

        private static void PlayGreedily(Game game, int maxActions)
        {
            var bot = new GreedyBot();
            for (int i = 0; i < maxActions && game.State.Stage != GameStage.Finished; i++)
            {
                int actor = game.ActingPlayers()[0];
                game.Apply(bot.Choose(game, actor));
            }
        }

        private static string Fingerprint(GameState state) => JsonConvert.SerializeObject(state);

        [Fact]
        public void CloneIsDeepFaithfulAndIndependent()
        {
            var game = NewMidGame(11, 3);
            string before = Fingerprint(game.State);

            var clone = SearchTools.CloneState(game.State);
            Assert.Equal(before, Fingerprint(clone));

            // Mutating the clone through real play must not touch the original.
            var cloneGame = Game.FromState(TestData.Db, clone);
            PlayGreedily(cloneGame, 40);
            Assert.Equal(before, Fingerprint(game.State));
            Assert.NotEqual(before, Fingerprint(cloneGame.State));
        }

        [Fact]
        public void CloneReplaysIdenticallyToTheOriginal()
        {
            // The RNG resumes from RngState, so the copy must replay byte-identically.
            var game = NewMidGame(12, 3);
            var clone = Game.FromState(TestData.Db, SearchTools.CloneState(game.State));
            var bot = new GreedyBot();
            for (int i = 0; i < 60; i++)
            {
                if (game.State.Stage == GameStage.Finished)
                {
                    break;
                }
                int actor = game.ActingPlayers()[0];
                var action = bot.Choose(game, actor);
                game.Apply(action);
                clone.Apply(bot.Choose(clone, actor));
            }
            Assert.Equal(Fingerprint(game.State), Fingerprint(clone.State));
        }

        [Fact]
        public void DeterminizationHidesOpponentsButPreservesTheVisibleWorld()
        {
            var game = NewMidGame(13, 4, warmupActions: 90);
            const int viewer = 0;
            var original = game.State;
            var world = SearchTools.CloneState(original);
            SearchTools.Determinize(world, viewer, new DeterministicRng(99));

            List<int> HiddenPool(GameState state) => state.LemonDeck
                .Concat(state.Players.Where(p => p.PlayerId != viewer).SelectMany(p => p.Hand))
                .OrderBy(id => id).ToList();

            Assert.Equal(original.Players[viewer].Hand, world.Players[viewer].Hand);

            // The hidden pool (rivals' hands plus the deck) holds exactly the same cards,
            // and every rival keeps their hand size.
            var hiddenBefore = HiddenPool(original);
            var hiddenAfter = HiddenPool(world);
            Assert.Equal(hiddenBefore, hiddenAfter);
            foreach (var player in original.Players.Where(p => p.PlayerId != viewer))
            {
                Assert.Equal(player.Hand.Count, world.Players[player.PlayerId].Hand.Count);
            }

            Assert.Equal(original.Market, world.Market);
            Assert.Equal(original.LemonDiscard, world.LemonDiscard);
            Assert.Equal(original.BlackMarketDiscard, world.BlackMarketDiscard);
            Assert.Equal(original.BlackMarketDeck.OrderBy(id => id),
                world.BlackMarketDeck.OrderBy(id => id)); // reshuffled, same cards

            // Opponents' secret lords are re-picked from their own deal, right count.
            foreach (var player in original.Players.Where(p => p.PlayerId != viewer))
            {
                var picked = world.Players[player.PlayerId].LemonLordKept;
                Assert.Equal(player.LemonLordKept.Count, picked.Count);
                Assert.All(picked, id => Assert.Contains(id, player.LemonLordDealt));
            }
        }

        [Fact]
        public void SearchBotFinishesAFullGameAgainstGreedy()
        {
            var game = Game.Create(TestData.Db, new[] { "Search", "G1", "G2" }, 7777);
            var bots = new Dictionary<int, IBot>
            {
                [0] = new SearchBot(seed: 1, budgetMs: 40, maxCandidates: 6, maxWorlds: 3),
                [1] = new GreedyBot(),
                [2] = new GreedyBot(),
            };
            GameRunner.PlayOut(game, bots);
            Assert.Equal(GameStage.Finished, game.State.Stage);
            Assert.NotEmpty(game.State.Winners);
        }

        [Fact]
        public void IsmctsBotFinishesAFullGameAgainstGreedy()
        {
            var game = Game.Create(TestData.Db, new[] { "Tree", "G1", "G2" }, 8888);
            var bots = new Dictionary<int, IBot>
            {
                [0] = new IsmctsBot(seed: 1, budgetMs: 10_000, maxIterations: 25),
                [1] = new GreedyBot(),
                [2] = new GreedyBot(),
            };
            GameRunner.PlayOut(game, bots);
            Assert.Equal(GameStage.Finished, game.State.Stage);
            Assert.NotEmpty(game.State.Winners);
        }
    }
}
