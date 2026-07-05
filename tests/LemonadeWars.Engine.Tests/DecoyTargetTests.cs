using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Inflatable Decoy defends its owner only: it may respond to an attack solely
    /// when its owner is the attack's current target (live-play bug 2026-07-05).
    /// </summary>
    public class DecoyTargetTests
    {
        [Fact]
        public void DecoyRespondsOnlyWhenYouAreTheTarget()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben", "Cal", "Dee" }, 13);
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
            GameFlowTests.PassAll(game);

            var s = game.State;
            // Quiet hands: nobody can tantrum/tag/reroll, so windows hinge on decoys.
            foreach (var p in s.Players)
            {
                p.Hand.RemoveAll(id =>
                {
                    string defId = s.LemonInstances[id].DefId;
                    if (defId == "tantrum" || defId == "out-of-stock" ||
                        defId == "tag-youre-it" || defId == "im-rubber-youre-glue")
                    {
                        s.LemonDiscard.Add(id);
                        return true;
                    }
                    return false;
                });
            }

            int attacker = s.ActivePlayer;
            int victim = (attacker + 1) % 4;
            int bystander = (attacker + 2) % 4;

            // An attack card for the attacker.
            int attack = s.LemonDeck.Concat(s.LemonDiscard)
                .First(id => s.LemonInstances[id].DefId == "taxes");
            s.LemonDeck.Remove(attack);
            s.LemonDiscard.Remove(attack);
            s.Players[attacker].Hand.Add(attack);

            // Decoys on BOTH the victim's and the bystander's turf.
            var decoys = s.BlackMarketDeck
                .Where(id => s.BlackMarketInstances[id].DefId == "inflatable-decoy")
                .Take(2).ToList();
            Assert.Equal(2, decoys.Count);
            foreach (int id in decoys)
            {
                s.BlackMarketDeck.Remove(id);
            }
            s.Players[victim].Turf.Equipped.Add(decoys[0]);
            s.Players[bystander].Turf.Equipped.Add(decoys[1]);

            game.Apply(new PlayLemonCard
            {
                PlayerId = attacker,
                CardInstanceId = attack,
                TargetPlayerId = victim,
            });

            // Only the target is awaited; the bystander's decoy grants nothing.
            Assert.Contains(victim, s.AwaitingResponse);
            Assert.DoesNotContain(bystander, s.AwaitingResponse);
            Assert.Contains(game.LegalMovesFor(victim),
                m => m is RespondToWindow r && r.EquippedInstanceId != null);
            Assert.DoesNotContain(game.LegalMovesFor(bystander),
                m => m is RespondToWindow r && r.EquippedInstanceId != null);

            // Even a bystander somehow awaited cannot use the decoy on someone
            // else's attack: the apply-side validation holds the line.
            s.AwaitingResponse.Add(bystander);
            Assert.Throws<InvalidActionException>(() => game.Apply(new RespondToWindow
            {
                PlayerId = bystander,
                EquippedInstanceId = decoys[1],
            }));
            s.AwaitingResponse.Remove(bystander);

            // The target's decoy works: attack cancelled, decoy discarded.
            game.Apply(new RespondToWindow
            {
                PlayerId = victim,
                EquippedInstanceId = decoys[0],
            });
            Assert.Empty(s.ResponseStack);
            Assert.DoesNotContain(decoys[0], s.Players[victim].Turf.Equipped);
            Assert.Contains(decoys[0], s.BlackMarketDiscard);
            Assert.Contains(attack, s.LemonDiscard);
        }

        /// <summary>
        /// Designer ruling: after I'm Rubber You're Glue, the reflector IS the attacker
        /// ("as if you played it"), so the original attacker — now the target — may
        /// use their own Inflatable Decoy against the reflected attack.
        /// </summary>
        [Fact]
        public void ReflectedAttackLetsTheOriginalAttackerDecoy()
        {
            var game = Game.Create(TestData.Db, new[] { "Ana", "Ben", "Cal", "Dee" }, 21);
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
            GameFlowTests.PassAll(game);

            var s = game.State;
            foreach (var p in s.Players)
            {
                p.Hand.RemoveAll(id =>
                {
                    string defId = s.LemonInstances[id].DefId;
                    if (defId == "tantrum" || defId == "out-of-stock" ||
                        defId == "tag-youre-it" || defId == "im-rubber-youre-glue")
                    {
                        s.LemonDiscard.Add(id);
                        return true;
                    }
                    return false;
                });
            }

            int attacker = s.ActivePlayer;
            int victim = (attacker + 1) % 4;

            int attack = s.LemonDeck.Concat(s.LemonDiscard)
                .First(id => s.LemonInstances[id].DefId == "taxes");
            s.LemonDeck.Remove(attack);
            s.LemonDiscard.Remove(attack);
            s.Players[attacker].Hand.Add(attack);

            int rubber = s.LemonDeck.Concat(s.LemonDiscard)
                .First(id => s.LemonInstances[id].DefId == "im-rubber-youre-glue");
            s.LemonDeck.Remove(rubber);
            s.LemonDiscard.Remove(rubber);
            s.Players[victim].Hand.Add(rubber);

            // The ATTACKER owns a decoy.
            int decoy = s.BlackMarketDeck
                .First(id => s.BlackMarketInstances[id].DefId == "inflatable-decoy");
            s.BlackMarketDeck.Remove(decoy);
            s.Players[attacker].Turf.Equipped.Add(decoy);

            game.Apply(new PlayLemonCard
            {
                PlayerId = attacker,
                CardInstanceId = attack,
                TargetPlayerId = victim,
            });
            game.Apply(new RespondToWindow { PlayerId = victim, CardInstanceId = rubber });

            // Reflection resolved: the attack now belongs to the victim and targets
            // the original attacker, whose decoy may answer.
            var reflected = s.ResponseStack.Last();
            Assert.Equal(victim, reflected.OwnerId);
            Assert.Equal(attacker, reflected.AttackTargetId);
            Assert.Contains(attacker, s.AwaitingResponse);
            Assert.Contains(game.LegalMovesFor(attacker),
                m => m is RespondToWindow r && r.EquippedInstanceId != null);

            game.Apply(new RespondToWindow { PlayerId = attacker, EquippedInstanceId = decoy });
            Assert.Empty(s.ResponseStack);
            Assert.Contains(decoy, s.BlackMarketDiscard);
            Assert.Contains(attack, s.LemonDiscard);
        }
    }
}
