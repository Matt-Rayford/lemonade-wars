using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.InteractionScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Inflatable Decoy defends its owner only: it may answer an attack solely when its
    /// owner is the attack's current target.
    /// </summary>
    public class DecoyTargetTests
    {
        [Fact]
        public void DecoyRespondsOnlyWhenYouAreTheTarget()
        {
            var game = ReadyToPlay(seed: 13);
            var state = game.State;
            DiscardReactionsFromHands(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int bystander = SeatAfterActive(game, 2);
            int attack = GiveCard(game, attacker, "taxes");
            var decoys = TakeDecoysFromDeck(game, 2);
            state.Players[victim].Turf.Equipped.Add(decoys[0]);
            state.Players[bystander].Turf.Equipped.Add(decoys[1]);

            game.Apply(new PlayLemonCard
            {
                PlayerId = attacker,
                CardInstanceId = attack,
                TargetPlayerId = victim,
            });

            // Only the target is awaited; the bystander's decoy grants nothing.
            Assert.Contains(victim, state.AwaitingResponse);
            Assert.DoesNotContain(bystander, state.AwaitingResponse);
            Assert.Contains(game.LegalMovesFor(victim), IsDecoyResponse);
            Assert.DoesNotContain(game.LegalMovesFor(bystander), IsDecoyResponse);

            // Even a bystander who is somehow awaited is refused when the move is applied.
            state.AwaitingResponse.Add(bystander);
            Assert.Throws<InvalidActionException>(() => game.Apply(new RespondToWindow
            {
                PlayerId = bystander,
                EquippedInstanceId = decoys[1],
            }));
            state.AwaitingResponse.Remove(bystander);

            game.Apply(new RespondToWindow
            {
                PlayerId = victim,
                EquippedInstanceId = decoys[0],
            });

            // The target's decoy cancels the attack and is spent.
            Assert.Empty(state.ResponseStack);
            Assert.DoesNotContain(decoys[0], state.Players[victim].Turf.Equipped);
            // The window died with the attack: an awaited player left behind here is shown
            // an empty Pass-only prompt.
            Assert.Empty(state.AwaitingResponse);
            Assert.Equal(attacker, Assert.Single(game.ActingPlayers()));
            Assert.Contains(decoys[0], state.BlackMarketDiscard);
            Assert.Contains(attack, state.LemonDiscard);
        }

        [Fact]
        public void ReflectedAttackLetsTheOriginalAttackerDecoy()
        {
            // Designer ruling: after I'm Rubber You're Glue the reflector IS the attacker
            // ("as if you played it"), so the original attacker is a target like any other.
            var game = ReadyToPlay(seed: 21);
            var state = game.State;
            DiscardReactionsFromHands(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int attack = GiveCard(game, attacker, "taxes");
            int rubber = GiveCard(game, victim, "im-rubber-youre-glue");
            int decoy = TakeDecoysFromDeck(game, 1)[0];
            state.Players[attacker].Turf.Equipped.Add(decoy);

            game.Apply(new PlayLemonCard
            {
                PlayerId = attacker,
                CardInstanceId = attack,
                TargetPlayerId = victim,
            });
            game.Apply(new RespondToWindow { PlayerId = victim, CardInstanceId = rubber });

            // The attack now belongs to the victim and targets the original attacker.
            var reflected = state.ResponseStack.Last();
            Assert.Equal(victim, reflected.OwnerId);
            Assert.Equal(attacker, reflected.AttackTargetId);
            Assert.Contains(attacker, state.AwaitingResponse);
            Assert.Contains(game.LegalMovesFor(attacker), IsDecoyResponse);

            game.Apply(new RespondToWindow { PlayerId = attacker, EquippedInstanceId = decoy });
            Assert.Empty(state.ResponseStack);
            Assert.Contains(decoy, state.BlackMarketDiscard);
            Assert.Contains(attack, state.LemonDiscard);
        }

        private static bool IsDecoyResponse(GameAction move) =>
            move is RespondToWindow respond && respond.EquippedInstanceId != null;

        /// <summary>Nobody can Tantrum, Tag, reflect or re-roll, so every window hinges on the decoys.</summary>
        private static void DiscardReactionsFromHands(Game game)
        {
            var state = game.State;
            foreach (var player in state.Players)
            {
                player.Hand.RemoveAll(id =>
                {
                    string defId = state.LemonInstances[id].DefId;
                    if (defId == "tantrum" || defId == "out-of-stock" ||
                        defId == "tag-youre-it" || defId == "im-rubber-youre-glue")
                    {
                        state.LemonDiscard.Add(id);
                        return true;
                    }
                    return false;
                });
            }
        }

        private static List<int> TakeDecoysFromDeck(Game game, int count)
        {
            var state = game.State;
            var decoys = state.BlackMarketDeck
                .Where(id => state.BlackMarketInstances[id].DefId == "inflatable-decoy")
                .Take(count).ToList();
            Assert.Equal(count, decoys.Count);
            foreach (int id in decoys)
            {
                state.BlackMarketDeck.Remove(id);
            }
            return decoys;
        }
    }
}
