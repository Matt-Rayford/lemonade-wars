using System.IO;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using TMPro;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The shell: menu -> (lobby) -> table. The table renders from PlayerView + typed
    /// legal moves through IGameSession, so offline play and networked play share every
    /// pixel of UI. Spawns itself on Play in any scene.
    ///
    /// Split by subsystem into LemonadeWarsApp.*.cs partials. This root owns the Unity
    /// lifecycle and the state shared between subsystems.
    ///
    /// Keys in game: [B] autopilot, [N] new local game (offline only), [D] debug dump.
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
            // The camera's clear color IS the table (every play zone is transparent). Set
            // unconditionally so the scene asset can't drift out of sync with UiKit.TableColor.
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
        private GameCursor _cursor;
        private Prompt _prompt;
        private CardPicker _picker;
        private CardPreview _preview;
        private TurnBanner _turnBanner;
        private DiceRoller _dice;
        private int _shownDiceHeadline;
        private EffectsPlayer _fx;
        private PlayTheatre _playTheatre;
        private RectTransform _turnActionRow;
        /// <summary>The message strip's vertical band on screen (0 bottom, 1 top): between the shelf and the board.</summary>
        private const float MessageStripBottom = 0.697f;
        private const float MessageStripTop = 0.77f;

        private TMP_Text _tableMessage;
        /// <summary>Same strip as the message: holds the End Turn button once actions are spent.</summary>
        private RectTransform _tableActionHost;
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
            _cursor = new GameCursor(() => _table.StickyDragActive);
        }

        private void Update()
        {
            _cursor.Tick();
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
            TickTableColor();

            bool escConsumed = TickRulebookKeys();

            if (_screen != Screen.Game)
            {
                return;
            }

            TickGameHotkeys(escConsumed);
            TickPointerDrivenTable();
            ReportTableSettledOnceIdle();
            ShowClockOnTurnBanner();
            TickDice();
            // What waited on ONWARD! is queued before the render, so the decisions
            // behind it (a Timeout's discards) open after its flight, not before.
            ReleaseAfterTurnBanner();
            // Render first: the turn banner opens in there, and the effects tick must
            // see it open in the SAME frame or a queued draw sound sneaks out early.
            RenderIfChanged();
            FlushRollEarnings();
            MaybeShowEndSummary();
            _fx.Tick();
            _playTheatre.Tick();
            SyncTimeoutCard();
            TickPresentationWatchdog();
        }

        private void TickPointerDrivenTable()
        {
            _table.TickStickyDrag(Input.mousePosition);
            _table.TickRummagePick(Input.mousePosition);
            _table.TickSupplyDrag(Input.mousePosition);
            _table.TickEquipTargeting(Input.mousePosition);
            _table.TickAttackTargeting(Input.mousePosition);
            _table.TickPlayerPick(Input.mousePosition);
            _table.TickHandScroll(Input.mousePosition);
            _table.TickBoardScroll(Input.mousePosition);
            _table.TickActionLog();
            _table.TickActionCountdowns();
            _table.TickDiscardScroll(Input.mousePosition);
            _picker.Tick(Input.mousePosition);
        }

        private void TickDice()
        {
            // A roll that is still open for responses keeps its die on the table.
            _dice.Linger = View != null && View.PendingRollValue != null;
            // A waiting Spoiled Rotten roll already holds earnings of its own: the die on the table
            // belongs to the roll before it, and stays only while that roll's payouts are playing.
            _dice.KeepForEarnings = _turnStartRollWaiting ? _fx.IsBusy : EarningsStillShowing();
            _dice.Tick();
            if (_dice.HeadlineRevision != _shownDiceHeadline)
            {
                _shownDiceHeadline = _dice.HeadlineRevision;
                _renderedRevision = -1; // the banner carries the die's words
            }
        }

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
        /// Rulebook keys work on every screen. Returns whether Esc was consumed here: it
        /// must not fall through into the in-game handling this same frame.
        /// </summary>
        private bool TickRulebookKeys()
        {
            if (_rulebook == null || !_rulebook.IsOpen)
            {
                return false; // null after an editor domain reload mid-play: nothing to drive
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
            // Letter hotkeys go quiet while the rulebook is up: its search box eats
            // keystrokes, and "n" must not restart the game mid-word.
            if (Input.GetKeyDown(KeyCode.N) && !_rulebook.IsOpen && _session is LocalGameSession)
            {
                StartLocalGame(_soloBots, _soloFirstDibs);
            }
            if (Input.GetKeyDown(KeyCode.B) && !_rulebook.IsOpen && _session != null)
            {
                ToggleHumanAutoplay();
            }
            if (Input.GetKeyDown(KeyCode.Escape) && !escConsumed)
            {
                PeelBackOneLayer();
            }
            if (Input.GetKeyDown(KeyCode.D) && !_rulebook.IsOpen && _session != null)
            {
                DumpDebugState();
            }
        }

        private void ToggleHumanAutoplay()
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

        /// <summary>Esc closes the pause menu, else whatever is cancellable (aiming, optional
        /// menus, discards); only with a clear table does it open the pause menu.</summary>
        private void PeelBackOneLayer()
        {
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
