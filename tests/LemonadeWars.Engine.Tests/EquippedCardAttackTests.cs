using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;
using static LemonadeWars.Engine.Tests.InteractionScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>Attacks that name one of the victim's equipped cards, including after a Tag re-aims them.</summary>
    public class EquippedCardAttackTests
    {
        [Fact]
        public void TaggedThatsNotFairRequiresRetarget()
        {
            var game = ReadyToPlay();
            StripAttackReactions(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int tagger = SeatAfterActive(game, 2);
            int newVictim = SeatAfterActive(game, 3);
            int victimsCard = RigTurfEquip(game, victim);
            int newVictimsCard = RigTurfEquip(game, newVictim);
            int attack = GiveCard(game, attacker, "thats-not-fair");
            int tag = GiveCard(game, tagger, "tag-youre-it");

            game.Apply(new PlayLemonCard
            {
                PlayerId = attacker,
                CardInstanceId = attack,
                TargetEquippedInstanceId = victimsCard,
            });
            game.Apply(new RespondToWindow { PlayerId = tagger, CardInstanceId = tag, RedirectTargetId = newVictim });
            PassWindowsOnly(game);

            // The attack's chosen card belongs to the old victim, so the attacker re-picks.
            var decision = Assert.Single(game.State.PendingDecisions);
            Assert.Equal(DecisionKind.AttackRetarget, decision.Kind);
            Assert.Equal(attacker, decision.PlayerId);

            game.Apply(new SubmitRetarget
            {
                PlayerId = attacker,
                StackItemId = decision.StackItemId!.Value,
                TargetEquippedInstanceId = newVictimsCard,
            });

            Assert.Contains(newVictimsCard, game.State.BlackMarketDiscard);          // the re-pick is destroyed
            Assert.Contains(victimsCard, game.State.Players[victim].Turf.Equipped); // the first pick survived
        }

        [Fact]
        public void FindersKeepersStealsAnEquippedCard()
        {
            var game = ReadyToPlay();
            StripAttackReactions(game);
            int thief = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int victimsCard = RigTurfEquip(game, victim);
            int attack = GiveCard(game, thief, "finders-keepers");

            game.Apply(new PlayLemonCard
            {
                PlayerId = thief,
                CardInstanceId = attack,
                TargetPlayerId = victim,
                TargetEquippedInstanceId = victimsCard,
                EquipStandInstanceId = LandingStandFor(game, thief, victimsCard),
            });

            Assert.DoesNotContain(victimsCard, game.State.Players[victim].Turf.Equipped);
            bool onOwnBoard = game.State.Players[thief].Turf.Equipped.Contains(victimsCard) ||
                game.State.Players[thief].Stands.Any(stand => stand.Equipped.Contains(victimsCard));
            Assert.True(onOwnBoard);
        }

        [Fact]
        public void TaggedFindersKeepersDropsTheStaleStolenCard()
        {
            var game = ReadyToPlay();
            StripAttackReactions(game);
            int thief = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int tagger = SeatAfterActive(game, 2);
            int newVictim = SeatAfterActive(game, 3);
            int victimsCard = RigTurfEquip(game, victim);
            RigTurfEquip(game, newVictim);
            int attack = GiveCard(game, thief, "finders-keepers");
            int tag = GiveCard(game, tagger, "tag-youre-it");

            game.Apply(new PlayLemonCard
            {
                PlayerId = thief,
                CardInstanceId = attack,
                TargetPlayerId = victim,
                TargetEquippedInstanceId = victimsCard,
                EquipStandInstanceId = LandingStandFor(game, thief, victimsCard),
            });
            // While the attack aims at the first victim, views advertise their card as the loot.
            Assert.Equal(game.State.BlackMarketInstances[victimsCard].DefId,
                game.ViewFor(victim).StackTop?.StolenDefId);

            game.Apply(new RespondToWindow { PlayerId = tagger, CardInstanceId = tag, RedirectTargetId = newVictim });
            PassWindowsOnly(game);

            // Re-aimed and awaiting the thief's re-pick: neither the stack nor any view may
            // keep showing the old victim's card.
            var item = game.State.ResponseStack.Single(i => i.LemonDefId == "finders-keepers");
            Assert.Null(item.TargetEquippedInstanceId);
            foreach (int seat in new[] { thief, victim, tagger, newVictim })
            {
                var top = game.ViewFor(seat).StackTop;
                if (top != null && top.DefId == "finders-keepers")
                {
                    Assert.Null(top.StolenDefId);
                }
            }
        }

        /// <summary>Where a stolen card is re-equipped: the thief's first Stand, or null for a Turf card.</summary>
        private static int? LandingStandFor(Game game, int thiefId, int stolenInstanceId) =>
            BlackMarketDefOf(game, stolenInstanceId).Target == EquipTarget.Turf
                ? (int?)null
                : game.State.Players[thiefId].Stands[0].InstanceId;
    }
}
