using System.Collections.Generic;
using System.IO;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The game table, rendered purely from a PlayerView (+ static CardDatabase for names
    /// and costs) — identical for local and networked sessions. Interactive elements get
    /// glow affordances and click/drag through to the app's handlers.
    /// One partial per subsystem (TableView.Hand.cs, TableView.Market.cs, ...); this
    /// file holds the cross-subsystem state, the zone layout, and Render orchestration.
    /// </summary>
    public sealed partial class TableView
    {
        // Callbacks shared by more than one subsystem (single-subsystem ones live in
        // their partial).
        public System.Func<bool> CanDrawLemon;      // hand draw slot + lemon pile chip
        public System.Action OnDrawCard;
        /// <summary>Fired when the player switches whose board is displayed.</summary>
        public System.Action OnBoardViewChanged;
        /// <summary>Whose board the table currently shows; -1 = your own.</summary>
        public int ViewedBoardPlayer = -1;
        // Consulted both when a hand gesture arms an aim and when a render re-binds one.
        public System.Func<int, ISet<int>> AttackTargetsFor; // hand id -> victims (null = not an attack)
        /// <summary>hand card id -> market indices it may take (null = not a market card).</summary>
        public System.Func<int, ISet<int>> MarketTargetsFor;

        /// <summary>Drop zones glow lemonade-yellow while a card is being dragged.</summary>
        private static readonly Color DropGlowHot = new Color(1f, 0.93f, 0.45f, 1f);
        private static readonly Color DropGlowWide = new Color(1f, 0.82f, 0.10f, 1f);

        // Target/anchor registries: repopulated by the zone partials during Render,
        // hit-tested and re-bound by the aiming and drag subsystems (see
        // ReacquireAttackTargeting).
        private readonly List<(int? Id, RectTransform Cell, Texture2D Art)> _dropCells =
            new List<(int?, RectTransform, Texture2D)>();
        private readonly List<(int Index, RectTransform Cell, GameObject Outer, GameObject Inner)>
            _marketCells = new List<(int, RectTransform, GameObject, GameObject)>();
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
        private CardDatabase _db; // set each Render; card names come from data, not ids

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
            Build(canvasRoot);
        }

        private static Texture2D LoadIcon(string fileName) =>
            UiKit.LoadTextureSharp(
                Path.Combine(Application.streamingAssetsPath, "icons", fileName));

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);

        private void Build(RectTransform root)
        {
            Root = UiKit.CreatePanel(root, "Table", new Color(0, 0, 0, 0));
            UiKit.Anchor(Root, Vector2.zero, Vector2.one);
            Root.GetComponent<Image>().raycastTarget = false;

            // Repeating badge pattern over the play area — everything below the shelf.
            UiKit.CreateWallpaper(Root, new Vector2(1f, 0.70f));

            // Left: player panels in turn order (you at the bottom), below the shelf.
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

            // Right column, mirroring the player panels: the action log. Collapsed
            // it's one slim "latest thing that happened" chip; expanded it's the
            // recent history — fast bot turns can be replayed at a glance.
            BuildActionLog();

            // Top: one full-width shelf — Black Market row, stand supply, Bragging
            // Rights, and the Black Market discard pile.
            // Full-bleed: the shelf runs edge to edge and meets the status bar, so the
            // chrome reads as one band instead of a floating panel.
            var market = UiKit.CreatePanel(Root, "Market", new Color(0, 0, 0, 0));
            UiKit.Anchor(market, new Vector2(0f, 0.70f), new Vector2(1f, 0.95f),
                new Vector2(0, 4), new Vector2(0, 0));
            _marketRow = UiKit.CreateCardRow(market, "MarketRow");

            // Hairline rule under the shelf: with every zone transparent, this is what
            // separates "the store" from "the table". It fills the 4px gap the shelf
            // leaves above its own anchor, so it never overlaps the cards.
            var shelfRule = UiKit.CreatePanel(Root, "ShelfRule", UiKit.ButtonColor);
            UiKit.Anchor(shelfRule, new Vector2(0f, 0.70f), new Vector2(1f, 0.70f),
                new Vector2(0, 0), new Vector2(0, 4));
            shelfRule.GetComponent<Image>().raycastTarget = false;

            // Center: your board (turf + stands). Also a drop zone for supply stands.
            // Transparent — cards float on the table; the Image still catches drops.
            // (The turn banner lives in the top status bar.)
            var board = UiKit.CreatePanel(Root, "Board", new Color(0, 0, 0, 0));
            UiKit.Anchor(board, new Vector2(0.21f, 0.21f), new Vector2(0.79f, 0.695f),
                new Vector2(3, 2), new Vector2(-3, -2));
            _boardPanel = board;
            board.gameObject.AddComponent<BoardDropZone>().SupplyDropped = HandleSupplyDrop;
            // The row is content-sized behind a mask: a 6th stand overflows to the
            // right and edge-scrolls (like the hand) instead of squishing the columns.
            // The soft edge only switches on while there IS overflow (see
            // TickBoardScroll) so a board that fits stays crisp.
            _boardMask = board.gameObject.AddComponent<RectMask2D>();
            _boardRow = UiKit.CreateCardRow(board, "BoardRow");
            _boardRow.anchorMin = new Vector2(0f, 0f);
            _boardRow.anchorMax = new Vector2(0f, 1f);
            _boardRow.pivot = new Vector2(0f, 0.5f);
            _boardRow.offsetMin = new Vector2(6f, 6f);
            _boardRow.offsetMax = new Vector2(6f, -6f);
            _boardRow.gameObject.AddComponent<ContentSizeFitter>().horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            // Cards sit at the BOTTOM of the band: the headroom above holds a full
            // 5-deep tucked-upgrade stack (5 x 40px peeks) without touching the shelf.
            _boardRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            _boardOwnerLabel = UiKit.CreateText(board, "", 20, TextAnchor.MiddleCenter,
                new Color(1f, 0.92f, 0.55f), body: true);
            _boardOwnerLabel.raycastTarget = false;
            UiKit.Anchor((RectTransform)_boardOwnerLabel.transform,
                new Vector2(0, 0.90f), new Vector2(1, 1));

            // Persistent actions: a floating button strip above the hand's peek band.
            var actions = UiKit.CreatePanel(Root, "Actions", new Color(0, 0, 0, 0));
            actions.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(actions, new Vector2(0.21f, 0.17f), new Vector2(0.79f, 0.22f),
                new Vector2(3, 1), new Vector2(-3, -1));
            var bar = new GameObject("ActionBarRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(actions, false);
            UiKit.Anchor((RectTransform)bar.transform, Vector2.zero, Vector2.one,
                new Vector2(8, 3), new Vector2(-8, -3));
            var barLayout = bar.GetComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 8;
            barLayout.childForceExpandHeight = true;
            barLayout.childForceExpandWidth = false;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            ActionBar = (RectTransform)bar.transform;

            // Bottom edge: the hand. Cards peek up from below the screen and rise on
            // hover, Dune: Imperium style. Built LAST so raised cards overlay the table.
            // A soft-edged mask clips the fan to its band, fading cards out at the
            // sides instead of letting them spill into the neighboring zones.
            _handHost = UiKit.CreatePanel(Root, "Hand", new Color(0, 0, 0, 0));
            _handHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(_handHost, new Vector2(0.245f, 0f), new Vector2(0.755f, 0f));
            _handHost.pivot = new Vector2(0.5f, 0f);
            _handHost.sizeDelta = new Vector2(_handHost.sizeDelta.x, 300f);
            _handHost.anchoredPosition = Vector2.zero;
            var handMask = _handHost.gameObject.AddComponent<RectMask2D>();
            handMask.softness = new Vector2Int(70, 0);

            // Bottom-right: your two secret Lemon Lords, same peek-and-rise treatment,
            // floating over the dark side zone.
            _lordHost = UiKit.CreatePanel(Root, "LemonLords", new Color(0, 0, 0, 0));
            _lordHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(_lordHost, new Vector2(0.79f, 0f), new Vector2(1f, 0f));
            _lordHost.pivot = new Vector2(0.5f, 0f);
            _lordHost.sizeDelta = new Vector2(_lordHost.sizeDelta.x, 300f);
            _lordHost.anchoredPosition = Vector2.zero;

            // Bottom-left: the First Dibs titles, peeking where the log used to live.
            _dibsHost = UiKit.CreatePanel(Root, "FirstDibs", new Color(0, 0, 0, 0));
            _dibsHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(_dibsHost, new Vector2(0f, 0f), new Vector2(0.21f, 0f));
            _dibsHost.pivot = new Vector2(0.5f, 0f);
            _dibsHost.sizeDelta = new Vector2(_dibsHost.sizeDelta.x, 300f);
            _dibsHost.anchoredPosition = Vector2.zero;

            BuildEquipTargeting();
            BuildDiscardViewer();
            BuildAttackTargeting();
        }

        public void Render(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            _db = db;
            // An in-progress aim SURVIVES the rebuild. Renders fire on every revision
            // bump (bot pacing ticks, other players' passes, keepalives), so tearing
            // targeting down here cancelled the player's arrow a second or two after
            // they drew it. The aiming state is plain data; only the visuals it points
            // at are rebuilt below, and ReacquireAttackTargeting re-binds them (or ends
            // the aim if it has actually gone stale).
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
                // Last: every list it re-binds against (_marketCells, _equipCells,
                // _handFrames, _playerRows) has just been repopulated above.
                ReacquireAttackTargeting(view);
            }
        }

        /// <summary>True while something Esc-cancellable is up (aiming, equip, discards).</summary>
        public bool HasActiveOverlay =>
            _attackCardId >= 0 || _targetingPick != null || _discardOverlay.gameObject.activeSelf;

        /// <summary>
        /// Esc: dismiss whichever aiming/browsing overlay is up — same outcome as
        /// clicking off it. Attack aim first, then equip targeting, then the discard
        /// browser (only one is ever open at a time).
        /// </summary>
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
            if (_discardOverlay.gameObject.activeSelf)
            {
                CloseDiscardViewer();
            }
        }

        /// <summary>
        /// Silently drop the pick overlays a CHANGED modal context is about to
        /// replace. Unlike <see cref="CancelOverlays"/> this never invokes cancel
        /// callbacks — those reopen the flow being replaced. Attack aiming is left
        /// alone: it is hand-armed, not modal-owned.
        /// </summary>
        public void DismissModalOverlays()
        {
            if (_targetingPick != null)
            {
                EndEquipTargeting();
            }
            if (_discardOverlay.gameObject.activeSelf)
            {
                CloseDiscardViewer();
            }
        }
    }
}
