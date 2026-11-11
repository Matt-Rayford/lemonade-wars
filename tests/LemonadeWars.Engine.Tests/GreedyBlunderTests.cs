using System;
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
        public void MoneyAttacksChaseTheVictimWhoCanActuallyPay()
        {
            var game = ReadyToAct(21, 3);
            var state = game.State;
            int actor = state.ActivePlayer;
            var me = state.Players[actor];
            var others = state.Players.Where(p => p.PlayerId != actor).ToList();
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
            Assert.Equal("taxes", state.LemonInstances[pick.CardInstanceId].DefId);
            Assert.Equal(rich.PlayerId, pick.TargetPlayerId);
        }

        [Fact]
        public void TaxesHitsTheLeaderNotTheRichLoser()
        {
            var game = ReadyToAct(41, 3);
            var state = game.State;
            int actor = state.ActivePlayer;
            var me = state.Players[actor];
            var others = state.Players.Where(p => p.PlayerId != actor).ToList();
            var richLoser = others[0]; // enumerated first, and the bigger nominal steal
            var leader = others[1];
            ClearNoise(game, me);
            StripToOneCard(game, actor, "taxes");
            me.Money = 0;

            // $10 off the loser vs $6 off the leader, but the leader is one VP from
            // ending the game and the loser is nowhere.
            while (richLoser.Stands.Count < 5) { AddStand(richLoser, "bargain"); }
            richLoser.Money = 12;
            while (leader.Stands.Count < 3) { AddStand(leader, "bargain"); }
            leader.Money = 6;
            leader.BraggingRights = 2;
            BuryHand(game, richLoser.PlayerId);
            BuryHand(game, leader.PlayerId);

            var pick = Assert.IsType<PlayLemonCard>(new GreedyBot().Choose(game, actor));
            Assert.Equal("taxes", state.LemonInstances[pick.CardInstanceId].DefId);
            Assert.Equal(leader.PlayerId, pick.TargetPlayerId);
        }

        [Fact]
        public void EqualThreatsFavorTheDefenselessVictim()
        {
            var game = ReadyToAct(42, 3);
            var state = game.State;
            int actor = state.ActivePlayer;
            var me = state.Players[actor];
            var others = state.Players.Where(p => p.PlayerId != actor).ToList();
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
            Assert.Equal("taxes", state.LemonInstances[pick.CardInstanceId].DefId);
            Assert.Equal(emptyHand.PlayerId, pick.TargetPlayerId);
        }

        // ------------------------------------------------------------- plans

        [Fact]
        public void RebrandProtectsFirstDibsStandProgress()
        {
            var game = ReadyToAct(23, 3);
            var state = game.State;
            int actor = state.ActivePlayer;
            var me = state.Players[actor];
            me.Money = 0;

            // Connoisseur (first to 3 Gourmet Stands and 1 other) is the only stand title
            // on offer, and we are two thirds of the way there.
            state.FirstDibsRow.Clear();
            state.FirstDibsRow.Add("connoisseur");
            me.Stands.Clear();
            AddStand(me, "gourmet");
            AddStand(me, "gourmet");
            AddStand(me, "classic");

            // Tantrums fill the hand (so drawing is cheap) without adding rival plays:
            // instants are not playable in the Play phase.
            StripToOneCard(game, actor, "rebrand");
            for (int i = 0; i < 6; i++)
            {
                GiveCard(game, actor, "tantrum");
            }
            me.LemonLordKept.Clear();
            me.LemonLordKept.Add("friendly-fran");

            var pick = Assert.IsType<PlayLemonCard>(new GreedyBot().Choose(game, actor));
            Assert.Equal("rebrand", state.LemonInstances[pick.CardInstanceId].DefId);
            // Converts the odd classic stand INTO the third gourmet, never a gourmet away.
            Assert.Equal("gourmet", pick.NewStandTypeId);
            Assert.Equal("classic",
                me.Stands.First(stand => stand.InstanceId == pick.TargetStandInstanceId).StandTypeId);
        }

        // ------------------------------------------------- response windows

        [Fact]
        public void TantrumCountersARubberGlueOnItsOwnAttack()
        {
            var game = ReadyToAct(43, 3);
            var state = game.State;
            int attacker = state.ActivePlayer;
            int victim = (attacker + 1) % 3;
            StripReactionCards(game);
            ClearLemonLords(game);

            int findersKeepers = Hold(game, attacker, "finders-keepers");
            int attackerTantrum = Hold(game, attacker, "tantrum");
            int rubberGlue = Hold(game, victim, "im-rubber-youre-glue");
            EquipFromDeck(game, state.Players[victim], "pink-lemonade", state.Players[victim].Stands[0]);

            var steal = game.LegalMovesFor(attacker).OfType<PlayLemonCard>()
                .First(play => play.CardInstanceId == findersKeepers && play.TargetPlayerId == victim);
            game.Apply(steal);
            game.Apply(new RespondToWindow { PlayerId = victim, CardInstanceId = rubberGlue });
            Assert.Contains(attacker, state.AwaitingResponse);

            // Passing here lets the reflection resolve and the steal turn on the attacker.
            var keepAlive = new RespondToWindow { PlayerId = attacker, CardInstanceId = attackerTantrum };
            Assert.False(MoveFilters.ObjectivelyWasted(game, attacker, keepAlive));
            var pick = Assert.IsType<RespondToWindow>(new GreedyBot().Choose(game, attacker));
            Assert.Equal(attackerTantrum, pick.CardInstanceId);
        }

        [Fact]
        public void TantrumCountersATagOnItsOwnAttack()
        {
            var game = ReadyToAct(44, 3);
            var state = game.State;
            int attacker = state.ActivePlayer;
            int victim = (attacker + 1) % 3;
            int redirectedTo = (attacker + 2) % 3;
            StripReactionCards(game);
            ClearLemonLords(game);

            int attack = Hold(game, attacker, "hoa-violation");
            int attackerTantrum = Hold(game, attacker, "tantrum");
            int tag = Hold(game, victim, "tag-youre-it");
            state.Players[victim].Money = 10;

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });
            game.Apply(new RespondToWindow { PlayerId = victim, CardInstanceId = tag, RedirectTargetId = redirectedTo });
            Assert.Contains(attacker, state.AwaitingResponse);

            var keepAlive = new RespondToWindow { PlayerId = attacker, CardInstanceId = attackerTantrum };
            Assert.False(MoveFilters.ObjectivelyWasted(game, attacker, keepAlive));
            var pick = Assert.IsType<RespondToWindow>(new GreedyBot().Choose(game, attacker));
            Assert.Equal(attackerTantrum, pick.CardInstanceId);
        }

        [Fact]
        public void TagRedirectsOntoTheLeader()
        {
            var game = ReadyToAct(48, 4);
            var state = game.State;
            int attacker = state.ActivePlayer;
            int victim = (attacker + 1) % 4;
            int thirdSeat = (attacker + 2) % 4;
            int fourthSeat = (attacker + 3) % 4;
            StripReactionCards(game);
            ClearLemonLords(game);

            // Redirect candidates enumerate by player id: make the LATER one the leader.
            var leader = state.Players[Math.Max(thirdSeat, fourthSeat)];
            var straggler = state.Players[Math.Min(thirdSeat, fourthSeat)];
            leader.BraggingRights = 2;
            straggler.BraggingRights = 0;

            int attack = Hold(game, attacker, "hoa-violation");
            int tag = Hold(game, victim, "tag-youre-it");
            state.Players[victim].Money = 10;

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });
            Assert.Contains(victim, state.AwaitingResponse);

            var pick = Assert.IsType<RespondToWindow>(new GreedyBot().Choose(game, victim));
            Assert.Equal(tag, pick.CardInstanceId);
            Assert.Equal(leader.PlayerId, pick.RedirectTargetId);
        }

        // ------------------------------------------------------- decisions

        [Fact]
        public void PickCardTakesTheMostValuableCard()
        {
            var game = ReadyToAct(45, 3);
            var state = game.State;
            int actor = state.ActivePlayer;
            var me = state.Players[actor];
            var victim = state.Players.First(p => p.PlayerId != actor);
            ClearNoise(game, me);

            // The revealed hand: a throwaway plan first, the tantrum second.
            BuryHand(game, victim.PlayerId);
            int rummage = Hold(game, victim.PlayerId, "rummage-sale");
            int tantrum = Hold(game, victim.PlayerId, "tantrum");
            state.PendingDecisions.Add(new PendingDecision
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
            var state = game.State;
            int actor = state.ActivePlayer;
            var me = state.Players[actor];
            ClearNoise(game, me);

            me.Stands.Clear();
            AddStand(me, "bargain"); // $1 a sale, enumerated first
            var gourmet = AddStand(me, "gourmet"); // $3 a sale
            state.PendingDecisions.Add(new PendingDecision
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
            var state = game.State;
            int actor = state.ActivePlayer;
            var me = state.Players[actor];
            var victim = state.Players.First(p => p.PlayerId != actor);
            ClearNoise(game, me);

            // Our Finders Keepers was Tagged onto a new victim holding a Pushy Salesman
            // (enumerated first) and a Diluted Lemonade (the card we actually want).
            victim.Stands.Clear();
            int pushy = EquipFromDeck(game, victim, "pushy-salesman-1", AddStand(victim, "classic"));
            int diluted = EquipFromDeck(game, victim, "diluted-lemonade", AddStand(victim, "classic"));
            const int attackItemId = 777;
            state.ResponseStack.Add(new StackItem
            {
                ItemId = attackItemId,
                Kind = StackItemKind.LemonPlay,
                OwnerId = actor,
                LemonDefId = "finders-keepers",
                AttackTargetId = victim.PlayerId,
            });
            state.PendingDecisions.Add(new PendingDecision
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
            var state = game.State;
            int actor = state.ActivePlayer;
            var me = state.Players[actor];
            var rival = state.Players.First(p => p.PlayerId != actor);
            ClearNoise(game, me);

            // One stand and exactly the first Bragging Rights price: mid-game the bot
            // would rather grow the economy, but the rival sits one VP from the end.
            rival.BraggingRights = 2;
            me.Stands.Clear();
            AddStand(me, "bargain");
            me.Money = 16;
            state.BraggingRightsSold = 0;
            state.Market.Clear();
            BuryHand(game, actor);

            Assert.IsType<BuyBraggingRights>(new GreedyBot().Choose(game, actor));
        }
    }
}
