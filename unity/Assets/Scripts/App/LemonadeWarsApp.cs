using System.IO;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The shell: menu -> (lobby) -> table. The table renders from PlayerView + typed
    /// legal moves through IGameSession, so offline play and networked play share every
    /// pixel of UI. Spawns itself on Play in any scene.
    ///
    /// Split by subsystem into LemonadeWarsApp.*.cs partials: Session (connect/lobby),
    /// GameEvents (theatre + action log), TableInput (gestures to moves), Render,
    /// Modals (decision surfaces), CardLookups, Diagnostics. This root owns the Unity
    /// lifecycle, the HUD build, and the state shared between subsystems.
    ///
    /// Keys in game: [B] autopilot, [N] new local game (offline only).
    /// </summary>
    public sealed partial class LemonadeWarsApp : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Camera.main == null)
            {
                var cam = new GameObject("Main Camera", typeof(Camera));
                cam.tag = "MainCamera";
            }
            // The camera's clear color IS the table: every play zone is transparent, so
            // this one value themes the whole screen. Set unconditionally so the scene
            // asset can't drift out of sync with UiKit.TableColor.
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = UiKit.TableColor;
            new GameObject("LemonadeWarsApp", typeof(LemonadeWarsApp));
        }

        private enum Screen
        {
            Menu,
            Lobby,
            Game,
        }

        private CardDatabase _db;
        private CardArt _art;
        private IGameSession _session;
        private RemoteGameSession _remote; // same object as _session when online
        private Screen _screen = Screen.Menu;

        // Cross-game turn-alert toast (top-right, any screen).
        private CanvasGroup _alertGroup;
        private TMP_Text _alertText;
        private float _alertUntil;

        private LobbyUi _lobby;
        private TableView _table;
        private Prompt _prompt;
        private CardPicker _picker;
        private CardPreview _preview;
        private TurnBanner _turnBanner;
        private DiceRoller _dice;
        private EffectsPlayer _fx;
        private PlayTheatre _playTheatre;
        private RectTransform _turnActionRow;
        private TMP_Text _tableMessage;
        /// <summary>Headline of the open response panel, shown as the table message while it is up.</summary>
        private string _reactionBanner = "";
        private PauseMenu _pause;
        private RulebookViewer _rulebook;
        private ReactionPanel _reaction;

        private int _renderedRevision = -1;
        private int _modalRevision = -1;
        private string _modalSignature = "";
        private bool _autoModalOpen; // last prompt/picker came from MaybeShowModal
        private bool _wasMyTurn;

        private PlayerView View => _session?.View;

        private void Start()
        {
            _db = CardDatabase.Load(Path.Combine(Application.streamingAssetsPath, "game-data"));
            _art = new CardArt(Application.streamingAssetsPath);
            BuildHud();
        }

        private void BuildHud()
        {
            var canvas = UiKit.CreateCanvas();
            var root = (RectTransform)canvas.transform;

            _preview = new CardPreview(root);
            _table = new TableView(root, _art, _preview);

            // The table's one line of words, in the clear strip between shelf and
            // board; built after the table so it sits over the wallpaper.
            _tableMessage = UiKit.CreateText(root, "", 22, TextAnchor.MiddleCenter, UiKit.ButtonColor);
            _tableMessage.raycastTarget = false;
            UiKit.AddTextShadow(_tableMessage);
            UiKit.Anchor((RectTransform)_tableMessage.transform,
                new Vector2(0.21f, 0.695f), new Vector2(0.79f, 0.75f));
            _table.BotLevelLookup = id => _session?.BotLevelOf(id);
            _table.TheatreBusy = () =>
                (_fx != null && _fx.IsBusy) || (_dice != null && _dice.IsBusy) ||
                (_turnBanner != null && _turnBanner.IsOpen) ||
                (_playTheatre != null && _playTheatre.IsBusy);
            _table.AimIsReaction = () => ReactionWindowDue(CurrentGroups());
            _table.OnHandCard = OpenHandMenu;
            _table.AttackTargetsFor = AttackTargets;
            _table.EquipTargetsFor = EquipAimTargets;
            _table.OnEquipPick = ConfirmEquipAttack;
            _table.OnAttackPick = ResolveAttackOnPlayer;
            _table.MarketTargetsFor = MarketTargets;
            _table.OnMarketPick = ResolveMarketTake;
            _table.CanBuyMarket = i => CurrentGroups()?.MarketMoves.ContainsKey(i) == true;
            _table.CanDrawLemon = () => DrawMove() != null;
            _table.OnDrawCard = () =>
            {
                var draw = DrawMove();
                if (draw != null)
                {
                    Submit(draw); // the card-draw sound covers it; no UI click
                }
            };
            _table.CanRefreshMarket = () => RefreshMove() != null;
            _table.OnRefreshMarket = () =>
            {
                var refresh = RefreshMove();
                if (refresh != null)
                {
                    Submit(refresh);
                }
            };
            _table.OnMarketDragStart = OnMarketDragStart;
            _table.OnMarketDragEnd = () =>
            {
                _preview.SetDragging(false);
                _table.ClearDropHighlights();
            };
            _table.OnMarketDrop = OnMarketDrop;
            _table.CanBuySupply = typeId => CurrentGroups()?.SupplyMoves.ContainsKey(typeId) == true;
            _table.OnSupplyDrop = OnSupplyDrop;
            _table.CanBuyBragging = () => CurrentGroups()?.BraggingMoves.Count > 0;
            _table.OnBraggingDrop = () =>
            {
                var moves = CurrentGroups()?.BraggingMoves;
                if (moves != null && moves.Count > 0)
                {
                    Submit(moves[0]);
                }
            };
            _table.OnBoardViewChanged = () => _renderedRevision = -1;
            // A refilled market card renders as an empty slot while the purchase drift
            // that exposed it is mid-air; delivery lifts it (see HandleMarketRefilled).
            _table.MarketCardHidden = instanceId => _hiddenMarketIds.Contains(instanceId);
            _table.SetVisible(false);

            _lobby = new LobbyUi(root, LoadConfiguredServerUrl(),
                PlayerPrefs.GetString("lw_name", "Player"));
            _lobby.OnStartSolo = StartLocalGame;
            _lobby.OnSaveSettings = name =>
            {
                PlayerPrefs.SetString("lw_name", name);
                PlayerPrefs.Save();
            };
            _lobby.OnHost = (url, name) => ConnectRemote(url, true, name, "", "");
            _lobby.OnJoin = (url, name, code) => ConnectRemote(url, false, name, code, "");
            _lobby.OnMyGames = (url, name) => ConnectRemote(url, false, name, "", "", listOnly: true);
            _lobby.OnGamesBack = () => BackToMenu("");
            _lobby.OnGamesRefresh = () => _remote?.ListGames();
            _lobby.OnAddBot = () => _remote?.AddBot();
            _lobby.OnRemoveBotSeat = seat => _remote?.RemoveBot(seat);
            _lobby.OnSetBotLevel = (seat, level) => _remote?.SetBotLevel(seat, level);
            _lobby.OnStart = () => _remote?.StartGame();
            _lobby.OnReadyToggle = ready => _remote?.SetReady(ready);
            _lobby.OnLeave = () => BackToMenu("");
            StartCoroutine(ServerStatusLoop());

            // Staged attacks sit under the effects layer, so reveal scrims cover them.
            _playTheatre = new PlayTheatre(root,
                playerId => _table.PlayerBarWorld(playerId),
                playerId => _table.PlayerBarEdgeWorld(playerId),
                playerId => _table.PurchaseShowcaseWorld(playerId),
                (playerId, on) => _table.SetVictimGlow(playerId, on),
                _preview,
                UiKit.LoadTextureSharp(Path.Combine(Application.streamingAssetsPath, "icons", "steal-blank.png")));
            _playTheatre.OnIdle = () =>
            {
                // Re-render so the reaction modal held behind the arrival beat opens now.
                _renderedRevision = -1;
                _modalRevision = -1;
            };

            // Above the table, below the modal overlays built after it. Queued reveals
            // wait for a staged attack's arrival beat, not for its reaction window.
            _fx = new EffectsPlayer(root,
                () => _dice.IsBusy || _turnBanner.IsOpen || _playTheatre.IsBusy,
                playerId => _table.PlayerBarWorld(playerId),
                playerId => _table.PlayerCashWorld(playerId),
                buyerId => _table.PurchaseShowcaseWorld(buyerId));
            _fx.OnFinished = () =>
            {
                // Re-render so modals held back behind the theatre open now.
                _renderedRevision = -1;
                _modalRevision = -1;
            };

            // Built last: overlays render on top.
            _prompt = new Prompt(root, this, _preview);
            _picker = new CardPicker(root, _preview, this);
            _dice = new DiceRoller(root);
            _dice.OnFinished = () =>
            {
                // Re-render so anything held back during the roll opens now.
                _renderedRevision = -1;
                _modalRevision = -1;
            };
            _turnBanner = new TurnBanner(root, this);
            _turnBanner.OnDismiss = () =>
            {
                // Re-render so any decision deferred behind the banner opens now.
                _renderedRevision = -1;
                _modalRevision = -1;
            };

            // Turn-alert toast: very last, so it floats over every screen.
            var alertPanel = UiKit.CreatePanel(root, "TurnAlert", new Color(0.10f, 0.12f, 0.16f, 0.96f));
            alertPanel.GetComponent<Image>().sprite = UiSprites.RoundedRect;
            alertPanel.GetComponent<Image>().type = Image.Type.Sliced;
            alertPanel.anchorMin = alertPanel.anchorMax = new Vector2(1f, 1f);
            alertPanel.pivot = new Vector2(1f, 1f);
            alertPanel.sizeDelta = new Vector2(420, 54);
            alertPanel.anchoredPosition = new Vector2(-16, -64);
            _alertText = UiKit.CreateText(alertPanel, "", 20,
                TextAnchor.MiddleCenter, UiKit.ButtonColor);
            _alertText.raycastTarget = false;
            UiKit.Anchor((RectTransform)_alertText.transform, Vector2.zero, Vector2.one,
                new Vector2(14, 2), new Vector2(-14, -2));
            _alertGroup = alertPanel.gameObject.AddComponent<CanvasGroup>();
            _alertGroup.alpha = 0f;
            _alertGroup.blocksRaycasts = false;
            UiKit.AddClick(alertPanel.gameObject, () => _alertUntil = 0f);

            // Turn actions live top-right, on the strip of bare table between the
            // shelf and the board: ending your turn is the button you reach for every
            // single turn. (Bot speed moved to the pause menu.)
            var turnActionsGo = new GameObject("TurnActions", typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            turnActionsGo.transform.SetParent(root, false);
            _turnActionRow = (RectTransform)turnActionsGo.transform;
            // Anchored to the strip's midline (shelf rule at 0.75, board top at 0.695)
            // so the row stays centered in it at any window height.
            UiKit.Anchor(_turnActionRow, new Vector2(1f, 0.7225f), new Vector2(1f, 0.7225f));
            _turnActionRow.pivot = new Vector2(1f, 0.5f);
            _turnActionRow.sizeDelta = new Vector2(430f, 40f);
            _turnActionRow.anchoredPosition = new Vector2(-16f, 0f);
            var turnActionsLayout = turnActionsGo.GetComponent<HorizontalLayoutGroup>();
            turnActionsLayout.spacing = 8;
            turnActionsLayout.childAlignment = TextAnchor.MiddleRight;
            turnActionsLayout.childForceExpandWidth = false;
            turnActionsLayout.childForceExpandHeight = true;
            turnActionsLayout.childControlWidth = true;
            turnActionsLayout.childControlHeight = true;

            // Response windows use the non-modal reaction panel: card in the market
            // band, buttons in the log column, the table left live for scouting.
            _reaction = new ReactionPanel(root, _preview);

            // Built last: the pause menu and rulebook draw above every screen.
            Sfx.ApplySavedVolume();
            _pause = new PauseMenu(root);
            _rulebook = new RulebookViewer(root, Application.streamingAssetsPath);
            _pause.OnRulebook = () => _rulebook.Open();
            _pause.OnQuit = () => BackToMenu("");
            _pause.OnCycleSpeed = CycleSpeed;
            _pause.SpeedLabel = CurrentSpeed;
            _pause.HasBots = () => _screen == Screen.Game &&
                (_session is LocalGameSession ||
                 (_remote != null && _remote.Room.Seats.Any(s => s.IsBot)));
            _lobby.OnRulebook = () => _rulebook.Open();
        }

        private void Update()
        {
            TickPendingConnection();

            // Mid-game connection loss: back to menu; the saved token allows Resume.
            if (_screen == Screen.Game && _remote != null && _remote.ConnectionError.Length > 0)
            {
                BackToMenu("Connection lost — use Resume to rejoin.");
                return;
            }

            _session?.Tick();
            TickLobby();
            TickTurnAlertFade();
            TickGamesListRefresh();

            bool escConsumed = TickRulebookKeys();

            if (_screen != Screen.Game)
            {
                return;
            }

            TickGameHotkeys(escConsumed);

            _table.TickSupplyDrag(Input.mousePosition);
            _table.TickEquipTargeting(Input.mousePosition);
            _table.TickAttackTargeting(Input.mousePosition);
            _table.TickPlayerPick(Input.mousePosition);
            _table.TickHandScroll(Input.mousePosition);
            _table.TickBoardScroll(Input.mousePosition);
            _table.TickDiscardScroll(Input.mousePosition);
            _picker.Tick(Input.mousePosition);
            // A roll that is still open for responses keeps its die on the table.
            _dice.Linger = View != null && View.PendingRollValue != null;
            _dice.Tick();
            // Render first: the turn banner opens in there, and the effects tick must
            // see it open in the SAME frame or a queued draw sound sneaks out early.
            RenderIfChanged();
            FlushSaleRecap();
            MaybeShowEndSummary();
            _fx.Tick();
            _playTheatre.Tick();
            TickPresentationWatchdog();
        }

        /// <summary>Turn-alert toast fade, on every screen.</summary>
        private void TickTurnAlertFade()
        {
            if (_alertGroup.alpha > 0f || Time.time < _alertUntil)
            {
                float target = Time.time < _alertUntil ? 1f : 0f;
                _alertGroup.alpha = Mathf.MoveTowards(_alertGroup.alpha, target, Time.deltaTime * 5f);
                if (_alertGroup.alpha <= 0f)
                {
                    _alertGroup.blocksRaycasts = false;
                }
            }
        }

        /// <summary>
        /// Rulebook keys work on every screen (main menu included). Returns whether Esc
        /// was consumed here — it must not fall through into the in-game handling this
        /// same frame.
        /// </summary>
        private bool TickRulebookKeys()
        {
            if (!_rulebook.IsOpen)
            {
                return false;
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                _rulebook.Step(-1);
            }
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                _rulebook.Step(1);
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _rulebook.Close();
                return true;
            }
            return false;
        }

        private void TickGameHotkeys(bool escConsumed)
        {
            // Letter hotkeys go quiet while the rulebook is up — its search box eats
            // keystrokes, and "n" must not restart the game mid-word.
            if (Input.GetKeyDown(KeyCode.N) && !_rulebook.IsOpen && _session is LocalGameSession)
            {
                StartLocalGame(_soloBots);
            }
            if (Input.GetKeyDown(KeyCode.B) && !_rulebook.IsOpen && _session != null)
            {
                _session.HumanAutoplay = !_session.HumanAutoplay;
                _prompt.Hide();
                _picker.Hide();
                _reaction.Hide();
                _turnBanner.Hide();
                _dice.Clear();
                _fx.Clear();
                _playTheatre.Clear();
                _renderedRevision = -1;
                _modalRevision = -1;
            }

            if (Input.GetKeyDown(KeyCode.Escape) && !escConsumed)
            {
                // Esc peels back one layer at a time: pause menu, then whatever is
                // cancellable (aiming, optional menus, discards) — and only with a
                // clear table does it open the pause menu.
                if (_pause.IsOpen)
                {
                    _pause.Close();
                }
                else if ((_prompt.IsOpen && _prompt.IsCancelable) || _table.HasActiveOverlay)
                {
                    if (_prompt.IsOpen && _prompt.IsCancelable)
                    {
                        _prompt.Hide();
                    }
                    _table.CancelOverlays();
                }
                else
                {
                    _pause.Open();
                }
            }
            if (Input.GetKeyDown(KeyCode.D) && !_rulebook.IsOpen && _session != null)
            {
                DumpDebugState();
            }
        }

        // -------------------------------------------------- shared plumbing

        private MoveGroups CurrentGroups()
        {
            if (_session == null || _session.HumanAutoplay || View == null)
            {
                return null;
            }
            return MoveGroups.From(View, _session.Moves);
        }

        private void Submit(GameAction action)
        {
            _prompt.Hide();
            _picker.Hide();
            _autoModalOpen = false;
            _session.Submit(action);
            _renderedRevision = -1;
        }

        private string NameOf(int playerId) =>
            View != null && playerId >= 0 && playerId < View.Players.Count
                ? View.Players[playerId].Name
                : $"P{playerId}";
    }
}
