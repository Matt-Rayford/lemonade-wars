using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;
using static LemonadeWars.Engine.Tests.StateRigging;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>Cards that move Tantrums between piles, and who holds the Whiniest Baby afterwards.</summary>
    public class TantrumPileTests
    {
        [Fact]
        public void ApologizeCannotBeTantrummedAndClearsATantrum()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum");
            int player = ActivePlayer(game);
            int armedRival = SeatAfterActive(game, 1);
            GiveCard(game, armedRival, "tantrum");
            int pileTantrum = PutTantrumInPile(game, player);
            int apologize = GiveCard(game, player, "apologize");

            game.Apply(new PlayLemonCard
            {
                PlayerId = player,
                CardInstanceId = apologize,
                TantrumInstanceId = pileTantrum,
            });

            // No window opened: Apologize is exempt, whatever the rival holds.
            Assert.Empty(game.State.AwaitingResponse);
            Assert.Empty(game.State.Players[player].TantrumPile);
            Assert.Contains(pileTantrum, game.State.LemonDiscard);
        }

        [Fact]
        public void ApologizeAutoPicksAndLapsesTheBabyTitle()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum");
            int player = ActivePlayer(game);
            // The only Tantrum in any pile, and the Baby title with it.
            int pileTantrum = PutTantrumInPile(game, player);
            game.State.WhiniestBabyHolder = player;
            int apologize = GiveCard(game, player, "apologize");

            // No TantrumInstanceId: the copies are interchangeable, the engine picks.
            var events = game.Apply(new PlayLemonCard { PlayerId = player, CardInstanceId = apologize });

            Assert.Empty(game.State.Players[player].TantrumPile);
            Assert.Contains(pileTantrum, game.State.LemonDiscard);
            Assert.Null(game.State.WhiniestBabyHolder);
            var moved = Assert.Single(events.OfType<WhiniestBabyMoved>());
            Assert.Null(moved.ToPlayerId);
        }

        [Fact]
        public void BabyTieGoesToMostRecentTantrumPlayer()
        {
            var game = ReadyToPlay(seed: 7);
            StripAttackReactions(game);
            int attacker = ActivePlayer(game);
            int thrower = SeatAfterActive(game, 1);
            int blamed = SeatAfterActive(game, 2);

            // The thrower PLAYS a Tantrum against an attack: one in the pile, play-recency stamped.
            int attack = GiveCard(game, attacker, "hoa-violation");
            int tantrum = GiveCard(game, thrower, "tantrum");
            game.State.Players[attacker].Money = 20;
            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = thrower });
            game.Apply(new RespondToWindow { PlayerId = thrower, CardInstanceId = tantrum });
            PassAll(game);
            Assert.Equal(thrower, game.State.WhiniestBabyHolder);
            Assert.True(game.State.Players[thrower].LastTantrumPlaySeq > 0);

            // Blame Changer hands a third player a Tantrum they never played: the LATEST GAIN.
            PutTantrumInPile(game, attacker);
            int blame = GiveCard(game, attacker, "blame-changer");
            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = blame, TargetPlayerId = blamed });
            PassAll(game);

            // Tied 1-1: the ruling gives the title to the most recent Tantrum PLAYER, not the latest gain.
            Assert.Equal(1, game.State.Players[thrower].TantrumPile.Count);
            Assert.Equal(1, game.State.Players[blamed].TantrumPile.Count);
            Assert.Equal(thrower, game.State.WhiniestBabyHolder);
        }

        [Fact]
        public void BlameChangerHandsOverATantrumAndTheBaby()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue");
            int blamer = ActivePlayer(game);
            int blamed = SeatAfterActive(game, 1);
            int pileTantrum = PutTantrumInPile(game, blamer);
            game.State.WhiniestBabyHolder = blamer;
            int card = GiveCard(game, blamer, "blame-changer");

            game.Apply(new PlayLemonCard
            {
                PlayerId = blamer,
                CardInstanceId = card,
                TargetPlayerId = blamed,
                TantrumInstanceId = pileTantrum,
            });

            Assert.Empty(game.State.Players[blamer].TantrumPile);
            Assert.Single(game.State.Players[blamed].TantrumPile);
            Assert.Equal(blamed, game.State.WhiniestBabyHolder);
        }

        [Fact]
        public void AnAttackWithoutAVictimIsRefusedBeforeAnythingIsSpent()
        {
            var game = ReadyToPlay(seed: 7);
            int blamer = ActivePlayer(game);
            PutTantrumInPile(game, blamer);
            int card = GiveCard(game, blamer, "blame-changer");
            int actionsBefore = game.State.ActionsRemaining;

            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new PlayLemonCard { PlayerId = blamer, CardInstanceId = card }));

            Assert.Contains(card, game.State.Players[blamer].Hand);
            Assert.Equal(actionsBefore, game.State.ActionsRemaining);
            Assert.Empty(game.State.ResponseStack);
        }
    }
}
