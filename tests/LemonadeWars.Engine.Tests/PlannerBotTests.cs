using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Newtonsoft.Json;
using Xunit;
using static LemonadeWars.Engine.Tests.BotScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// The planner must finish games, stay deterministic, watch its clock, and see
    /// through the two blunders playtesters reported: hitting the wrong victim and
    /// sabotaging its own attack in a response window.
    /// </summary>
    public class PlannerBotTests
    {
        private static PlannerBot QuickPlanner(ulong seed = 1) => new PlannerBot(seed, new PlannerConfig { BudgetMs = 60, Worlds = 1 });

        // ------------------------------------------------------------ budget

        [Fact]
        public void HardPlannerStaysNearItsBudgetWithABigHand()
        {
            // Regression: the planner once valued every root move in full before it
            // looked at its clock, and a late-game hand froze the bot for seconds.
            var game = LateGamePlayPhaseOfSeatZero();
            Assert.NotNull(game);
            DealTheWidestHandToSeatZero(game);
            int moveCount = game.LegalMovesFor(0).Count;
            Assert.True(moveCount > 40, $"the hand only produced {moveCount} moves");

            var bot = new PlannerBot(1, new PlannerConfig
            {
                BudgetMs = 300, Worlds = 6, RootBeam = 6, RoundLookahead = true,
            });
            var stopwatch = Stopwatch.StartNew();
            bot.Choose(game, 0);
            Assert.True(stopwatch.ElapsedMilliseconds < 2500,
                $"a 300ms budget over {moveCount} moves took {stopwatch.ElapsedMilliseconds}ms: the planner is not watching its clock");
        }

        /// <summary>
        /// A copy of a greedy game at the LAST moment seat 0 had its own Play phase with
        /// nothing pending: boards full of stands and upgrades. Null if it never did.
        /// </summary>
        private static Game? LateGamePlayPhaseOfSeatZero()
        {
            var played = Game.Create(TestData.Db, new[] { "Plan", "G1", "G2", "G3" }, 4242);
            var greedy = new GreedyBot();
            Game? snapshot = null;
            int applied = 0;
            while (played.State.Stage != GameStage.Finished && applied++ < 2000)
            {
                var state = played.State;
                if (state.Stage == GameStage.Playing && state.Phase == TurnPhase.Play && state.ActivePlayer == 0 &&
                    state.ActionsRemaining >= 2 &&
                    state.PendingDecisions.Count == 0 && state.AwaitingResponse.Count == 0)
                {
                    snapshot = Game.FromState(TestData.Db, SearchTools.CloneState(state));
                }
                int actor = played.ActingPlayers()[0];
                played.Apply(greedy.Choose(played, actor));
            }
            return snapshot;
        }

        /// <summary>
        /// The widest cards there are: every steal times every tucked card times every
        /// destination, every attack times every victim, every Rummage combo.
        /// </summary>
        private static void DealTheWidestHandToSeatZero(Game game)
        {
            var state = game.State;
            var wanted = new[]
            {
                "finders-keepers", "finders-keepers", "finders-keepers",
                "thats-not-fair", "thats-not-fair", "thats-not-fair", "thats-not-fair",
                "rummage-sale", "rummage-sale", "reduce-and-reuse", "reduce-and-reuse",
                "taxes", "taxes", "hoa-violation",
            };
            var hand = state.Players[0].Hand;
            hand.Clear();
            var taken = new HashSet<int>();
            foreach (string defId in wanted)
            {
                int id = state.LemonInstances.Values
                    .First(card => card.DefId == defId && !taken.Contains(card.InstanceId)).InstanceId;
                taken.Add(id);
                state.LemonDeck.Remove(id);
                state.LemonDiscard.Remove(id);
                foreach (var player in state.Players)
                {
                    player.Hand.Remove(id);
                }
                hand.Add(id);
            }
        }

        // -------------------------------------------------------- full games

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
        public void EveryResearchOptionSurvivesAFullGame()
        {
            // Sampled reactions, averaged rolls and evaluator-driven rivals all run extra
            // engine code inside plans; a wedge there shows as a stalled or thrown game.
            var config = new PlannerConfig
            {
                BudgetMs = 40,
                Worlds = 2,
                RootBeam = 3,
                RoundLookahead = true,
                SampledReactions = true,
                RollSamples = 2,
                EvaluatorRivals = true,
            };
            var game = Game.Create(TestData.Db, new[] { "Wam", "G1", "G2", "G3" }, 2468);
            var bots = new Dictionary<int, IBot>
            {
                [0] = new PlannerBot(3, config),
                [1] = new GreedyBot(),
                [2] = new GreedyBot(),
                [3] = new GreedyBot(),
            };
            GameRunner.PlayOut(game, bots);
            Assert.Equal(GameStage.Finished, game.State.Stage);
        }

        [Fact]
        public void PlannerIsDeterministicForASeed()
        {
            GameAction Pick()
            {
                var game = ReadyToAct(77, 4);
                return QuickPlanner(9).Choose(game, game.State.ActivePlayer);
            }
            Assert.Equal(JsonConvert.SerializeObject(Pick()),
                JsonConvert.SerializeObject(Pick()));
        }

        // ---------------------------------------------------------- blunders

        [Fact]
        public void TaxesHitTheLeaderNotTheRichLoser()
        {
            var game = ReadyToAct(21, 3);
            var state = game.State;
            int actor = state.ActivePlayer;
            var others = state.Players.Where(p => p.PlayerId != actor).ToList();
            var loser = others[0];
            var leader = others[1];
            StripToOneCard(game, actor, "taxes");
            state.Players[actor].Money = 0;
            foreach (var player in state.Players)
            {
                player.LemonLordKept.Clear();
                player.Hand.RemoveAll(id => id != state.Players[actor].Hand.FirstOrDefault() &&
                    player.PlayerId != actor && IsReaction(game, id));
            }
            // Equal nominal steal ($6 each: 3 stands, both can pay), but one of them
            // is two VP up and about to win.
            while (loser.Stands.Count < 3) { AddStand(loser, "bargain"); }
            while (leader.Stands.Count < 3) { AddStand(leader, "bargain"); }
            loser.Money = 12;
            leader.Money = 6;
            leader.BraggingRights = 2;

            var pick = Assert.IsType<PlayLemonCard>(QuickPlanner().Choose(game, actor));
            Assert.Equal(leader.PlayerId, pick.TargetPlayerId);
        }

        [Fact]
        public void TantrumsTheReflectionThatWouldTurnItsOwnAttackAround()
        {
            var game = ReadyToAct(33, 3);
            var state = game.State;
            int attacker = state.ActivePlayer;
            int victim = (attacker + 1) % 3;
            StripReactionCards(game);
            ClearLemonLords(game);
            int attack = Hold(game, attacker, "finders-keepers");
            int tantrum = Hold(game, attacker, "tantrum");
            int rubberGlue = Hold(game, victim, "im-rubber-youre-glue");
            // Something worth stealing on the victim's first stand, and something on the
            // attacker's for the bounce.
            int loot = EquipOnFirstStand(game, victim, "diluted-lemonade");
            EquipOnFirstStand(game, attacker, "diluted-lemonade");

            game.Apply(new PlayLemonCard
            {
                PlayerId = attacker,
                CardInstanceId = attack,
                TargetPlayerId = victim,
                TargetEquippedInstanceId = loot,
                EquipStandInstanceId = state.Players[attacker].Stands[1].InstanceId,
            });
            game.Apply(new RespondToWindow { PlayerId = victim, CardInstanceId = rubberGlue });
            Assert.Contains(attacker, state.AwaitingResponse);

            var pick = Assert.IsType<RespondToWindow>(QuickPlanner().Choose(game, attacker));
            Assert.Equal(tantrum, pick.CardInstanceId);
        }

        private static bool IsReaction(Game game, int instanceId)
        {
            string defId = game.State.LemonInstances[instanceId].DefId;
            return defId == "tantrum" || defId == "tag-youre-it" || defId == "im-rubber-youre-glue";
        }

        private static int EquipOnFirstStand(Game game, int playerId, string defId)
        {
            var player = game.State.Players[playerId];
            return EquipFromDeck(game, player, defId, player.Stands[0]);
        }
    }
}
