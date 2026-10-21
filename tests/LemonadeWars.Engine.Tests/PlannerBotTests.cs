using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// The planner must finish games, stay deterministic, and see through the two
    /// blunders playtesters reported: hitting the wrong victim and sabotaging its own
    /// attack in a response window.
    /// </summary>
    public class PlannerBotTests
    {
        private static PlannerBot QuickPlanner(ulong seed = 1) => new PlannerBot(seed, budgetMs: 60, worlds: 1);

        [Fact]
        public void PlannerFinishesAFullGameAgainstGreedy()
        {
            var game = Game.Create(TestData.Db, new[] { "Plan", "G1", "G2" }, 4242);
            var bots = new Dictionary<int, IBot>
            {
                [0] = QuickPlanner(),
                [1] = new GreedyBot(),
                [2] = new GreedyBot(),
            };
            GameRunner.PlayOut(game, bots);
            Assert.Equal(GameStage.Finished, game.State.Stage);
            Assert.NotEmpty(game.State.Winners);
        }

        [Fact]
        public void PlannerIsDeterministicForASeed()
        {
            GameAction Pick()
            {
                var game = BotScenario.ReadyToAct(77, 4);
                return QuickPlanner(9).Choose(game, game.State.ActivePlayer);
            }
            Assert.Equal(Newtonsoft.Json.JsonConvert.SerializeObject(Pick()),
                Newtonsoft.Json.JsonConvert.SerializeObject(Pick()));
        }

        [Fact]
        public void TaxesHitTheLeaderNotTheRichLoser()
        {
            var game = BotScenario.ReadyToAct(21, 3);
            var s = game.State;
            int actor = s.ActivePlayer;
            var others = s.Players.Where(p => p.PlayerId != actor).ToList();
            var loser = others[0];
            var leader = others[1];
            BotScenario.StripToOneCard(game, actor, "taxes");
            s.Players[actor].Money = 0;
            foreach (var p in s.Players)
            {
                p.LemonLordKept.Clear();
                p.Hand.RemoveAll(id => id != s.Players[actor].Hand.FirstOrDefault() &&
                    p.PlayerId != actor && IsReaction(game, id));
            }
            // Equal nominal steal ($6 each: 3 stands, both can pay), but one of them
            // is two VP up and about to win.
            while (loser.Stands.Count < 3) { BotScenario.AddStand(loser, "bargain"); }
            while (leader.Stands.Count < 3) { BotScenario.AddStand(leader, "bargain"); }
            loser.Money = 12;
            leader.Money = 6;
            leader.BraggingRights = 2;

            var pick = Assert.IsType<PlayLemonCard>(QuickPlanner().Choose(game, actor));
            Assert.Equal(leader.PlayerId, pick.TargetPlayerId);
        }

        [Fact]
        public void TantrumsTheReflectionThatWouldTurnItsOwnAttackAround()
        {
            var game = BotScenario.ReadyToAct(33, 3);
            var s = game.State;
            int a = s.ActivePlayer;
            int b = (a + 1) % 3;
            BotScenario.StripAllHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue");
            foreach (var p in s.Players)
            {
                p.LemonLordKept.Clear();
            }
            int attack = BotScenario.Hold(game, a, "finders-keepers");
            int tantrum = BotScenario.Hold(game, a, "tantrum");
            int rubber = BotScenario.Hold(game, b, "im-rubber-youre-glue");
            // Something worth stealing on B's turf, and something on A's for the bounce.
            int loot = GiveTurfCard(game, b, "diluted-lemonade");
            GiveTurfCard(game, a, "diluted-lemonade");

            game.Apply(new PlayLemonCard
            {
                PlayerId = a,
                CardInstanceId = attack,
                TargetPlayerId = b,
                TargetEquippedInstanceId = loot,
                EquipStandInstanceId = s.Players[a].Stands[1].InstanceId,
            });
            game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = rubber });
            Assert.Contains(a, s.AwaitingResponse);

            var pick = Assert.IsType<RespondToWindow>(QuickPlanner().Choose(game, a));
            Assert.Equal(tantrum, pick.CardInstanceId);
        }

        private static bool IsReaction(Game game, int instanceId)
        {
            string defId = game.State.LemonInstances[instanceId].DefId;
            return defId == "tantrum" || defId == "tag-youre-it" || defId == "im-rubber-youre-glue";
        }

        private static int GiveTurfCard(Game game, int playerId, string defId)
        {
            var s = game.State;
            int id = s.BlackMarketDeck.First(i => s.BlackMarketInstances[i].DefId == defId);
            s.BlackMarketDeck.Remove(id);
            var stand = s.Players[playerId].Stands[0];
            stand.Equipped.Add(id);
            return id;
        }
    }
}
