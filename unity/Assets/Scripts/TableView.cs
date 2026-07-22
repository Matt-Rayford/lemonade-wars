using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The game table, rendered purely from a PlayerView (+ static CardDatabase for names
    /// and costs) — identical for local and networked sessions. Interactive elements get
    /// glow affordances and click/drag through to the app's handlers.
    /// </summary>
    public sealed class TableView
    {
        public System.Action<int> OnHandCard;      // lemon instance id

        // Market drag & drop.
        public System.Func<int, bool> CanBuyMarket;         // market index -> any legal buy?
        public System.Action<int> OnMarketDragStart;        // market index
        public System.Action OnMarketDragEnd;
        public System.Action<int, int?> OnMarketDrop;       // market index, stand id (null = turf)

        // Supply drag & drop (stand purchases with row-position insert).
        public System.Func<string, bool> CanBuySupply;      // stand type -> any legal buy?
        public System.Action<string, int> OnSupplyDrop;     // stand type, insert index

        // Bragging Rights drag & drop (shelf card -> your VP column).
        public System.Func<bool> CanBuyBragging;
        public System.Action OnBraggingDrop;
        private GameObject _vpGlow;

        private static readonly Color GlowInnerColor = new Color(1f, 0.97f, 0.88f, 1f);
        private static readonly Color GlowOuterColor = new Color(1f, 0.96f, 0.82f, 0.80f);
        /// <summary>Drop zones glow lemonade-yellow while a card is being dragged.</summary>
        private static readonly Color DropGlowHot = new Color(1f, 0.93f, 0.45f, 1f);
        private static readonly Color DropGlowWide = new Color(1f, 0.82f, 0.10f, 1f);

        private readonly List<(int? StandId, GameObject Glow)> _dropGlows =
            new List<(int?, GameObject)>();
        private readonly HashSet<int?> _validDropTargets = new HashSet<int?>();
        private RectTransform _canvasRoot;

        // Equip targeting: the table dims EXCEPT the board band — you aim at your real
        // stands (tucked upgrades visible, so "that one is full" is legible), with the
        // taken card floating up top and a dashed arrow to the hovered cell.
        private RectTransform _targetingRoot;
        private RectTransform _targetingCardHost;
        private RectTransform _targetingCopiesHost;
        private RectTransform _arrowHost;
        private ISet<int?> _targetingValid;
        private System.Action<int?> _targetingPick;
        private System.Action _targetingCancel;
        private int? _targetingHover;
        private bool _targetingHoverAny;
        private Vector2 _targetingCardBottom;
        private readonly List<(int? Id, RectTransform Cell, Texture2D Art)> _dropCells =
            new List<(int?, RectTransform, Texture2D)>();

        // Attack targeting: aim a hand attack card at a player bar, armed by clicking
        // the card or by dragging it out of the hand band. Red glow on the hovered bar,
        // red dashed arrow from the card to the bar (or the cursor while searching).
        public System.Func<int, ISet<int>> AttackTargetsFor; // hand id -> victims (null = not an attack)
        public System.Action<int, int> OnAttackPick;         // hand id, victim player id
        private static readonly Color AttackGlowColor = new Color(1f, 0.30f, 0.22f, 0.95f);
        private static readonly Color AttackArrowColor = new Color(0.95f, 0.27f, 0.21f);
        private RectTransform _attackRoot;
        private RectTransform _attackArrowHost;
        private ISet<int> _attackValid;
        private int _attackCardId = -1;
        private int _attackHover = -1;   // player id, -1 = none
        private bool _attackDragMode;    // true: release fires; false: second click fires
        private bool _attackDragAborted; // Esc mid-drag: stay disarmed until release
        private Vector2 _attackArrowFrom;
        private Vector2 _attackArrowTo;
        private readonly List<(int PlayerId, RectTransform Row, GameObject Glow)> _playerRows =
            new List<(int, RectTransform, GameObject)>();

        // Supply-drag insertion preview.
        private RectTransform _boardPanel;
        private readonly List<RectTransform> _standCells = new List<RectTransform>();
        private GameObject _spacer;
        private LayoutWidthTween _spacerTween;
        private int _supplyInsertIndex = -1;
        private bool _supplyDragActive;

        private readonly CardArt _art;
        private readonly CardPreview _preview;
        private readonly MonoBehaviour _host;
        private CardDatabase _db; // set each Render; card names come from data, not ids

        private RectTransform _playersColumn;
        private Texture2D _lemonIcon;
        private Texture2D _vpIcon;
        private Texture2D _cashIcon;
        private Texture2D _tantrumIcon;
        private TMP_Text _boardOwnerLabel;
        private bool _viewingOwnBoard = true;
        /// <summary>Whose board the table currently shows; -1 = your own.</summary>
        public int ViewedBoardPlayer = -1;
        /// <summary>playerId -> "easy"/"medium"/"hard" for bot seats, null for humans.</summary>
        public System.Func<int, string> BotLevelLookup;

        // Hand arrangement: the player's preferred order, kept across renders.
        private readonly List<int> _handOrder = new List<int>();
        private int _reorderCardId = -1;
        private Vector2 _handDragStart;
        private float _handStartX;
        private float _handSpacing;
        /// <summary>Fired when the player switches whose board is displayed.</summary>
        public System.Action OnBoardViewChanged;
        private RectTransform _marketRow;

        /// <summary>Dark badge palette; a player's color is a stable hash of their name.</summary>
        private static readonly Color[] BadgeColors =
        {
            new Color(0.55f, 0.24f, 0.24f), new Color(0.23f, 0.42f, 0.58f),
            new Color(0.25f, 0.48f, 0.32f), new Color(0.56f, 0.40f, 0.16f),
            new Color(0.45f, 0.28f, 0.55f), new Color(0.20f, 0.48f, 0.48f),
            new Color(0.58f, 0.30f, 0.44f), new Color(0.38f, 0.42f, 0.20f),
            new Color(0.30f, 0.32f, 0.58f), new Color(0.50f, 0.34f, 0.24f),
        };
        private RectTransform _boardRow;
        private RectTransform _handHost;
        private RectTransform _lordHost;
        private RectTransform _dibsHost;
        private RectTransform _discardOverlay;
        private RectTransform _discardGrid;
        private ScrollRect _discardScrollRect;
        private TMP_Text _discardTitle;
        private GameObject _discardTakeButton;
        private GameObject _discardBackButton;
        private TMP_Text _discardTakeLabel;
        private System.Action<int> _discardOnTake;
        private System.Action _discardOnBack;
        private int? _discardSelectedId;
        private readonly Dictionary<int, GameObject> _discardSelectGlows =
            new Dictionary<int, GameObject>();

        // Hand fan scrolling (when the hand outgrows the center band).
        private readonly List<(int Id, RectTransform Frame, float BaseX,
            HandCardMotion Motion, float RestY)> _handFrames =
            new List<(int, RectTransform, float, HandCardMotion, float)>();
        private float _handScroll;
        private float _handMaxScroll;
        public RectTransform Root { get; private set; }
        public RectTransform ActionBar { get; private set; }

        public TableView(RectTransform canvasRoot, CardArt art, CardPreview preview,
            MonoBehaviour host)
        {
            _art = art;
            _preview = preview;
            _host = host;
            _canvasRoot = canvasRoot;
            _lemonIcon = LoadIcon("lemon.png");
            _vpIcon = LoadIcon("VictoryPoint.png");
            _cashIcon = LoadIcon("Cash.png");
            _tantrumIcon = LoadIcon("Tantrum.png");
            Build(canvasRoot);
        }

        private static Texture2D LoadIcon(string fileName)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "icons", fileName);
            if (!File.Exists(path))
            {
                return null;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);

        private void Build(RectTransform root)
        {
            Root = UiKit.CreatePanel(root, "Table", new Color(0, 0, 0, 0));
            UiKit.Anchor(Root, Vector2.zero, Vector2.one);
            Root.GetComponent<Image>().raycastTarget = false;

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
            var market = UiKit.CreatePanel(Root, "Market", UiKit.PanelColor);
            UiKit.Anchor(market, new Vector2(0f, 0.70f), new Vector2(1f, 0.95f),
                new Vector2(6, 4), new Vector2(-6, -4));
            _marketRow = UiKit.CreateCardRow(market, "MarketRow");

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

        // ------------------------------------------------- equip targeting

        /// <summary>
        /// Invisible full-screen layer for aiming a just-taken card at a turf/stand.
        /// It intercepts the pointer itself and hit-tests the board cells manually, so
        /// hover glows and clicks work without fighting the cells' own handlers.
        /// </summary>
        private void BuildEquipTargeting()
        {
            _targetingRoot = UiKit.CreatePanel(Root, "EquipTargeting", new Color(0, 0, 0, 0));
            UiKit.Anchor(_targetingRoot, Vector2.zero, Vector2.one);
            // Dim everything EXCEPT the board band: the live stands (tucked upgrades
            // and all) are the targets, so the player aims with full information.
            var dimColor = new Color(0f, 0f, 0f, 0.66f);
            void Dim(Vector2 min, Vector2 max)
            {
                var panel = UiKit.CreatePanel(_targetingRoot, "Dim", dimColor);
                panel.GetComponent<Image>().raycastTarget = false;
                UiKit.Anchor(panel, min, max);
            }
            Dim(new Vector2(0f, 0.695f), new Vector2(1f, 1f));      // market band + up
            Dim(new Vector2(0f, 0f), new Vector2(1f, 0.21f));       // action bar + hand
            Dim(new Vector2(0f, 0.21f), new Vector2(0.21f, 0.695f)); // player column
            Dim(new Vector2(0.79f, 0.21f), new Vector2(1f, 0.695f)); // log column
            UiKit.AddClick(_targetingRoot.gameObject, () =>
            {
                var pick = _targetingPick;
                var cancel = _targetingCancel;
                bool onTarget = _targetingHoverAny;
                int? target = _targetingHover;
                EndEquipTargeting();
                if (onTarget)
                {
                    pick?.Invoke(target);
                }
                else
                {
                    cancel?.Invoke();
                }
            });

            var copiesGo = new GameObject("TargetCopies", typeof(RectTransform));
            copiesGo.transform.SetParent(_targetingRoot, false);
            _targetingCopiesHost = (RectTransform)copiesGo.transform;
            UiKit.Anchor(_targetingCopiesHost, Vector2.zero, Vector2.one);

            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            arrowGo.transform.SetParent(_targetingRoot, false);
            _arrowHost = (RectTransform)arrowGo.transform;
            UiKit.Anchor(_arrowHost, Vector2.zero, Vector2.one);

            var cardGo = new GameObject("FloatCard", typeof(RectTransform));
            cardGo.transform.SetParent(_targetingRoot, false);
            _targetingCardHost = (RectTransform)cardGo.transform;
            UiKit.Anchor(_targetingCardHost, Vector2.zero, Vector2.one);

            _targetingRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Dim everything but the board, float the taken card centered in the market
        /// band, and wait for a click on one of your real stands (or the turf).
        /// </summary>
        public void BeginEquipTargeting(Texture2D texture, ISet<int?> validTargets,
            System.Action<int?> onPick, System.Action onCancel)
        {
            const float width = 190f;
            const float height = 266f;

            _targetingValid = validTargets;
            _targetingPick = onPick;
            _targetingCancel = onCancel;
            _targetingHover = null;
            _targetingHoverAny = false;
            UiKit.Clear(_targetingCardHost);
            UiKit.Clear(_targetingCopiesHost);
            UiKit.Clear(_arrowHost);

            // The taken card floats centered in the market band.
            var anchor = new Vector2(0, _targetingRoot.rect.height * 0.325f);
            var image = UiKit.CreateCardImage(_targetingCardHost, texture, width, height);
            image.raycastTarget = false;
            var frame = (RectTransform)image.transform.parent;
            frame.GetComponent<Image>().raycastTarget = false;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(width, height);
            frame.anchoredPosition = anchor;
            var shadow = frame.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(0, -6f);
            _targetingCardBottom = anchor + new Vector2(0, -height * 0.5f);

            _targetingRoot.SetAsLastSibling();
            _targetingRoot.gameObject.SetActive(true);
        }

        /// <summary>Called every frame by the app; drives hover glow + the dashed arrow.</summary>
        public void TickEquipTargeting(Vector2 screenPosition)
        {
            if (_targetingPick == null)
            {
                return;
            }
            int? hover = null;
            bool any = false;
            foreach (var (id, cell, _) in _dropCells)
            {
                if (_targetingValid.Contains(id) &&
                    RectTransformUtility.RectangleContainsScreenPoint(cell, screenPosition))
                {
                    hover = id;
                    any = true;
                    break;
                }
            }
            if (any == _targetingHoverAny && hover == _targetingHover)
            {
                return;
            }
            _targetingHover = hover;
            _targetingHoverAny = any;

            // Glow and arrow are rebuilt from the LIVE cell position: the real board
            // is the target surface now, and it can scroll under the overlay.
            UiKit.Clear(_targetingCopiesHost);
            UiKit.Clear(_arrowHost);
            if (any)
            {
                foreach (var (id, cell, _) in _dropCells)
                {
                    if (Equals(id, hover))
                    {
                        var cardFrame = cell.Find("Card") as RectTransform ?? cell;
                        Vector2 local = _targetingRoot.InverseTransformPoint(
                            cardFrame.TransformPoint(cardFrame.rect.center));
                        UiKit.CreateGlow(_targetingCopiesHost,
                            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), local,
                            cardFrame.rect.width + 30, cardFrame.rect.height + 30, DropGlowHot);

                        var world = cell.TransformPoint(
                            new Vector3(cell.rect.center.x, cell.rect.yMax - 16f, 0));
                        UiKit.DrawDashedArrow(_arrowHost, _targetingCardBottom,
                            (Vector2)_targetingRoot.InverseTransformPoint(world),
                            UiKit.ButtonColor);
                        break;
                    }
                }
            }
        }

        /// <summary>Dismiss the targeting layer without invoking pick or cancel.</summary>
        private void EndEquipTargeting()
        {
            _targetingPick = null;
            _targetingCancel = null;
            _targetingValid = null;
            _targetingHover = null;
            _targetingHoverAny = false;
            _targetingRoot.gameObject.SetActive(false);
            UiKit.Clear(_targetingCardHost);
            UiKit.Clear(_targetingCopiesHost);
            UiKit.Clear(_arrowHost);
        }

        // ------------------------------------------------ attack targeting

        /// <summary>
        /// Invisible full-screen layer for aiming an attack at a player bar. Like equip
        /// targeting it owns the pointer and hit-tests the bars manually, but the table
        /// stays live underneath — the armed card just holds its raised pose in the hand.
        /// </summary>
        private void BuildAttackTargeting()
        {
            _attackRoot = UiKit.CreatePanel(Root, "AttackTargeting", new Color(0, 0, 0, 0));
            UiKit.Anchor(_attackRoot, Vector2.zero, Vector2.one);
            // Click mode: a click on a glowing bar fires, anywhere else disarms.
            UiKit.AddClick(_attackRoot.gameObject, FinishAttackTargeting);

            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            arrowGo.transform.SetParent(_attackRoot, false);
            _attackArrowHost = (RectTransform)arrowGo.transform;
            UiKit.Anchor(_attackArrowHost, Vector2.zero, Vector2.one);

            _attackRoot.gameObject.SetActive(false);
            BuildPlayerPick();
        }

        private RectTransform _pickRoot;
        private RectTransform _pickArrowHost;
        private RectTransform _pickCard;
        private ISet<int> _pickValid;
        private System.Action<int> _pickOnPick;
        private int _pickHover = -1;
        private Vector2 _pickArrowFrom;
        private Vector2 _pickArrowTo;

        public bool PlayerPickActive => _pickRoot != null && _pickRoot.gameObject.activeSelf;

        private void BuildPlayerPick()
        {
            _pickRoot = UiKit.CreatePanel(Root, "PlayerPick", new Color(0, 0, 0, 0));
            UiKit.Anchor(_pickRoot, Vector2.zero, Vector2.one);
            UiKit.AddClick(_pickRoot.gameObject, FinishPlayerPick);

            var cardGo = new GameObject("PickCard", typeof(RectTransform), typeof(RawImage),
                typeof(Shadow));
            cardGo.transform.SetParent(_pickRoot, false);
            _pickCard = (RectTransform)cardGo.transform;
            _pickCard.anchorMin = _pickCard.anchorMax = new Vector2(0.5f, 0.55f);
            _pickCard.sizeDelta = new Vector2(300f, 420f);
            cardGo.GetComponent<RawImage>().raycastTarget = false;
            var shadow = cardGo.GetComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(0, -6f);

            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            arrowGo.transform.SetParent(_pickRoot, false);
            _pickArrowHost = (RectTransform)arrowGo.transform;
            UiKit.Anchor(_pickArrowHost, Vector2.zero, Vector2.one);
            _pickRoot.gameObject.SetActive(false);
        }

        public void BeginPlayerPick(Texture2D cardArt, ISet<int> validTargets, System.Action<int> onPick)
        {
            _pickValid = validTargets;
            _pickOnPick = onPick;
            _pickHover = -1;
            _pickArrowFrom = _pickArrowTo = new Vector2(float.NaN, float.NaN);
            _pickCard.GetComponent<RawImage>().texture = cardArt;
            _pickCard.gameObject.SetActive(cardArt != null);
            UiKit.Clear(_pickArrowHost);
            _pickRoot.SetAsLastSibling();
            _pickRoot.gameObject.SetActive(true);
            _preview.SetDragging(true); // no magnify pop-ups while aiming
        }

        public void EndPlayerPick()
        {
            if (!PlayerPickActive)
            {
                return;
            }
            foreach (var (_, _, glow) in _playerRows)
            {
                if (glow != null)
                {
                    glow.SetActive(false);
                }
            }
            _pickValid = null;
            _pickOnPick = null;
            _pickHover = -1;
            UiKit.Clear(_pickArrowHost);
            _pickRoot.gameObject.SetActive(false);
            _preview.SetDragging(false);
        }

        /// <summary>Called every frame by the app; drives the bar glow + the arrow.</summary>
        public void TickPlayerPick(Vector2 screenPosition)
        {
            if (!PlayerPickActive)
            {
                return;
            }
            int hover = -1;
            foreach (var (playerId, row, _) in _playerRows)
            {
                if (row != null && _pickValid.Contains(playerId) &&
                    RectTransformUtility.RectangleContainsScreenPoint(row, screenPosition))
                {
                    hover = playerId;
                    break;
                }
            }
            if (hover != _pickHover)
            {
                _pickHover = hover;
                foreach (var (playerId, _, glow) in _playerRows)
                {
                    if (glow != null)
                    {
                        glow.SetActive(playerId == hover);
                    }
                }
            }

            // Arrow: from the middle of the floating card, trailing the cursor.
            Vector2 from = _pickRoot.InverseTransformPoint(_pickCard.TransformPoint(Vector3.zero));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _pickRoot, screenPosition, null, out var to);
            if ((from - _pickArrowFrom).sqrMagnitude < 4f &&
                (to - _pickArrowTo).sqrMagnitude < 4f)
            {
                return;
            }
            _pickArrowFrom = from;
            _pickArrowTo = to;
            UiKit.Clear(_pickArrowHost);
            UiKit.DrawDashedArrow(_pickArrowHost, from, to, AttackArrowColor);
        }

        /// <summary>The decision is blocking: clicking empty space keeps aiming.</summary>
        private void FinishPlayerPick()
        {
            if (_pickHover < 0)
            {
                return;
            }
            int victim = _pickHover;
            var onPick = _pickOnPick;
            EndPlayerPick();
            onPick?.Invoke(victim);
        }

        private void BeginAttackTargeting(int cardInstanceId, ISet<int> validTargets, bool dragMode)
        {
            if (_attackCardId >= 0)
            {
                return;
            }
            _attackCardId = cardInstanceId;
            _attackValid = validTargets;
            _attackDragMode = dragMode;
            _attackHover = -1;
            _attackArrowFrom = _attackArrowTo = new Vector2(float.NaN, float.NaN);
            _preview.SetDragging(true); // no magnify pop-ups while aiming

            // Hold the armed card raised and on top: the intercept layer eats the hover
            // events that would otherwise drop it back into the fan.
            foreach (var entry in _handFrames)
            {
                if (entry.Id == cardInstanceId && entry.Frame != null)
                {
                    entry.Frame.SetAsLastSibling();
                    entry.Motion.TargetY = 12f; // the fan's raised pose
                    break;
                }
            }
            _attackRoot.gameObject.SetActive(true);
        }

        /// <summary>Re-arm targeting for a hand card (BACK from a follow-up picker).</summary>
        public void RestartAttackTargeting(int cardInstanceId)
        {
            var targets = AttackTargetsFor?.Invoke(cardInstanceId);
            if (targets != null && targets.Count > 0)
            {
                BeginAttackTargeting(cardInstanceId, targets, dragMode: false);
            }
        }

        /// <summary>Called every frame by the app; drives the bar glow + the red arrow.</summary>
        public void TickAttackTargeting(Vector2 screenPosition)
        {
            if (_attackCardId < 0)
            {
                return;
            }

            int hover = -1;
            foreach (var (playerId, row, _) in _playerRows)
            {
                if (row != null && _attackValid.Contains(playerId) &&
                    RectTransformUtility.RectangleContainsScreenPoint(row, screenPosition))
                {
                    hover = playerId;
                    break;
                }
            }
            if (hover != _attackHover)
            {
                _attackHover = hover;
                foreach (var (playerId, _, glow) in _playerRows)
                {
                    if (glow != null)
                    {
                        glow.SetActive(playerId == hover);
                    }
                }
            }

            // Arrow: from the armed card's top edge, trailing the cursor.
            var from = _attackArrowFrom;
            foreach (var entry in _handFrames)
            {
                if (entry.Id == _attackCardId && entry.Frame != null)
                {
                    var world = entry.Frame.TransformPoint(
                        new Vector3(0, entry.Frame.rect.yMax, 0));
                    from = _attackRoot.InverseTransformPoint(world);
                    break;
                }
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _attackRoot, screenPosition, null, out var to);
            // Redraw only on real movement — the arrow is rebuilt from scratch.
            if ((from - _attackArrowFrom).sqrMagnitude < 4f &&
                (to - _attackArrowTo).sqrMagnitude < 4f)
            {
                return;
            }
            _attackArrowFrom = from;
            _attackArrowTo = to;
            UiKit.Clear(_attackArrowHost);
            UiKit.DrawDashedArrow(_attackArrowHost, from, to, AttackArrowColor);
        }

        /// <summary>
        /// Esc: dismiss whichever aiming/browsing overlay is up — same outcome as
        /// clicking off it. Attack aim first, then equip targeting, then the discard
        /// browser (only one is ever open at a time).
        /// </summary>
        /// <summary>True while something Esc-cancellable is up (aiming, equip, discards).</summary>
        public bool HasActiveOverlay =>
            _attackCardId >= 0 || _targetingPick != null || _discardOverlay.gameObject.activeSelf;

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

        /// <summary>Fire on the hovered bar, or just disarm when aimed at nothing.</summary>
        private void FinishAttackTargeting()
        {
            int cardId = _attackCardId;
            int victim = _attackHover;
            EndAttackTargeting();
            if (cardId >= 0 && victim >= 0)
            {
                OnAttackPick?.Invoke(cardId, victim);
            }
        }

        private void EndAttackTargeting()
        {
            if (_attackCardId < 0)
            {
                return;
            }
            for (int i = 0; i < _handFrames.Count; i++)
            {
                var entry = _handFrames[i];
                if (entry.Id == _attackCardId && entry.Frame != null)
                {
                    entry.Frame.SetSiblingIndex(i);
                    entry.Motion.TargetY = entry.RestY;
                    break;
                }
            }
            foreach (var (_, _, glow) in _playerRows)
            {
                if (glow != null)
                {
                    glow.SetActive(false);
                }
            }
            _attackCardId = -1;
            _attackHover = -1;
            _attackValid = null;
            UiKit.Clear(_attackArrowHost);
            _attackRoot.gameObject.SetActive(false);
            _preview.SetDragging(false);
        }

        /// <summary>
        /// Full-screen discard browser: dim backdrop, title, and a vertically scrolling
        /// grid of cards bounded to the center band. Click anywhere off a card to close.
        /// </summary>
        private void BuildDiscardViewer()
        {
            _discardOverlay = UiKit.CreatePanel(Root, "DiscardOverlay", new Color(0, 0, 0, 0.82f));
            UiKit.Anchor(_discardOverlay, Vector2.zero, Vector2.one);
            UiKit.AddClick(_discardOverlay.gameObject, CloseDiscardViewer);

            _discardTitle = UiKit.CreateText(_discardOverlay, "", 34,
                TextAnchor.MiddleCenter, Color.white);
            UiKit.Anchor((RectTransform)_discardTitle.transform,
                new Vector2(0.1f, 0.89f), new Vector2(0.9f, 0.97f));
            _discardTitle.raycastTarget = false;
            UiKit.AddTextShadow(_discardTitle);

            // Transparent-but-raycastable scroll surface: the wheel works anywhere in
            // the band, and a click between cards still closes the viewer.
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(_discardOverlay, false);
            UiKit.Anchor((RectTransform)scrollGo.transform, new Vector2(0.21f, 0.02f), new Vector2(0.79f, 0.89f));
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            UiKit.AddClick(scrollGo, CloseDiscardViewer);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            UiKit.Anchor((RectTransform)viewportGo.transform, Vector2.zero, Vector2.one);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero; // default is (100,100): +100px width overflow
            // Reading size — same scale as the Lemon Lord picker at game setup.
            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(280, 392);
            grid.spacing = new Vector2(18, 18);
            // Generous side padding: edge cards keep their rounded corners and glow
            // clear of the viewport mask instead of getting shaved off.
            grid.padding = new RectOffset(20, 20, 12, 12);
            grid.childAlignment = TextAnchor.UpperCenter;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = (RectTransform)viewportGo.transform;
            scroll.content = content;
            _discardGrid = content;
            _discardScrollRect = scroll;

            // Bottom-center: "TAKE <CARD>" — only exists while a pick is required.
            var buttonGo = new GameObject("TakeButton", typeof(RectTransform), typeof(Image), typeof(Shadow));
            buttonGo.transform.SetParent(_discardOverlay, false);
            var buttonRect = (RectTransform)buttonGo.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(440f, 58f);
            buttonRect.anchoredPosition = new Vector2(0, 26f);
            var buttonImage = buttonGo.GetComponent<Image>();
            buttonImage.sprite = UiSprites.RoundedRect;
            buttonImage.type = Image.Type.Sliced;
            buttonImage.color = UiKit.ButtonColor;
            var buttonShadow = buttonGo.GetComponent<Shadow>();
            buttonShadow.effectColor = new Color(0, 0, 0, 0.5f);
            buttonShadow.effectDistance = new Vector2(0, -4f);
            _discardTakeLabel = UiKit.CreateText(buttonGo.transform, "", 24,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
            _discardTakeLabel.raycastTarget = false;
            UiKit.Anchor((RectTransform)_discardTakeLabel.transform, Vector2.zero, Vector2.one);
            UiKit.AddHover(buttonGo,
                () => buttonImage.color = new Color(1f, 0.92f, 0.35f),
                () => buttonImage.color = UiKit.ButtonColor);
            UiKit.AddClick(buttonGo, () =>
            {
                var onTake = _discardOnTake;
                int? picked = _discardSelectedId;
                CloseDiscardViewer();
                if (onTake != null && picked is int id)
                {
                    onTake(id);
                }
            });
            _discardTakeButton = buttonGo;

            // Bottom-left: BACK — only for multi-step flows (e.g. Finders Keepers,
            // returning to the victim choice).
            var backGo = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Shadow));
            backGo.transform.SetParent(_discardOverlay, false);
            var backRect = (RectTransform)backGo.transform;
            backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 0f);
            backRect.pivot = new Vector2(0f, 0f);
            backRect.sizeDelta = new Vector2(180f, 58f);
            backRect.anchoredPosition = new Vector2(30f, 26f);
            var backImage = backGo.GetComponent<Image>();
            backImage.sprite = UiSprites.RoundedRect;
            backImage.type = Image.Type.Sliced;
            backImage.color = new Color(0.15f, 0.18f, 0.25f, 0.96f);
            var backShadow = backGo.GetComponent<Shadow>();
            backShadow.effectColor = new Color(0, 0, 0, 0.5f);
            backShadow.effectDistance = new Vector2(0, -4f);
            var backLabel = UiKit.CreateText(backGo.transform, "< BACK", 22,
                TextAnchor.MiddleCenter, new Color(0.96f, 0.96f, 0.92f));
            backLabel.raycastTarget = false;
            UiKit.Anchor((RectTransform)backLabel.transform, Vector2.zero, Vector2.one);
            UiKit.AddHover(backGo,
                () => backImage.color = new Color(0.22f, 0.26f, 0.35f, 0.96f),
                () => backImage.color = new Color(0.15f, 0.18f, 0.25f, 0.96f));
            UiKit.AddClick(backGo, () =>
            {
                var back = _discardOnBack;
                CloseDiscardViewer();
                back?.Invoke();
            });
            _discardBackButton = backGo;

            _discardOverlay.gameObject.SetActive(false);
        }

        /// <summary>Browse a discard pile (no selection, no button).</summary>
        private void OpenDiscardViewer(string title, IReadOnlyList<PlayerView.CardInfo> cards,
            bool blackMarket)
        {
            _discardTitle.text = cards.Count == 0 ? $"{title} — EMPTY" : $"{title} ({cards.Count})";
            FillDiscardGrid(cards, blackMarket, null);
            _discardOverlay.gameObject.SetActive(true);
        }

        /// <summary>
        /// Pick a card out of a discard pile (Reduce and Reuse, Reverse Engineer...):
        /// click a card to select it, then confirm with the TAKE button. Clicking off a
        /// card cancels — nothing has been submitted yet at that point.
        /// </summary>
        public void OpenDiscardPicker(string title, IReadOnlyList<PlayerView.CardInfo> cards,
            bool blackMarket, System.Func<PlayerView.CardInfo, string> nameOf,
            System.Action<int> onTake, System.Action onBack = null)
        {
            _discardTitle.text = title;
            _discardOnTake = onTake;
            _discardOnBack = onBack;
            FillDiscardGrid(cards, blackMarket, nameOf);
            _discardBackButton.SetActive(onBack != null);
            _discardOverlay.gameObject.SetActive(true);
        }

        private void FillDiscardGrid(IReadOnlyList<PlayerView.CardInfo> cards, bool blackMarket,
            System.Func<PlayerView.CardInfo, string> nameOf)
        {
            UiKit.Clear(_discardGrid);
            _discardSelectGlows.Clear();
            _discardSelectedId = null;
            _discardTakeButton.SetActive(false);

            for (int i = cards.Count - 1; i >= 0; i--) // newest discard first
            {
                var card = cards[i];
                var texture = blackMarket
                    ? _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square)
                    : _art.Lemon(card.DefId);

                // Wrapper cell: the selection glow must sit OUTSIDE the card's rounded
                // mask, or it would be clipped to the card shape.
                var cellGo = new GameObject("DiscardCell", typeof(RectTransform));
                cellGo.transform.SetParent(_discardGrid, false);
                var cell = (RectTransform)cellGo.transform;
                var glow = UiKit.CreateGlow(cell, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, 280 + 52, 392 + 52, DropGlowHot);
                var image = UiKit.CreateCardImage(cell, texture, 280, 392);
                UiKit.Anchor((RectTransform)image.transform.parent, Vector2.zero, Vector2.one);

                _preview.Attach(image.gameObject, texture);
                if (nameOf != null)
                {
                    int instanceId = card.InstanceId;
                    string takeName = nameOf(card);
                    _discardSelectGlows[instanceId] = glow;
                    UiKit.AddClick(image.gameObject, () => SelectDiscardCard(instanceId, takeName));
                }
            }
        }

        private void SelectDiscardCard(int instanceId, string name)
        {
            foreach (var pair in _discardSelectGlows)
            {
                pair.Value.SetActive(pair.Key == instanceId);
            }
            _discardSelectedId = instanceId;
            _discardTakeLabel.text = $"TAKE {name.ToUpperInvariant()}";
            _discardTakeButton.SetActive(true);
        }

        /// <summary>
        /// Hover-based scrolling for the discard browser/picker: the top and bottom 20%
        /// of the screen glide the grid, faster nearer the edge. Call every frame.
        /// </summary>
        public void TickDiscardScroll(Vector2 screenPosition)
        {
            if (!_discardOverlay.gameObject.activeSelf)
            {
                return;
            }
            float scrollable = _discardGrid.rect.height - _discardScrollRect.viewport.rect.height;
            if (scrollable <= 1f)
            {
                return; // everything fits, nothing to scroll
            }
            float fraction = screenPosition.y / Mathf.Max(1f, Screen.height);
            const float zone = 0.2f;
            float direction = 0f;
            if (fraction > 1f - zone)
            {
                direction = Mathf.InverseLerp(1f - zone, 1f, fraction);
            }
            else if (fraction < zone)
            {
                direction = -Mathf.InverseLerp(zone, 0f, fraction);
            }
            if (direction == 0f)
            {
                return;
            }
            _discardScrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                _discardScrollRect.verticalNormalizedPosition +
                direction * 900f * Time.deltaTime / scrollable);
        }

        private void CloseDiscardViewer()
        {
            _discardOverlay.gameObject.SetActive(false);
            _discardOnTake = null;
            _discardOnBack = null;
            _discardSelectedId = null;
            _discardTakeButton.SetActive(false);
            _discardBackButton.SetActive(false);
        }

        // ------------------------------------------------------------ render

        // ------------------------------------------------------- action log

        private RectTransform _logCollapsed;
        private RectTransform _logExpandedPanel;
        private RectTransform _logList;
        private TMP_Text _logLatest;
        private readonly List<string> _logLines = new List<string>();
        private bool _logExpanded;
        private string _logSignature = "";

        private void BuildActionLog()
        {
            var zone = UiKit.CreatePanel(Root, "ActionLog", new Color(0, 0, 0, 0));
            zone.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(zone, new Vector2(0.79f, 0.24f), new Vector2(1f, 0.695f),
                new Vector2(4, 0), new Vector2(-10, -4));

            // Collapsed: the latest line on a slim chip at the zone's bottom.
            _logCollapsed = UiKit.CreatePanel(zone, "LogCollapsed", new Color(0, 0, 0, 0.40f));
            UiKit.Anchor(_logCollapsed, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 0), new Vector2(0, 26));
            _logLatest = UiKit.CreateText(_logCollapsed, "", 13, TextAnchor.MiddleLeft,
                new Color(0.86f, 0.88f, 0.92f), body: true);
            _logLatest.raycastTarget = false;
            UiKit.Anchor((RectTransform)_logLatest.transform, Vector2.zero, Vector2.one,
                new Vector2(8, 0), new Vector2(-26, 0));
            var expandHint = UiKit.CreateText(_logCollapsed, "+", 15, TextAnchor.MiddleCenter,
                new Color(1f, 0.92f, 0.55f));
            expandHint.raycastTarget = false;
            UiKit.Anchor((RectTransform)expandHint.transform, new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(-24, 0), new Vector2(-4, 0));
            UiKit.AddClick(_logCollapsed.gameObject, ToggleLog);
            _logCollapsed.gameObject.SetActive(false);

            // Expanded: the recent history, newest at the top, header click collapses.
            _logExpandedPanel = UiKit.CreatePanel(zone, "LogExpanded", new Color(0.05f, 0.07f, 0.11f, 0.93f));
            UiKit.Anchor(_logExpandedPanel, Vector2.zero, Vector2.one);
            var header = UiKit.CreatePanel(_logExpandedPanel, "LogHeader", new Color(0, 0, 0, 0.45f));
            UiKit.Anchor(header, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -24), new Vector2(0, 0));
            var headerText = UiKit.CreateText(header, "ACTION LOG", 13, TextAnchor.MiddleLeft,
                new Color(1f, 0.92f, 0.55f));
            headerText.raycastTarget = false;
            UiKit.Anchor((RectTransform)headerText.transform, Vector2.zero, Vector2.one,
                new Vector2(8, 0), new Vector2(0, 0));
            var collapseHint = UiKit.CreateText(header, "–", 15, TextAnchor.MiddleCenter,
                new Color(1f, 0.92f, 0.55f));
            collapseHint.raycastTarget = false;
            UiKit.Anchor((RectTransform)collapseHint.transform, new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(-24, 0), new Vector2(-4, 0));
            UiKit.AddClick(header.gameObject, ToggleLog);
            var listHost = UiKit.CreatePanel(_logExpandedPanel, "LogLines", new Color(0, 0, 0, 0));
            listHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(listHost, Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -26));
            _logList = UiKit.CreateScrollList(listHost);
            _logList.GetComponent<VerticalLayoutGroup>().spacing = 2;
            _logExpandedPanel.gameObject.SetActive(false);
        }

        private void ToggleLog()
        {
            _logExpanded = !_logExpanded;
            _logExpandedPanel.gameObject.SetActive(_logExpanded);
            RenderLog();
        }

        /// <summary>Feed the friendly action-log lines (oldest first).</summary>
        public void SetLog(IEnumerable<string> lines)
        {
            _logLines.Clear();
            if (lines != null)
            {
                _logLines.AddRange(lines);
            }
            string signature = _logLines.Count + "|" +
                (_logLines.Count > 0 ? _logLines[_logLines.Count - 1] : "");
            if (signature == _logSignature)
            {
                return;
            }
            _logSignature = signature;
            RenderLog();
        }

        private void RenderLog()
        {
            _logLatest.text = _logLines.Count > 0 ? _logLines[_logLines.Count - 1] : "";
            _logCollapsed.gameObject.SetActive(!_logExpanded && _logLines.Count > 0);
            if (!_logExpanded)
            {
                return;
            }
            UiKit.Clear(_logList);
            for (int i = _logLines.Count - 1; i >= 0; i--)
            {
                var line = UiKit.CreateText(_logList, _logLines[i], 13, TextAnchor.MiddleLeft,
                    i == _logLines.Count - 1 ? Color.white : new Color(0.78f, 0.80f, 0.84f),
                    body: true);
                line.gameObject.AddComponent<LayoutElement>().minHeight = 18;
            }
        }

        public void Render(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            _db = db;
            if (_attackCardId >= 0)
            {
                // Hand and player bars are about to be rebuilt under the aiming layer.
                EndAttackTargeting();
            }
            _pickHover = -1;
            RenderMarket(view, db, groups);
            RenderBoard(view);
            RenderHand(view, groups);
            RenderLords(view);
            RenderFirstDibs(view);
            RenderSupply(view, db, groups);
            RenderPlayers(view);
        }

        private void RenderMarket(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            UiKit.Clear(_marketRow);
            // The discard piles form their own group: Lemon, then Black Market,
            // separated from the face-up market cards.
            BuildDiscardPile(view, blackMarket: false);
            BuildDiscardPile(view, blackMarket: true);
            AddRowGap();
            for (int i = 0; i < view.Market.Count; i++)
            {
                var card = view.Market[i];
                var texture = _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square);
                BuildMarketCell(i, texture);
            }
        }

        /// <summary>A market card: hover glows it; drag it onto your turf/stands to buy.</summary>
        private void BuildMarketCell(int marketIndex, Texture2D texture)
        {
            const float width = 154f;
            const float height = 216f;

            var cell = new GameObject("MarketCell", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_marketRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width + 8;
            cellElement.preferredHeight = height + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var liftGo = new GameObject("Lift", typeof(RectTransform));
            liftGo.transform.SetParent(cell.transform, false);
            var lift = (RectTransform)liftGo.transform;
            lift.anchorMin = new Vector2(0.5f, 0f);
            lift.anchorMax = new Vector2(0.5f, 0f);
            lift.pivot = new Vector2(0.5f, 0f);
            lift.sizeDelta = new Vector2(width, height);
            lift.anchoredPosition = Vector2.zero;

            // Glow margins split evenly above and below: centered on the card.
            var top = new Vector2(0.5f, 1f);
            var glowOuter = UiKit.CreateGlow(lift, top, top, new Vector2(0, 22),
                width + 44, height + 44, GlowOuterColor);
            var glowInner = UiKit.CreateGlow(lift, top, top, new Vector2(0, 10),
                width + 20, height + 20, GlowInnerColor);

            var image = UiKit.CreateCardImage(lift, texture, width, height);
            var frame = (RectTransform)image.transform.parent;
            frame.anchorMin = top;
            frame.anchorMax = top;
            frame.pivot = top;
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(width, height);

            _preview.Attach(image.gameObject, texture);
            var drag = image.gameObject.AddComponent<DragSource>();
            drag.Kind = DragKind.MarketCard;
            drag.MarketIndex = marketIndex;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.GlowInner = glowInner;
            drag.GlowOuter = glowOuter;
            drag.CanAct = () => CanBuyMarket?.Invoke(marketIndex) == true;
            drag.DragStarted = () => OnMarketDragStart?.Invoke(marketIndex);
            drag.DragEnded = () => OnMarketDragEnd?.Invoke();
        }

        private void RenderBoard(PlayerView view)
        {
            UiKit.Clear(_boardRow);
            if (_targetingPick != null)
            {
                // The board is being rebuilt under the targeting layer: dismiss it.
                EndEquipTargeting();
            }
            _dropGlows.Clear();
            _dropCells.Clear();
            _standCells.Clear();
            _spacer = null; // destroyed with the row; recreated lazily
            _spacerTween = null;
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
            RectTransform vpCell;
            if (vpTextures.Count == 0)
            {
                vpCell = AddVictoryPlaceholder(_boardRow);
            }
            else
            {
                vpCell = AddCard(_boardRow, vpTextures[0], 188, 263,
                    vpTextures.Count == 1 ? "1 VP" : $"{vpTextures.Count} VP", false, null);
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

        private void RenderHand(PlayerView view, MoveGroups groups)
        {
            UiKit.Clear(_handHost);
            _handFrames.Clear();
            int count = view.Hand.Count;
            if (count == 0)
            {
                _handOrder.Clear();
                return;
            }

            // Respect the player's own arrangement (drag a card sideways to move it);
            // cards we haven't seen yet — fresh draws — join on the right.
            var hand = view.Hand
                .OrderBy(c =>
                {
                    int index = _handOrder.IndexOf(c.InstanceId);
                    return index < 0 ? int.MaxValue : index;
                })
                .ToList();
            _handOrder.Clear();
            _handOrder.AddRange(hand.Select(c => c.InstanceId));

            const float width = 190f;
            const float height = 266f;
            const float raisedY = 12f;   // fully visible, floating just off the edge
            const float peek = 172f;     // ~2/3 visible at rest, one even row

            float hostWidth = _handHost.rect.width;
            if (hostWidth < 10f)
            {
                hostWidth = 1075f; // first-frame fallback before canvas layout settles
            }
            // Constant Dune Imperium overlap; a big hand scrolls instead of compressing
            // past readability. The usable width is inset well past the mask's fade
            // zones so the end cards rest comfortably inside the band at either
            // scroll extreme, not flush against its edges.
            const float edgeFade = 130f;
            float usable = hostWidth - edgeFade * 2f;
            float spacing = width * 0.72f;
            float span = width + spacing * (count - 1);
            _handMaxScroll = Mathf.Max(0f, span - usable);
            _handScroll = Mathf.Clamp(_handScroll, 0f, _handMaxScroll);
            float startX = _handMaxScroll > 0f
                ? -hostWidth / 2f + edgeFade + width / 2f
                : -span / 2f + width / 2f;
            _handStartX = startX;
            _handSpacing = spacing;

            for (int i = 0; i < count; i++)
            {
                var card = hand[i];
                int optionCount = groups?.HandMoves.TryGetValue(card.InstanceId, out var moves) == true
                    ? moves.Count
                    : 0;
                var texture = _art.Lemon(card.DefId);

                var image = UiKit.CreateCardImage(_handHost, texture, width, height);
                var frame = (RectTransform)image.transform.parent;
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
                frame.pivot = new Vector2(0.5f, 0f);
                frame.sizeDelta = new Vector2(width, height);
                float restY = peek - height;
                float baseX = startX + i * spacing;
                frame.anchoredPosition = new Vector2(baseX - _handScroll, restY);
                var motion = frame.gameObject.AddComponent<HandCardMotion>();
                motion.TargetY = restY;
                int captured = card.InstanceId;
                _handFrames.Add((captured, frame, baseX, motion, restY));

                if (optionCount > 0)
                {
                    // Attack cards aim at a player bar instead of opening the menu.
                    UiKit.AddClick(image.gameObject, () =>
                    {
                        var targets = AttackTargetsFor?.Invoke(captured);
                        if (targets != null && targets.Count > 0)
                        {
                            BeginAttackTargeting(captured, targets, dragMode: false);
                        }
                        else
                        {
                            OnHandCard?.Invoke(captured);
                        }
                    });

                    // Or drag: past the hand band the card arms and the arrow appears;
                    // release over a glowing bar to fire, anywhere else to abort.
                    var drag = image.gameObject.AddComponent<DragRelay>();
                    drag.Began = position =>
                    {
                        _attackDragAborted = false;
                        _handDragStart = position;
                        _preview.SetDragging(true);
                    };
                    drag.Moved = position =>
                    {
                        if (_attackCardId < 0 && _reorderCardId < 0 && !_attackDragAborted &&
                            !RectTransformUtility.RectangleContainsScreenPoint(_handHost, position))
                        {
                            var targets = AttackTargetsFor?.Invoke(captured);
                            if (targets != null && targets.Count > 0)
                            {
                                BeginAttackTargeting(captured, targets, dragMode: true);
                            }
                            return;
                        }
                        // Sideways inside the band: rearrange the hand instead.
                        if (_attackCardId < 0 &&
                            RectTransformUtility.RectangleContainsScreenPoint(_handHost, position))
                        {
                            if (_reorderCardId < 0 &&
                                Mathf.Abs(position.x - _handDragStart.x) > 30f)
                            {
                                _reorderCardId = captured;
                                frame.SetAsLastSibling();
                                motion.TargetY = raisedY;
                            }
                            if (_reorderCardId == captured)
                            {
                                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                                    _handHost, position, null, out var local);
                                frame.anchoredPosition = new Vector2(
                                    local.x, frame.anchoredPosition.y);
                            }
                        }
                    };
                    drag.Ended = position =>
                    {
                        if (_reorderCardId == captured)
                        {
                            _reorderCardId = -1;
                            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                                _handHost, position, null, out var local);
                            int slot = _handSpacing > 0f
                                ? Mathf.RoundToInt((local.x + _handScroll - _handStartX) / _handSpacing)
                                : 0;
                            _handOrder.Remove(captured);
                            _handOrder.Insert(Mathf.Clamp(slot, 0, _handOrder.Count), captured);
                            OnBoardViewChanged?.Invoke(); // force a re-render in the new order
                        }
                        if (_attackCardId == captured && _attackDragMode)
                        {
                            FinishAttackTargeting();
                        }
                        _preview.SetDragging(false);
                    };
                }

                int sibling = i;
                UiKit.AddHover(image.gameObject,
                    () =>
                    {
                        if (_attackCardId >= 0)
                        {
                            return; // the armed card owns the top slot while aiming
                        }
                        frame.SetAsLastSibling();
                        motion.TargetY = raisedY;
                    },
                    () =>
                    {
                        if (_attackCardId == captured)
                        {
                            return; // stays raised while aiming
                        }
                        frame.SetSiblingIndex(sibling);
                        motion.TargetY = restY;
                    });
                _preview.Attach(image.gameObject, texture);
            }
        }

        private float _boardScroll;
        private RectMask2D _boardMask;

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

        /// <summary>
        /// Hover-based fan scrolling: with the cursor inside the hand zone, the outer
        /// 20% on each side auto-scrolls that direction, faster nearer the edge.
        /// Call every frame.
        /// </summary>
        public void TickHandScroll(Vector2 screenPosition)
        {
            if (_handMaxScroll <= 0f ||
                !RectTransformUtility.RectangleContainsScreenPoint(_handHost, screenPosition))
            {
                return;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _handHost, screenPosition, null, out var local);
            var rect = _handHost.rect;
            float fraction = (local.x - rect.xMin) / Mathf.Max(1f, rect.width);

            const float zone = 0.2f;
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

            _handScroll = Mathf.Clamp(
                _handScroll + velocity * 900f * Time.deltaTime, 0f, _handMaxScroll);
            foreach (var entry in _handFrames)
            {
                if (entry.Frame != null)
                {
                    var position = entry.Frame.anchoredPosition;
                    position.x = entry.BaseX - _handScroll;
                    entry.Frame.anchoredPosition = position;
                }
            }
        }

        /// <summary>
        /// First Dibs titles: overlapped peek cards, bottom-left. Unclaimed titles from
        /// the row first, then claimed ones wearing their claimant's name chip.
        /// </summary>
        private void RenderFirstDibs(PlayerView view)
        {
            UiKit.Clear(_dibsHost);
            var entries = new List<(string TitleId, string ClaimedBy)>();
            foreach (string titleId in view.FirstDibsRow)
            {
                entries.Add((titleId, null));
            }
            foreach (var player in view.Players)
            {
                foreach (string claimed in player.FirstDibsClaimed)
                {
                    entries.Add((claimed, player.Name));
                }
            }
            if (entries.Count == 0)
            {
                return;
            }

            const float width = 190f;
            const float height = 266f;
            const float raisedY = 12f;
            const float peek = 158f;

            float available = _dibsHost.rect.width;
            if (available < 10f)
            {
                available = 390f;
            }
            // The zone is narrow: compress overlap as needed, hover-raise keeps it readable.
            float spacing = entries.Count > 1
                ? Mathf.Min(width * 0.72f, (available - width) / (entries.Count - 1))
                : 0f;
            float startX = -(width + spacing * (entries.Count - 1)) / 2f + width / 2f;

            for (int i = 0; i < entries.Count; i++)
            {
                var (titleId, claimedBy) = entries[i];
                var texture = _art.Title(titleId);

                var image = UiKit.CreateCardImage(_dibsHost, texture, width, height);
                var frame = (RectTransform)image.transform.parent;
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
                frame.pivot = new Vector2(0.5f, 0f);
                frame.sizeDelta = new Vector2(width, height);
                float restY = peek - height;
                frame.anchoredPosition = new Vector2(startX + i * spacing, restY);
                var motion = frame.gameObject.AddComponent<HandCardMotion>();
                motion.TargetY = restY;

                if (claimedBy != null)
                {
                    var chip = UiKit.CreatePanel(frame, "ClaimChip", UiKit.ButtonColor);
                    UiKit.Anchor(chip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                    chip.sizeDelta = new Vector2(width - 40f, 24f);
                    chip.anchoredPosition = new Vector2(0, -24f);
                    var chipText = UiKit.CreateText(chip, claimedBy.ToUpperInvariant(), 13,
                        TextAnchor.MiddleCenter, UiKit.ButtonTextColor, body: true);
                    UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);
                }

                int sibling = i;
                UiKit.AddHover(image.gameObject,
                    () =>
                    {
                        frame.SetAsLastSibling();
                        motion.TargetY = raisedY;
                    },
                    () =>
                    {
                        frame.SetSiblingIndex(sibling);
                        motion.TargetY = restY;
                    });
                _preview.Attach(image.gameObject, texture);
            }
        }

        /// <summary>Your two secret Lemon Lord titles: overlapped peek cards, bottom-right.</summary>
        private void RenderLords(PlayerView view)
        {
            UiKit.Clear(_lordHost);
            int count = view.LemonLordStatus.Count;
            if (count == 0)
            {
                return;
            }

            const float width = 190f;
            const float height = 266f;
            const float raisedY = 12f;
            const float peek = 158f;
            float spacing = width * 0.72f;
            float startX = -(width + spacing * (count - 1)) / 2f + width / 2f;

            for (int i = 0; i < count; i++)
            {
                var lord = view.LemonLordStatus[i];
                var texture = _art.Title(lord.TitleId);

                var image = UiKit.CreateCardImage(_lordHost, texture, width, height);
                var frame = (RectTransform)image.transform.parent;
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
                frame.pivot = new Vector2(0.5f, 0f);
                frame.sizeDelta = new Vector2(width, height);
                float restY = peek - height;
                frame.anchoredPosition = new Vector2(startX + i * spacing, restY);
                var motion = frame.gameObject.AddComponent<HandCardMotion>();
                motion.TargetY = restY;

                if (lord.Met)
                {
                    var chip = UiKit.CreatePanel(frame, "MetChip", UiKit.ButtonColor);
                    UiKit.Anchor(chip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                    chip.sizeDelta = new Vector2(74f, 24f);
                    chip.anchoredPosition = new Vector2(0, -24f);
                    var chipText = UiKit.CreateText(chip, "MET!", 14,
                        TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
                    UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);
                }

                int sibling = i;
                UiKit.AddHover(image.gameObject,
                    () =>
                    {
                        frame.SetAsLastSibling();
                        motion.TargetY = raisedY;
                    },
                    () =>
                    {
                        frame.SetSiblingIndex(sibling);
                        motion.TargetY = restY;
                    });
                _preview.Attach(image.gameObject, texture);
            }
        }

        /// <summary>Appends to the market row — call after RenderMarket (which clears it).</summary>
        private void RenderSupply(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            // Equal stretchy gaps split the shelf: market | stands | Bragging Rights.
            AddRowGap();

            foreach (var type in db.StandTypes)
            {
                var texture = view.SupplyTopShapes.TryGetValue(type.Id, out var shape)
                    ? _art.Stand(type.Id, shape)
                    : _art.Stand(type.Id);
                BuildSupplyCell(type.Id, texture);
            }

            if (view.NextBraggingRightsPrice is int braggingPrice)
            {
                AddRowGap();
                // Index derived from price: $16 base, +$2 per sale.
                int sold = (braggingPrice - 16) / 2;
                BuildBraggingCell(braggingPrice, _art.BraggingRights(sold));
            }
        }

        /// <summary>
        /// The Bragging Rights shelf card: wears its price, and drags onto your VP
        /// column to buy — the same lift-glow-ghost language as the market cards.
        /// </summary>
        private void BuildBraggingCell(int price, Texture2D texture)
        {
            const float width = 154f;
            const float height = 216f;

            var cell = new GameObject("BraggingCell", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_marketRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width + 8;
            cellElement.preferredHeight = height + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var liftGo = new GameObject("Lift", typeof(RectTransform));
            liftGo.transform.SetParent(cell.transform, false);
            var lift = (RectTransform)liftGo.transform;
            lift.anchorMin = new Vector2(0.5f, 0f);
            lift.anchorMax = new Vector2(0.5f, 0f);
            lift.pivot = new Vector2(0.5f, 0f);
            lift.sizeDelta = new Vector2(width, height);
            lift.anchoredPosition = Vector2.zero;

            var top = new Vector2(0.5f, 1f);
            var glowOuter = UiKit.CreateGlow(lift, top, top, new Vector2(0, 22),
                width + 44, height + 44, GlowOuterColor);
            var glowInner = UiKit.CreateGlow(lift, top, top, new Vector2(0, 10),
                width + 20, height + 20, GlowInnerColor);

            var image = UiKit.CreateCardImage(lift, texture, width, height);
            var frame = (RectTransform)image.transform.parent;
            frame.anchorMin = top;
            frame.anchorMax = top;
            frame.pivot = top;
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(width, height);
            _preview.Attach(image.gameObject, texture);

            // Price chip along the bottom edge; click-transparent like the count chips.
            var chip = UiKit.CreatePanel(frame, "PriceChip", new Color(0, 0, 0, 0.78f));
            UiKit.Anchor(chip, new Vector2(0, 0), new Vector2(1, 0));
            chip.sizeDelta = new Vector2(0, 30);
            chip.pivot = new Vector2(0.5f, 0);
            chip.GetComponent<Image>().raycastTarget = false;
            var chipText = UiKit.CreateText(chip, $"${price}", 16,
                TextAnchor.MiddleCenter, Color.white, body: true);
            chipText.raycastTarget = false;
            UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);

            var drag = image.gameObject.AddComponent<DragSource>();
            drag.Kind = DragKind.BraggingRights;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.GlowInner = glowInner;
            drag.GlowOuter = glowOuter;
            drag.CanAct = () => CanBuyBragging?.Invoke() == true;
            drag.DragStarted = () =>
            {
                _preview.SetDragging(true);
                if (_vpGlow != null)
                {
                    _vpGlow.SetActive(true);
                }
            };
            drag.DragEnded = () =>
            {
                _preview.SetDragging(false);
                if (_vpGlow != null)
                {
                    _vpGlow.SetActive(false);
                }
            };
        }

        /// <summary>A discard pile: card back, count on hover, click to browse.</summary>
        private void BuildDiscardPile(PlayerView view, bool blackMarket)
        {
            var back = _art.Back(blackMarket ? "blackMarket" : "lemon");
            var image = UiKit.CreateCardImage(_marketRow, back, 154, 216);
            var frame = (RectTransform)image.transform.parent;

            var discards = blackMarket ? view.BlackMarketDiscard : view.LemonDiscard;
            int count = discards.Count;
            var chip = UiKit.CreatePanel(frame, "CountChip", new Color(0, 0, 0, 0.78f));
            UiKit.Anchor(chip, new Vector2(0, 0.5f), new Vector2(1, 0.5f));
            chip.sizeDelta = new Vector2(0, 36);
            // Click-transparent, or the chip steals the raycast from the card under the
            // cursor and hover enters/exits in an endless flicker loop.
            chip.GetComponent<Image>().raycastTarget = false;
            var chipText = UiKit.CreateText(chip,
                count == 1 ? "1 DISCARD" : $"{count} DISCARDS", 16,
                TextAnchor.MiddleCenter, Color.white, body: true);
            chipText.raycastTarget = false;
            UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);
            chip.gameObject.SetActive(false);

            UiKit.AddHover(image.gameObject,
                () => chip.gameObject.SetActive(true),
                () => chip.gameObject.SetActive(false));
            UiKit.AddClick(image.gameObject, () => OpenDiscardViewer(
                blackMarket ? "BLACK MARKET DISCARDS" : "LEMON DISCARDS",
                discards, blackMarket));
        }

        /// <summary>A flexible spacer; multiple gaps in the row share leftover width equally.</summary>
        private void AddRowGap()
        {
            var gap = new GameObject("RowGap", typeof(RectTransform), typeof(LayoutElement));
            gap.transform.SetParent(_marketRow, false);
            var gapElement = gap.GetComponent<LayoutElement>();
            gapElement.preferredWidth = 24;
            gapElement.flexibleWidth = 1;
        }

        /// <summary>
        /// Player panels, Dune Imperium style: turn order top to bottom starting with
        /// the player after you; you sit pinned at the bottom. A lemon marks whoever
        /// holds the active turn.
        /// </summary>
        private void RenderPlayers(PlayerView view)
        {
            UiKit.Clear(_playersColumn);
            _playerRows.Clear();
            int count = view.Players.Count;
            int viewedBoard = ViewedBoardPlayer >= 0 ? ViewedBoardPlayer : view.ViewerId;
            for (int offset = 1; offset <= count; offset++)
            {
                int playerId = (view.ViewerId + offset) % count;
                bool isMe = playerId == view.ViewerId;
                if (isMe)
                {
                    // Stretchy spacer pushes your own panel to the column's bottom.
                    var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
                    spacer.transform.SetParent(_playersColumn, false);
                    spacer.GetComponent<LayoutElement>().flexibleHeight = 1;
                }
                BuildPlayerRow(view, playerId, isMe, playerId == viewedBoard);
            }
        }

        private void BuildPlayerRow(PlayerView view, int playerId, bool isMe, bool isViewedBoard)
        {
            var player = view.Players[playerId];

            var row = new GameObject("PlayerRow", typeof(RectTransform), typeof(Image),
                typeof(LayoutElement));
            row.transform.SetParent(_playersColumn, false);
            var background = row.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.color = new Color(0, 0, 0, 0.32f);
            row.GetComponent<LayoutElement>().minHeight = 82;

            // Active-turn lemon, right edge.
            if (playerId == view.ActivePlayer && _lemonIcon != null)
            {
                var lemonGo = new GameObject("ActiveLemon", typeof(RectTransform), typeof(RawImage));
                lemonGo.transform.SetParent(row.transform, false);
                var lemonRect = (RectTransform)lemonGo.transform;
                lemonRect.anchorMin = lemonRect.anchorMax = new Vector2(1f, 0.5f);
                lemonRect.pivot = new Vector2(1f, 0.5f);
                lemonRect.sizeDelta = new Vector2(38f, 38f);
                lemonRect.anchoredPosition = new Vector2(-10f, 0);
                var lemonImage = lemonGo.GetComponent<RawImage>();
                lemonImage.texture = _lemonIcon;
                lemonImage.raycastTarget = false;
            }

            // Circular badge on the left: dark color hashed from the name, big initial.
            var badgeGo = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(row.transform, false);
            var badgeRect = (RectTransform)badgeGo.transform;
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0f, 0.5f);
            badgeRect.sizeDelta = new Vector2(56f, 56f);
            badgeRect.anchoredPosition = new Vector2(12f, 0);
            var badgeImage = badgeGo.GetComponent<Image>();
            badgeImage.sprite = UiSprites.Circle;
            badgeImage.type = Image.Type.Simple;
            badgeImage.preserveAspect = true;
            badgeImage.color = BadgeColors[StableNameHash(player.Name) % BadgeColors.Length];
            badgeImage.raycastTarget = false;
            string initial = string.IsNullOrEmpty(player.Name)
                ? "?"
                : char.ToUpperInvariant(player.Name[0]).ToString();
            var letter = UiKit.CreateText(badgeGo.transform, initial, 30,
                TextAnchor.MiddleCenter, Color.white);
            letter.raycastTarget = false;
            UiKit.Anchor((RectTransform)letter.transform, Vector2.zero, Vector2.one);

            // Name on top, VP / Cash below. Bot seats wear their difficulty in the
            // same colors as the lobby chips.
            string nameLabel = player.Name + (isMe ? "  (you)" : "");
            string botLevel = BotLevelLookup?.Invoke(playerId);
            if (!string.IsNullOrEmpty(botLevel))
            {
                string levelColor = botLevel == "hard" ? "#FF9E73"
                    : botLevel == "easy" ? "#9EE59E" : "#D9E0EB";
                nameLabel += $"  <size=13><color={levelColor}>{botLevel.ToUpperInvariant()}</color></size>";
            }
            var nameText = UiKit.CreateText(row.transform,
                nameLabel, 19, TextAnchor.LowerLeft, Color.white);
            nameText.raycastTarget = false;
            UiKit.Anchor((RectTransform)nameText.transform, new Vector2(0, 0.56f), new Vector2(1, 1),
                new Vector2(80, 2), new Vector2(-52, -2));
            // Icon stats under the name: VP, cash, and tantrums (the physical game
            // keeps tantrums public on the table; the bar is our table).
            var statsGo = new GameObject("Stats", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            statsGo.transform.SetParent(row.transform, false);
            UiKit.Anchor((RectTransform)statsGo.transform, new Vector2(0, 0), new Vector2(1, 0.56f),
                new Vector2(80, 4), new Vector2(-52, -1));
            var statsLayout = statsGo.GetComponent<HorizontalLayoutGroup>();
            statsLayout.spacing = 4;
            statsLayout.childAlignment = TextAnchor.MiddleLeft;
            statsLayout.childForceExpandWidth = false;
            statsLayout.childForceExpandHeight = false;
            statsLayout.childControlWidth = true;
            statsLayout.childControlHeight = true;
            AddStat(statsGo.transform, _vpIcon, player.InGameVictoryPoints.ToString());
            AddStat(statsGo.transform, _cashIcon, $"${player.Money}");
            // Hand size is targeting intel: fewer cards = fewer possible reactions.
            AddStat(statsGo.transform, _art.Back("lemon"), player.HandCount.ToString(), iconWidth: 26f);
            AddStat(statsGo.transform, _tantrumIcon, player.TantrumCount.ToString(), last: true);

            // Yellow rim on whoever's board the table currently displays.
            if (isViewedBoard)
            {
                var borderGo = new GameObject("ViewedBorder", typeof(RectTransform), typeof(Image));
                borderGo.transform.SetParent(row.transform, false);
                UiKit.Anchor((RectTransform)borderGo.transform, Vector2.zero, Vector2.one,
                    new Vector2(-2, -2), new Vector2(2, 2));
                var borderImage = borderGo.GetComponent<Image>();
                borderImage.sprite = UiSprites.RoundedOutline;
                borderImage.type = Image.Type.Sliced;
                borderImage.color = UiKit.ButtonColor;
                borderImage.raycastTarget = false;
            }

            // Red glow around the OUTSIDE while an attack is being aimed at this
            // player: an outward-fading ring whose rim hugs the bar's edge (the ring
            // sprite's rim sits 31 units inside its rect), leaving the bar readable.
            var attackGlowGo = new GameObject("AttackGlow", typeof(RectTransform), typeof(Image));
            attackGlowGo.transform.SetParent(row.transform, false);
            attackGlowGo.transform.SetAsFirstSibling();
            UiKit.Anchor((RectTransform)attackGlowGo.transform, Vector2.zero, Vector2.one,
                new Vector2(-31, -31), new Vector2(31, 31));
            var attackGlowImage = attackGlowGo.GetComponent<Image>();
            attackGlowImage.sprite = UiSprites.GlowRing;
            attackGlowImage.type = Image.Type.Sliced;
            attackGlowImage.color = AttackGlowColor;
            attackGlowImage.raycastTarget = false;
            attackGlowGo.SetActive(false);
            _playerRows.Add((playerId, (RectTransform)row.transform, attackGlowGo));

            // Click a bar to view that player's board; your own bar comes home.
            int clickedId = playerId;
            int viewerId = view.ViewerId;
            UiKit.AddClick(row, () =>
            {
                ViewedBoardPlayer = clickedId == viewerId ? -1 : clickedId;
                OnBoardViewChanged?.Invoke();
            });
        }

        /// <summary>One icon-plus-count chip in a player bar's stat row.</summary>
        private static void AddStat(Transform parent, Texture2D icon, string count, bool last = false,
            float iconWidth = 36f)
        {
            if (icon != null)
            {
                var iconGo = new GameObject("StatIcon", typeof(RectTransform), typeof(RawImage),
                    typeof(LayoutElement));
                iconGo.transform.SetParent(parent, false);
                var iconElement = iconGo.GetComponent<LayoutElement>();
                iconElement.preferredWidth = iconWidth;
                iconElement.preferredHeight = 36;
                iconElement.flexibleWidth = 0;
                var iconImage = iconGo.GetComponent<RawImage>();
                iconImage.texture = icon;
                iconImage.raycastTarget = false;
            }
            var text = UiKit.CreateText(parent, count, 18, TextAnchor.MiddleLeft,
                new Color(0.82f, 0.84f, 0.88f), body: true);
            text.raycastTarget = false;
            var textElement = text.gameObject.AddComponent<LayoutElement>();
            textElement.flexibleWidth = 0;
            if (!last)
            {
                var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
                gap.transform.SetParent(parent, false);
                gap.GetComponent<LayoutElement>().preferredWidth = 6;
            }
        }

        /// <summary>World-space center of a player's bar — anchor for floaters/effects.</summary>
        public Vector3? PlayerBarWorld(int playerId)
        {
            foreach (var (id, row, _) in _playerRows)
            {
                if (id == playerId && row != null)
                {
                    return row.TransformPoint(row.rect.center);
                }
            }
            return null;
        }

        /// <summary>Deterministic across sessions (unlike string.GetHashCode).</summary>
        private static int StableNameHash(string name)
        {
            int hash = 17;
            foreach (char c in name ?? "")
            {
                hash = hash * 31 + c;
            }
            return Mathf.Abs(hash);
        }

        // ------------------------------------------- supply drag insertion

        /// <summary>A supply pile: hover glows it; drag it into your board row to buy a stand.</summary>
        private void BuildSupplyCell(string standTypeId, Texture2D texture)
        {
            const float width = 154f;
            const float height = 216f;

            var cell = new GameObject("SupplyCell", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_marketRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width + 8;
            cellElement.preferredHeight = height + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var liftGo = new GameObject("Lift", typeof(RectTransform));
            liftGo.transform.SetParent(cell.transform, false);
            var lift = (RectTransform)liftGo.transform;
            lift.anchorMin = new Vector2(0.5f, 0f);
            lift.anchorMax = new Vector2(0.5f, 0f);
            lift.pivot = new Vector2(0.5f, 0f);
            lift.sizeDelta = new Vector2(width, height);
            lift.anchoredPosition = Vector2.zero;

            // Glow margins split evenly above and below: centered on the card.
            var top = new Vector2(0.5f, 1f);
            var glowOuter = UiKit.CreateGlow(lift, top, top, new Vector2(0, 22),
                width + 44, height + 44, GlowOuterColor);
            var glowInner = UiKit.CreateGlow(lift, top, top, new Vector2(0, 10),
                width + 20, height + 20, GlowInnerColor);

            var image = UiKit.CreateCardImage(lift, texture, width, height);
            var frame = (RectTransform)image.transform.parent;
            frame.anchorMin = top;
            frame.anchorMax = top;
            frame.pivot = top;
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(width, height);

            _preview.Attach(image.gameObject, texture);
            var drag = image.gameObject.AddComponent<DragSource>();
            drag.Kind = DragKind.SupplyStand;
            drag.SupplyTypeId = standTypeId;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.GlowInner = glowInner;
            drag.GlowOuter = glowOuter;
            drag.CanAct = () => CanBuySupply?.Invoke(standTypeId) == true;
            drag.DragStarted = () =>
            {
                _supplyDragActive = true;
                _preview.SetDragging(true);
            };
            drag.DragEnded = () =>
            {
                _supplyDragActive = false;
                _preview.SetDragging(false);
                HideInsertPreview();
            };
        }

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

            _spacer = new GameObject("InsertSpacer", typeof(RectTransform),
                typeof(LayoutElement), typeof(LayoutWidthTween));
            _spacer.transform.SetParent(_boardRow, false);
            var element = _spacer.GetComponent<LayoutElement>();
            element.preferredWidth = 0;
            element.preferredHeight = 10;
            element.flexibleWidth = 0;

            int sibling = index < _standCells.Count
                ? _standCells[index].GetSiblingIndex()
                : _boardRow.childCount - 1;
            _spacer.transform.SetSiblingIndex(sibling);

            _spacerTween = _spacer.GetComponent<LayoutWidthTween>();
            _spacerTween.SetTarget(162f);
        }

        private void CollapseActiveSpacer()
        {
            if (_spacerTween != null)
            {
                _spacerTween.SetTarget(0f, destroyAtZero: true);
                _spacer = null;
                _spacerTween = null;
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
            AddTuckedStack(cell, equipped
                .Select(e => _art.BlackMarket(e.DefId, e.Shape ?? Shape.Square))
                .ToList());
        }

        /// <summary>
        /// Cards tucked BEHIND a cell's front card: each peeks its top strip out above,
        /// stacked like the physical game. Hover a peeking strip for the preview.
        /// </summary>
        private void AddTuckedStack(RectTransform cell, List<Texture2D> textures)
        {
            const float peek = 40f;   // the top icon bar, minus a hair — 5 stacks fit
            const float width = 188f; // same scale as the card it tucks behind
            const float height = 263f;
            var cardFrame = cell.Find("Card");
            if (cardFrame == null)
            {
                return;
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
                rect.anchoredPosition = new Vector2(0, peek * (i + 1));
                var image = go.GetComponent<RawImage>();
                image.texture = texture;
                image.material = UiKit.RoundedImageMaterial(width, height);
                _preview.Attach(go, texture);
            }
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

        /// <summary>
        /// Dashed outline matching the cards' rounded-corner geometry: dashes walk the
        /// full rounded-rect path (corners included) at an even spacing computed to fit
        /// the perimeter exactly, each rotated to its local tangent.
        /// </summary>
        private static void AddDashedRoundedOutline(RectTransform host, float width,
            float height, float radius, Color color)
        {
            const float thickness = 3f;
            const float dashLength = 13f;
            const float targetSpacing = 24f; // dash + gap, before fitting

            // Straight-edge lengths on the dash centerline.
            float straightW = width - 2f * radius;
            float straightH = height - 2f * radius;
            float arc = radius * Mathf.PI / 2f;
            float perimeter = 2f * (straightW + straightH) + 4f * arc;
            int count = Mathf.Max(8, Mathf.RoundToInt(perimeter / targetSpacing));
            float step = perimeter / count;

            for (int i = 0; i < count; i++)
            {
                var (position, angle) = PointOnRoundedRect(i * step, straightW, straightH, radius);
                AddDash(host, position, new Vector2(dashLength, thickness), color, angle);
            }
        }

        /// <summary>Position + tangent angle at distance d along a clockwise rounded rect
        /// (starting at the top edge's left end), centered on the origin.</summary>
        private static (Vector2 Position, float Angle) PointOnRoundedRect(
            float d, float straightW, float straightH, float radius)
        {
            float arc = radius * Mathf.PI / 2f;
            float hw = straightW / 2f;
            float hh = straightH / 2f;

            (Vector2, float) OnArc(Vector2 center, float startDegrees, float t)
            {
                float theta = startDegrees - 90f * t;
                var direction = new Vector2(
                    Mathf.Cos(theta * Mathf.Deg2Rad), Mathf.Sin(theta * Mathf.Deg2Rad));
                return (center + radius * direction, theta - 90f);
            }

            if (d < straightW) // top edge, left to right
            {
                return (new Vector2(-hw + d, hh + radius), 0f);
            }
            d -= straightW;
            if (d < arc)
            {
                return OnArc(new Vector2(hw, hh), 90f, d / arc);
            }
            d -= arc;
            if (d < straightH) // right edge, downward
            {
                return (new Vector2(hw + radius, hh - d), 90f);
            }
            d -= straightH;
            if (d < arc)
            {
                return OnArc(new Vector2(hw, -hh), 0f, d / arc);
            }
            d -= arc;
            if (d < straightW) // bottom edge, right to left
            {
                return (new Vector2(hw - d, -hh - radius), 0f);
            }
            d -= straightW;
            if (d < arc)
            {
                return OnArc(new Vector2(-hw, -hh), -90f, d / arc);
            }
            d -= arc;
            if (d < straightH) // left edge, upward
            {
                return (new Vector2(-hw - radius, -hh + d), 90f);
            }
            d -= straightH;
            return OnArc(new Vector2(-hw, hh), 180f, Mathf.Clamp01(d / arc));
        }

        private static void AddDash(RectTransform host, Vector2 position, Vector2 size,
            Color color, float angleDegrees = 0f)
        {
            var go = new GameObject("Dash", typeof(RectTransform), typeof(Image),
                typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            go.transform.SetParent(host, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localEulerAngles = new Vector3(0, 0, angleDegrees);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }
    }

    /// <summary>
    /// Eases a hand card toward its target height — the rise-on-hover, sink-on-exit
    /// motion. Exponential smoothing: fast start, soft landing.
    /// </summary>
    public sealed class HandCardMotion : MonoBehaviour
    {
        public float TargetY;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void Update()
        {
            var position = _rect.anchoredPosition;
            position.y = Mathf.Lerp(position.y, TargetY, 1f - Mathf.Exp(-14f * Time.deltaTime));
            _rect.anchoredPosition = position;
        }
    }
}
