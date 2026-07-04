using System.Linq;
using LemonadeWars.Engine.Data;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>Mirrors tools/convert_cards.py's validation: the JSON must match the rulebook's box contents.</summary>
    public class CardDatabaseTests
    {
        private readonly CardDatabase _db = TestData.Db;

        [Fact]
        public void LemonDeckHas69CardsPlus2Timeouts()
        {
            Assert.Equal(69, _db.LemonCards.Where(c => c.Type != LemonCardType.Timeout).Sum(c => c.Count));
            Assert.Equal(2, _db.LemonCards.Where(c => c.Type == LemonCardType.Timeout).Sum(c => c.Count));
        }

        [Fact]
        public void LemonTypeSplitMatchesNameplates()
        {
            int Copies(LemonCardType t) =>
                _db.LemonCards.Where(c => c.Type == t).Sum(c => c.Count);
            Assert.Equal(22, Copies(LemonCardType.Plan));
            Assert.Equal(22, Copies(LemonCardType.Attack));
            Assert.Equal(25, Copies(LemonCardType.Instant));
        }

        [Fact]
        public void BlackMarketHas69CopiesWithEvenShapes()
        {
            Assert.Equal(69, _db.BlackMarketCards.Sum(c => c.Count));
            var shapes = _db.BlackMarketCards.SelectMany(c => c.Shapes).ToList();
            Assert.Equal(23, shapes.Count(s => s == Shape.Diamond));
            Assert.Equal(23, shapes.Count(s => s == Shape.Circle));
            Assert.Equal(23, shapes.Count(s => s == Shape.Square));
        }

        [Fact]
        public void TitleCountsMatchRulebook()
        {
            Assert.Equal(16, _db.FirstDibsTitles.Count);
            Assert.Equal(20, _db.LemonLordTitles.Count);
            Assert.All(_db.FirstDibsTitles.Concat(_db.LemonLordTitles), t => Assert.Equal(1, t.VictoryPoints));
        }

        [Fact]
        public void StandEconomyMatchesCards()
        {
            Assert.Equal(54, _db.StandTypes.Sum(s => s.Count));

            var bargain = _db.StandType("bargain");
            Assert.Equal(new[] { 1, 2, 3 }, bargain.SaleNumbers);
            Assert.Equal(1, bargain.BaseEarnings);
            Assert.Equal(1, bargain.UpgradeSlots);

            var classic = _db.StandType("classic");
            Assert.Equal(new[] { 4, 5 }, classic.SaleNumbers);
            Assert.Equal(2, classic.BaseEarnings);
            Assert.Equal(2, classic.UpgradeSlots);

            var gourmet = _db.StandType("gourmet");
            Assert.Equal(new[] { 6 }, gourmet.SaleNumbers);
            Assert.Equal(3, gourmet.BaseEarnings);
            Assert.Equal(4, gourmet.UpgradeSlots);

            // Every die face sells for someone.
            var allNumbers = _db.StandTypes.SelectMany(s => s.SaleNumbers)
                .Concat(_db.Turf.PowerPourNumbers).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, allNumbers);
        }

        [Fact]
        public void BraggingRightsPricesEscalateByTwo()
        {
            var prices = _db.Supporting.BraggingRightsPrices;
            Assert.Equal(11, prices.Count);
            Assert.Equal(16, prices[0]);
            Assert.Equal(36, prices[^1]);
            for (int i = 1; i < prices.Count; i++)
            {
                Assert.Equal(2, prices[i] - prices[i - 1]);
            }
        }

        [Theory]
        [InlineData("flow-master", 15)]     // Power Pour cards
        [InlineData("sales-engine", 10)]    // On Sale cards
        [InlineData("no-bullies-allowed", 8)] // Defense cards
        [InlineData("card-shark", 6)]       // Gain Card cards
        public void LemonLordIconCountsMatchDeckContents(string titleId, int expectedQualifying)
        {
            // The Title CSV's designer metadata said how many qualifying cards exist;
            // re-verify against the actual deck so a rebalance can't silently break a title.
            var title = _db.Title(titleId);
            Assert.NotNull(title.CountedIcon);

            int actual = title.CountedIcon switch
            {
                "power-pour" => _db.BlackMarketCards.Where(c => c.Timing == EffectTiming.PowerPour).Sum(c => c.Count),
                "sale" => _db.BlackMarketCards.Where(c => c.Timing == EffectTiming.OnSale).Sum(c => c.Count),
                "shield" => _db.BlackMarketCards.Where(c => c.Category == "Defense").Sum(c => c.Count),
                "draw" => _db.BlackMarketCards.Where(c => c.Category == "Gain Card").Sum(c => c.Count),
                _ => -1,
            };
            Assert.Equal(expectedQualifying, actual);
        }
    }
}
