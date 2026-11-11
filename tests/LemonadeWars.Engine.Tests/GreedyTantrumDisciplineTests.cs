using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.BotScenario;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// A Tantrum is worth its card to defend yourself, to revive your own play, or to
    /// block a player about to win. Anything else is spite: filtered from search, and
    /// passed by the greedy bot.
    /// </summary>
    public class GreedyTantrumDisciplineTests
    {
        [Fact]
        public void BystanderTantrumsAreSpiteAndGetPassed()
        {
            var game = ReadyToAct(31, 3);
            var state = game.State;
            int active = state.ActivePlayer;
            int bystander = (active + 2) % 3;
            StripReactionCards(game);
            int plan = Hold(game, active, "market-forecasting");
            int tantrum = Hold(game, bystander, "tantrum");
            state.Players[bystander].LemonLordKept.Clear(); // no Friendly Fran / Expert Whiner noise
            state.Players[active].BraggingRights = 0;
            state.Players[active].FirstDibsClaimed.Clear();

            // The plan touches nobody, and the bystander is the only armed responder.
            game.Apply(new PlayLemonCard { PlayerId = active, CardInstanceId = plan });
            Assert.Contains(bystander, state.AwaitingResponse);

            var spite = new RespondToWindow { PlayerId = bystander, CardInstanceId = tantrum };
            Assert.True(MoveFilters.ObjectivelyWasted(game, bystander, spite));
            Assert.IsType<PassWindow>(new GreedyBot().Choose(game, bystander));

            // The same play from a player one VP short of ending the game IS worth the
            // card: the leader-block must survive the spite filter.
            state.Players[active].BraggingRights = 2;
            Assert.False(MoveFilters.ObjectivelyWasted(game, bystander, spite));
            Assert.IsType<RespondToWindow>(new GreedyBot().Choose(game, bystander));
        }

        [Fact]
        public void TantrumStillCancelsAttacksOnTheBot()
        {
            var game = ReadyToAct(32, 3);
            var state = game.State;
            int attacker = state.ActivePlayer;
            int bystander = (attacker + 1) % 3;
            int victim = (attacker + 2) % 3;
            StripReactionCards(game);
            int attack = Hold(game, attacker, "hoa-violation");
            int bystanderTantrum = Hold(game, bystander, "tantrum");
            int victimTantrum = Hold(game, victim, "tantrum");
            state.Players[bystander].LemonLordKept.Clear();
            state.Players[victim].LemonLordKept.Clear();
            state.Players[victim].Money = 10;

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });

            // The target defending itself is never filtered, and greedy plays it.
            var defend = new RespondToWindow { PlayerId = victim, CardInstanceId = victimTantrum };
            Assert.False(MoveFilters.ObjectivelyWasted(game, victim, defend));
            var pick = Assert.IsType<RespondToWindow>(new GreedyBot().Choose(game, victim));
            Assert.Equal(victimTantrum, pick.CardInstanceId);

            // A Tantrum on an attack aimed at someone ELSE is spite.
            var spite = new RespondToWindow { PlayerId = bystander, CardInstanceId = bystanderTantrum };
            Assert.True(MoveFilters.ObjectivelyWasted(game, bystander, spite));
            Assert.IsType<PassWindow>(new GreedyBot().Choose(game, bystander));
        }

        [Fact]
        public void CounterTantrumProtectsTheBotsOwnPlay()
        {
            var game = ReadyToAct(33, 3);
            var state = game.State;
            int active = state.ActivePlayer;
            int canceller = (active + 1) % 3;
            int bystander = (active + 2) % 3;
            StripReactionCards(game);
            int plan = Hold(game, active, "market-forecasting");
            int activeTantrum = Hold(game, active, "tantrum");
            int cancellerTantrum = Hold(game, canceller, "tantrum");
            int bystanderTantrum = Hold(game, bystander, "tantrum");
            ClearLemonLords(game);

            game.Apply(new PlayLemonCard { PlayerId = active, CardInstanceId = plan });
            game.Apply(new RespondToWindow { PlayerId = canceller, CardInstanceId = cancellerTantrum });

            // Counter-tantruming revives the bot's own play: worthwhile, and greedy takes it.
            var counter = new RespondToWindow { PlayerId = active, CardInstanceId = activeTantrum };
            Assert.False(MoveFilters.ObjectivelyWasted(game, active, counter));
            var pick = Assert.IsType<RespondToWindow>(new GreedyBot().Choose(game, active));
            Assert.Equal(activeTantrum, pick.CardInstanceId);

            // The third player has no stake in the canceller's Tantrum: cancelling it is spite.
            var spite = new RespondToWindow { PlayerId = bystander, CardInstanceId = bystanderTantrum };
            Assert.True(MoveFilters.ObjectivelyWasted(game, bystander, spite));
            Assert.IsType<PassWindow>(new GreedyBot().Choose(game, bystander));
        }
    }
}
