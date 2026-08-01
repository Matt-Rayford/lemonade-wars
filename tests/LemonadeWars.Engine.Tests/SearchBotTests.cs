using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using Newtonsoft.Json;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// The search bot's foundations, each of which fails SILENTLY if wrong: cloning
    /// (a shallow copy corrupts the real game), determinization (a leak lets the bot
    /// cheat), and the search loop itself (must not throw across whole games).
    /// </summary>
    public class SearchBotTests
    {
        private static Game NewMidGame(ulong seed, int players, int warmupActions = 60)
        {
            var names = new[] { "Ana", "Ben", "Cal", "Dee" }.Take(players).ToArray();
            var game = Game.Create(TestData.Db, names, seed);
            var bot = new GreedyBot();
            for (int i = 0; i < warmupActions && game.State.Stage != GameStage.Finished; i++)
            {
                int actor = game.ActingPlayers()[0];
                game.Apply(bot.Choose(game, actor));
            }
            return game;
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
            var bot = new GreedyBot();
            for (int i = 0; i < 40 && cloneGame.State.Stage != GameStage.Finished; i++)
            {
                int actor = cloneGame.ActingPlayers()[0];
                cloneGame.Apply(bot.Choose(cloneGame, actor));
            }
            Assert.Equal(before, Fingerprint(game.State));
            Assert.NotEqual(before, Fingerprint(cloneGame.State));
        }

        [Fact]
        public void CloneReplaysIdenticallyToTheOriginal()
        {
            // Same state + same actions must give byte-identical results — the RNG
            // resumes from RngState, so determinism survives the copy.
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

            // The viewer's own hand is untouched, in order.
            Assert.Equal(original.Players[viewer].Hand, world.Players[viewer].Hand);

            // Opponents keep their hand SIZES; the hidden pool is conserved exactly
            // (same multiset of instance ids across opponents' hands + the deck).
            var hiddenBefore = original.LemonDeck
                .Concat(original.Players.Where(p => p.PlayerId != viewer).SelectMany(p => p.Hand))
                .OrderBy(id => id).ToList();
            var hiddenAfter = world.LemonDeck
                .Concat(world.Players.Where(p => p.PlayerId != viewer).SelectMany(p => p.Hand))
                .OrderBy(id => id).ToList();
            Assert.Equal(hiddenBefore, hiddenAfter);
            foreach (var player in original.Players.Where(p => p.PlayerId != viewer))
            {
                Assert.Equal(player.Hand.Count, world.Players[player.PlayerId].Hand.Count);
            }

            // Public zones stay byte-identical.
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

        [Fact]
        public void WastedNumberPlacementsAreFilteredFromSearch()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben" }, seed: 5);
            var me = game.State.Players[0];
            me.Stands.Add(new StandInstance { InstanceId = 9001, StandTypeId = "classic", Shape = LemonadeWars.Engine.Data.Shape.Square }); // sells 4,5
            me.Stands.Add(new StandInstance { InstanceId = 9002, StandTypeId = "bargain", Shape = LemonadeWars.Engine.Data.Shape.Square }); // sells 1,2,3

            int pushy4 = game.State.BlackMarketDeck
                .First(id => game.State.BlackMarketInstances[id].DefId == "pushy-salesman-4");
            game.State.Market.Insert(0, pushy4);

            var ontoClassic = new BuyBlackMarket { MarketIndex = 0, TargetStandInstanceId = 9001 };
            var ontoBargain = new BuyBlackMarket { MarketIndex = 0, TargetStandInstanceId = 9002 };

            // A 4 onto a stand already selling on 4 is objectively wasted; the same
            // card onto the bargain stand is a real upgrade.
            Assert.True(MoveFilters.ObjectivelyWasted(game, 0, ontoClassic));
            Assert.False(MoveFilters.ObjectivelyWasted(game, 0, ontoBargain));

            var kept = MoveFilters.DropWasted(game, 0, new GameAction[] { ontoClassic, ontoBargain });
            Assert.Same(ontoBargain, Assert.Single(kept));
            // Never empties the list: with only wasted options, they stay legal.
            Assert.Single(MoveFilters.DropWasted(game, 0, new GameAction[] { ontoClassic }));

            // Spiked Lemonade duplicates a pour number we already cover — wasted too,
            // unless the secret Pour Master lord collects duplicates.
            int pour = game.PourNumbersOf(me).First();
            int spiked = game.State.BlackMarketDeck
                .First(id => game.State.BlackMarketInstances[id].DefId == $"spiked-lemonade-{pour}");
            game.State.Market.Insert(0, spiked);
            var dupPour = new BuyBlackMarket { MarketIndex = 0 };
            Assert.True(MoveFilters.ObjectivelyWasted(game, 0, dupPour));
            me.LemonLordKept.Add("pour-master");
            Assert.False(MoveFilters.ObjectivelyWasted(game, 0, dupPour));
        }

        // ------------------------------------------------- value-blind attack rigs

        private static int _rigId = 9100;

        /// <summary>Deal through setup and the first draw, stopping in the active player's Play phase.</summary>
        private static Game ReadyToAct(ulong seed, int players)
        {
            var names = new[] { "Ana", "Ben", "Cal", "Dee" }.Take(players).ToArray();
            var game = Game.Create(TestData.Db, names, seed);
            foreach (var p in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = p.PlayerId,
                    KeepTitleIds = p.LemonLordDealt.Take(2).ToList(),
                });
            }
            while (game.State.Stage == GameStage.InitialBuys)
            {
                int buyer = game.State.InitialBuyQueue[0];
                game.Apply(new InitialBuyStand { PlayerId = buyer, StandTypeId = "bargain" });
                game.Apply(new InitialBuyEnd { PlayerId = buyer });
            }
            var warmup = new GreedyBot();
            while (game.State.Phase != TurnPhase.Play && game.State.Stage != GameStage.Finished)
            {
                game.Apply(warmup.Choose(game, game.ActingPlayers()[0]));
            }
            return game;
        }

        /// <summary>Move one instance of a lemon def into the player's hand, from wherever it is.</summary>
        private static void GiveCard(Game game, int playerId, string defId)
        {
            var s = game.State;
            int Find(List<int> zone) => zone.FirstOrDefault(id => s.LemonInstances[id].DefId == defId);

            int found = Find(s.LemonDeck);
            if (found != 0)
            {
                s.LemonDeck.Remove(found);
            }
            else if ((found = Find(s.LemonDiscard)) != 0)
            {
                s.LemonDiscard.Remove(found);
            }
            else
            {
                foreach (var p in s.Players)
                {
                    found = Find(p.Hand);
                    if (found != 0)
                    {
                        p.Hand.Remove(found);
                        break;
                    }
                }
            }
            Assert.NotEqual(0, found);
            s.Players[playerId].Hand.Add(found);
        }

        /// <summary>Bury the player's hand at the bottom of the deck, then deal them one named card.</summary>
        private static void StripToOneCard(Game game, int playerId, string defId)
        {
            var hand = game.State.Players[playerId].Hand;
            game.State.LemonDeck.AddRange(hand);
            hand.Clear();
            GiveCard(game, playerId, defId);
        }

        private static void AddStand(PlayerState player, string typeId) =>
            player.Stands.Add(new StandInstance
            {
                InstanceId = _rigId++,
                StandTypeId = typeId,
                Shape = LemonadeWars.Engine.Data.Shape.Circle,
            });

        [Fact]
        public void MoneyAttacksChaseTheVictimWhoCanActuallyPay()
        {
            var game = ReadyToAct(21, 3);
            var s = game.State;
            int actor = s.ActivePlayer;
            var me = s.Players[actor];
            var others = s.Players.Where(p => p.PlayerId != actor).ToList();
            var broke = others[0];  // enumerated first: the old flat scoring picked this one
            var rich = others[1];

            // Taxes is the only playable card and there is no money to spend elsewhere,
            // so the whole decision is which victim to hit.
            StripToOneCard(game, actor, "taxes");
            me.Money = 0;
            // Equal stand counts => an equal NOMINAL steal ($10). Only the wallets differ,
            // and StealMoney caps the transfer at what the victim actually holds.
            while (broke.Stands.Count < 5) { AddStand(broke, "bargain"); }
            while (rich.Stands.Count < 5) { AddStand(rich, "bargain"); }
            broke.Money = 1;
            rich.Money = 20;

            var pick = Assert.IsType<PlayLemonCard>(new GreedyBot().Choose(game, actor));
            Assert.Equal("taxes", s.LemonInstances[pick.CardInstanceId].DefId);
            Assert.Equal(rich.PlayerId, pick.TargetPlayerId);
        }

        [Fact]
        public void MoneyAttacksOnABrokeVictimAreFilteredFromSearch()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben" }, seed: 5);
            var victim = game.State.Players[1];
            AddStand(victim, "bargain");

            int Card(string defId) =>
                game.State.LemonInstances.First(kv => kv.Value.DefId == defId).Key;

            var taxes = new PlayLemonCard { CardInstanceId = Card("taxes"), TargetPlayerId = 1 };
            victim.Money = 0;
            Assert.True(MoveFilters.ObjectivelyWasted(game, 0, taxes));   // $0 recovered = no-op
            victim.Money = 1;
            Assert.False(MoveFilters.ObjectivelyWasted(game, 0, taxes));  // bad target, not a wasted one

            // The other two cash grabs follow the same rule; card steals are untouched.
            foreach (string defId in new[] { "hoa-violation", "sharing-is-caring" })
            {
                var grab = new PlayLemonCard { CardInstanceId = Card(defId), TargetPlayerId = 1 };
                victim.Money = 0;
                Assert.True(MoveFilters.ObjectivelyWasted(game, 0, grab));
                victim.Money = 3;
                Assert.False(MoveFilters.ObjectivelyWasted(game, 0, grab));
            }
            victim.Money = 0;
            Assert.False(MoveFilters.ObjectivelyWasted(game, 0,
                new PlayLemonCard { CardInstanceId = Card("smear-campaign"), TargetPlayerId = 1 }));

            // The broke victim drops out of the shortlist; the paying one survives.
            var rich = new PlayLemonCard { CardInstanceId = Card("taxes"), TargetPlayerId = 0 };
            game.State.Players[0].Money = 8;
            AddStand(game.State.Players[0], "bargain");
            var kept = MoveFilters.DropWasted(game, 1, new GameAction[] { taxes, rich });
            Assert.Same(rich, Assert.Single(kept));
        }

        [Fact]
        public void RebrandProtectsFirstDibsStandProgress()
        {
            var game = ReadyToAct(23, 3);
            var s = game.State;
            int actor = s.ActivePlayer;
            var me = s.Players[actor];
            me.Money = 0;

            // Connoisseur (first to 3 Gourmet Stands and 1 other) is the only stand title
            // on offer, and we are two thirds of the way there.
            s.FirstDibsRow.Clear();
            s.FirstDibsRow.Add("connoisseur");
            me.Stands.Clear();
            AddStand(me, "gourmet");
            AddStand(me, "gourmet");
            AddStand(me, "classic");

            // Rebrand plus a hand of tantrums: instants are not playable in the Play
            // phase, so the hand is full (drawing is cheap) without adding rival plays.
            StripToOneCard(game, actor, "rebrand");
            for (int i = 0; i < 6; i++)
            {
                GiveCard(game, actor, "tantrum");
            }
            me.LemonLordKept.Clear();
            me.LemonLordKept.Add("friendly-fran");

            var pick = Assert.IsType<PlayLemonCard>(new GreedyBot().Choose(game, actor));
            Assert.Equal("rebrand", s.LemonInstances[pick.CardInstanceId].DefId);
            // Converts the odd classic stand INTO the third gourmet — never a gourmet away.
            Assert.Equal("gourmet", pick.NewStandTypeId);
            Assert.Equal("classic",
                me.Stands.First(st => st.InstanceId == pick.TargetStandInstanceId).StandTypeId);
        }

        [Fact]
        public void BotFactoryMapsLevels()
        {
            Assert.IsType<EasyBot>(BotFactory.Create("easy", 1));
            Assert.IsType<GreedyBot>(BotFactory.Create("medium", 1));
            Assert.IsType<SearchBot>(BotFactory.Create("hard", 1));
            Assert.IsType<SearchBot>(BotFactory.Create("wambulance", 1));
            Assert.Equal("wambulance", BotFactory.Normalize(" WAMBULANCE "));
            Assert.Equal("wambulance", BotFactory.Normalize("wambulence")); // legacy spelling from persisted rooms
            Assert.IsType<GreedyBot>(BotFactory.Create(null, 1));
            Assert.IsType<GreedyBot>(BotFactory.Create("HARD??", 1));
            Assert.Equal("hard", BotFactory.Normalize(" Hard "));
        }
    }
}
