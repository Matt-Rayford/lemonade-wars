using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // First Dibs before the game: the Random / Manual choice on the setup screens, and the
    // screen where a host on Manual picks the row from all the First Dibs cards.
    public sealed partial class LobbyUi
    {
        /// <summary>Every First Dibs card with its art, in catalogue order. Set by the app, which owns the card data.</summary>
        public System.Func<IReadOnlyList<(string TitleId, Texture2D Art)>> FirstDibsCards;
        /// <summary>Hovering a card in the picker enlarges it, as on the table.</summary>
        public CardPreview Preview;

        public bool FirstDibsPickerOpen => _dibsPickRoot.gameObject.activeSelf;

        private const int PickerColumns = 8;
        private static readonly Vector2 PickerCellPx = new Vector2(183f, 252f); // a 5:7 card inside the rim
        /// <summary>The yellow of a picked card's cell shows around its art by this much.</summary>
        private const float PickedRimPx = 6f;
        private static readonly Color PassedOverTint = new Color(0.4f, 0.4f, 0.45f);

        private RectTransform _lobbyDibsModeRow;
        private RectTransform _soloDibsModeRow;
        private bool _soloPicksFirstDibs;

        private RectTransform _dibsPickRoot;
        private RectTransform _dibsPickGrid;
        private RectTransform _dibsPickActions;
        private TMP_Text _dibsPickTally;
        private readonly List<(string TitleId, Image Cell, RawImage Art)> _dibsPickCells =
            new List<(string, Image, RawImage)>();
        private readonly List<string> _dibsPicked = new List<string>();
        private int _dibsNeeded;
        private System.Action<IReadOnlyList<string>> _onDibsAccepted;
        private System.Action _onDibsPickBack;

        /// <summary>A null <paramref name="onChange"/> shows the setting without letting it be changed.</summary>
        private static void ShowFirstDibsModes(RectTransform row, bool isManual, System.Action<bool> onChange)
        {
            UiKit.Clear(row);
            AddChoice(row, "RANDOM", !isManual,
                onChange != null && isManual ? () => onChange(false) : (System.Action)null);
            AddChoice(row, "MANUAL", isManual,
                onChange != null && !isManual ? () => onChange(true) : (System.Action)null);
        }

        // ------------------------------------------------------- the picker

        private void BuildFirstDibsPicker(RectTransform canvasRoot)
        {
            _dibsPickRoot = UiKit.CreatePanel(canvasRoot, "FirstDibsPicker", UiKit.TableColor);
            UiKit.Anchor(_dibsPickRoot, Vector2.zero, Vector2.one);
            CreateScreenTitle(_dibsPickRoot, "FIRST DIBS");

            _dibsPickTally = UiKit.CreateText(_dibsPickRoot, "", 24,
                TextAnchor.MiddleCenter, ChoiceHeaderColor);
            UiKit.Anchor((RectTransform)_dibsPickTally.transform, new Vector2(0.1f, 0.735f), new Vector2(0.9f, 0.785f));

            var gridGo = new GameObject("Cards", typeof(RectTransform), typeof(GridLayoutGroup));
            gridGo.transform.SetParent(_dibsPickRoot, false);
            _dibsPickGrid = (RectTransform)gridGo.transform;
            UiKit.Anchor(_dibsPickGrid, new Vector2(0.03f, 0.18f), new Vector2(0.97f, 0.725f));
            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = PickerCellPx;
            grid.spacing = new Vector2(ButtonGapPx, ButtonGapPx);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = PickerColumns;
            grid.childAlignment = TextAnchor.MiddleCenter;

            float stackTopPx = 0f;
            _dibsPickActions = CreateButtonRow(_dibsPickRoot, "PickerActions", Vector2.zero, Vector2.one);
            StackOnBaseline(_dibsPickActions, MenuButtonHeightPx, ref stackTopPx);
            _dibsPickRoot.gameObject.SetActive(false);
        }

        /// <summary>Opens with nothing picked. <paramref name="onAccept"/> gets the row in the order it was picked.</summary>
        public void ShowFirstDibsPicker(int neededCount, System.Action<IReadOnlyList<string>> onAccept,
            System.Action onBack)
        {
            ActivateOnly(_dibsPickRoot);
            _dibsNeeded = neededCount;
            _onDibsAccepted = onAccept;
            _onDibsPickBack = onBack;
            _dibsPicked.Clear();

            UiKit.Clear(_dibsPickGrid);
            _dibsPickCells.Clear();
            foreach (var (titleId, art) in FirstDibsCards())
            {
                AddPickerCard(titleId, art);
            }
            ShowPickerState();
        }

        /// <summary>The table changed size while the host was choosing: the newest picks beyond the new row are dropped.</summary>
        public void SetFirstDibsNeeded(int neededCount)
        {
            if (neededCount == _dibsNeeded)
            {
                return;
            }
            _dibsNeeded = neededCount;
            if (_dibsPicked.Count > neededCount)
            {
                _dibsPicked.RemoveRange(neededCount, _dibsPicked.Count - neededCount);
            }
            ShowPickerState();
        }

        private void AddPickerCard(string titleId, Texture2D art)
        {
            var cellGo = new GameObject("Card", typeof(RectTransform), typeof(Image));
            cellGo.transform.SetParent(_dibsPickGrid, false);
            var cell = cellGo.GetComponent<Image>();
            cell.sprite = UiSprites.RoundedRect;
            cell.type = Image.Type.Sliced;

            var artGo = new GameObject("Art", typeof(RectTransform), typeof(RawImage));
            artGo.transform.SetParent(cellGo.transform, false);
            UiKit.Anchor((RectTransform)artGo.transform, Vector2.zero, Vector2.one,
                new Vector2(PickedRimPx, PickedRimPx), new Vector2(-PickedRimPx, -PickedRimPx));
            var artImage = artGo.GetComponent<RawImage>();
            artImage.texture = art;
            artImage.raycastTarget = false; // the cell takes the click and the hover

            _dibsPickCells.Add((titleId, cell, artImage));
            Preview?.Attach(cellGo, art);
            UiKit.AddClick(cellGo, () => TogglePick(titleId));
        }

        private void TogglePick(string titleId)
        {
            if (_dibsPicked.Remove(titleId))
            {
                Sfx.Play(Sfx.ButtonClick);
            }
            else if (_dibsPicked.Count < _dibsNeeded)
            {
                _dibsPicked.Add(titleId);
                Sfx.Play(Sfx.ButtonClick);
            }
            ShowPickerState();
        }

        /// <summary>Picked cards wear a yellow rim; once the row is full, the rest dim.</summary>
        private void ShowPickerState()
        {
            int stillToPick = _dibsNeeded - _dibsPicked.Count;
            _dibsPickTally.text = $"PICK {_dibsNeeded} CARDS FOR THIS GAME — {_dibsPicked.Count} OF {_dibsNeeded} PICKED";
            foreach (var (titleId, cell, art) in _dibsPickCells)
            {
                bool isPicked = _dibsPicked.Contains(titleId);
                cell.color = isPicked ? UiKit.ButtonColor : Color.clear;
                art.color = isPicked || stillToPick > 0 ? Color.white : PassedOverTint;
            }

            UiKit.Clear(_dibsPickActions);
            UiKit.CreateButton(_dibsPickActions, "Back", 20, () => _onDibsPickBack?.Invoke());
            if (stillToPick > 0)
            {
                UiKit.CreateButton(_dibsPickActions, $"PICK {stillToPick} MORE", 20, () => { }, clickSound: null);
            }
            else
            {
                UiKit.CreateButton(_dibsPickActions, "Accept", 20,
                    () => _onDibsAccepted?.Invoke(_dibsPicked.ToList()), highlighted: true);
            }
        }
    }
}
