using System.Collections.Generic;
using System.IO;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The game table, rendered purely from a PlayerView (plus the static CardDatabase for
    /// names and costs), so local and networked sessions look identical. One partial per
    /// subsystem; this file holds the shared state, the zone layout and Render.
    /// </summary>
    public sealed partial class TableView
    {
        /// <summary>The board band's span in canvas fractions. Its left edge leaves room for the staged-card
        /// column beside the player bars and puts the VP cell flush with the hand's first card.</summary>
        public const float BoardLeft = 0.313f;
        public const float BoardRight = 1f;
        public const float BoardBottom = 0.21f;
        public const float BoardTop = 0.695f;
        /// <summary>The action log's column, top-right over the board band's headroom.</summary>
        public const float LogLeft = 0.83f;
        /// <summary>The band's clip edge sits this far left of the first cell, so the Victory Points outline is not nibbled at rest.</summary>
        public const float BoardLeadPx = 10f;

        // Hooks used by more than one subsystem; single-subsystem hooks live in their partial.
        public System.Func<bool> CanDrawLemon;
        public System.Action OnDrawCard;
        public System.Action OnBoardViewChanged;
        /// <summary>Whose board the table shows; -1 is your own.</summary>
        public int ViewedBoardPlayer = -1;
        /// <summary>Hand card id to the players it may attack; null when the card is not an attack.</summary>
        public System.Func<int, ISet<int>> AttackTargetsFor;
        /// <summary>Hand card id to the market indices it may take; null when it is not a market card.</summary>
        public System.Func<int, ISet<int>> MarketTargetsFor;

        private static readonly Color DropGlowHot = new Color(1f, 0.93f, 0.45f, 1f);
        private static readonly Color DropGlowWide = new Color(1f, 0.82f, 0.10f, 1f);

        // Repopulated by the zone partials on every Render; the aiming and drag subsystems
        // hit-test and re-bind against them.
        private readonly List<(int? Id, RectTransform Cell, Texture2D Art)> _dropCells =
            new List<(int?, RectTransform, Texture2D)>();
        private readonly List<(int Index, RectTransform Cell, GameObject Outer, GameObject Inner)>
            _marketCells = new List<(int, RectTransform, GameObject, GameObject)>();
        private readonly Dictionary<int, RectTransform> _cashIcons = new Dictionary<int, RectTransform>();
        private readonly Dictionary<int, RectTransform> _avatarBadges = new Dictionary<int, RectTransform>();
        private readonly List<(int PlayerId, RectTransform Row, GameObject Glow)> _playerRows =
            new List<(int, RectTransform, GameObject)>();
        private readonly List<(int Id, RectTransform Strip, GameObject Glow)> _equipCells =
            new List<(int, RectTransform, GameObject)>();
        private readonly List<(int Id, RectTransform Frame, float BaseX,
            HandCardMotion Motion, float RestY)> _handFrames =
            new List<(int, RectTransform, float, HandCardMotion, float)>();
        /// <summary>Glow on your VP column while a Bragging Rights drag is live.</summary>
        private GameObject _vpGlow;

        private RectTransform _canvasRoot;
        private readonly CardArt _art;
        private readonly CardPreview _preview;
        private CardDatabase _db; // set on each Render

        public RectTransform Root { get; private set; }
        public RectTransform ActionBar { get; private set; }

        public TableView(RectTransform canvasRoot, CardArt art, CardPreview preview)
        {
            _art = art;
            _preview = preview;
            _canvasRoot = canvasRoot;
            _lemonIcon = LoadIcon("lemon.png");
            _vpIcon = LoadIcon("VictoryPoint.png");
            _cashIcon = LoadIcon("Cash.png");
            _tantrumIcon = LoadIcon("Tantrum.png");
            _hourglassIcon = LoadIcon("Hourglass.png");
            _pinnedTagIcon = LoadIcon("PriceTag.png");
            _actionLogIcon = LoadIcon("ActionLog.png");
            Build(canvasRoot);
        }

        private static Texture2D LoadIcon(string fileName) =>
            UiKit.LoadTextureSharp(
                Path.Combine(Application.streamingAssetsPath, "icons", fileName));

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);

        /// <summary>Zone order is draw order: each zone covers the ones built before it.</summary>
        private void Build(RectTransform canvasRoot)
        {
            BuildTableRoot(canvasRoot);
            BuildPlayersColumn();
            BuildShelfBand();
            BuildBoardBand();
            // After the board: its drop-catching panel runs to the screen's right edge and
            // would otherwise sit on top of the log and swallow every click on its header.
            BuildActionLog();
            BuildActionBarStrip();
            // After every table zone, so raised cards overlay the table.
            BuildBottomPeekZones();
            BuildEquipTargeting();
            BuildDiscardViewer();
            BuildAttackTargeting();
        }

        private void BuildTableRoot(RectTransform canvasRoot)
        {
            Root = UiKit.CreatePanel(canvasRoot, "Table", new Color(0, 0, 0, 0));
            UiKit.Anchor(Root, Vector2.zero, Vector2.one);
            Root.GetComponent<Image>().raycastTarget = false;
            UiKit.CreateWallpaper(Root, Vector2.one);
        }

        private void BuildPlayersColumn()
        {
            var playersGo = new GameObject("Players", typeof(RectTransform), typeof(VerticalLayoutGroup));
            playersGo.transform.SetParent(Root, false);
            UiKit.Anchor((RectTransform)playersGo.transform,
                new Vector2(0, 0.24f), new Vector2(0.21f, 0.695f),
                new Vector2(10, 6), new Vector2(-4, -4));
            var playersLayout = playersGo.GetComponent<VerticalLayoutGroup>();
            playersLayout.spacing = 10;
            playersLayout.childAlignment = TextAnchor.UpperLeft;
            playersLayout.childForceExpandHeight = false;
            playersLayout.childForceExpandWidth = true;
            playersLayout.childControlHeight = true;
            playersLayout.childControlWidth = true;
            _playersColumn = (RectTransform)playersGo.transform;
        }

        /// <summary>One full-bleed band for the discard piles, Black Market row, stand supply and Bragging Rights.</summary>
        private void BuildShelfBand()
        {
            var shelf = UiKit.CreatePanel(Root, "Market", new Color(0, 0, 0, 0));
            UiKit.Anchor(shelf, new Vector2(0f, 0.75f), new Vector2(1f, 1f),
                new Vector2(0, 4), new Vector2(0, 0));
            _marketRow = UiKit.CreateCardRow(shelf, "MarketRow");
        }

        private void BuildBoardBand()
        {
            // Transparent, but its Image still catches supply-stand drops.
            var board = UiKit.CreatePanel(Root, "Board", new Color(0, 0, 0, 0));
            UiKit.Anchor(board, new Vector2(BoardLeft, BoardBottom), new Vector2(BoardRight, BoardTop),
                new Vector2(3 - BoardLeadPx, 2), new Vector2(-3, -2));
            _boardPanel = board;
            board.gameObject.AddComponent<BoardDropZone>().SupplyDropped = HandleSupplyDrop;
            // The row is content-sized behind a mask: a sixth stand overflows to the right
            // and edge-scrolls (see TickBoardScroll) instead of squishing the columns.
            _boardMask = board.gameObject.AddComponent<RectMask2D>();
            _boardRow = UiKit.CreateCardRow(board, "BoardRow");
            _boardRow.anchorMin = new Vector2(0f, 0f);
            _boardRow.anchorMax = new Vector2(0f, 1f);
            _boardRow.pivot = new Vector2(0f, 0.5f);
            _boardRow.offsetMin = new Vector2(6f + BoardLeadPx, 6f);
            _boardRow.offsetMax = new Vector2(6f + BoardLeadPx, -6f);
            _boardRow.gameObject.AddComponent<ContentSizeFitter>().horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            // Bottom-aligned: the headroom above holds a five-deep tucked stack clear of the shelf.
            _boardRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            _boardOwnerLabel = UiKit.CreateText(board, "", 20, TextAnchor.MiddleCenter,
                new Color(1f, 0.92f, 0.55f), body: true);
            _boardOwnerLabel.raycastTarget = false;
            UiKit.Anchor((RectTransform)_boardOwnerLabel.transform,
                new Vector2(0, 0.90f), new Vector2(1, 1));
        }

        private void BuildActionBarStrip()
        {
            var actions = UiKit.CreatePanel(Root, "Actions", new Color(0, 0, 0, 0));
            actions.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(actions, new Vector2(BoardLeft, 0.17f), new Vector2(BoardRight, 0.22f),
                new Vector2(3, 1), new Vector2(-3, -1));
            var bar = new GameObject("ActionBarRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(actions, false);
            // Inset top and bottom so a button touches neither the stand captions nor the hand.
            UiKit.Anchor((RectTransform)bar.transform, Vector2.zero, Vector2.one,
                new Vector2(8, 9), new Vector2(-8, -9));
            var barLayout = bar.GetComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 8;
            barLayout.childAlignment = TextAnchor.MiddleCenter;
            barLayout.childForceExpandHeight = true;
            barLayout.childForceExpandWidth = false;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            ActionBar = (RectTransform)bar.transform;
        }

        private void BuildBottomPeekZones()
        {
            _handHost = CreateBottomPeekZone("Hand", 0.245f, 0.755f);
            // Soft edges: cards fade out at the sides instead of spilling into the neighbouring zones.
            var handMask = _handHost.gameObject.AddComponent<RectMask2D>();
            handMask.softness = new Vector2Int(70, 0);

            _lordHost = CreateBottomPeekZone("LemonLords", 0.79f, 1f);
            _dibsHost = CreateBottomPeekZone("FirstDibs", 0f, 0.21f);
        }

        /// <summary>A click-through zone on the screen's bottom edge, for cards that peek up from below it.</summary>
        private RectTransform CreateBottomPeekZone(string name, float leftFraction, float rightFraction)
        {
            var zone = UiKit.CreatePanel(Root, name, new Color(0, 0, 0, 0));
            zone.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(zone, new Vector2(leftFraction, 0f), new Vector2(rightFraction, 0f));
            zone.pivot = new Vector2(0.5f, 0f);
            zone.sizeDelta = new Vector2(zone.sizeDelta.x, 300f);
            zone.anchoredPosition = Vector2.zero;
            return zone;
        }

        public void Render(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            _db = db;
            // An aim in progress survives the rebuild: renders fire on every revision bump, so
            // only the visuals the aim points at are rebuilt, then re-bound at the end.
            bool wasAiming = _attackCardId >= 0;
            _pickHover = -1;
            RenderMarket(view, db, groups);
            RenderBoard(view);
            RenderHand(view, groups);
            RenderLords(view);
            RenderFirstDibs(view);
            RenderSupply(view, db, groups);
            RenderPlayers(view);
            if (wasAiming)
            {
                // Last: every list it re-binds against has just been repopulated above.
                ReacquireAttackTargeting(view);
            }
        }

        /// <summary>True while something Esc-cancellable is up (aiming, equip, discards).</summary>
        public bool HasActiveOverlay =>
            _attackCardId >= 0 || _targetingPick != null || _discardOverlay.gameObject.activeSelf ||
            StickyDragActive || RummagePickActive;

        /// <summary>Esc: dismiss whichever overlay is up, with the same outcome as clicking off it. Only one is ever open.</summary>
        public void CancelOverlays()
        {
            if (_attackCardId >= 0)
            {
                // Esc during a drag must not re-arm on the very next pointer move.
                _attackDragAborted = _attackDragMode;
                EndAttackTargeting();
                return;
            }
            if (_targetingPick != null)
            {
                var cancel = _targetingCancel;
                EndEquipTargeting();
                cancel?.Invoke();
                return;
            }
            if (StickyDragActive)
            {
                CancelStickyDrag();
                return;
            }
            if (RummagePickActive)
            {
                OnRummageCancel?.Invoke();
                return;
            }
            if (_discardOverlay.gameObject.activeSelf)
            {
                CloseDiscardViewer();
            }
        }

        /// <summary>
        /// Drops the pick overlays a changed modal context is about to replace, without their cancel
        /// callbacks (those reopen the flow being replaced). Attack aiming is hand-armed, so it stays.
        /// </summary>
        public void DismissModalOverlays()
        {
            if (_targetingPick != null)
            {
                EndEquipTargeting();
            }
            CancelStickyDrag();
            if (_discardOverlay.gameObject.activeSelf)
            {
                CloseDiscardViewer();
            }
        }
    }
}
