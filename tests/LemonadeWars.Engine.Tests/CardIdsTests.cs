using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LemonadeWars.Engine.Data;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// The id constant classes are written by hand against game-data: a card added,
    /// renamed or removed there must show up here, or rules code would name a card that
    /// does not exist.
    /// </summary>
    public class CardIdsTests
    {
        public static IEnumerable<object[]> Catalogues() => new[]
        {
            new object[] { typeof(LemonCardIds), TestData.Db.LemonCards.Select(card => card.Id) },
            new object[] { typeof(BlackMarketCardIds), TestData.Db.BlackMarketCards.Select(card => card.Id) },
            new object[] { typeof(FirstDibsIds), TestData.Db.FirstDibsTitles.Select(title => title.Id) },
            new object[] { typeof(LemonLordIds), TestData.Db.LemonLordTitles.Select(title => title.Id) },
            new object[] { typeof(StandTypeIds), TestData.Db.StandTypes.Select(standType => standType.Id) },
        };

        [Theory]
        [MemberData(nameof(Catalogues))]
        public void ConstantsMatchTheGameData(Type idClass, IEnumerable<string> idsInGameData)
        {
            var constants = idClass
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral)
                .Select(field => (string)field.GetRawConstantValue()!);

            Assert.Equal(idsInGameData.OrderBy(id => id), constants.OrderBy(id => id));
        }
    }
}
