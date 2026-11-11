using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.InteractionScenario;

namespace LemonadeWars.Engine.Tests
{
    public class TantrumContestTests
    {
        [Fact]
        public void UncontestedPlanResolvesSynchronously()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int planner = ActivePlayer(game);
            int card = GiveCard(game, planner, "automation");

            game.Apply(new PlayLemonCard { PlayerId = planner, CardInstanceId = card });

            Assert.Empty(game.State.ResponseStack);
            Assert.Empty(game.State.AwaitingResponse);
            Assert.Equal(3, game.State.ActionsRemaining); // 2 - 1 action + 2 from Automation
            Assert.Contains(card, game.State.LemonDiscard);
        }

        [Fact]
        public void TantrumCancelsPlayAndAssignsWhiniestBaby()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int planner = ActivePlayer(game);
            int objector = SeatAfterActive(game, 1);
            int plan = GiveCard(game, planner, "market-forecasting");
            int tantrum = GiveCard(game, objector, "tantrum");
            int handBefore = game.State.Players[planner].Hand.Count;

            game.Apply(new PlayLemonCard { PlayerId = planner, CardInstanceId = plan });
            Assert.Equal(new[] { objector }, game.State.AwaitingResponse);

            game.Apply(new RespondToWindow { PlayerId = objector, CardInstanceId = tantrum });

            Assert.Empty(game.State.ResponseStack);
            // Cancelled: no cards drawn, plan discarded, action still spent.
            Assert.Equal(handBefore - 1, game.State.Players[planner].Hand.Count);
            Assert.Contains(plan, game.State.LemonDiscard);
            Assert.Equal(1, game.State.ActionsRemaining);
            Assert.Single(game.State.Players[objector].TantrumPile);
            Assert.Equal(objector, game.State.WhiniestBabyHolder);
        }

        [Fact]
        public void CounterTantrumLetsThePlayResolve()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            BuryTimeouts(game);
            int planner = ActivePlayer(game);
            int objector = SeatAfterActive(game, 1);
            int plan = GiveCard(game, planner, "market-forecasting");
            int objection = GiveCard(game, objector, "tantrum");
            int counter = GiveCard(game, planner, "tantrum");
            int handBefore = game.State.Players[planner].Hand.Count;

            game.Apply(new PlayLemonCard { PlayerId = planner, CardInstanceId = plan });
            game.Apply(new RespondToWindow { PlayerId = objector, CardInstanceId = objection });
            // The window is now on the objector's Tantrum, which the planner may Tantrum.
            game.Apply(new RespondToWindow { PlayerId = planner, CardInstanceId = counter });

            // The objection is cancelled, so Market Forecasting draws 3: -plan -counter +3.
            Assert.Equal(handBefore - 2 + 3, game.State.Players[planner].Hand.Count);
            // One Tantrum each: the tie goes to whoever threw one most recently.
            Assert.Single(game.State.Players[planner].TantrumPile);
            Assert.Single(game.State.Players[objector].TantrumPile);
            Assert.Equal(planner, game.State.WhiniestBabyHolder);
        }

        [Fact]
        public void ACounteredTantrumReopensTheOriginalCard()
        {
            // Rule: a Tantrum that is itself Tantrummed leaves the original card open again,
            // and its victim may answer anew with ANY reaction (here a Tag).
            var game = ReadyToPlay();
            StripAttackReactions(game);
            BuryTimeouts(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int attack = GiveCard(game, attacker, "hoa-violation");
            int counter = GiveCard(game, attacker, "tantrum");
            int objection = GiveCard(game, victim, "tantrum");
            int victimsTag = GiveCard(game, victim, "tag-youre-it");

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });
            game.Apply(new RespondToWindow { PlayerId = victim, CardInstanceId = objection });
            game.Apply(new RespondToWindow { PlayerId = attacker, CardInstanceId = counter });

            Assert.Single(game.State.ResponseStack);
            Assert.Equal("hoa-violation", game.State.ResponseStack[0].LemonDefId);
            Assert.Equal(new[] { victim }, game.State.AwaitingResponse);
            Assert.Contains(game.LegalMovesFor(victim), m =>
                m is RespondToWindow respond && respond.CardInstanceId == victimsTag);
            // Both Tantrums were thrown, so both were gained.
            Assert.Single(game.State.Players[attacker].TantrumPile);
            Assert.Single(game.State.Players[victim].TantrumPile);
        }

        [Fact]
        public void OnceEveryonePassesTheStackResolvesWithoutAskingAgain()
        {
            // Rule: reactions end once everyone has passed. The victim reflects with Rubber,
            // a bystander Tantrums the Rubber unanswered, and the attack lands: the Tag the
            // victim still holds gets no second window.
            var game = ReadyToPlay();
            StripAttackReactions(game);
            BuryTimeouts(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int bystander = SeatAfterActive(game, 2);
            int attack = GiveCard(game, attacker, "hoa-violation");
            int rubber = GiveCard(game, victim, "im-rubber-youre-glue");
            GiveCard(game, victim, "tag-youre-it");
            int tantrum = GiveCard(game, bystander, "tantrum");
            game.State.Players[victim].Money = 20;
            int attackerMoneyBefore = game.State.Players[attacker].Money;

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });
            game.Apply(new RespondToWindow { PlayerId = victim, CardInstanceId = rubber });
            game.Apply(new RespondToWindow { PlayerId = bystander, CardInstanceId = tantrum });

            Assert.Empty(game.State.ResponseStack);
            Assert.Empty(game.State.AwaitingResponse);
            Assert.Equal(15, game.State.Players[victim].Money);
            Assert.Equal(attackerMoneyBefore + 5, game.State.Players[attacker].Money);
        }

        [Fact]
        public void CannotTantrumYourOwnCardOrRespondUninvited()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int planner = ActivePlayer(game);
            int objector = SeatAfterActive(game, 1);
            int plan = GiveCard(game, planner, "market-forecasting");
            int ownTantrum = GiveCard(game, planner, "tantrum");
            GiveCard(game, objector, "tantrum");

            game.Apply(new PlayLemonCard { PlayerId = planner, CardInstanceId = plan });

            Assert.DoesNotContain(planner, game.State.AwaitingResponse);
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new RespondToWindow { PlayerId = planner, CardInstanceId = ownTantrum }));
            GameFlowTests.PassAll(game);
        }

        [Fact]
        public void ContestedGamesAreDeterministic()
        {
            string PlayContestedTurn()
            {
                var game = ReadyToPlay(99);
                StripHands(game, "tantrum");
                int planner = ActivePlayer(game);
                int objector = SeatAfterActive(game, 1);
                int plan = GiveCard(game, planner, "market-forecasting");
                int objection = GiveCard(game, objector, "tantrum");
                int counter = GiveCard(game, planner, "tantrum");
                game.Apply(new PlayLemonCard { PlayerId = planner, CardInstanceId = plan });
                game.Apply(new RespondToWindow { PlayerId = objector, CardInstanceId = objection });
                game.Apply(new RespondToWindow { PlayerId = planner, CardInstanceId = counter });
                GameFlowTests.ApplyAndPass(game, new EndTurn { PlayerId = planner });
                return game.SnapshotJson();
            }

            Assert.Equal(PlayContestedTurn(), PlayContestedTurn());
        }
    }
}
