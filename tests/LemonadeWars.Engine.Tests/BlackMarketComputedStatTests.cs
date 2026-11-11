using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;
using static LemonadeWars.Engine.Tests.StateRigging;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>Equipped cards that change sale numbers, pour numbers or earnings.</summary>
    public class BlackMarketComputedStatTests
    {
        [Fact]
        public void PushySalesmanAddsItsPrintedNumber()
        {
            var game = ReadyToPlay(seed: 11);
            var player = game.State.Players[ActivePlayer(game)];
            var bargainStand = player.Stands[0]; // sells on 1-3
            RigEquip(game, player.PlayerId, "pushy-salesman-6", bargainStand.InstanceId);

            Assert.Equal(new[] { 1, 2, 3, 6 }, game.SaleNumbersOf(bargainStand).OrderBy(x => x));
        }

        [Fact]
        public void SpikedLemonadeAddsPourNumberAndInterestPaysPerNumber()
        {
            var game = ReadyToPlay(seed: 11);
            StripHands(game, "tantrum");
            int activeId = ActivePlayer(game);
            var player = game.State.Players[activeId];
            player.Turf.PowerPourNumber = 2;
            RigEquip(game, activeId, "spiked-lemonade-5");
            RigEquip(game, activeId, "interest");
            Assert.Equal(new[] { 2, 5 }, game.PourNumbersOf(player).OrderBy(x => x));

            var money = game.State.Players.Select(p => p.Money).ToList();
            EndTurnWithRoll(game, 5);

            // A 5 misses every Bargain Stand (1-3). The Spiked 5 pours: $1 base + Interest's
            // $2 for two pour numbers.
            Assert.Equal(money[activeId] + 3, player.Money);
        }

        [Fact]
        public void LeftyAndRightyBoostNeighborStands()
        {
            var game = ReadyToPlay(seed: 11);
            var player = game.State.Players[ActivePlayer(game)];
            var left = player.Stands[0];
            var right = player.Stands[1];
            RigEquip(game, player.PlayerId, "lefty-loosey", right.InstanceId);  // boosts stand to its left
            RigEquip(game, player.PlayerId, "righty-tighty", left.InstanceId); // boosts stand to its right

            Assert.Equal(2, game.StandEarnings(player, left));  // 1 base + lefty on right neighbor
            Assert.Equal(2, game.StandEarnings(player, right)); // 1 base + righty on left neighbor
        }

        [Fact]
        public void StandRowInsertPositionIsRespected()
        {
            var game = ReadyToPlay(seed: 11);
            int activeId = ActivePlayer(game);
            var player = game.State.Players[activeId];
            player.Money = 50;
            var originalFirst = player.Stands[0].InstanceId;

            game.Apply(new BuyStand { PlayerId = activeId, StandTypeId = "classic", InsertIndex = 0 });

            Assert.Equal("classic", player.Stands[0].StandTypeId);
            Assert.Equal(originalFirst, player.Stands[1].InstanceId);
        }
    }
}
