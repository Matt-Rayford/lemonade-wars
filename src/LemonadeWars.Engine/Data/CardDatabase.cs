using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Engine.Data
{
    /// <summary>
    /// Immutable, validated view of everything in game-data/. Loaded once and shared;
    /// all game state refers back to definitions by id.
    /// </summary>
    public sealed class CardDatabase
    {
        public IReadOnlyList<LemonCardDef> LemonCards { get; }
        public IReadOnlyList<BlackMarketCardDef> BlackMarketCards { get; }
        public IReadOnlyList<TitleDef> FirstDibsTitles { get; }
        public IReadOnlyList<TitleDef> LemonLordTitles { get; }
        public IReadOnlyList<StandTypeDef> StandTypes { get; }
        public TurfDef Turf { get; }
        public SupportingDef Supporting { get; }
        public GameConfig Config { get; }

        private readonly Dictionary<string, LemonCardDef> _lemonById;
        private readonly Dictionary<string, BlackMarketCardDef> _blackMarketById;
        private readonly Dictionary<string, TitleDef> _titleById;
        private readonly Dictionary<string, StandTypeDef> _standTypeById;

        public LemonCardDef Lemon(string id) => Lookup(_lemonById, id, "lemon card");
        public BlackMarketCardDef BlackMarket(string id) => Lookup(_blackMarketById, id, "black market card");
        public TitleDef Title(string id) => Lookup(_titleById, id, "title");
        public StandTypeDef StandType(string id) => Lookup(_standTypeById, id, "stand type");

        private static T Lookup<T>(Dictionary<string, T> map, string id, string kind)
        {
            if (map.TryGetValue(id, out var def))
            {
                return def;
            }
            throw new KeyNotFoundException($"No {kind} with id '{id}'.");
        }

        private CardDatabase(
            List<LemonCardDef> lemon,
            List<BlackMarketCardDef> blackMarket,
            List<TitleDef> firstDibs,
            List<TitleDef> lemonLord,
            List<StandTypeDef> standTypes,
            TurfDef turf,
            SupportingDef supporting,
            GameConfig config)
        {
            LemonCards = lemon;
            BlackMarketCards = blackMarket;
            FirstDibsTitles = firstDibs;
            LemonLordTitles = lemonLord;
            StandTypes = standTypes;
            Turf = turf;
            Supporting = supporting;
            Config = config;

            _lemonById = lemon.ToDictionary(c => c.Id);
            _blackMarketById = blackMarket.ToDictionary(c => c.Id);
            _titleById = firstDibs.Concat(lemonLord).ToDictionary(t => t.Id);
            _standTypeById = standTypes.ToDictionary(s => s.Id);
        }

        public static CardDatabase Load(string gameDataDir)
        {
            JObject Read(string file) =>
                JObject.Parse(File.ReadAllText(Path.Combine(gameDataDir, file)));

            var lemonJson = Read("lemon-cards.json");
            var blackMarketJson = Read("black-market-cards.json");
            var titlesJson = Read("titles.json");
            var standsJson = Read("stands.json");
            var turfJson = Read("turf.json");
            var supportingJson = Read("supporting.json");
            var configJson = Read("config.json");

            var db = new CardDatabase(
                lemonJson["cards"]!.Select(ParseLemonCard).ToList(),
                blackMarketJson["cards"]!.Select(ParseBlackMarketCard).ToList(),
                ParseTitles(titlesJson, "firstDibs", TitleKind.FirstDibs),
                ParseTitles(titlesJson, "lemonLord", TitleKind.LemonLord),
                standsJson["standTypes"]!.Select(ParseStandType).ToList(),
                ParseTurf(turfJson),
                ParseSupporting(supportingJson),
                ParseConfig(configJson, standsJson));
            db.Validate();
            return db;
        }

        private static LemonCardDef ParseLemonCard(JToken card) =>
            new LemonCardDef
            {
                Id = (string)card["id"]!,
                Name = (string)card["name"]!,
                Type = EnumMaps.Parse(EnumMaps.LemonType, (string)card["type"]!, "lemon type"),
                Count = (int)card["count"]!,
                Effect = (string)card["effect"]!,
                Flavor = (string?)card["flavor"] ?? "",
                Condition = (string?)card["condition"],
            };

        private static BlackMarketCardDef ParseBlackMarketCard(JToken card) =>
            new BlackMarketCardDef
            {
                Id = (string)card["id"]!,
                Name = (string)card["name"]!,
                Target = EnumMaps.Parse(EnumMaps.Target, (string)card["target"]!, "equip target"),
                Cost = (int)card["cost"]!,
                Count = (int)card["count"]!,
                Timing = EnumMaps.Parse(EnumMaps.Timing, (string)card["timing"]!, "timing"),
                Category = (string)card["category"]!,
                Effect = (string)card["effect"]!,
                Flavor = (string?)card["flavor"] ?? "",
                Shapes = card["shapes"]!
                    .Select(s => EnumMaps.Parse(EnumMaps.ShapeName, (string)s!, "shape"))
                    .ToList(),
                Icons = card["icons"]!.Select(i => (string)i!).ToList(),
                Number = (int?)card["number"],
            };

        private static List<TitleDef> ParseTitles(JObject titlesJson, string key, TitleKind kind) =>
            titlesJson[key]!.Select(title => new TitleDef
            {
                Id = (string)title["id"]!,
                Name = (string)title["name"]!,
                Kind = kind,
                VictoryPoints = (int)title["victoryPoints"]!,
                Condition = (string)title["condition"]!,
                Flavor = (string?)title["flavor"] ?? "",
                CountedIcon = (string?)title["countedIcon"],
                RequiredCount = (int?)title["requiredCount"],
            }).ToList();

        private static StandTypeDef ParseStandType(JToken standType) =>
            new StandTypeDef
            {
                Id = (string)standType["id"]!,
                Name = (string)standType["name"]!,
                BaseCost = (int)standType["baseCost"]!,
                UpgradeSlots = (int)standType["upgradeSlots"]!,
                SaleNumbers = standType["saleNumbers"]!.Select(n => (int)n!).ToList(),
                BaseEarnings = (int)standType["baseEarnings"]!,
                Count = (int)standType["count"]!,
                Shapes = ((JObject)standType["shapes"]!).Properties().ToDictionary(
                    p => EnumMaps.Parse(EnumMaps.ShapeName, p.Name, "shape"),
                    p => (int)p.Value!),
            };

        private static TurfDef ParseTurf(JObject turfJson) =>
            new TurfDef
            {
                Count = (int)turfJson["count"]!,
                UpgradeSlots = (int)turfJson["upgradeSlots"]!,
                PowerPourNumbers = turfJson["powerPourNumbers"]!.Select(n => (int)n!).ToList(),
            };

        private static SupportingDef ParseSupporting(JObject supportingJson) =>
            new SupportingDef
            {
                BraggingRightsPrices = supportingJson["braggingRights"]!["prices"]!
                    .Select(p => (int)p!).ToList(),
                BraggingRightsPurchaseLimitPerTurn =
                    (int)supportingJson["braggingRights"]!["purchaseLimitPerTurn"]!,
            };

        /// <summary>The stand price escalation lives in stands.json but is a table-level rule.</summary>
        private static GameConfig ParseConfig(JObject configJson, JObject standsJson)
        {
            var setup = configJson["setup"]!;
            var turn = configJson["turn"]!;
            return new GameConfig
            {
                MinPlayers = (int)configJson["players"]!["min"]!,
                MaxPlayers = (int)configJson["players"]!["max"]!,
                VictoryPointsToTriggerEnd = (int)configJson["victoryPointsToTriggerEnd"]!,
                SaleDieSides = (int)configJson["saleDie"]!,
                StartingMoney = (int)setup["startingMoney"]!,
                StartingHandSize = (int)setup["startingHandSize"]!,
                LemonLordDealt = (int)setup["lemonLordDealt"]!,
                LemonLordKept = (int)setup["lemonLordKept"]!,
                BlackMarketFaceUp = (int)setup["blackMarketFaceUp"]!,
                BlackMarketFaceUp2Player = (int)setup["blackMarketFaceUp2Player"]!,
                TurnStartDraw = (int)turn["startDraw"]!,
                ActionsPerTurn = (int)turn["actions"]!,
                TimeoutHandLimit = (int)turn["timeoutHandLimit"]!,
                BlackMarketRefreshCost = (int)turn["blackMarketRefreshCost"]!,
                StandCostEscalationPerOwned =
                    (int)standsJson["costEscalationPerOwnedStand"]!,
            };
        }

        /// <summary>Fail fast if the data does not match the rulebook's component list.</summary>
        private void Validate()
        {
            void Check(bool ok, string message)
            {
                if (!ok)
                {
                    throw new InvalidOperationException($"Card data validation failed: {message}");
                }
            }

            Check(LemonCards.Where(c => c.Type != LemonCardType.Timeout).Sum(c => c.Count) == 69,
                "expected 69 Lemon cards");
            Check(LemonCards.Where(c => c.Type == LemonCardType.Timeout).Sum(c => c.Count) == 2,
                "expected 2 Timeout cards");
            Check(BlackMarketCards.Sum(c => c.Count) == 69, "expected 69 Black Market cards");
            Check(BlackMarketCards.All(c => c.Shapes.Count == c.Count),
                "every Black Market copy needs a shape");
            Check(FirstDibsTitles.Count == 16, "expected 16 First Dibs titles");
            Check(LemonLordTitles.Count == 20, "expected 20 Lemon Lord titles");
            Check(Supporting.BraggingRightsPrices.Count == 11, "expected 11 Bragging Rights");
            Check(StandTypes.Sum(s => s.Count) == 54, "expected 54 Stands");
            Check(Turf.Count == Turf.PowerPourNumbers.Count,
                "each Turf card needs a power pour number");
        }
    }
}
