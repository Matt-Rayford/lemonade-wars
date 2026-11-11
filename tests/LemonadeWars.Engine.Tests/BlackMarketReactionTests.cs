using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.BlackMarketScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>Equipped cards that answer another player's card.</summary>
    public class BlackMarketReactionTests
    {
        [Fact]
        public void SwearJarChargesTantrumThrowers()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int activeId = Active(game);
            int rivalId = Seat(game, 1);
            RigEquip(game, activeId, "swear-jar");
            int plan = GiveCard(game, activeId, "market-forecasting");
            int tantrum = GiveCard(game, rivalId, "tantrum");
            game.State.Players[activeId].Money = 20;
            game.State.Players[rivalId].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = activeId, CardInstanceId = plan });
            game.Apply(new RespondToWindow { PlayerId = rivalId, CardInstanceId = tantrum });
            GameFlowTests.PassAll(game);

            // The tantrum still cancelled the plan, but its thrower paid the jar's owner $2.
            Assert.Equal(22, game.State.Players[activeId].Money);
            Assert.Equal(18, game.State.Players[rivalId].Money);
        }

        [Fact]
        public void InflatableDecoyDiscardsAnAttack()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int activeId = Active(game);
            int rivalId = Seat(game, 1);
            int decoy = RigEquip(game, rivalId, "inflatable-decoy");
            int attack = GiveCard(game, activeId, "hoa-violation");
            game.State.Players[activeId].Money = 20;
            game.State.Players[rivalId].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = activeId, CardInstanceId = attack, TargetPlayerId = rivalId });
            Assert.Contains(rivalId, game.State.AwaitingResponse); // the decoy makes its owner eligible
            game.Apply(new RespondToWindow { PlayerId = rivalId, EquippedInstanceId = decoy });

            Assert.Equal(20, game.State.Players[activeId].Money); // attack never resolved
            Assert.Equal(20, game.State.Players[rivalId].Money);
            Assert.Contains(decoy, game.State.BlackMarketDiscard);
            Assert.Contains(attack, game.State.LemonDiscard);
        }

        [Fact]
        public void BouncerLetsTheVictimCounterAttack()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int activeId = Active(game);
            int rivalId = Seat(game, 1);
            RigEquip(game, rivalId, "bouncer");
            int attack = GiveCard(game, activeId, "hoa-violation");
            int counter = GiveCard(game, rivalId, "taxes");
            game.State.Players[activeId].Money = 20;
            game.State.Players[rivalId].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = activeId, CardInstanceId = attack, TargetPlayerId = rivalId });

            var decision = Assert.Single(game.State.PendingDecisions);
            Assert.Equal(DecisionKind.BouncerAttack, decision.Kind);
            Assert.Equal(rivalId, decision.PlayerId);
            // Free counter-attack: Taxes steals $2 per stand, and the attacker has 2, so $4.
            game.Apply(new PlayLemonCard { PlayerId = rivalId, CardInstanceId = counter, TargetPlayerId = activeId });
            GameFlowTests.PassAll(game);

            Assert.Equal(20 + 5 - 4, game.State.Players[activeId].Money);
            Assert.Equal(20 - 5 + 4, game.State.Players[rivalId].Money);
        }
    }
}
