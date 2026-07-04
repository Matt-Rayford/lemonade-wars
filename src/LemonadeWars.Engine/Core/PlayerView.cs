using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// What one seat is allowed to see. This is the ONLY state object that should ever
    /// cross the network to a client: full <see cref="GameState"/> (deck order, other
    /// hands, secret titles, RNG state) stays server-side.
    ///
    /// Public by the rulebook: money, hand SIZES, boards, tantrum piles, discards, market,
    /// claimed titles, status card holders. Private to the viewer: own hand, own Lemon Lord
    /// titles, and a victim's hand while resolving Don's Blessings.
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

        public List<PlayerPanel> Players { get; set; } = new List<PlayerPanel>();

        // ---- Viewer-private ----
        public List<CardInfo> Hand { get; set; } = new List<CardInfo>();
        public List<string> LemonLordDealt { get; set; } = new List<string>();
        public List<string> LemonLordKept { get; set; } = new List<string>();
        /// <summary>Don's Blessings mid-resolution: the revealed victim hand, else null.</summary>
        public List<CardInfo>? RevealedHand { get; set; }

        // ---- Shared zones ----
        public int LemonDeckCount { get; set; }
        public List<CardInfo> LemonDiscard { get; set; } = new List<CardInfo>();
        public int BlackMarketDeckCount { get; set; }
        public List<CardInfo> Market { get; set; } = new List<CardInfo>();
        public List<CardInfo> BlackMarketDiscard { get; set; } = new List<CardInfo>();
        public List<string> FirstDibsRow { get; set; } = new List<string>();
        public Dictionary<string, int> StandSupplyCounts { get; set; } = new Dictionary<string, int>();
        public int? NextBraggingRightsPrice { get; set; }
        public int? WhiniestBabyHolder { get; set; }
        public int? SpoiledRottenHolder { get; set; }

        // ---- Interaction surface ----
        public List<int> AwaitingResponse { get; set; } = new List<int>();
        public int? PendingRollValue { get; set; }
        /// <summary>The viewer's own pending decisions (kinds only for other players).</summary>
        public List<DecisionKind> MyPendingDecisions { get; set; } = new List<DecisionKind>();
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
                Hand = viewer.Hand.Select(Lemon).ToList(),
                LemonLordDealt = viewer.LemonLordDealt.ToList(),
                LemonLordKept = viewer.LemonLordKept.ToList(),
                LemonDeckCount = State.LemonDeck.Count,
                LemonDiscard = State.LemonDiscard.Select(Lemon).ToList(),
                BlackMarketDeckCount = State.BlackMarketDeck.Count,
                Market = State.Market.Select(Bm).ToList(),
                BlackMarketDiscard = State.BlackMarketDiscard.Select(Bm).ToList(),
                FirstDibsRow = State.FirstDibsRow.ToList(),
                StandSupplyCounts = State.StandSupply.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
                NextBraggingRightsPrice =
                    State.BraggingRightsSold < Db.Supporting.BraggingRightsPrices.Count
                        ? Db.Supporting.BraggingRightsPrices[State.BraggingRightsSold]
                        : (int?)null,
                WhiniestBabyHolder = State.WhiniestBabyHolder,
                SpoiledRottenHolder = State.SpoiledRottenHolder,
                AwaitingResponse = State.AwaitingResponse.ToList(),
                PendingRollValue = State.PendingRoll?.Value,
                MyPendingDecisions = State.PendingDecisions
                    .Where(d => d.PlayerId == viewerId)
                    .Select(d => d.Kind).ToList(),
                Winners = State.Winners.ToList(),
            };

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
