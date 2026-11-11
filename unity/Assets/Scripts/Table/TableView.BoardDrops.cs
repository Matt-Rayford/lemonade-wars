using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Everything that lands ON the board: market-card drops on the turf and stands, the
    // supply-stand insert preview, and the Bragging Rights drop on the VP column.
    public sealed partial class TableView
    {
        /// <summary>Market index, then the stand's instance id (null for the turf).</summary>
        public System.Action<int, int?> OnMarketDrop;
        /// <summary>Stand type id, then its insert index among your stands.</summary>
        public System.Action<string, int> OnSupplyDrop;
        public System.Action OnBraggingDrop;

        /// <summary>Extra size of a board cell's wide halo and hot ring, both sides together.</summary>
        private const float DropGlowWidePx = 45f;
        private const float DropGlowHotPx = 21f;
        private const float InsertPreviewGapPx = 162f;

        private readonly List<(int? StandId, GameObject Glow)> _dropGlows =
            new List<(int?, GameObject)>();
        private readonly HashSet<int?> _validDropTargets = new HashSet<int?>();

        private GameObject _insertSpacer;
        private LayoutWidthTween _insertSpacerTween;
        private int _supplyInsertIndex = -1;
        private bool _supplyDragActive;

        // ------------------------------------------- supply drag insertion

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
                    // The middle of a stand keeps the current slot, so the preview does not flicker there.
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
            _insertSpacerTween.SetTarget(InsertPreviewGapPx);
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
                return;
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
            var wide = UiKit.CreateGlow(cell, top, top, new Vector2(0, DropGlowWidePx / 2f),
                BoardCardWidthPx + DropGlowWidePx, BoardCardHeightPx + DropGlowWidePx, DropGlowWide);
            wide.transform.SetAsFirstSibling();
            var hot = UiKit.CreateGlow(cell, top, top, new Vector2(0, DropGlowHotPx / 2f),
                BoardCardWidthPx + DropGlowHotPx, BoardCardHeightPx + DropGlowHotPx, DropGlowHot);
            hot.transform.SetSiblingIndex(1);

            _dropGlows.Add((standInstanceId, wide));
            _dropGlows.Add((standInstanceId, hot));
            _dropCells.Add((standInstanceId, cell, art));
        }

        private void MakeBraggingDropTarget(RectTransform vpCell)
        {
            var vpFrame = (RectTransform)(vpCell.Find("Card") ?? vpCell.Find("Frame"));
            if (vpFrame != null)
            {
                _vpGlow = UiKit.CreateGlow(vpFrame, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero,
                    BoardCardWidthPx + DropGlowHotPx, BoardCardHeightPx + DropGlowHotPx, DropGlowHot);
            }
            vpCell.gameObject.AddComponent<DropTarget>().BraggingDropped =
                () => OnBraggingDrop?.Invoke();
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
                // Reachable from DragSource.OnDisable during a re-render teardown,
                // when these glows may already be destroyed.
                if (glow != null)
                {
                    glow.SetActive(false);
                }
            }
        }
    }
}
