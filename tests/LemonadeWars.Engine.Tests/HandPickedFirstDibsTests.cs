using System;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;

namespace LemonadeWars.Engine.Tests
{
    public class HandPickedFirstDibsTests
    {
        private static string[] FirstTitles(int count) =>
            TestData.Db.FirstDibsTitles.Take(count).Select(title => title.Id).ToArray();

        [Fact]
        public void TheHandPickedRowIsTheRowInPlay()
        {
            var picked = FirstTitles(5).Reverse().ToArray();

            var game = Game.Create(TestData.Db, FourPlayers, seed: 7, picked);

            Assert.Equal(picked, game.State.FirstDibsRow);
        }

        [Fact]
        public void HandPickingTheRowChangesNothingElseAboutTheDeal()
        {
            var dealt = Game.Create(TestData.Db, FourPlayers, seed: 99);
            var picked = Game.Create(TestData.Db, FourPlayers, seed: 99, FirstTitles(5));

            Assert.Equal(dealt.State.FirstPlayer, picked.State.FirstPlayer);
            Assert.Equal(dealt.State.Market, picked.State.Market);
            Assert.Equal(dealt.State.LemonDeck, picked.State.LemonDeck);
            for (int seat = 0; seat < FourPlayers.Length; seat++)
            {
                Assert.Equal(dealt.State.Players[seat].Hand, picked.State.Players[seat].Hand);
                Assert.Equal(dealt.State.Players[seat].LemonLordDealt, picked.State.Players[seat].LemonLordDealt);
            }
        }

        [Theory]
        [InlineData(4)] // one short of the five a four-player game needs
        [InlineData(6)]
        public void TheRowMustBeOneCardPerPlayerPlusOne(int count)
        {
            var error = Assert.Throws<ArgumentException>(
                () => Game.Create(TestData.Db, FourPlayers, seed: 1, FirstTitles(count)));
            Assert.Contains("needs 5 First Dibs cards", error.Message);
        }

        [Fact]
        public void ACardCannotBePickedTwice()
        {
            var picked = FirstTitles(5);
            picked[4] = picked[0];

            Assert.Throws<ArgumentException>(() => Game.Create(TestData.Db, FourPlayers, seed: 1, picked));
        }

        [Fact]
        public void OnlyFirstDibsCardsCanBePicked()
        {
            var picked = FirstTitles(5);
            picked[2] = TestData.Db.LemonLordTitles[0].Id;

            Assert.Throws<ArgumentException>(() => Game.Create(TestData.Db, FourPlayers, seed: 1, picked));
        }
    }
}
