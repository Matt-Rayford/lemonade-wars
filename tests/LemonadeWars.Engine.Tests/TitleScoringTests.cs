using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.BlackMarketScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>First Dibs titles are claimed the moment their condition is met; Lemon Lords score at game end.</summary>
    public class TitleScoringTests
    {
        [Fact]
        public void PennyPincherIsClaimedOnTheFifthBargainStand()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int activeId = Active(game);
            var player = game.State.Players[activeId];
            player.Money = 100;
            game.State.FirstDibsRow.Clear();
            game.State.FirstDibsRow.Add("penny-pincher");

            // 2 from the draft; buy 2 more, end turn, buy the 5th next round.
            GameFlowTests.ApplyAndPass(game, new BuyStand { PlayerId = activeId, StandTypeId = "bargain" });
            GameFlowTests.ApplyAndPass(game, new BuyStand { PlayerId = activeId, StandTypeId = "bargain" });
            Assert.Empty(player.FirstDibsClaimed);

            for (int i = 0; i < game.State.Players.Count; i++)
            {
                GameFlowTests.ApplyAndPass(game, new EndTurn { PlayerId = Active(game) });
            }
            Assert.Equal(activeId, Active(game));
            player.Money = 100;
            GameFlowTests.ApplyAndPass(game, new BuyStand { PlayerId = activeId, StandTypeId = "bargain" });

            Assert.Contains("penny-pincher", player.FirstDibsClaimed);
            Assert.Empty(game.State.FirstDibsRow);
            Assert.Equal(1, player.InGameVictoryPoints);
        }

        [Fact]
        public void BigEarnerTriggersFromRollEarnings()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int activeId = Active(game);
            game.State.FirstDibsRow.Clear();
            game.State.FirstDibsRow.Add("big-earner");
            game.State.RollStats[activeId] = new RollStats { Earned = 9 };

            GameFlowTests.ApplyAndPass(game, new DrawLemonCard { PlayerId = activeId });

            Assert.Contains("big-earner", game.State.Players[activeId].FirstDibsClaimed);
        }

        [Fact]
        public void CrankyPantsCountsPileTantrums()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int activeId = Active(game);
            var player = game.State.Players[activeId];
            game.State.FirstDibsRow.Clear();
            game.State.FirstDibsRow.Add("cranky-pants");
            RigTantrums(game, activeId, 3);

            GameFlowTests.ApplyAndPass(game, new DrawLemonCard { PlayerId = activeId });

            Assert.Contains("cranky-pants", player.FirstDibsClaimed);
        }

        [Fact]
        public void LemonLordTitlesScoreAtGameEnd()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            var state = game.State;
            int first = state.FirstPlayer;
            var player = state.Players[first];

            // Kept titles the test can verify: Friendly Fran (no tantrums) + Hoarder (10+ cards).
            player.LemonLordKept.Clear();
            player.LemonLordKept.AddRange(new[] { "friendly-fran", "hoarder" });
            while (player.Hand.Count < 10)
            {
                int id = state.LemonDeck.First(x => state.LemonInstances[x].DefId != "timeout");
                state.LemonDeck.Remove(id);
                player.Hand.Add(id);
            }
            // Other players keep nothing so the winner is unambiguous.
            foreach (var rival in state.Players.Where(p => p.PlayerId != first))
            {
                rival.LemonLordKept.Clear();
            }

            Assert.True(game.MeetsLemonLord(player, "friendly-fran"));
            Assert.True(game.MeetsLemonLord(player, "hoarder"));

            // Drive to game end: first player buys 3 Bragging Rights over 3 rounds.
            for (int round = 0; round < 3 && state.Stage != GameStage.Finished; round++)
            {
                for (int i = 0; i < state.Players.Count && state.Stage != GameStage.Finished; i++)
                {
                    var active = state.Players[state.ActivePlayer];
                    if (active.PlayerId == first)
                    {
                        active.Money = 100;
                        GameFlowTests.ApplyAndPass(game, new BuyBraggingRights { PlayerId = first });
                    }
                    if (state.Stage != GameStage.Finished)
                    {
                        GameFlowTests.ApplyAndPass(game, new EndTurn { PlayerId = state.ActivePlayer });
                    }
                }
            }

            Assert.Equal(GameStage.Finished, state.Stage);
            // 3 bragging rights + 2 fulfilled Lemon Lords = 5 VP; sole winner.
            Assert.Equal(new List<int> { first }, state.Winners);
        }
    }
}
