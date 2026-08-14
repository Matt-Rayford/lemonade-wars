using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The board band: the viewed player's VP column, turf and stands, its edge
    // scrolling, and everything that lands ON it — market-card drops, the supply-stand
    // insert preview, and the Bragging Rights drop.
    public sealed partial class TableView
    {
        public System.Action<int, int?> OnMarketDrop;       // market index, stand id (null = turf)
        public System.Action<string, int> OnSupplyDrop;     // stand type, insert index
        public System.Action OnBraggingDrop;

        private RectTransform _boardPanel;
        private RectTransform _boardRow;
        private RectMask2D _boardMask;
        private float _boardScroll;
        private TMP_Text _boardOwnerLabel;
        private bool _viewingOwnBoard = true;

        private readonly List<(int? StandId, GameObject Glow)> _dropGlows =
            new List<(int?, GameObject)>();
        private readonly HashSet<int?> _validDropTargets = new HashSet<int?>();

        // Supply-drag insertion preview.
        private readonly List<RectTransform> _standCells = new List<RectTransform>();
        private GameObject _insertSpacer;
        private LayoutWidthTween _insertSpacerTween;
        private int _supplyInsertIndex = -1;
        private bool _supplyDragActive;

        private void RenderBoard(PlayerView view)
        {
            UiKit.Clear(_boardRow);
            _equipCells.Clear(); // strips live on the board's tucked cards
            if (_targetingPick != null)
            {
                // The board is being rebuilt under the targeting layer: dismiss it.
                EndEquipTargeting();
            }
            _dropGlows.Clear();
            _dropCells.Clear();
            _standCells.Clear();
            _insertSpacer = null; // destroyed with the row; recreated lazily
            _insertSpacerTween = null;
            _supplyInsertIndex = -1;

            // Whose board: yours interacts; an opponent's is strictly view-only —
            // no drop targets, no insert previews, nothing to submit against.
            int boardOwnerId = ViewedBoardPlayer >= 0 ? ViewedBoardPlayer : view.ViewerId;
            if (boardOwnerId >= view.Players.Count)
            {
                boardOwnerId = view.ViewerId; // stale selection from a previous game
            }
            _viewingOwnBoard = boardOwnerId == view.ViewerId;
            var owner = view.Players[boardOwnerId];
            _boardOwnerLabel.text = _viewingOwnBoard ? "" : $"{owner.Name}'s board — view only";

            // Victory Point cards (First Dibs titles + Bragging Rights) live in their
            // own column LEFT of the Turf; extras tuck behind the front card the way
            // upgrades tuck behind stands. Empty: a dashed drop-zone style placeholder.
            var vpTextures = new List<Texture2D>();
            foreach (string titleId in owner.FirstDibsClaimed)
            {
                vpTextures.Add(_art.Title(titleId));
            }
            for (int i = 0; i < owner.BraggingRights; i++)
            {
                vpTextures.Add(_art.BraggingRights(i) ?? _art.Back("braggingRights"));
            }
            // Game over: everyone's secret Lemon Lords are revealed (FinishedLords is
            // empty until then), so they join the stack — click a rival's bar and their
            // lords are right there with the rest of their points. Only MET lords join:
            // an unmet lord in the VP stack reads as points that were never scored.
            foreach (var lord in owner.FinishedLords)
            {
                if (lord.Met)
                {
                    vpTextures.Add(_art.Title(lord.TitleId));
                }
            }
            int scoredVp = owner.InGameVictoryPoints + owner.FinishedLords.Count(l => l.Met);
            RectTransform vpCell;
            if (vpTextures.Count == 0)
            {
                vpCell = AddVictoryPlaceholder(_boardRow);
            }
            else
            {
                vpCell = AddCard(_boardRow, vpTextures[0], 188, 263,
                    scoredVp == 1 ? "1 VP" : $"{scoredVp} VP", false, null);
                AddTuckedStack(vpCell, vpTextures.Skip(1).ToList());
            }
            _vpGlow = null;
            if (_viewingOwnBoard)
            {
                // Bragging Rights dragged from the shelf land here.
                var vpFrame = (RectTransform)(vpCell.Find("Card") ?? vpCell.Find("Frame"));
                if (vpFrame != null)
                {
                    _vpGlow = UiKit.CreateGlow(vpFrame, new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f), Vector2.zero, 188 + 26, 263 + 26, DropGlowHot);
                }
                vpCell.gameObject.AddComponent<DropTarget>().BraggingDropped =
                    () => OnBraggingDrop?.Invoke();
            }

            var turfTexture = _art.Turf(owner.TurfPowerPourNumber);
            var turfCaption = "Pours " + string.Join(",", owner.PourNumbers);
            var turfCell = AddCard(_boardRow, turfTexture, 188, 263, turfCaption, false, null);
            AddEquipList(turfCell, owner.TurfEquipped);
            if (_viewingOwnBoard)
            {
                MakeDropTarget(turfCell, null, turfTexture);
            }

            foreach (var stand in owner.Stands)
            {
                string caption = $"[{string.Join(",", stand.SaleNumbers)}] ${stand.Earnings}";
                var standTexture = _art.Stand(stand.StandTypeId, stand.Shape);
                var cell = AddCard(_boardRow, standTexture, 188, 263, caption, false, null);
                AddEquipList(cell, stand.Equipped);
                if (_viewingOwnBoard)
                {
                    MakeDropTarget(cell, stand.InstanceId, standTexture);
                    _standCells.Add(cell);
                }
            }
        }

        /// <summary>
        /// Same disappearing edge-scroll as the hand, for a board with 6+ stands:
        /// hover near the band's left/right edge to slide the row. Call every frame.
        /// </summary>
        public void TickBoardScroll(Vector2 screenPosition)
        {
            if (_boardPanel == null || _boardRow == null)
            {
                return;
            }
            float overflow = _boardRow.rect.width + 12f - _boardPanel.rect.width;
            // The hand-style faded edge doubles as the "this scrolls" cue.
            var softness = overflow > 0f ? new Vector2Int(70, 0) : Vector2Int.zero;
            if (_boardMask.softness != softness)
            {
                _boardMask.softness = softness;
            }
            if (overflow <= 0f)
            {
                if (_boardScroll != 0f)
                {
                    _boardScroll = 0f;
                    ApplyBoardScroll();
                }
                return;
            }
            _boardScroll = Mathf.Clamp(_boardScroll, 0f, overflow);
            if (!RectTransformUtility.RectangleContainsScreenPoint(_boardPanel, screenPosition))
            {
                return;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _boardPanel, screenPosition, null, out var local);
            var rect = _boardPanel.rect;
            float fraction = (local.x - rect.xMin) / Mathf.Max(1f, rect.width);
            const float zone = 0.15f;
            float velocity = 0f;
            if (fraction < zone)
            {
                velocity = -Mathf.InverseLerp(zone, 0f, fraction);
            }
            else if (fraction > 1f - zone)
            {
                velocity = Mathf.InverseLerp(1f - zone, 1f, fraction);
            }
            if (velocity == 0f)
            {
                return;
            }
            _boardScroll = Mathf.Clamp(
                _boardScroll + velocity * 700f * Time.deltaTime, 0f, overflow);
            ApplyBoardScroll();
        }

        private void ApplyBoardScroll()
        {
            var position = _boardRow.anchoredPosition;
            position.x = 6f - _boardScroll;
            _boardRow.anchoredPosition = position;
        }

        // ------------------------------------------- supply drag insertion

        /// <summary>Called every frame by the app while a supply stand is being dragged.</summary>
        public void TickSupplyDrag(Vector2 screenPosition)
        {
            if (!_supplyDragActive || !_viewingOwnBoard)
            {
                return;
            }
            if (!RectTransformUtility.RectangleContainsScreenPoint(_boardPanel, screenPosition))
            {
                HideInsertPreview();
                return;
            }
            int index = ComputeInsertIndex(screenPosition);
            if (index != _supplyInsertIndex)
            {
                _supplyInsertIndex = index;
                ShowInsertPreview(index);
            }
        }

        private int ComputeInsertIndex(Vector2 screenPosition)
        {
            for (int i = 0; i < _standCells.Count; i++)
            {
                var cell = _standCells[i];
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    cell, screenPosition, null, out var local);
                float fraction = (local.x - cell.rect.xMin) / Mathf.Max(1f, cell.rect.width);
                if (fraction < 0f)
                {
                    return i;
                }
                if (fraction <= 1f)
                {
                    if (fraction < 0.4f)
                    {
                        return i;
                    }
                    if (fraction > 0.6f)
                    {
                        return i + 1;
                    }
                    return _supplyInsertIndex >= 0 ? _supplyInsertIndex : i;
                }
            }
            return _standCells.Count;
        }

        private void ShowInsertPreview(int index)
        {
            CollapseActiveSpacer();

            _insertSpacer = new GameObject("InsertSpacer", typeof(RectTransform),
                typeof(LayoutElement), typeof(LayoutWidthTween));
            _insertSpacer.transform.SetParent(_boardRow, false);
            var element = _insertSpacer.GetComponent<LayoutElement>();
            element.preferredWidth = 0;
            element.preferredHeight = 10;
            element.flexibleWidth = 0;

            int sibling = index < _standCells.Count
                ? _standCells[index].GetSiblingIndex()
                : _boardRow.childCount - 1;
            _insertSpacer.transform.SetSiblingIndex(sibling);

            _insertSpacerTween = _insertSpacer.GetComponent<LayoutWidthTween>();
            _insertSpacerTween.SetTarget(162f);
        }

        private void CollapseActiveSpacer()
        {
            if (_insertSpacerTween != null)
            {
                _insertSpacerTween.SetTarget(0f, destroyAtZero: true);
                _insertSpacer = null;
                _insertSpacerTween = null;
            }
        }

        private void HideInsertPreview()
        {
            _supplyInsertIndex = -1;
            CollapseActiveSpacer();
        }

        private void HandleSupplyDrop(string standTypeId)
        {
            if (!_viewingOwnBoard)
            {
                return; // you can't build on someone else's turf
            }
            int index = _supplyInsertIndex >= 0 ? _supplyInsertIndex : _standCells.Count;
            OnSupplyDrop?.Invoke(standTypeId, index);
        }

        // --------------------------------------------------------- drop zones

        private void MakeDropTarget(RectTransform cell, int? standInstanceId, Texture2D art)
        {
            var target = cell.gameObject.AddComponent<DropTarget>();
            target.StandInstanceId = standInstanceId;
            target.Dropped = (marketIndex, standId) => OnMarketDrop?.Invoke(marketIndex, standId);
            target.SupplyDropped = HandleSupplyDrop;
            target.HoverChanged = OnDropTargetHover;

            var top = new Vector2(0.5f, 1f);
            var wide = UiKit.CreateGlow(cell, top, top, new Vector2(0, 30),
                188 + 60, 263 + 60, DropGlowWide);
            wide.transform.SetAsFirstSibling();
            var hot = UiKit.CreateGlow(cell, top, top, new Vector2(0, 14),
                188 + 28, 263 + 28, DropGlowHot);
            hot.transform.SetSiblingIndex(1);

            _dropGlows.Add((standInstanceId, wide));
            _dropGlows.Add((standInstanceId, hot));
            _dropCells.Add((standInstanceId, cell, art));
        }

        public void SetValidDropTargets(ISet<int?> validTargets)
        {
            _validDropTargets.Clear();
            foreach (var id in validTargets)
            {
                _validDropTargets.Add(id);
            }
        }

        private void OnDropTargetHover(int? standId, bool hovering)
        {
            bool show = hovering && _validDropTargets.Contains(standId);
            foreach (var (id, glow) in _dropGlows)
            {
                if (Equals(id, standId))
                {
                    glow.SetActive(show);
                }
            }
        }

        public void ClearDropHighlights()
        {
            _validDropTargets.Clear();
            foreach (var (_, glow) in _dropGlows)
            {
                glow.SetActive(false);
            }
        }

        // ----------------------------------------------------------- helpers

        private RectTransform AddCard(RectTransform parent, Texture2D texture,
            float width, float height, string caption, bool clickable, System.Action onClick)
        {
            var cell = new GameObject("Cell", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(LayoutElement));
            cell.transform.SetParent(parent, false);
            var layout = cell.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var image = UiKit.CreateCardImage((RectTransform)cell.transform, texture, width, height);
            _preview.Attach(image.gameObject, texture);
            if (clickable && onClick != null)
            {
                UiKit.AddClick(image.gameObject, () => onClick());
            }

            if (!string.IsNullOrEmpty(caption) || clickable)
            {
                UiKit.CreateBadge((RectTransform)cell.transform, caption, 13,
                    clickable ? UiKit.ButtonColor : new Color(0, 0, 0, 0.55f))
                    .color = clickable ? UiKit.ButtonTextColor : Color.white;
            }
            return (RectTransform)cell.transform;
        }

        /// <summary>
        /// Equipped Black Market cards drawn as real cards tucked BEHIND the turf/stand:
        /// each peeks its top strip out above the card, stacked like the physical game.
        /// Hover a peeking strip for the enlarged preview.
        /// </summary>
        private void AddEquipList(RectTransform cell, List<PlayerView.CardInfo> equipped)
        {
            var rects = AddTuckedStack(cell, equipped
                .Select(e => _art.BlackMarket(e.DefId, e.Shape ?? Shape.Square))
                .ToList());
            // Each tucked card's visible peek strip becomes an aim target of its own
            // (That's Not Fair / Finders Keepers can hit the card, not just the bar).
            for (int i = 0; i < rects.Count; i++)
            {
                var stripGo = new GameObject("AimStrip", typeof(RectTransform));
                stripGo.transform.SetParent(rects[i], false);
                var strip = (RectTransform)stripGo.transform;
                UiKit.Anchor(strip, new Vector2(0, 1), new Vector2(1, 1),
                    new Vector2(0, -TuckPeek), new Vector2(0, 0));
                var glow = UiKit.CreateGlow(strip, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, 188f + 26f, TuckPeek + 26f,
                    AttackGlowColor);
                // Ring, not fill: the strip IS the card's face — a solid glow would
                // paint over exactly what the player is trying to aim at.
                glow.GetComponent<Image>().sprite = UiSprites.GlowRing;
                glow.SetActive(false);
                _equipCells.Add((equipped[i].InstanceId, strip, glow));
            }
        }

        /// <summary>
        /// Cards tucked BEHIND a cell's front card: each peeks its top strip out above,
        /// stacked like the physical game. Hover a peeking strip for the preview.
        /// Returns the created card rects, index-aligned with the input.
        /// </summary>
        private const float TuckPeek = 40f; // the top icon bar, minus a hair — 5 stacks fit

        private List<RectTransform> AddTuckedStack(RectTransform cell, List<Texture2D> textures)
        {
            const float width = 188f; // same scale as the card it tucks behind
            const float height = 263f;
            var rects = new List<RectTransform>(new RectTransform[textures.Count]);
            var cardFrame = cell.Find("Card");
            if (cardFrame == null)
            {
                return rects;
            }
            // Deepest card first; each next one is inserted just before the front card,
            // so it draws over the slivers behind it and under the front card itself.
            for (int i = textures.Count - 1; i >= 0; i--)
            {
                var texture = textures[i];
                var go = new GameObject("Tucked", typeof(RectTransform), typeof(RawImage),
                    typeof(LayoutElement));
                go.GetComponent<LayoutElement>().ignoreLayout = true;
                go.transform.SetParent(cell, false);
                go.transform.SetSiblingIndex(cardFrame.GetSiblingIndex());
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(width, height);
                rect.anchoredPosition = new Vector2(0, TuckPeek * (i + 1));
                var image = go.GetComponent<RawImage>();
                image.texture = texture;
                image.material = UiKit.RoundedImageMaterial(width, height);
                _preview.Attach(go, texture);
                rects[i] = rect;
            }
            return rects;
        }

        /// <summary>The empty VP column: a green dashed frame naming what goes there.</summary>
        private RectTransform AddVictoryPlaceholder(RectTransform parent)
        {
            const float width = 188f;
            const float height = 263f;
            var green = new Color(0.45f, 0.85f, 0.50f, 0.85f);

            // Mirrors AddCard's cell shape so the row lays out identically.
            var cell = new GameObject("VpPlaceholder", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(LayoutElement));
            cell.transform.SetParent(parent, false);
            var layout = cell.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            // Invisible but raycastable: dropped Bragging Rights must land somewhere.
            var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image),
                typeof(LayoutElement));
            frameGo.transform.SetParent(cell.transform, false);
            frameGo.GetComponent<LayoutElement>().preferredHeight = height;
            frameGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var frame = (RectTransform)frameGo.transform;

            AddDashedRoundedOutline(frame, width - 4f, height - 4f,
                UiKit.CardCornerRadius(width), green);

            var label = UiKit.CreateText(frame, "Victory\nPoints", 24,
                TextAnchor.MiddleCenter, green);
            label.raycastTarget = false;
            UiKit.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one);

            // Caption-height footer so the placeholder's bottom edge lines up with
            // its captioned neighbors in the bottom-aligned board row.
            UiKit.CreateBadge((RectTransform)cell.transform, "0 VP", 13,
                new Color(0, 0, 0, 0.55f)).color = Color.white;
            return (RectTransform)cell.transform;
        }
    }
}
