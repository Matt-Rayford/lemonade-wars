using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.BotScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Visibly dumb moves playtesters caught the medium bot making, each pinned as a
    /// scripted position: the only sensible choice must win on score, not on luck of
    /// enumeration order.
    /// </summary>
    public class GreedyBlunderTests
    {
        private static void ClearNoise(Game game, PlayerState me)
        {
            game.State.FirstDibsRow.Clear();
            me.LemonLordKept.Clear();
        }

        // ------------------------------------------------------- attack targeting

        [Fact]
        public void TaxesHitsTheLeaderNotTheRichLoser()
        {
            var game = ReadyToAct(41, 3);
            var s = game.State;
            int actor = s.ActivePlayer;
            var me = s.Players[actor];
            var others = s.Players.Where(p => p.PlayerId != actor).ToList();
            var richLoser = others[0]; // enumerated first, and the bigger nominal steal
            var leader = others[1];
            ClearNoise(game, me);
            StripToOneCard(game, actor, "taxes");
            me.Money = 0;

            // $10 off the loser vs $6 off the leader — but the leader is one VP from
            // ending the game, and the loser is nowhere.
            while (richLoser.Stands.Count < 5) { AddStand(richLoser, "bargain"); }
            richLoser.Money = 12;
            while (leader.Stands.Count < 3) { AddStand(leader, "bargain"); }
            leader.Money = 6;
            leader.BraggingRights = 2;
            BuryHand(game, richLoser.PlayerId);
            BuryHand(game, leader.PlayerId);

            var pick = Assert.IsType<PlayLemonCard>(new GreedyBot().Choose(game, actor));
            Assert.Equal("taxes", s.LemonInstances[pick.CardInstanceId].DefId);
            Assert.Equal(leader.PlayerId, pick.TargetPlayerId);
        }

        [Fact]
        public void EqualThreatsFavorTheDefenselessVictim()
        {
            var game = ReadyToAct(42, 3);
            var s = game.State;
            int actor = s.ActivePlayer;
            var me = s.Players[actor];
            var others = s.Players.Where(p => p.PlayerId != actor).ToList();
            var fatHand = others[0]; // enumerated first: a tie would pick this one
            var emptyHand = others[1];
            ClearNoise(game, me);
            StripToOneCard(game, actor, "taxes");
            me.Money = 0;

            // Identical public standing; only the hands differ.
            foreach (var victim in others)
            {
                while (victim.Stands.Count < 5) { AddStand(victim, "bargain"); }
                victim.Money = 10;
            }
            while (fatHand.Hand.Count < 7) { GiveCard(game, fatHand.PlayerId, "tantrum"); }
            BuryHand(game, emptyHand.PlayerId);

            var pick = Assert.IsType<PlayLemonCard>(new GreedyBot().Choose(game, actor));
            Assert.Equal("taxes", s.LemonInstances[pick.CardInstanceId].DefId);
            Assert.Equal(emptyHand.PlayerId, pick.TargetPlayerId);
        }

        // ------------------------------------------------- response windows

        [Fact]
        public void TantrumCountersARubberGlueOnItsOwnAttack()
        {
            var game = ReadyToAct(43, 3);
            var s = game.State;
            int a = s.ActivePlayer;
            int b = (a + 1) % 3;
            StripAllHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue");
            foreach (var p in s.Players) { p.LemonLordKept.Clear(); }

            int finders = Hold(game, a, "finders-keepers");
            int tantrumA = Hold(game, a, "tantrum");
            int rubber = Hold(game, b, "im-rubber-youre-glue");
            EquipFromDeck(game, s.Players[b], "pink-lemonade", s.Players[b].Stands[0]);

            var steal = game.LegalMovesFor(a).OfType<PlayLemonCard>()
                .First(p => p.CardInstanceId == finders && p.TargetPlayerId == b);
            game.Apply(steal);
            game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = rubber });
            Assert.Contains(a, s.AwaitingResponse);

            // Passing here lets the reflection resolve and the steal turn on A itself.
            var keepAlive = new RespondToWindow { PlayerId = a, CardInstanceId = tantrumA };
            Assert.False(MoveFilters.ObjectivelyWasted(game, a, keepAlive));
            var pick = Assert.IsType<RespondToWindow>(new GreedyBot().Choose(game, a));
            Assert.Equal(tantrumA, pick.CardInstanceId);
        }

        [Fact]
        public void TantrumCountersATagOnItsOwnAttack()
        {
            var game = ReadyToAct(44, 3);
            var s = game.State;
            int a = s.ActivePlayer;
            int b = (a + 1) % 3;
            int c = (a + 2) % 3;
            StripAllHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue");
            foreach (var p in s.Players) { p.LemonLordKept.Clear(); }

            int attack = Hold(game, a, "hoa-violation");
            int tantrumA = Hold(game, a, "tantrum");
            int tag = Hold(game, b, "tag-youre-it");
            s.Players[b].Money = 10;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = attack, TargetPlayerId = b });
            game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = tag, RedirectTargetId = c });
            Assert.Contains(a, s.AwaitingResponse);

            var keepAlive = new RespondToWindow { PlayerId = a, CardInstanceId = tantrumA };
            Assert.False(MoveFilters.ObjectivelyWasted(game, a, keepAlive));
            var pick = Assert.IsType<RespondToWindow>(new GreedyBot().Choose(game, a));
            Assert.Equal(tantrumA, pick.CardInstanceId);
        }

        [Fact]
        public void TagRedirectsOntoTheLeader()
        {
            var game = ReadyToAct(48, 4);
            var s = game.State;
            int a = s.ActivePlayer;
            int b = (a + 1) % 4;
            int c = (a + 2) % 4;
            int d = (a + 3) % 4;
            StripAllHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue");
            foreach (var p in s.Players) { p.LemonLordKept.Clear(); }

            // Redirect candidates enumerate by player id: make the LATER one the leader.
            var leader = s.Players[System.Math.Max(c, d)];
            var straggler = s.Players[System.Math.Min(c, d)];
            leader.BraggingRights = 2;
            straggler.BraggingRights = 0;

            int attack = Hold(game, a, "hoa-violation");
            int tag = Hold(game, b, "tag-youre-it");
            s.Players[b].Money = 10;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = attack, TargetPlayerId = b });
            Assert.Contains(b, s.AwaitingResponse);

            var pick = Assert.IsType<RespondToWindow>(new GreedyBot().Choose(game, b));
            Assert.Equal(tag, pick.CardInstanceId);
            Assert.Equal(leader.PlayerId, pick.RedirectTargetId);
        }

        // ------------------------------------------------------- decisions

        [Fact]
        public void PickCardTakesTheMostValuableCard()
        {
            var game = ReadyToAct(45, 3);
            var s = game.State;
            int actor = s.ActivePlayer;
            var me = s.Players[actor];
            var victim = s.Players.First(p => p.PlayerId != actor);
            ClearNoise(game, me);

            // The revealed hand: a throwaway plan first, the tantrum second.
            BuryHand(game, victim.PlayerId);
            int rummage = Hold(game, victim.PlayerId, "rummage-sale");
            int tantrum = Hold(game, victim.PlayerId, "tantrum");
            s.PendingDecisions.Add(new PendingDecision
            {
                PlayerId = actor,
                Kind = DecisionKind.AbilityPickCard,
                ChosenPlayerId = victim.PlayerId,
            });

            var pick = Assert.IsType<SubmitAbilityChoice>(new GreedyBot().Choose(game, actor));
            Assert.Equal(tantrum, Assert.Single(pick.CardInstanceIds));
            Assert.NotEqual(rummage, pick.CardInstanceIds[0]);
        }

        [Fact]
        public void WordOfMouthSellsTheBestEarningStand()
        {
            var game = ReadyToAct(46, 3);
            var s = game.State;
            int actor = s.ActivePlayer;
            var me = s.Players[actor];
            ClearNoise(game, me);

            me.Stands.Clear();
            AddStand(me, "bargain"); // $1 a sale, enumerated first
            var gourmet = AddStand(me, "gourmet"); // $3 a sale
            s.PendingDecisions.Add(new PendingDecision
            {
                PlayerId = actor,
                Kind = DecisionKind.WordOfMouthStand,
            });

            var pick = Assert.IsType<SubmitAbilityChoice>(new GreedyBot().Choose(game, actor));
            Assert.Equal(gourmet.InstanceId, pick.StandInstanceId);
        }

        [Fact]
        public void RetargetTakesTheBestLoot()
        {
            var game = ReadyToAct(47, 3);
            var s = game.State;
            int actor = s.ActivePlayer;
            var me = s.Players[actor];
            var victim = s.Players.First(p => p.PlayerId != actor);
            ClearNoise(game, me);

            // Our Finders Keepers was Tagged onto a new victim holding a Pushy Salesman
            // (enumerated first) and a Diluted Lemonade (the card we actually want).
            victim.Stands.Clear();
            int pushy = EquipFromDeck(game, victim, "pushy-salesman-1", AddStand(victim, "classic"));
            int diluted = EquipFromDeck(game, victim, "diluted-lemonade", AddStand(victim, "classic"));
            const int attackItemId = 777;
            s.ResponseStack.Add(new StackItem
            {
                ItemId = attackItemId,
                Kind = StackItemKind.LemonPlay,
                OwnerId = actor,
                LemonDefId = "finders-keepers",
                AttackTargetId = victim.PlayerId,
            });
            s.PendingDecisions.Add(new PendingDecision
            {
                PlayerId = actor,
                Kind = DecisionKind.AttackRetarget,
                StackItemId = attackItemId,
            });

            var pick = Assert.IsType<SubmitRetarget>(new GreedyBot().Choose(game, actor));
            Assert.Equal(diluted, pick.TargetEquippedInstanceId);
            Assert.NotEqual(pushy, pick.TargetEquippedInstanceId);
        }

        // --------------------------------------------------------- endgame

        [Fact]
        public void NearGameEndPrefersBraggingRightsOverAStand()
        {
            var game = ReadyToAct(49, 3);
            var s = game.State;
            int actor = s.ActivePlayer;
            var me = s.Players[actor];
            var rival = s.Players.First(p => p.PlayerId != actor);
            ClearNoise(game, me);

            // One stand and exactly the first Bragging Rights price: mid-game the bot
            // would rather grow the economy, but the rival sits one VP from the end.
            rival.BraggingRights = 2;
            me.Stands.Clear();
            AddStand(me, "bargain");
            me.Money = 16;
            s.BraggingRightsSold = 0;
            s.Market.Clear();
            BuryHand(game, actor);

            Assert.IsType<BuyBraggingRights>(new GreedyBot().Choose(game, actor));
        }
    }
}
