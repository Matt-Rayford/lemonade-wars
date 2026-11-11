using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // World positions on the shelf that flights lift from and land on, and the art a
    // held or gliding shelf card wears.
    public sealed partial class TableView
    {
        /// <summary>Shelf cards by market slot index, including placeholder sockets, so a slot's spot is known while empty.</summary>
        private readonly Dictionary<int, RectTransform> _marketSlotAnchors =
            new Dictionary<int, RectTransform>();
        private readonly Dictionary<string, RectTransform> _supplyAnchors =
            new Dictionary<string, RectTransform>();
        private RectTransform _braggingAnchor;
        private RectTransform _lemonPileAnchor;
        /// <summary>Swept cards go to it and dealt cards come from it.</summary>
        private RectTransform _blackMarketDeckAnchor;

        private readonly Dictionary<string, Texture2D> _supplyArt = new Dictionary<string, Texture2D>();
        public Texture2D BraggingArt { get; private set; }

        /// <summary>The art on top of a supply pile right now; null once it is sold out.</summary>
        public Texture2D SupplyArtOf(string standTypeId) =>
            _supplyArt.TryGetValue(standTypeId, out var art) ? art : null;

        /// <summary>The whole row is being destroyed: its anchors and art go with it.</summary>
        private void ForgetShelfAnchors()
        {
            _marketSlotAnchors.Clear();
            _supplyAnchors.Clear();
            _braggingAnchor = null;
            _lemonPileAnchor = null;
            BraggingArt = null;
            _supplyArt.Clear();
            _blackMarketDeckAnchor = null;
        }

        /// <summary>Valid for card and placeholder cells alike; null when the slot does not exist (the
        /// market shrank with an empty deck).</summary>
        public Vector3? MarketSlotWorld(int index) =>
            _marketSlotAnchors.TryGetValue(index, out var cell) ? CellCenterWorld(cell) : null;

        public Vector3? StandSupplyWorld(string standTypeId) =>
            _supplyAnchors.TryGetValue(standTypeId, out var cell) ? CellCenterWorld(cell) : null;

        /// <summary>Null once Bragging Rights are sold out.</summary>
        public Vector3? BraggingShelfWorld() => CellCenterWorld(_braggingAnchor);

        public Vector3? BlackMarketDeckWorld() => CellCenterWorld(_blackMarketDeckAnchor);

        public Vector3? LemonPileWorld() => CellCenterWorld(_lemonPileAnchor);

        /// <summary>Effects resolve origins in the same frame the shelf is rebuilt, before the layout
        /// group has placed its children: settle the row first.</summary>
        private Vector3? CellCenterWorld(RectTransform cell)
        {
            if (cell == null) // also true for a destroyed cell mid-teardown
            {
                return null;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_marketRow);
            return cell.TransformPoint(cell.rect.center);
        }
    }
}
