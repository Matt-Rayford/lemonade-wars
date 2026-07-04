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

        /// <summary>Load and validate all card data from a game-data directory.</summary>
        public static CardDatabase Load(string gameDataDir)
        {
            JObject Read(string file) =>
                JObject.Parse(File.ReadAllText(Path.Combine(gameDataDir, file)));

            var lemonJson = Read("lemon-cards.json");
            var bmJson = Read("black-market-cards.json");
            var titlesJson = Read("titles.json");
            var standsJson = Read("stands.json");
            var turfJson = Read("turf.json");
            var supportingJson = Read("supporting.json");
            var configJson = Read("config.json");

            var lemon = lemonJson["cards"]!.Select(c => new LemonCardDef
            {
                Id = (string)c["id"]!,
                Name = (string)c["name"]!,
                Type = EnumMaps.Parse(EnumMaps.LemonType, (string)c["type"]!, "lemon type"),
                Count = (int)c["count"]!,
                Effect = (string)c["effect"]!,
                Flavor = (string?)c["flavor"] ?? "",
                Condition = (string?)c["condition"],
            }).ToList();

            var blackMarket = bmJson["cards"]!.Select(c => new BlackMarketCardDef
            {
                Id = (string)c["id"]!,
                Name = (string)c["name"]!,
                Target = EnumMaps.Parse(EnumMaps.Target, (string)c["target"]!, "equip target"),
                Cost = (int)c["cost"]!,
                Count = (int)c["count"]!,
                Timing = EnumMaps.Parse(EnumMaps.Timing, (string)c["timing"]!, "timing"),
                Category = (string)c["category"]!,
                Effect = (string)c["effect"]!,
                Flavor = (string?)c["flavor"] ?? "",
                Shapes = c["shapes"]!
                    .Select(s => EnumMaps.Parse(EnumMaps.ShapeName, (string)s!, "shape"))
                    .ToList(),
                Icons = c["icons"]!.Select(i => (string)i!).ToList(),
            }).ToList();

            List<TitleDef> ReadTitles(string key, TitleKind kind) =>
                titlesJson[key]!.Select(t => new TitleDef
                {
                    Id = (string)t["id"]!,
                    Name = (string)t["name"]!,
                    Kind = kind,
                    VictoryPoints = (int)t["victoryPoints"]!,
                    Condition = (string)t["condition"]!,
                    Flavor = (string?)t["flavor"] ?? "",
                    CountedIcon = (string?)t["countedIcon"],
                    RequiredCount = (int?)t["requiredCount"],
                }).ToList();

            var firstDibs = ReadTitles("firstDibs", TitleKind.FirstDibs);
            var lemonLord = ReadTitles("lemonLord", TitleKind.LemonLord);

            var standTypes = standsJson["standTypes"]!.Select(s => new StandTypeDef
            {
                Id = (string)s["id"]!,
                Name = (string)s["name"]!,
                BaseCost = (int)s["baseCost"]!,
                UpgradeSlots = (int)s["upgradeSlots"]!,
                SaleNumbers = s["saleNumbers"]!.Select(n => (int)n!).ToList(),
                BaseEarnings = (int)s["baseEarnings"]!,
                Count = (int)s["count"]!,
                Shapes = ((JObject)s["shapes"]!).Properties().ToDictionary(
                    p => EnumMaps.Parse(EnumMaps.ShapeName, p.Name, "shape"),
                    p => (int)p.Value!),
            }).ToList();

            var turf = new TurfDef
            {
                Count = (int)turfJson["count"]!,
                UpgradeSlots = (int)turfJson["upgradeSlots"]!,
                PowerPourNumbers = turfJson["powerPourNumbers"]!.Select(n => (int)n!).ToList(),
            };

            var supporting = new SupportingDef
            {
                BraggingRightsPrices = supportingJson["braggingRights"]!["prices"]!
                    .Select(p => (int)p!).ToList(),
                BraggingRightsPurchaseLimitPerTurn =
                    (int)supportingJson["braggingRights"]!["purchaseLimitPerTurn"]!,
            };

            var setup = configJson["setup"]!;
            var turn = configJson["turn"]!;
            var config = new GameConfig
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

            var db = new CardDatabase(
                lemon, blackMarket, firstDibs, lemonLord, standTypes, turf, supporting, config);
            db.Validate();
            return db;
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
