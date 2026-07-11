using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// What one seat is allowed to see. This is the ONLY state object that should ever
    /// cross the network to a client: full <see cref="GameState"/> (deck order, other
    /// hands, secret titles, RNG state) stays server-side. Rich enough to drive the whole
    /// client UI without touching the Game object.
    /// </summary>
    public sealed class PlayerView
    {
        public int ViewerId { get; set; }
        public GameStage Stage { get; set; }
        public TurnPhase Phase { get; set; }
        public int ActivePlayer { get; set; }
        public int FirstPlayer { get; set; }
        public int ActionsRemaining { get; set; }
        public int BmOnlyActionsRemaining { get; set; }
        /// <summary>Seats the game currently waits on (turn, window, or decisions).</summary>
        public List<int> ActingPlayers { get; set; } = new List<int>();
        /// <summary>Whose pick it is during the setup draft.</summary>
        public int? CurrentInitialBuyer { get; set; }

        public List<PlayerPanel> Players { get; set; } = new List<PlayerPanel>();

        // ---- Viewer-private ----
        public List<CardInfo> Hand { get; set; } = new List<CardInfo>();
        public List<string> LemonLordDealt { get; set; } = new List<string>();
        public List<LordStatus> LemonLordStatus { get; set; } = new List<LordStatus>();
        /// <summary>Don's Blessings mid-resolution: the revealed victim hand, else null.</summary>
        public List<CardInfo>? RevealedHand { get; set; }

        // ---- Shared zones ----
        public int LemonDeckCount { get; set; }
        public List<CardInfo> LemonDiscard { get; set; } = new List<CardInfo>();
        public int BlackMarketDeckCount { get; set; }
        public List<CardInfo> Market { get; set; } = new List<CardInfo>();
        /// <summary>Price the viewer would pay per market slot (Peddlin' Pete included).</summary>
        public List<int> MarketPrices { get; set; } = new List<int>();
        public List<CardInfo> BlackMarketDiscard { get; set; } = new List<CardInfo>();
        public List<string> FirstDibsRow { get; set; } = new List<string>();
        public Dictionary<string, int> StandSupplyCounts { get; set; } = new Dictionary<string, int>();
        /// <summary>Shape on top of each non-empty supply stack — the stand you would get.</summary>
        public Dictionary<string, Shape> SupplyTopShapes { get; set; } = new Dictionary<string, Shape>();
        /// <summary>Price the viewer would pay per stand type (ownership escalation included).</summary>
        public Dictionary<string, int> SupplyPrices { get; set; } = new Dictionary<string, int>();
        public int? NextBraggingRightsPrice { get; set; }
        public int? WhiniestBabyHolder { get; set; }
        public int? SpoiledRottenHolder { get; set; }

        // ---- Interaction surface ----
        public List<int> AwaitingResponse { get; set; } = new List<int>();
        public int? PendingRollValue { get; set; }
        /// <summary>Top of the response stack, when a window is open.</summary>
        public StackTopInfo? StackTop { get; set; }
        /// <summary>The viewer's own pending decisions, with the payloads their UI needs.</summary>
        public List<DecisionInfo> MyDecisions { get; set; } = new List<DecisionInfo>();
        /// <summary>A money steal against the viewer awaits a Profit Share response.</summary>
        public bool TheftOnMe { get; set; }
        public List<int> Winners { get; set; } = new List<int>();

        /// <summary>One player's public board.</summary>
        public sealed class PlayerPanel
        {
            public int PlayerId { get; set; }
            public string Name { get; set; } = "";
            public int Money { get; set; }
            public int HandCount { get; set; }
            public int TantrumCount { get; set; }
            public int TurfPowerPourNumber { get; set; }
            /// <summary>All pour numbers including Spiked Lemonades.</summary>
            public List<int> PourNumbers { get; set; } = new List<int>();
            public List<CardInfo> TurfEquipped { get; set; } = new List<CardInfo>();
            public bool TurfTrapped { get; set; }
            public List<StandPanel> Stands { get; set; } = new List<StandPanel>();
            public List<string> FirstDibsClaimed { get; set; } = new List<string>();
            public int BraggingRights { get; set; }
            public int InGameVictoryPoints { get; set; }
        }

        public sealed class StandPanel
        {
            public int InstanceId { get; set; }
            public string StandTypeId { get; set; } = "";
            public Shape Shape { get; set; }
            public List<CardInfo> Equipped { get; set; } = new List<CardInfo>();
            public List<int> SaleNumbers { get; set; } = new List<int>();
            public int Earnings { get; set; }
        }

        public sealed class CardInfo
        {
            public int InstanceId { get; set; }
            public string DefId { get; set; } = "";
            /// <summary>Printed shape for Black Market copies; null for Lemon cards.</summary>
            public Shape? Shape { get; set; }
        }

        public sealed class LordStatus
        {
            public string TitleId { get; set; } = "";
            public bool Met { get; set; }
        }

        public sealed class StackTopInfo
        {
            public int OwnerId { get; set; }
            public bool IsPurchase { get; set; }
            public string DefId { get; set; } = "";
            public Shape? Shape { get; set; }
            public int? AttackTargetId { get; set; }
            /// <summary>Finders Keepers / That's Not Fair: the equipped card being taken (public table info).</summary>
            public string? StolenDefId { get; set; }
            public Shape? StolenShape { get; set; }
        }

        public sealed class DecisionInfo
        {
            public DecisionKind Kind { get; set; }
            public int RequiredCount { get; set; }
            public int RequiredMoney { get; set; }
            public int? StolenCardId { get; set; }
            public int? ChosenPlayerId { get; set; }
            public int? CardInstanceId { get; set; }
            public int? StackItemId { get; set; }
            /// <summary>When set, only these hand cards may answer (Whiniest Baby's drawn pair).</summary>
            public List<int>? EligibleCardIds { get; set; }
        }
    }

    public sealed partial class Game
    {
        /// <summary>Project the current state into what <paramref name="viewerId"/> may see.</summary>
        public PlayerView ViewFor(int viewerId)
        {
            var viewer = Player(viewerId);

            PlayerView.CardInfo Lemon(int id) => new PlayerView.CardInfo
            {
                InstanceId = id,
                DefId = State.LemonInstances[id].DefId,
            };
            PlayerView.CardInfo Bm(int id) => new PlayerView.CardInfo
            {
                InstanceId = id,
                DefId = State.BlackMarketInstances[id].DefId,
                Shape = State.BlackMarketInstances[id].Shape,
            };

            var view = new PlayerView
            {
                ViewerId = viewerId,
                Stage = State.Stage,
                Phase = State.Phase,
                ActivePlayer = State.ActivePlayer,
                FirstPlayer = State.FirstPlayer,
                ActionsRemaining = State.ActionsRemaining,
                BmOnlyActionsRemaining = State.BmOnlyActionsRemaining,
                ActingPlayers = ActingPlayers().ToList(),
                CurrentInitialBuyer =
                    State.Stage == GameStage.InitialBuys && State.InitialBuyQueue.Count > 0
                        ? State.InitialBuyQueue[0]
                        : (int?)null,
                Hand = viewer.Hand.Select(Lemon).ToList(),
                LemonLordDealt = viewer.LemonLordDealt.ToList(),
                LemonLordStatus = viewer.LemonLordKept
                    .Select(id => new PlayerView.LordStatus
                    {
                        TitleId = id,
                        Met = MeetsLemonLord(viewer, id),
                    }).ToList(),
                LemonDeckCount = State.LemonDeck.Count,
                LemonDiscard = State.LemonDiscard.Select(Lemon).ToList(),
                BlackMarketDeckCount = State.BlackMarketDeck.Count,
                Market = State.Market.Select(Bm).ToList(),
                MarketPrices = State.Market
                    .Select(id => BlackMarketPrice(viewerId,
                        Db.BlackMarket(State.BlackMarketInstances[id].DefId)))
                    .ToList(),
                BlackMarketDiscard = State.BlackMarketDiscard.Select(Bm).ToList(),
                FirstDibsRow = State.FirstDibsRow.ToList(),
                StandSupplyCounts = State.StandSupply.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
                SupplyTopShapes = State.StandSupply
                    .Where(kv => kv.Value.Count > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value[0]),
                SupplyPrices = Db.StandTypes.ToDictionary(t => t.Id, t => StandPrice(viewerId, t.Id)),
                NextBraggingRightsPrice =
                    State.BraggingRightsSold < Db.Supporting.BraggingRightsPrices.Count
                        ? Db.Supporting.BraggingRightsPrices[State.BraggingRightsSold]
                        : (int?)null,
                WhiniestBabyHolder = State.WhiniestBabyHolder,
                SpoiledRottenHolder = State.SpoiledRottenHolder,
                AwaitingResponse = State.AwaitingResponse.ToList(),
                PendingRollValue = State.PendingRoll?.Value,
                TheftOnMe = State.TheftQueue.Count > 0 && State.TheftQueue[0].VictimId == viewerId,
                MyDecisions = State.PendingDecisions
                    .Where(d => d.PlayerId == viewerId)
                    .Select(d => new PlayerView.DecisionInfo
                    {
                        Kind = d.Kind,
                        RequiredCount = d.RequiredCount,
                        RequiredMoney = d.RequiredMoney,
                        StolenCardId = d.StolenCardId,
                        ChosenPlayerId = d.ChosenPlayerId,
                        CardInstanceId = d.CardInstanceId,
                        StackItemId = d.StackItemId,
                        EligibleCardIds = d.EligibleCardIds?.ToList(),
                    }).ToList(),
                Winners = State.Winners.ToList(),
            };

            if (State.ResponseStack.Count > 0)
            {
                var top = State.ResponseStack[State.ResponseStack.Count - 1];
                view.StackTop = new PlayerView.StackTopInfo
                {
                    OwnerId = top.OwnerId,
                    IsPurchase = top.Kind == StackItemKind.BlackMarketPurchase,
                    DefId = top.Kind == StackItemKind.BlackMarketPurchase
                        ? State.BlackMarketInstances[top.BmInstanceId!.Value].DefId
                        : top.LemonDefId,
                    Shape = top.Kind == StackItemKind.BlackMarketPurchase
                        ? State.BlackMarketInstances[top.BmInstanceId!.Value].Shape
                        : (Shape?)null,
                    AttackTargetId = top.AttackTargetId,
                };
                if (top.TargetEquippedInstanceId is int stolen &&
                    State.BlackMarketInstances.TryGetValue(stolen, out var stolenInstance))
                {
                    view.StackTop.StolenDefId = stolenInstance.DefId;
                    view.StackTop.StolenShape = stolenInstance.Shape;
                }
            }

            // Don's Blessings: the ability owner looks at the victim's hand while picking.
            var peek = State.PendingDecisions.FirstOrDefault(d =>
                d.PlayerId == viewerId && d.Kind == DecisionKind.AbilityPickCard);
            if (peek?.ChosenPlayerId is int victimId)
            {
                view.RevealedHand = Player(victimId).Hand.Select(Lemon).ToList();
            }

            foreach (var p in State.Players)
            {
                view.Players.Add(new PlayerView.PlayerPanel
                {
                    PlayerId = p.PlayerId,
                    Name = p.Name,
                    Money = p.Money,
                    HandCount = p.Hand.Count,
                    TantrumCount = p.TantrumPile.Count,
                    TurfPowerPourNumber = p.Turf.PowerPourNumber,
                    PourNumbers = PourNumbersOf(p).OrderBy(x => x).ToList(),
                    TurfEquipped = p.Turf.Equipped.Select(Bm).ToList(),
                    TurfTrapped = p.Turf.TrapInstanceId != null,
                    FirstDibsClaimed = p.FirstDibsClaimed.ToList(),
                    BraggingRights = p.BraggingRights,
                    InGameVictoryPoints = p.InGameVictoryPoints,
                    Stands = p.Stands.Select(s => new PlayerView.StandPanel
                    {
                        InstanceId = s.InstanceId,
                        StandTypeId = s.StandTypeId,
                        Shape = s.Shape,
                        Equipped = s.Equipped.Select(Bm).ToList(),
                        SaleNumbers = SaleNumbersOf(s).OrderBy(x => x).ToList(),
                        Earnings = StandEarnings(p, s),
                    }).ToList(),
                });
            }

            return view;
        }
    }
}
