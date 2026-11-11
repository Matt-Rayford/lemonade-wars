using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The board band: the viewed player's VP column, turf and stands, with the cards
    // tucked behind them.
    public sealed partial class TableView
    {
        private const float BoardCardWidthPx = 188f;
        private const float BoardCardHeightPx = 263f;
        /// <summary>How much of a tucked card shows above the card in front: its top icon bar, so five fit in the band's headroom.</summary>
        private const float TuckPeek = 40f;
        private const float TuckedSlideSeconds = 0.18f;

        /// <summary>True for an equipped card whose flight onto the board is still in the air: it holds
        /// its tucked place, drawn invisible, until the flight lands.</summary>
        public System.Func<int, bool> EquippedCardInvisible;

        /// <summary>Each tucked card's height in its stack as last drawn, by instance id.</summary>
        private readonly Dictionary<int, float> _tuckedCardHeights = new Dictionary<int, float>();
        /// <summary>The cell each equipped card belongs to, picked-for-sale cards included.</summary>
        private readonly Dictionary<int, RectTransform> _equipHomeCells = new Dictionary<int, RectTransform>();

        private RectTransform _boardPanel;
        private RectTransform _boardRow;
        private RectMask2D _boardMask;
        private TMP_Text _boardOwnerLabel;
        private bool _viewingOwnBoard = true;
        private RectTransform _vpCell;
        private RectTransform _turfCell;
        /// <summary>Your own stands, left to right; empty while a rival's board is shown.</summary>
        private readonly List<RectTransform> _standCells = new List<RectTransform>();

        private void RenderBoard(PlayerView view)
        {
            UiKit.Clear(_boardRow);
            _equipCells.Clear();
            _equipHomeCells.Clear();
            UnbindEquipAimFromOldBoardCells();
            _dropGlows.Clear();
            _dropCells.Clear();
            _standCells.Clear();
            _insertSpacer = null; // destroyed with the row
            _insertSpacerTween = null;
            _supplyInsertIndex = -1;

            var owner = SelectBoardOwner(view);
            BuildVictoryColumn(owner);
            BuildTurfCell(owner);
            BuildStandCells(owner);
            EndEquipAimWithoutBoardTargets();
        }

        /// <summary>An equip aim survives the rebuild (a pacing revision must not eject the player mid-pick);
        /// its hover re-binds to the fresh cells on the next tick.</summary>
        private void UnbindEquipAimFromOldBoardCells()
        {
            if (_targetingPick == null)
            {
                return;
            }
            _targetingHover = null;
            _targetingHoverAny = false;
            UiKit.Clear(_targetingCopiesHost);
            UiKit.Clear(_targetingArrowHost);
        }

        /// <summary>Your own board interacts; a rival's is view-only, with no drop targets or insert preview.</summary>
        private PlayerView.PlayerPanel SelectBoardOwner(PlayerView view)
        {
            int boardOwnerId = ViewedBoardPlayer >= 0 ? ViewedBoardPlayer : view.ViewerId;
            if (boardOwnerId >= view.Players.Count)
            {
                boardOwnerId = view.ViewerId; // stale selection from a previous game
            }
            _viewingOwnBoard = boardOwnerId == view.ViewerId;
            var owner = view.Players[boardOwnerId];
            _boardOwnerLabel.text = _viewingOwnBoard ? "" : $"{owner.Name}'s board — view only";
            return owner;
        }

        private void BuildVictoryColumn(PlayerView.PlayerPanel owner)
        {
            var victoryArts = VictoryCardArts(owner);
            int scoredVp = owner.InGameVictoryPoints + owner.FinishedLords.Count(lord => lord.Met);
            RectTransform vpCell;
            if (victoryArts.Count == 0)
            {
                vpCell = AddVictoryPlaceholder(_boardRow);
            }
            else
            {
                vpCell = AddBoardCard(_boardRow, victoryArts[0], scoredVp == 1 ? "1 VP" : $"{scoredVp} VP");
                AddTuckedStack(vpCell, victoryArts.Skip(1).ToList());
            }
            _vpGlow = null;
            _vpCell = vpCell;
            if (_viewingOwnBoard)
            {
                MakeBraggingDropTarget(vpCell);
            }
        }

        private List<Texture2D> VictoryCardArts(PlayerView.PlayerPanel owner)
        {
            var arts = new List<Texture2D>();
            foreach (string titleId in owner.FirstDibsClaimed)
            {
                arts.Add(_art.Title(titleId));
            }
            for (int i = 0; i < owner.BraggingRights; i++)
            {
                arts.Add(_art.BraggingRights(i) ?? _art.Back("braggingRights"));
            }
            // FinishedLords is empty until the game ends. Only MET lords join: an unmet lord
            // in the stack would read as points that were never scored.
            foreach (var lord in owner.FinishedLords)
            {
                if (lord.Met)
                {
                    arts.Add(_art.Title(lord.TitleId));
                }
            }
            return arts;
        }

        private void BuildTurfCell(PlayerView.PlayerPanel owner)
        {
            var turfTexture = _art.Turf(owner.TurfPowerPourNumber);
            var turfCaption = "Pours " + string.Join(",", owner.PourNumbers);
            var turfCell = AddBoardCard(_boardRow, turfTexture, turfCaption);
            _turfCell = turfCell;
            TuckEquippedCards(turfCell, owner.TurfEquipped);
            if (_viewingOwnBoard)
            {
                MakeDropTarget(turfCell, null, turfTexture);
            }
        }

        private void BuildStandCells(PlayerView.PlayerPanel owner)
        {
            foreach (var stand in owner.Stands)
            {
                string caption = $"[{string.Join(",", stand.SaleNumbers)}] ${stand.Earnings}";
                var standTexture = _art.Stand(stand.StandTypeId, stand.Shape);
                var cell = AddBoardCard(_boardRow, standTexture, caption);
                TuckEquippedCards(cell, stand.Equipped);
                if (_viewingOwnBoard)
                {
                    MakeDropTarget(cell, stand.InstanceId, standTexture);
                    _standCells.Add(cell);
                }
            }
        }

        /// <summary>A kept equip aim ends once the rebuilt board shows none of its targets. Silently: no
        /// cancel callback fires, so the modal pipeline can re-offer the choice.</summary>
        private void EndEquipAimWithoutBoardTargets()
        {
            if (_targetingPick == null)
            {
                return;
            }
            bool anyValidTarget = _targetingEquipValid != null
                ? _equipCells.Any(strip => _targetingEquipValid.Contains(strip.Id))
                : _dropCells.Any(cell => _targetingValid.Contains(cell.Id));
            if (!anyValidTarget)
            {
                EndEquipTargeting();
            }
        }

        /// <summary>Tucked stacks and flight anchors find the card inside the returned cell by its child name, "Card".</summary>
        private RectTransform AddBoardCard(RectTransform parent, Texture2D texture, string caption)
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
            cellElement.preferredWidth = BoardCardWidthPx;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var image = UiKit.CreateCardImage((RectTransform)cell.transform, texture,
                BoardCardWidthPx, BoardCardHeightPx);
            _preview.Attach(image.gameObject, texture);

            if (!string.IsNullOrEmpty(caption))
            {
                UiKit.CreateBadge((RectTransform)cell.transform, caption, 13,
                    new Color(0, 0, 0, 0.55f)).color = Color.white;
            }
            return (RectTransform)cell.transform;
        }

        private void TuckEquippedCards(RectTransform cell, List<PlayerView.CardInfo> allEquipped)
        {
            foreach (var card in allEquipped)
            {
                _equipHomeCells[card.InstanceId] = cell;
            }
            // A card picked for a sale lies on the staged pile, so the stack closes up without it.
            var equipped = allEquipped.Where(card => !_rummagePicked.Contains(card.InstanceId)).ToList();
            var tuckedCards = AddTuckedStack(cell, equipped
                .Select(card => _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square))
                .ToList());
            for (int i = 0; i < tuckedCards.Count; i++)
            {
                SlideTuckedCardFromLastHeight(tuckedCards[i], equipped[i].InstanceId);
            }
            for (int i = 0; i < tuckedCards.Count; i++)
            {
                if (tuckedCards[i] != null && EquippedCardInvisible?.Invoke(equipped[i].InstanceId) == true)
                {
                    tuckedCards[i].GetComponent<RawImage>().color = new Color(1f, 1f, 1f, 0f);
                }
            }
            for (int i = 0; i < tuckedCards.Count; i++)
            {
                AddAimStrip(tuckedCards[i], equipped[i].InstanceId);
            }
        }

        /// <summary>
        /// Cards tucked behind a cell's front card, each peeking its top strip out above it.
        /// Returns their rects index-aligned with the input (all null when the cell has no front card).
        /// </summary>
        /// <summary>The stack is rebuilt in its new order on every render; a card whose height changed eases there.</summary>
        private void SlideTuckedCardFromLastHeight(RectTransform tuckedCard, int instanceId)
        {
            if (tuckedCard == null)
            {
                return;
            }
            var restingPosition = tuckedCard.anchoredPosition;
            if (_tuckedCardHeights.TryGetValue(instanceId, out float lastHeight) &&
                !Mathf.Approximately(lastHeight, restingPosition.y))
            {
                tuckedCard.anchoredPosition = new Vector2(restingPosition.x, lastHeight);
                UiTween.SlideTo(tuckedCard, restingPosition, TuckedSlideSeconds);
            }
            _tuckedCardHeights[instanceId] = restingPosition.y;
        }

        private List<RectTransform> AddTuckedStack(RectTransform cell, List<Texture2D> textures)
        {
            var tuckedCards = new List<RectTransform>(new RectTransform[textures.Count]);
            var cardFrame = cell.Find("Card");
            if (cardFrame == null)
            {
                return tuckedCards;
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
                rect.sizeDelta = new Vector2(BoardCardWidthPx, BoardCardHeightPx);
                rect.anchoredPosition = new Vector2(0, TuckPeek * (i + 1));
                var image = go.GetComponent<RawImage>();
                image.texture = texture;
                image.material = UiKit.RoundedImageMaterial(BoardCardWidthPx, BoardCardHeightPx);
                _preview.Attach(go, texture);
                tuckedCards[i] = rect;
            }
            return tuckedCards;
        }

        private RectTransform AddVictoryPlaceholder(RectTransform parent)
        {
            var green = new Color(0.45f, 0.85f, 0.50f, 0.85f);

            // Mirrors AddBoardCard's cell shape so the row lays out identically.
            var cell = new GameObject("VpPlaceholder", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(LayoutElement));
            cell.transform.SetParent(parent, false);
            var layout = cell.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = BoardCardWidthPx;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            // Invisible but raycastable: dropped Bragging Rights must land somewhere.
            var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image),
                typeof(LayoutElement));
            frameGo.transform.SetParent(cell.transform, false);
            frameGo.GetComponent<LayoutElement>().preferredHeight = BoardCardHeightPx;
            frameGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var frame = (RectTransform)frameGo.transform;

            AddDashedRoundedOutline(frame, BoardCardWidthPx - 4f, BoardCardHeightPx - 4f,
                UiKit.CardCornerRadius(BoardCardWidthPx), green);

            var label = UiKit.CreateText(frame, "Victory\nPoints", 24,
                TextAnchor.MiddleCenter, green);
            label.raycastTarget = false;
            UiKit.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one);

            // A caption-height footer lines the placeholder's bottom edge up with its captioned neighbours.
            UiKit.CreateBadge((RectTransform)cell.transform, "0 VP", 13,
                new Color(0, 0, 0, 0.55f)).color = Color.white;
            return (RectTransform)cell.transform;
        }
    }
}
