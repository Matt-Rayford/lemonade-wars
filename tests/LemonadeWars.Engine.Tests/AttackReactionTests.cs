using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;
using static LemonadeWars.Engine.Tests.StateRigging;

namespace LemonadeWars.Engine.Tests
{
    public class AttackReactionTests
    {
        [Fact]
        public void HoaViolationStealsFive()
        {
            var game = ReadyToPlay(seed: 7);
            StripAttackReactions(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int card = GiveCard(game, attacker, "hoa-violation");
            game.State.Players[attacker].Money = 20;
            game.State.Players[victim].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = card, TargetPlayerId = victim });

            Assert.Equal(25, game.State.Players[attacker].Money);
            Assert.Equal(15, game.State.Players[victim].Money);
        }

        [Fact]
        public void TagMovesTheAttackToAnotherPlayer()
        {
            var game = ReadyToPlay(seed: 7);
            StripAttackReactions(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int tagger = SeatAfterActive(game, 2);
            int newVictim = SeatAfterActive(game, 3);
            int attack = GiveCard(game, attacker, "hoa-violation");
            int tag = GiveCard(game, tagger, "tag-youre-it");
            SetEveryonesMoney(game, 20);

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });
            // Card text: the attack cannot be tagged onto the attacker.
            Assert.Throws<InvalidActionException>(() => game.Apply(
                new RespondToWindow { PlayerId = tagger, CardInstanceId = tag, RedirectTargetId = attacker }));
            game.Apply(new RespondToWindow { PlayerId = tagger, CardInstanceId = tag, RedirectTargetId = newVictim });
            PassAll(game);

            Assert.Equal(25, game.State.Players[attacker].Money);
            Assert.Equal(20, game.State.Players[victim].Money); // dodged
            Assert.Equal(15, game.State.Players[newVictim].Money); // tagged
        }

        [Fact]
        public void RubberGlueReflectsTheAttack()
        {
            var game = ReadyToPlay(seed: 7);
            StripAttackReactions(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int attack = GiveCard(game, attacker, "hoa-violation");
            int rubber = GiveCard(game, victim, "im-rubber-youre-glue");
            game.State.Players[attacker].Money = 20;
            game.State.Players[victim].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });
            game.Apply(new RespondToWindow { PlayerId = victim, CardInstanceId = rubber });
            PassAll(game);

            Assert.Equal(15, game.State.Players[attacker].Money);
            Assert.Equal(25, game.State.Players[victim].Money);
        }

        [Fact]
        public void TwoPlayerTagDiscardsTheAttack()
        {
            var game = ReadyToPlay(seed: 7, names: TwoPlayers);
            StripAttackReactions(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int attack = GiveCard(game, attacker, "hoa-violation");
            int tag = GiveCard(game, victim, "tag-youre-it");
            game.State.Players[attacker].Money = 20;
            game.State.Players[victim].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });
            game.Apply(new RespondToWindow { PlayerId = victim, CardInstanceId = tag });
            PassAll(game);

            // 2-player rule: with nobody to move it to, the attack is discarded.
            Assert.Equal(20, game.State.Players[attacker].Money);
            Assert.Equal(20, game.State.Players[victim].Money);
            Assert.Contains(attack, game.State.LemonDiscard);
        }

        [Fact]
        public void ProfitShareRecoversHalfRoundedUp()
        {
            var game = ReadyToPlay(seed: 7);
            StripAttackReactions(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int attack = GiveCard(game, attacker, "sharing-is-caring");
            int profitShare = GiveCard(game, victim, "profit-share");
            game.State.Players[attacker].Money = 20;
            game.State.Players[victim].Money = 17; // steal ceil(17/2) = 9

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });
            // The theft has resolved; the window now open is the victim's Profit Share.
            Assert.Equal(new[] { victim }, game.State.AwaitingResponse);
            game.Apply(new RespondToWindow { PlayerId = victim, CardInstanceId = profitShare });

            // Recovers ceil(9/2) = 5: attacker 20 + 9 - 5 = 24, victim 17 - 9 + 5 = 13.
            Assert.Equal(24, game.State.Players[attacker].Money);
            Assert.Equal(13, game.State.Players[victim].Money);
        }

        [Fact]
        public void ProfitShareWindowOpensAfterPassingTheAttackWindow()
        {
            var game = ReadyToPlay(seed: 7);
            StripAttackReactions(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            game.State.Players[attacker].Money = 20;
            game.State.Players[victim].Money = 10;
            int attack = GiveCard(game, attacker, "taxes");
            GiveCard(game, victim, "tantrum"); // makes the victim eligible for the ATTACK window first
            int profitShare = GiveCard(game, victim, "profit-share");

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });
            Assert.Contains(victim, game.State.AwaitingResponse); // the attack window
            game.Apply(new PassWindow { PlayerId = victim });     // decline to tantrum

            // Passing the attack window must not consume the later, different THEFT window:
            // Taxes has stolen, and Profit Share is on offer.
            Assert.NotEmpty(game.State.TheftQueue);
            Assert.Contains(victim, game.State.AwaitingResponse);
            Assert.Contains(game.LegalMovesFor(victim),
                m => m is RespondToWindow respond && respond.CardInstanceId == profitShare);
        }
    }
}
