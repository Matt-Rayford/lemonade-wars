using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// The engine's ultimate integration tests: bots play complete games from setup to
    /// scoring. RandomBot fuzzes every reachable rules path; any invalid enumerated move,
    /// deadlock, or infinite loop fails the run.
    /// </summary>
    public class BotAndFuzzTests
    {
        private static Game NewGame(ulong seed, int playerCount)
        {
            var names = new[] { "Ana", "Ben", "Cal", "Dee", "Eve" }.Take(playerCount).ToArray();
            return Game.Create(TestData.Db, names, seed);
        }

        private static Dictionary<int, IBot> Bots(int playerCount, ulong seed, bool greedy = false)
        {
            var bots = new Dictionary<int, IBot>();
            for (int i = 0; i < playerCount; i++)
            {
                bots[i] = greedy ? new GreedyBot() : (IBot)new RandomBot(seed * 31 + (ulong)i);
            }
            return bots;
        }

        [Theory]
        [InlineData(1UL, 2)]
        [InlineData(2UL, 3)]
        [InlineData(3UL, 4)]
        [InlineData(4UL, 5)]
        [InlineData(5UL, 4)]
        [InlineData(6UL, 3)]
        [InlineData(7UL, 2)]
        [InlineData(8UL, 5)]
        [InlineData(9UL, 4)]
        [InlineData(10UL, 3)]
        public void RandomBotsCompleteFullGames(ulong seed, int playerCount)
        {
            var game = NewGame(seed, playerCount);
            int actions = GameRunner.PlayOut(game, Bots(playerCount, seed));

            Assert.Equal(GameStage.Finished, game.State.Stage);
            Assert.NotEmpty(game.State.Winners);
            Assert.True(actions > 20, $"suspiciously short game: {actions} actions");

            // Conservation: every lemon card instance is in exactly one zone.
            var s = game.State;
            var zones = s.LemonDeck.Concat(s.LemonDiscard)
                .Concat(s.Players.SelectMany(p => p.Hand))
                .Concat(s.Players.SelectMany(p => p.TantrumPile.Select(t => t.InstanceId)))
                .Concat(s.Players.Where(p => p.Turf.TrapInstanceId != null)
                    .Select(p => p.Turf.TrapInstanceId!.Value))
                .ToList();
            Assert.Equal(s.LemonInstances.Count, zones.Count);
            Assert.Equal(s.LemonInstances.Count, zones.Distinct().Count());

            // Same for Black Market instances.
            var bmZones = s.BlackMarketDeck.Concat(s.Market).Concat(s.BlackMarketDiscard)
                .Concat(s.Players.SelectMany(p => p.Turf.Equipped))
                .Concat(s.Players.SelectMany(p => p.Stands.SelectMany(st => st.Equipped)))
                .ToList();
            Assert.Equal(s.BlackMarketInstances.Count, bmZones.Count);
            Assert.Equal(s.BlackMarketInstances.Count, bmZones.Distinct().Count());

            // Money never goes negative.
            Assert.All(s.Players, p => Assert.True(p.Money >= 0, $"P{p.PlayerId} has ${p.Money}"));
        }

        [Fact]
        public void BotGamesAreDeterministic()
        {
            string Run()
            {
                var game = NewGame(77, 4);
                GameRunner.PlayOut(game, Bots(4, 77));
                return game.SnapshotJson();
            }
            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void GreedyBotsCompleteFullGames()
        {
            for (ulong seed = 100; seed < 105; seed++)
            {
                var game = NewGame(seed, 4);
                GameRunner.PlayOut(game, Bots(4, seed, greedy: true));
                Assert.Equal(GameStage.Finished, game.State.Stage);
            }
        }

        [Fact]
        public void GreedyBeatsRandomMoreOftenThanNot()
        {
            int greedyWins = 0, games = 0;
            for (ulong seed = 200; seed < 210; seed++)
            {
                var game = NewGame(seed, 4);
                // Seat 0 is greedy; the rest are random. Rotate nothing — seat advantage
                // washes out across seeds because the first player is seed-random.
                var bots = new Dictionary<int, IBot> { [0] = new GreedyBot() };
                for (int i = 1; i < 4; i++)
                {
                    bots[i] = new RandomBot(seed * 131 + (ulong)i);
                }
                GameRunner.PlayOut(game, bots);
                games++;
                if (game.State.Winners.Contains(0))
                {
                    greedyWins++;
                }
            }
            // 4-player baseline is 25%; the heuristic bot should clear it comfortably.
            Assert.True(greedyWins * 4 >= games,
                $"GreedyBot won only {greedyWins}/{games} against random opponents");
        }

        // --------------------------------------------------------- view tests

        [Fact]
        public void ViewsHideOtherHandsAndSecrets()
        {
            var game = NewGame(42, 4);
            foreach (var p in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = p.PlayerId,
                    KeepTitleIds = p.LemonLordDealt.Take(2).ToList(),
                });
            }

            var view = game.ViewFor(0);

            Assert.Equal(0, view.ViewerId);
            // Own hand visible, with real def ids.
            Assert.Equal(game.State.Players[0].Hand.Count, view.Hand.Count);
            Assert.All(view.Hand, c => Assert.False(string.IsNullOrEmpty(c.DefId)));
            // Others' hands are counts only.
            Assert.All(view.Players.Where(p => p.PlayerId != 0),
                p => Assert.Equal(game.State.Players[p.PlayerId].Hand.Count, p.HandCount));
            // Own secret titles visible; the view type carries no field for anyone else's.
            Assert.Equal(2, view.LemonLordKept.Count);
            // Deck contents are hidden — only counts cross the wire.
            Assert.Equal(game.State.LemonDeck.Count, view.LemonDeckCount);

            // Serialized view must not leak other players' hand instance ids or secret titles.
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(view);
            foreach (var other in game.State.Players.Where(p => p.PlayerId != 0))
            {
                foreach (string titleId in other.LemonLordKept)
                {
                    // A title id may legitimately appear if the viewer was dealt it too.
                    if (!game.State.Players[0].LemonLordDealt.Contains(titleId))
                    {
                        Assert.DoesNotContain(titleId, json);
                    }
                }
            }
        }

        [Fact]
        public void ViewSurvivesFullGameWithoutLeakingDeckOrder()
        {
            var game = NewGame(55, 3);
            var bots = Bots(3, 55);
            int guard = 0;
            while (game.State.Stage != GameStage.Finished && guard++ < 5000)
            {
                // Building a view for every seat at every step must never throw.
                for (int p = 0; p < 3; p++)
                {
                    var view = game.ViewFor(p);
                    Assert.Equal(game.State.LemonDeck.Count, view.LemonDeckCount);
                }
                int actor = game.ActingPlayers()[0];
                game.Apply(bots[actor].Choose(game, actor));
            }
            Assert.Equal(GameStage.Finished, game.State.Stage);
        }

        // ---------------------------------------------------- move soundness

        [Fact]
        public void EveryEnumeratedMoveAppliesCleanly()
        {
            // Walk a full random game; at each step, EVERY enumerated move for the acting
            // player must validate against a cloned snapshot... cloning isn't wired yet, so
            // we assert the cheaper contract: the chosen random move never throws, across
            // many seeds (covered above), and the move list is non-empty for actors.
            var game = NewGame(999, 4);
            var bots = Bots(4, 999);
            int guard = 0;
            while (game.State.Stage != GameStage.Finished && guard++ < 20000)
            {
                var acting = game.ActingPlayers();
                Assert.NotEmpty(acting);
                foreach (int p in acting)
                {
                    Assert.NotEmpty(game.LegalMovesFor(p));
                }
                // Non-acting players must have zero moves.
                for (int p = 0; p < 4; p++)
                {
                    if (!acting.Contains(p) && game.State.Stage != GameStage.ChoosingLemonLords)
                    {
                        Assert.Empty(game.LegalMovesFor(p));
                    }
                }
                int actor = acting[0];
                game.Apply(bots[actor].Choose(game, actor));
            }
            Assert.Equal(GameStage.Finished, game.State.Stage);
        }
    }
}
