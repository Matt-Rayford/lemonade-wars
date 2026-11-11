using LemonadeWars.Engine.Ai;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    public class BotFactoryTests
    {
        [Fact]
        public void BotFactoryMapsLevels()
        {
            Assert.IsType<EasyBot>(BotFactory.Create("easy", 1));
            Assert.IsType<PlannerBot>(BotFactory.Create("medium", 1));
            Assert.IsType<PlannerBot>(BotFactory.Create("hard", 1));
            Assert.IsType<PlannerBot>(BotFactory.Create("wambulance", 1));
            Assert.Equal("wambulance", BotFactory.Normalize(" WAMBULANCE "));
            Assert.Equal("wambulance", BotFactory.Normalize("wambulence")); // legacy spelling from persisted rooms
            Assert.IsType<PlannerBot>(BotFactory.Create(null, 1));
            Assert.IsType<PlannerBot>(BotFactory.Create("HARD??", 1));
            Assert.Equal("hard", BotFactory.Normalize(" Hard "));
        }
    }
}
