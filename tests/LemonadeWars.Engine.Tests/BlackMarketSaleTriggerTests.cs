using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.BlackMarketScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>On Sale and Power Pour abilities.</summary>
    public class BlackMarketSaleTriggerTests
    {
        [Fact]
        public void MeditationDrawsOnSale()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int activeId = Active(game);
            var player = game.State.Players[activeId];
            RigEquip(game, activeId, "meditation", player.Stands[0].InstanceId);
            int hand = player.Hand.Count;

            EndTurnWithRoll(game, 1); // both bargains sell; meditation draws 1

            Assert.Equal(hand + 1, player.Hand.Count);
        }

        [Fact]
        public void JuiceBoxJoeyStealsFromAChosenVictim()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int activeId = Active(game);
            int rivalId = Seat(game, 1);
            var player = game.State.Players[activeId];
            RigEquip(game, activeId, "juice-box-joey", player.Stands[0].InstanceId);
            foreach (var p in game.State.Players)
            {
                p.Money = 20;
            }

            HoldRollWindowOpen(game);
            EndTurnWithRollUntilDecision(game, 2);

            var decision = Assert.Single(game.State.PendingDecisions);
            Assert.Equal(DecisionKind.AbilityVictim, decision.Kind);
            game.Apply(new SubmitAbilityChoice { PlayerId = activeId, TargetPlayerId = rivalId });
            GameFlowTests.PassAll(game);

            // Two bargains sold (+$2), $1 stolen, and $1 more if the owner's pour number is the 2 rolled.
            Assert.Equal(20 + 2 + 1 + (game.PourNumbersOf(player).Contains(2) ? 1 : 0),
                game.State.Players[activeId].Money);
        }

        [Fact]
        public void VictimChoiceAbilitiesCanBeDeclined()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int activeId = Active(game);
            var player = game.State.Players[activeId];
            RigEquip(game, activeId, "juice-box-joey", player.Stands[0].InstanceId);
            foreach (var p in game.State.Players)
            {
                p.Money = 20;
            }

            HoldRollWindowOpen(game);
            EndTurnWithRollUntilDecision(game, 2);

            var decision = Assert.Single(game.State.PendingDecisions);
            Assert.Equal(DecisionKind.AbilityVictim, decision.Kind);
            Assert.Contains(game.LegalMovesFor(activeId),
                m => m is SubmitAbilityChoice choice && choice.Decline);

            var events = game.Apply(new SubmitAbilityChoice { PlayerId = activeId, Decline = true });
            Assert.Contains(events, e => e is AbilityDeclined);
            Assert.Empty(game.State.PendingDecisions);
            GameFlowTests.PassAll(game);

            // Nobody was robbed: every rival still holds their $20.
            foreach (var p in game.State.Players)
            {
                if (p.PlayerId != activeId)
                {
                    Assert.True(p.Money >= 20);
                }
            }
        }

        [Fact]
        public void WhispersOfFateDrawsTwoDiscardsOne()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int activeId = Active(game);
            var player = game.State.Players[activeId];
            player.Turf.PowerPourNumber = 4;
            RigEquip(game, activeId, "whispers-of-fate");
            int hand = player.Hand.Count;

            HoldRollWindowOpen(game);
            EndTurnWithRollUntilDecision(game, 4);

            var discard = game.State.PendingDecisions
                .Single(d => d.Kind == DecisionKind.AbilityDiscard);
            Assert.Equal(activeId, discard.PlayerId);
            // "Discard 1 OF THEM": only the two fresh draws (the newest hand cards) are
            // eligible, and an older hand card is rejected outright.
            Assert.Equal(new[] { player.Hand[^2], player.Hand[^1] }, discard.EligibleCardIds);
            Assert.Throws<InvalidActionException>(() => game.Apply(new SubmitAbilityChoice
            {
                PlayerId = activeId,
                CardInstanceIds = new List<int> { player.Hand[0] },
            }));
            game.Apply(new SubmitAbilityChoice
            {
                PlayerId = activeId,
                CardInstanceIds = new List<int> { player.Hand[^1] },
            });
            GameFlowTests.PassAll(game);

            Assert.Equal(hand + 1, player.Hand.Count); // +2 drawn, -1 discarded
        }

        [Fact]
        public void HalfPintHarryStealsARandomCardAndGivesOneBack()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int activeId = Active(game);
            int victimId = Seat(game, 1);
            var player = game.State.Players[activeId];
            var victim = game.State.Players[victimId];
            player.Turf.PowerPourNumber = 6;
            RigEquip(game, activeId, "half-pint-harry");
            int playerHand = player.Hand.Count;

            HoldRollWindowOpen(game);
            int victimHand = victim.Hand.Count; // counted with the Out of Stock the victim now holds
            EndTurnWithRollUntilDecision(game, 6);

            game.Apply(new SubmitAbilityChoice { PlayerId = activeId, TargetPlayerId = victimId });
            var giveBack = game.State.PendingDecisions
                .Single(d => d.Kind == DecisionKind.AbilityGiveBack);
            int stolen = giveBack.StolenCardId!.Value;
            int given = player.Hand.First(id => id != stolen);
            game.Apply(new SubmitAbilityChoice
            {
                PlayerId = activeId,
                CardInstanceIds = new List<int> { given },
            });
            GameFlowTests.PassAll(game);

            Assert.Contains(stolen, player.Hand);
            Assert.Contains(given, victim.Hand);
            Assert.Equal(playerHand, player.Hand.Count);      // net zero: +1 stolen, -1 given
            // Victim: -1 stolen, +1 given back, +1 turn-start draw (they are next player).
            Assert.Equal(victimHand + 1, victim.Hand.Count);
        }
    }
}
