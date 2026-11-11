using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.BlackMarketScenario;

namespace LemonadeWars.Engine.Tests
{
    public class BlackMarketTurnAbilityTests
    {
        [Fact]
        public void DownsellModifiesThePendingRoll()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            int activeId = Active(game);
            var player = game.State.Players[activeId];
            int downsell = RigEquip(game, activeId, "downsell");
            HoldRollWindowOpen(game);
            // The owner holds one too: a natural 1 would otherwise (correctly) drop Downsell
            // from their options before the test can force the value.
            GiveCard(game, activeId, "out-of-stock");
            int money = player.Money;

            game.Apply(new EndTurn { PlayerId = activeId });
            Assert.Contains(activeId, game.State.AwaitingResponse);
            game.State.PendingRoll!.Value = 4;
            game.Apply(new UseTurnAbility { PlayerId = activeId, EquippedInstanceId = downsell });
            Assert.Equal(3, game.State.PendingRoll!.Value);

            // Once per turn.
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new UseTurnAbility { PlayerId = activeId, EquippedInstanceId = downsell }));
            GameFlowTests.PassAll(game);

            // Roll 3: both bargains sell.
            Assert.True(game.State.Players[activeId].Money >= money + 2);
        }

        [Fact]
        public void BoundaryRollsHideDownsellAndSugaredUp()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            int activeId = Active(game);
            int downsell = RigEquip(game, activeId, "downsell");
            int sugared = RigEquip(game, activeId, "sugared-up");
            GiveCard(game, activeId, "out-of-stock"); // keeps the roll window open for the owner

            game.Apply(new EndTurn { PlayerId = activeId });

            // A 1 cannot be downsold; a 6 cannot be sugared up.
            game.State.PendingRoll!.Value = 1;
            Assert.DoesNotContain(game.LegalMovesFor(activeId),
                m => m is UseTurnAbility use && use.EquippedInstanceId == downsell);
            Assert.Contains(game.LegalMovesFor(activeId),
                m => m is UseTurnAbility use && use.EquippedInstanceId == sugared);
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new UseTurnAbility { PlayerId = activeId, EquippedInstanceId = downsell }));

            game.State.PendingRoll!.Value = 6;
            Assert.DoesNotContain(game.LegalMovesFor(activeId),
                m => m is UseTurnAbility use && use.EquippedInstanceId == sugared);
            Assert.Contains(game.LegalMovesFor(activeId),
                m => m is UseTurnAbility use && use.EquippedInstanceId == downsell);
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new UseTurnAbility { PlayerId = activeId, EquippedInstanceId = sugared }));
        }

        [Fact]
        public void LiquidEnergyGrantsAnExtraTableSale()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            int activeId = Active(game);
            int liquid = RigEquip(game, activeId, "liquid-energy");
            var money = game.State.Players.Select(p => p.Money).ToList();

            var events = game.Apply(new UseTurnAbility { PlayerId = activeId, EquippedInstanceId = liquid }).ToList();
            int roll = events.OfType<SaleRolled>().Single().Value;

            // Table-wide: everyone's bargains sell on 1-3.
            if (roll <= 3)
            {
                foreach (var p in game.State.Players)
                {
                    Assert.True(p.Money >= money[p.PlayerId] + 2);
                }
            }
            Assert.Equal(TurnPhase.Play, game.State.Phase); // turn continues
        }
    }
}
