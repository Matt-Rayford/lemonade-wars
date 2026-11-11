using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.InteractionScenario;

namespace LemonadeWars.Engine.Tests
{
    public class SaleRollCardTests
    {
        [Fact]
        public void OutOfStockRerollsTheSaleDie()
        {
            var game = ReadyToPlay();
            StripHands(game, "out-of-stock");
            int roller = ActivePlayer(game);
            int responder = SeatAfterActive(game, 1);
            int outOfStock = GiveCard(game, responder, "out-of-stock");

            var events = new List<GameEvent>();
            events.AddRange(game.Apply(new EndTurn { PlayerId = roller }));
            Assert.NotNull(game.State.PendingRoll);

            events.AddRange(game.Apply(new RespondToWindow { PlayerId = responder, CardInstanceId = outOfStock }));
            PassWindowsOnly(game, events);

            var rerolled = events.OfType<DieRerolled>().Single();
            Assert.Equal(responder, rerolled.ByPlayerId);
            Assert.Null(game.State.PendingRoll);
            Assert.NotEqual(roller, game.State.ActivePlayer);
            Assert.Contains(outOfStock, game.State.LemonDiscard);
        }

        [Fact]
        public void NightShiftsSellsForTheRollerOnly()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            int roller = ActivePlayer(game);
            int card = GiveCard(game, roller, "night-shifts");
            var moneyBefore = game.State.Players.Select(p => p.Money).ToList();

            var events = game.Apply(new PlayLemonCard { PlayerId = roller, CardInstanceId = card }).ToList();
            int roll = events.OfType<SaleRolled>().Single().Value;

            var rollerState = game.State.Players[roller];
            int expected = moneyBefore[roller] + DraftedBoardEarnings(rollerState, roll);
            Assert.Equal(expected, rollerState.Money);
            foreach (var other in game.State.Players.Where(p => p.PlayerId != roller))
            {
                Assert.Equal(moneyBefore[other.PlayerId], other.Money);
            }
        }

        [Fact]
        public void StealTheCashboxSkimsTheNextSale()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue",
                "profit-share", "out-of-stock");
            int thief = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int trap = GiveCard(game, thief, "steal-the-cashbox");
            SetEveryonesMoney(game, 20);

            game.Apply(new PlayLemonCard { PlayerId = thief, CardInstanceId = trap, TargetPlayerId = victim });
            Assert.Equal(trap, game.State.Players[victim].Turf.TrapInstanceId);

            var events = game.Apply(new EndTurn { PlayerId = thief }).ToList();
            int roll = events.OfType<SaleRolled>().Single().Value;

            var victimState = game.State.Players[victim];
            int victimEarned = DraftedBoardEarnings(victimState, roll);
            // The trap is spent either way; the victim's earnings (up to $10) went to the thief.
            Assert.Null(victimState.Turf.TrapInstanceId);
            Assert.Contains(trap, game.State.LemonDiscard);
            Assert.Equal(20, victimState.Money);
            int thiefEarned = DraftedBoardEarnings(game.State.Players[thief], roll);
            Assert.Equal(20 + thiefEarned + victimEarned, game.State.Players[thief].Money);
        }

        /// <summary>Two drafted Bargain stands pay $1 each on 1-3; the Turf pays $1 on its Power Pour number.</summary>
        private static int DraftedBoardEarnings(PlayerState player, int roll) =>
            (roll <= 3 ? 2 : 0) + (player.Turf.PowerPourNumber == roll ? 1 : 0);
    }
}
