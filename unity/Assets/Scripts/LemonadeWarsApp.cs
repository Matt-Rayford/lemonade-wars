using System.Collections.Generic;
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
    /// Keys in game: [B] autopilot, [N] new local game (offline only).
    /// </summary>
    public sealed class LemonadeWarsApp : MonoBehaviour
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

        // Lobby verb deferred until the socket opens.
        private bool _pendingSend;
        private bool _pendingCreate;
        private bool _pendingListOnly;
        private string _pendingName;
        private string _pendingCode;
        private string _pendingToken;

        // Cross-game turn-alert toast (top-right, any screen).
        private CanvasGroup _alertGroup;
        private TMP_Text _alertText;
        private float _alertUntil;
        private float _lastGamesRefresh;
        private float _pendingSince;
        private const float ConnectTimeoutSeconds = 10f;

        private LobbyUi _lobby;
        private TableView _table;
        private Prompt _prompt;
        private CardPicker _picker;
        private CardPreview _preview;
        private TurnBanner _turnBanner;
        private DiceRoller _dice;
        private EffectsPlayer _fx;
        private TMP_Text _statusText;
        private TMP_Text _topBanner;
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

        /// <summary>
        /// Default server URL from StreamingAssets/client-config.json (gitignored; written
        /// by tools/sync_unity.sh, baked into builds, editable post-build). The last URL a
        /// player actually connected to takes precedence.
        /// </summary>
        private static string LoadConfiguredServerUrl()
        {
            string remembered = PlayerPrefs.GetString("lw_server", "");
            if (!string.IsNullOrEmpty(remembered))
            {
                return remembered;
            }
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "client-config.json");
                if (File.Exists(path))
                {
                    var config = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                    string url = (string)config["serverUrl"];
                    if (!string.IsNullOrEmpty(url))
                    {
                        return url;
                    }
                }
            }
            catch (System.Exception)
            {
                // Malformed config: fall through to the dev default.
            }
            return "ws://localhost:5225/ws";
        }

        private void BuildHud()
        {
            var canvas = UiKit.CreateCanvas();
            var root = (RectTransform)canvas.transform;

            // Lemonade-yellow chrome across the top, black type on it.
            var statusPanel = UiKit.CreatePanel(root, "Status", UiKit.ButtonColor);
            UiKit.Anchor(statusPanel, new Vector2(0, 0.95f), new Vector2(1, 1));
            _statusText = UiKit.CreateText(statusPanel, "", 18, TextAnchor.MiddleLeft,
                UiKit.ButtonTextColor, body: true);
            UiKit.Anchor((RectTransform)_statusText.transform, Vector2.zero, Vector2.one,
                new Vector2(14, 0), new Vector2(-14, 0));
            // Turn/phase banner, centered in the top bar.
            _topBanner = UiKit.CreateText(statusPanel, "", 22, TextAnchor.MiddleCenter,
                UiKit.ButtonTextColor);
            UiKit.Anchor((RectTransform)_topBanner.transform, Vector2.zero, Vector2.one,
                new Vector2(220, 0), new Vector2(-220, 0));

            _preview = new CardPreview(root);
            _table = new TableView(root, _art, _preview, this);
            _table.BotLevelLookup = id => _session?.BotLevelOf(id);
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

            // Above the table, below the modal overlays built after it.
            _fx = new EffectsPlayer(root,
                () => _dice.IsBusy || _turnBanner.IsOpen,
                playerId => _table.PlayerBarWorld(playerId));
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

            // Turn actions live top-right, in the bar: ending your turn is the button
            // you reach for every single turn. (Bot speed moved to the pause menu.)
            var turnActionsGo = new GameObject("TurnActions", typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            turnActionsGo.transform.SetParent(root, false);
            _turnActionRow = (RectTransform)turnActionsGo.transform;
            // Anchored to the status bar's midline (it spans 0.95-1.0) so the row stays
            // centered in the bar at any window height.
            UiKit.Anchor(_turnActionRow, new Vector2(1f, 0.975f), new Vector2(1f, 0.975f));
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
            PauseMenu.ApplySavedVolume();
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

        private PauseMenu _pause;
        private RulebookViewer _rulebook;
        private ReactionPanel _reaction;
        /// <summary>The open reaction's headline, shown in the yellow bar.</summary>
        private string _reactionBanner = "";

        /// <summary>
        /// True while a stack moment needs the viewer AND the table should stay live:
        /// a response window, or a retarget of the viewer's own attack (both are
        /// decided by reading the other players' boards, which a blur modal hides).
        /// </summary>
        private bool ReactionWindowDue(MoveGroups groups)
        {
            if (groups == null || !groups.IsModal || groups.ModalMoves.Count == 0)
            {
                return false;
            }
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null)
            {
                // A stack play to react to, or a settled die you may still tweak
                // (Out of Stock, Downsell, Sugared Up, Take Two).
                return (View.StackTop != null || View.PendingRollValue != null) &&
                       View.AwaitingResponse.Contains(View.ViewerId);
            }
            // Bouncer resolves AFTER its attack leaves the stack, so it has no stack
            // top — but picking who to hit back needs the table just as much.
            return decision.Kind == DecisionKind.AttackRetarget ||
                   decision.Kind == DecisionKind.BouncerAttack;
        }

        // ------------------------------------------------------- game speed

        private const string SpeedPref = "lw_game_speed";
        private static readonly string[] SpeedOrder = { "slow", "medium", "fast" };
        private RectTransform _turnActionRow;

        private static float LocalStepSeconds(string speed) =>
            speed == "slow" ? 2.0f : speed == "fast" ? 0.45f : 1.0f;

        /// <summary>Local games follow the saved preference; online rooms follow the server.</summary>
        private string CurrentSpeed() =>
            _remote != null && _screen == Screen.Game
                ? (string.IsNullOrEmpty(_remote.Room.Speed) ? "medium" : _remote.Room.Speed)
                : PlayerPrefs.GetString(SpeedPref, "medium");

        private void CycleSpeed()
        {
            string next = SpeedOrder[
                (System.Array.IndexOf(SpeedOrder, CurrentSpeed()) + 1) % SpeedOrder.Length];
            PlayerPrefs.SetString(SpeedPref, next);
            PlayerPrefs.Save();
            Sfx.Play(Sfx.BotSpeed(next));
            if (_session is LocalGameSession local)
            {
                local.BotStepSeconds = LocalStepSeconds(next);
            }
            else
            {
                _remote?.SetSpeed(next); // the room broadcast confirms + syncs everyone
            }
        }

        private void OnTurnAlert(string code)
        {
            _alertText.text = $"YOUR TURN — GAME {code}";
            _alertUntil = Time.time + 5f;
            _alertGroup.blocksRaycasts = true;
            if (_pendingListOnly && _remote != null && _remote.Room.YourSeat < 0)
            {
                _remote.ListGames(); // browsing My Games: badge updates immediately
            }
        }

        // ------------------------------------------------------------ flows

        private IReadOnlyList<(string Name, string Level)> _soloBots =
            new[] { ("Benny", "medium"), ("Cleo", "medium"), ("Dex", "medium") };

        private void StartLocalGame(IReadOnlyList<(string Name, string Level)> bots)
        {
            _soloBots = bots.ToList();
            var names = new List<string> { _lobby.DisplayName };
            names.AddRange(_soloBots.Select(b => b.Name));

            _remote?.Dispose();
            _remote = null;
            _session?.Dispose();
            var localSession = new LocalGameSession(_db, names.ToArray(), 0,
                (ulong)System.DateTime.Now.Ticks,
                _soloBots.Select(b => b.Level).ToList());
            localSession.BotStepSeconds = LocalStepSeconds(PlayerPrefs.GetString(SpeedPref, "medium"));
            _session = localSession;
            _session.EventEmitted += OnGameEvent;
            EnterGame();
        }

        /// <summary>Durable identity secret; generated once and never shown anywhere.</summary>
        private static string PlayerKey
        {
            get
            {
                string key = PlayerPrefs.GetString("lw_player_key", "");
                if (string.IsNullOrEmpty(key))
                {
                    key = System.Guid.NewGuid().ToString("N") + System.Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString("lw_player_key", key);
                    PlayerPrefs.Save();
                }
                return key;
            }
        }

        private void ConnectRemote(string url, bool create, string name, string code,
            string token, bool listOnly = false)
        {
            _session?.Dispose();
            _remote = RemoteGameSession.Connect(url);
            _session = _remote;
            _session.EventEmitted += OnGameEvent;
            _remote.TurnAlert += OnTurnAlert;
            _pendingSend = true;
            _pendingSince = Time.time;
            _pendingCreate = create;
            _pendingListOnly = listOnly;
            _pendingName = name;
            _pendingCode = code;
            _pendingToken = token;
            PlayerPrefs.SetString("lw_server", url);
            PlayerPrefs.SetString("lw_name", name);
            _screen = Screen.Lobby;
            _lobbyRevision = -1;
            _lobby.ShowLobby(_remote.Room, "Connecting to " + url + "...", false);
        }

        /// <summary>A My Games row was picked: enter that room on the same connection.</summary>
        private void JoinFromList(string code)
        {
            if (_remote == null)
            {
                return;
            }
            // The stored per-room token still helps pre-identity games; identity
            // covers everything created from now on.
            string token = PlayerPrefs.GetString("lw_code", "") == code
                ? PlayerPrefs.GetString("lw_token", "")
                : "";
            _remote.JoinRoom(code, _lobby.DisplayName,
                string.IsNullOrEmpty(token) ? null : token);
            _lobbyRevision = -1;
            _lobby.ShowLobby(_remote.Room, $"Joining {code}...", false);
        }

        /// <summary>Remember enough to resume this room after a crash or redeploy.</summary>
        private void SaveResume()
        {
            if (_remote == null || string.IsNullOrEmpty(_remote.Room.Token))
            {
                return;
            }
            PlayerPrefs.SetString("lw_code", _remote.Room.Code);
            PlayerPrefs.SetString("lw_token", _remote.Room.Token);
            PlayerPrefs.Save();
        }

        private int _lobbyRevision = -1;

        /// <summary>Lobby refresh + started transition, driven off the session revision.</summary>
        private void TickLobby()
        {
            if (_remote == null || _screen != Screen.Lobby)
            {
                return;
            }
            if (_remote.Room.Started)
            {
                EnterGame();
                return;
            }
            if (_remote.Revision == _lobbyRevision)
            {
                return;
            }
            _lobbyRevision = _remote.Revision;
            if (_pendingListOnly && _remote.Room.YourSeat < 0)
            {
                // Browsing My Games: keep rendering the list until a room is picked.
                _lobby.ShowGames(_remote.GamesList, JoinFromList);
                return;
            }
            string status = _remote.ConnectionError.Length > 0
                ? _remote.ConnectionError
                : _remote.Log.LastOrDefault(l => l.StartsWith("!")) ?? "";
            _lobby.ShowLobby(_remote.Room, status, _remote.MyReady);
            SaveResume();
        }

        private void EnterGame()
        {
            _screen = Screen.Game;
            SaveResume();
            _lobby.HideAll();
            _table.SetVisible(true);
            _prompt.Hide();
            _picker.Hide();
            _reaction.Hide();
            _turnBanner.Hide();
            _dice.Clear();
            _fx.Clear();
            _table.ViewedBoardPlayer = -1;
            _table.SuppressWhinyBaby = false; // a dropped mid-flight claim must not hide it forever
            _table.SuppressSpoiledRotten = false;
            _renderedRevision = -1;
            _modalRevision = -1;
            _wasMyTurn = false;
            _actionLog.Clear();
            _saleEarnings = null;
            _endShown = false;
        }

        /// <summary>Typed engine events drive presentation moments (dice, later: effects).</summary>
        private void OnGameEvent(GameEvent gameEvent)
        {
            if (_session == null || _session.HumanAutoplay)
            {
                return; // autopilot is for fast testing — no theatre
            }
            string logLine = LogLine(gameEvent);
            if (logLine != null)
            {
                _actionLog.Add(logLine);
                if (_actionLog.Count > 40)
                {
                    _actionLog.RemoveAt(0);
                }
            }
            if (gameEvent is SaleRolled roll)
            {
                // Played, not queued: it belongs to the tumble, which passive effects
                // deliberately wait behind.
                Sfx.Play(Sfx.SaleRoll);
                _dice.Enqueue(NameOf(roll.PlayerId), roll.Value, roll.PlayerId == _session.Seat,
                    roll.Purpose);
                // Start collecting this roll's earnings for the "you earned" recap.
                _saleEarnings = new List<string>();
                _saleTotal = 0;
            }
            else if (gameEvent is StandSold sold)
            {
                _fx.QueueMoney(sold.PlayerId, sold.Earnings);
                if (sold.PlayerId == _session.Seat && _saleEarnings != null)
                {
                    _saleEarnings.Add($"{StandName(sold.StandInstanceId)} +${sold.Earnings}");
                    _saleTotal += sold.Earnings;
                }
            }
            else if (gameEvent is DieRerolled reroll)
            {
                Sfx.Play(Sfx.SaleRoll); // a second real tumble deserves the same sound
                _dice.EnqueueReroll(NameOf(reroll.ByPlayerId), reroll.NewValue,
                    reroll.ByPlayerId == _session.Seat);
            }
            else if (gameEvent is RollModified modified)
            {
                _dice.EnqueueModifier(NameOf(modified.PlayerId), BlackMarketName(modified.SourceDefId),
                    modified.NewValue, modified.PlayerId == _session.Seat);
            }
            else if (gameEvent is MoneyChanged money)
            {
                _fx.QueueMoney(money.PlayerId, money.Amount);
                if (money.PlayerId == _session.Seat && money.Amount > 0 && _saleEarnings != null)
                {
                    _saleEarnings.Add($"{money.Reason} +${money.Amount}");
                    _saleTotal += money.Amount;
                }
            }
            else if (gameEvent is CardDrawn drawn && drawn.PlayerId == _session.Seat)
            {
                // Queued, not played: the turn-start draw waits behind ONWARD!.
                _fx.QueueSound(Sfx.CardDraw);
            }
            else if (gameEvent is TimeoutDrawn timeout && timeout.PlayerId == _session.Seat)
            {
                _fx.QueueSound(Sfx.CardDraw);
            }
            else if (gameEvent is TitleClaimed title && title.PlayerId == _session.Seat)
            {
                Sfx.Play(Sfx.TitleClaim);
            }
            else if (gameEvent is BraggingRightsPurchased brag && brag.PlayerId == _session.Seat)
            {
                Sfx.Play(Sfx.TitleClaim); // a title claim first, a purchase second
            }
            else if (gameEvent is WhiniestBabyMoved baby && baby.ToPlayerId == _session.Seat)
            {
                // Queued: the card often lands at turn start, so the wail waits for
                // ONWARD! and plays when you can actually see why. The claim reveal
                // holds the card centered (readable), then flies it into the lord fan
                // — the fan keeps the card HIDDEN until the flight delivers it (the
                // table renders the new state immediately, well before the theatre).
                _table.SuppressWhinyBaby = true;
                _fx.QueueSound(Sfx.WhinyBaby);
                _fx.QueueClaim(_art.WhiniestBaby(), "You are the Whiniest Baby!",
                    () => _table.WhinyBabyLandingWorld(),
                    () =>
                    {
                        _table.SuppressWhinyBaby = false;
                        _renderedRevision = -1; // surface the landed card now
                    });
            }
            else if (gameEvent is SpoiledRottenMoved spoiled && spoiled.ToPlayerId == _session.Seat)
            {
                // Same claim theatre as the baby — reveal, then fly into the lord fan.
                _table.SuppressSpoiledRotten = true;
                _fx.QueueSound(Sfx.TitleClaim);
                _fx.QueueClaim(_art.SpoiledRotten(), "You are Spoiled Rotten!",
                    () => _table.SpoiledRottenLandingWorld(),
                    () =>
                    {
                        _table.SuppressSpoiledRotten = false;
                        _renderedRevision = -1;
                    });
            }
            else if (gameEvent is BlackMarketPurchased bought && bought.PlayerId == _session.Seat)
            {
                // Queued, so the register rings alongside the "-$X" floater.
                _fx.QueueSound(Sfx.CashRegister);
            }
            else if (gameEvent is StandPurchased standBought && standBought.PlayerId == _session.Seat)
            {
                _fx.QueueSound(Sfx.CashRegister);
            }
            else if (gameEvent is MoneyStolen theft)
            {
                _fx.QueueMoneySteal(theft.FromPlayerId, theft.ToPlayerId, theft.Amount);
            }
            else if (gameEvent is CardsStolen cardTheft)
            {
                _fx.QueueCardFly(_art.Back("lemon"),
                    cardTheft.FromPlayerId, cardTheft.ToPlayerId, cardTheft.Count);
            }
            else if (gameEvent is HandsTraded trade)
            {
                _fx.QueueCardFly(_art.Back("lemon"), trade.PlayerA, trade.PlayerB, 1);
                _fx.QueueCardFly(_art.Back("lemon"), trade.PlayerB, trade.PlayerA, 1);
            }
            else if (gameEvent is LemonCardPlayed played &&
                     played.TargetPlayerId is int victim)
            {
                // The clash is audible for both duelists, silent for bystanders —
                // queued, so it waits out dice theatre instead of stinging mid-roll.
                if (played.PlayerId == _session.Seat || victim == _session.Seat)
                {
                    _fx.QueueSound(Sfx.AttackCard);
                }
                if (played.PlayerId != _session.Seat)
                {
                    // Someone ELSE aimed an attack: one card-reveal beat naming both
                    // sides. If a response modal is about to open for us, the window
                    // event below cancels this — the modal tells the same story.
                    string victimName = victim == _session.Seat
                        ? "YOU"
                        : NameOf(victim).ToUpperInvariant();
                    _fx.QueueReveal(_art.Lemon(played.DefId),
                        $"{NameOf(played.PlayerId).ToUpperInvariant()} ATTACKS {victimName} WITH " +
                        $"{LemonName(played.DefId).ToUpperInvariant()}!");
                }
            }
            else if (gameEvent is ResponseWindowOpened window &&
                     window.AwaitingPlayers.Contains(_session.Seat))
            {
                _fx.CancelPendingReveals();
            }
            else if (gameEvent is AttackRedirected redirect)
            {
                _fx.QueueToast($"{NameOf(redirect.ByPlayerId).ToUpperInvariant()} TAGS IT TO " +
                    $"{NameOf(redirect.NewTargetId).ToUpperInvariant()}!");
            }
            else if (gameEvent is AttackReflected reflect)
            {
                _fx.QueueToast($"{NameOf(reflect.ByPlayerId).ToUpperInvariant()} " +
                    "REFLECTS IT BACK!");
            }
            else if (gameEvent is AttackFizzled fizzle)
            {
                _fx.QueueToast($"{LemonName(fizzle.DefId).ToUpperInvariant()} FIZZLES!");
            }
            else if (gameEvent is PlayCancelled cancelled)
            {
                // The Inflatable Decoy is an equipped reaction with no play event of
                // its own — without a reveal it cancels invisibly and reads as a bug.
                if (cancelled.WithDefId == "inflatable-decoy" && cancelled.ByPlayerId is int blocker)
                {
                    _fx.QueueReveal(_art.BlackMarket("inflatable-decoy", Shape.Square),
                        $"{NameOf(blocker)} blocks with Inflatable Decoy!");
                }
                string byWhat = cancelled.ByPlayerId is int by && cancelled.WithDefId != null
                    ? $" BY {NameOf(by).ToUpperInvariant()}'S {CardName(cancelled.WithDefId).ToUpperInvariant()}"
                    : "";
                _fx.QueueToast($"{NameOf(cancelled.OwnerId).ToUpperInvariant()}'S " +
                    $"{CardName(cancelled.DefId).ToUpperInvariant()} IS CANCELLED{byWhat}!");
            }
        }

        private string LemonName(string defId)
        {
            try
            {
                return _db.Lemon(defId).Name;
            }
            catch
            {
                return TitleCased(defId);
            }
        }

        private string BlackMarketName(string defId)
        {
            try
            {
                return _db.BlackMarket(defId).Name;
            }
            catch
            {
                return TitleCased(defId);
            }
        }

        /// <summary>
        /// A card name when the catalog isn't known up front: PlayCancelled carries a
        /// Black Market id when a PURCHASE is tantrummed and a Lemon id otherwise.
        /// </summary>
        private string CardName(string defId)
        {
            try
            {
                return _db.Lemon(defId).Name;
            }
            catch
            {
                return BlackMarketName(defId);
            }
        }

        /// <summary>Last-resort display for an id with no catalog entry: "early-worm" -> "Early Worm".</summary>
        private static string TitleCased(string defId)
        {
            var words = defId.Replace("-", " ").Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
                }
            }
            return string.Join(" ", words);
        }

        private void Update()
        {
            // Deferred lobby verb once the socket is open.
            if (_remote != null && _pendingSend)
            {
                if (_remote.Connected)
                {
                    _pendingSend = false;
                    // Identity first on every connection: seats bind to it, and the
                    // welcome reply carries the My Games list.
                    _remote.Hello(PlayerKey, _pendingName);
                    if (_pendingCreate)
                    {
                        _remote.CreateRoom(_pendingName);
                    }
                    else if (!_pendingListOnly)
                    {
                        _remote.JoinRoom(_pendingCode, _pendingName,
                            string.IsNullOrEmpty(_pendingToken) ? null : _pendingToken);
                    }
                }
                else if (_remote.ConnectionError.Length > 0)
                {
                    _pendingSend = false;
                    BackToMenu(_remote.ConnectionError);
                }
                else if (Time.time - _pendingSince > ConnectTimeoutSeconds)
                {
                    // A dead host can hang ConnectAsync with no error for minutes —
                    // give up cleanly instead of trapping the player on CONNECTING.
                    _pendingSend = false;
                    BackToMenu("Could not reach the server — check the address and try again.");
                }
            }

            // Mid-game connection loss: back to menu; the saved token allows Resume.
            if (_screen == Screen.Game && _remote != null && _remote.ConnectionError.Length > 0)
            {
                BackToMenu("Connection lost — use Resume to rejoin.");
                return;
            }

            _session?.Tick();
            TickLobby();

            // Turn-alert toast fade, on every screen.
            if (_alertGroup.alpha > 0f || Time.time < _alertUntil)
            {
                float target = Time.time < _alertUntil ? 1f : 0f;
                _alertGroup.alpha = Mathf.MoveTowards(_alertGroup.alpha, target, Time.deltaTime * 5f);
                if (_alertGroup.alpha <= 0f)
                {
                    _alertGroup.blocksRaycasts = false;
                }
            }

            // Browsing My Games: keep the YOUR TURN badges fresh.
            if (_pendingListOnly && _screen == Screen.Lobby && _remote != null &&
                _remote.Room.YourSeat < 0 && _remote.Connected &&
                Time.time - _lastGamesRefresh > 20f)
            {
                _lastGamesRefresh = Time.time;
                _remote.ListGames();
            }

            // Rulebook keys work on every screen (main menu included); a consumed Esc
            // must not fall through into the in-game handling this same frame.
            bool escConsumed = false;
            if (_rulebook.IsOpen)
            {
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
                    escConsumed = true;
                }
            }

            if (_screen != Screen.Game)
            {
                return;
            }

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

            _table.TickSupplyDrag(Input.mousePosition);
            _table.TickEquipTargeting(Input.mousePosition);
            _table.TickAttackTargeting(Input.mousePosition);
            _table.TickPlayerPick(Input.mousePosition);
            _table.TickHandScroll(Input.mousePosition);
            _table.TickBoardScroll(Input.mousePosition);
            _table.TickDiscardScroll(Input.mousePosition);
            // A roll that is still open for responses keeps its die on the table.
            _dice.Linger = View != null && View.PendingRollValue != null;
            _dice.Tick();
            // Render first: the turn banner opens in there, and the effects tick must
            // see it open in the SAME frame or a queued draw sound sneaks out early.
            RenderIfChanged();
            // Roll fully resolved: recap what the viewer earned. The entries collect
            // across the Applies of a roll (windows can hold it open several frames),
            // so flush only once the pending roll is gone.
            if (_saleEarnings != null && View != null && View.PendingRollValue == null)
            {
                if (_saleTotal > 0)
                {
                    // Coins first so they ring under the recap, not after it.
                    _fx.QueueSound(Sfx.Coins);
                    _fx.QueueToast($"YOU EARN ${_saleTotal}:  " +
                        string.Join("  ·  ", _saleEarnings).ToUpperInvariant());
                }
                _saleEarnings = null;
            }
            // The final scoreboard waits for the last effects to land, then shows once.
            if (View != null && View.Stage == GameStage.Finished && !_endShown &&
                !_fx.IsBusy && !_dice.IsBusy && !_turnBanner.IsOpen && !_picker.IsOpen)
            {
                _endShown = true;
                ShowEndSummary();
            }
            _fx.Tick();
            TickPresentationWatchdog();
        }

        private List<string> _saleEarnings;
        private int _saleTotal;
        private bool _endShown;
        private readonly List<string> _actionLog = new List<string>();

        /// <summary>
        /// Game over: announce the winner and reveal everyone's full score — in-game VP
        /// plus the secret Lemon Lords, met or missed.
        /// </summary>
        private void ShowEndSummary()
        {
            _prompt.Hide();
            var rows = View.Players
                .Select(p =>
                {
                    int lordVp = p.FinishedLords.Count(l => l.Met);
                    int total = p.InGameVictoryPoints + lordVp;
                    string lords = p.FinishedLords.Count > 0
                        ? "   [" + string.Join(", ", p.FinishedLords.Select(l =>
                            _db.Title(l.TitleId).Name + (l.Met ? " +1" : " —"))) + "]"
                        : "";
                    return (total, label: $"{p.Name} — {total} VP{lords}");
                })
                .OrderByDescending(r => r.total)
                .ToList();
            string winners = string.Join(" & ", View.Winners.Select(NameOf));
            string verb = View.Winners.Count > 1 ? "WIN" : "WINS";
            _prompt.Show($"{winners.ToUpperInvariant()} {verb} THE LEMONADE WARS!",
                new List<Texture2D>(),
                rows.Select(r => new Prompt.Option(r.label, null)).ToList(),
                showCancel: true);
        }

        private string StandName(int standInstanceId)
        {
            foreach (var panel in View.Players)
            {
                foreach (var stand in panel.Stands)
                {
                    if (stand.InstanceId == standInstanceId)
                    {
                        return _db.StandType(stand.StandTypeId).Name;
                    }
                }
            }
            return "Stand";
        }

        /// <summary>
        /// What a die is being thrown for, in plain words — null for the ordinary end-of-turn
        /// sale, which needs no label. Callers append it only when there is something to say.
        /// </summary>
        private static string RollPurposeName(RollPurpose? purpose)
        {
            switch (purpose)
            {
                case RollPurpose.NightShifts:
                    return "Night Shifts";
                case RollPurpose.SpoiledRotten:
                    return "Spoiled Rotten";
                case RollPurpose.ExtraSale:
                    return "Extra sale";
                case RollPurpose.TradeWinds:
                    return "Trade Winds";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Friendly one-liner for the action log; null for events too noisy to log
        /// (draws, individual money ticks, window bookkeeping).
        /// </summary>
        private string LogLine(GameEvent e)
        {
            switch (e)
            {
                case TurnStarted turn: return $"— {NameOf(turn.PlayerId)}'s turn —";
                case LemonCardPlayed played:
                    return $"{NameOf(played.PlayerId)} plays {LemonName(played.DefId)}" +
                           (played.TargetPlayerId is int t ? $" on {NameOf(t)}" : "");
                case PlayCancelled cancelled:
                    return $"{NameOf(cancelled.OwnerId)}'s {CardName(cancelled.DefId)} is cancelled" +
                        (cancelled.ByPlayerId is int by && cancelled.WithDefId != null
                            ? $" by {NameOf(by)}'s {CardName(cancelled.WithDefId)}"
                            : "");
                case AttackRedirected redirect:
                    return $"{NameOf(redirect.ByPlayerId)} tags the attack to {NameOf(redirect.NewTargetId)}";
                case AttackReflected reflect:
                    return $"{NameOf(reflect.ByPlayerId)} reflects the attack onto {NameOf(reflect.NewTargetId)}";
                case AttackFizzled fizzle:
                    return $"{NameOf(fizzle.OwnerId)}'s {LemonName(fizzle.DefId)} fizzles";
                case StandPurchased standBuy:
                    return $"{NameOf(standBuy.PlayerId)} buys a {_db.StandType(standBuy.StandTypeId).Name}";
                case BlackMarketPurchased bmBuy:
                    return $"{NameOf(bmBuy.PlayerId)} buys {BlackMarketName(bmBuy.DefId)}";
                case BraggingRightsPurchased brag:
                    return $"{NameOf(brag.PlayerId)} buys Bragging Rights (${brag.Price})";
                case TitleClaimed title:
                    return $"{NameOf(title.PlayerId)} claims {_db.Title(title.TitleId).Name}!";
                case SaleRolled rolled:
                    return $"{NameOf(rolled.PlayerId)} rolls a {rolled.Value}" +
                           (RollPurposeName(rolled.Purpose) is string why ? $" ({why})" : "");
                case DieRerolled reroll:
                    return $"{NameOf(reroll.ByPlayerId)} plays Out of Stock — reroll: {reroll.NewValue}";
                case RollModified modified:
                    return $"{NameOf(modified.PlayerId)}'s {BlackMarketName(modified.SourceDefId)} " +
                           $"changes the roll to {modified.NewValue}";
                case MoneyStolen theft:
                    return $"{NameOf(theft.ToPlayerId)} steals ${theft.Amount} from {NameOf(theft.FromPlayerId)}";
                case CardsStolen cards:
                    return $"{NameOf(cards.ToPlayerId)} steals {cards.Count} card(s) from {NameOf(cards.FromPlayerId)}";
                case HandsTraded traded:
                    return $"{NameOf(traded.PlayerA)} and {NameOf(traded.PlayerB)} trade hands";
                case TimeoutDrawn timeout:
                    return $"{NameOf(timeout.PlayerId)} draws a Timeout!";
                case WhiniestBabyMoved baby when baby.ToPlayerId is int b:
                    return $"{NameOf(b)} is now the Whiniest Baby";
                case SpoiledRottenMoved rotten when rotten.ToPlayerId is int r:
                    return $"{NameOf(r)} is now Spoiled Rotten";
                case TrapPlaced trap:
                    return $"{NameOf(trap.OwnerId)} sets a trap on {NameOf(trap.OnPlayerId)}'s turf";
                case GameEnded ended:
                    return $"Game over — {string.Join(", ", ended.Winners.Select(NameOf))} wins!";
                default:
                    return null;
            }
        }

        private float _stuckSince = -1f;
        private float _fxBusySince = -1f;
        private int _fxBlocksSeen = -1;

        /// <summary>
        /// Self-healing for the soft-lock family: when the player is owed a modal but
        /// nothing is on screen (or an overlay is open-but-invisible, or the effects
        /// queue has been "busy" implausibly long), log exactly what was seen and
        /// force the presentation machinery to recover. The game state is always
        /// fine underneath — these locks are pure presentation, so healing is safe.
        /// </summary>
        private void TickPresentationWatchdog()
        {
            // Effects stuck: blocking theatre should advance every ~2s on its own.
            // Measure PROGRESS, not total time — a big exchange (attack, tag,
            // retarget, steals) legitimately chains theatre well past 8s, and the old
            // total-time check cleared it mid-show. Only a single effect that sits
            // for 8s without a new one starting is actually wedged.
            if (_fx.IsBusy)
            {
                if (_fxBusySince < 0f || _fx.BlocksStarted != _fxBlocksSeen)
                {
                    _fxBusySince = Time.time;
                    _fxBlocksSeen = _fx.BlocksStarted;
                }
                else if (Time.time - _fxBusySince > 8f)
                {
                    Debug.LogWarning("[LW] watchdog: one effect busy >8s — clearing. " + _fx.DebugState());
                    _fx.Clear();
                    _fxBusySince = -1f;
                }
            }
            else
            {
                _fxBusySince = -1f;
            }

            var groups = CurrentGroups();
            bool blocked = _turnBanner.IsOpen || _dice.IsBusy || _fx.IsBusy;
            bool modalDue = groups != null && groups.IsModal && groups.ModalMoves.Count > 0;
            bool modalVisible = (_prompt.IsOpen && _prompt.RootVisible) ||
                                (_picker.IsOpen && _picker.RootVisible) ||
                                _table.PlayerPickActive ||
                                _reaction.IsOpen;
            bool bannerGhost = _turnBanner.IsOpen && !_turnBanner.RootVisible;
            bool overlayGhost = (_prompt.IsOpen && !_prompt.RootVisible) ||
                                (_picker.IsOpen && !_picker.RootVisible);
            bool stuck = (modalDue && !modalVisible && !blocked) || bannerGhost || overlayGhost;
            if (!stuck)
            {
                _stuckSince = -1f;
                return;
            }
            if (_stuckSince < 0f)
            {
                _stuckSince = Time.time;
                return;
            }
            if (Time.time - _stuckSince < 2f)
            {
                return; // a fresh modal legitimately takes a frame or two
            }
            _stuckSince = -1f;
            Debug.LogWarning("[LW] watchdog: presentation stuck — healing. " +
                $"modalDue={modalDue} modalVisible={modalVisible} bannerGhost={bannerGhost} " +
                $"overlayGhost={overlayGhost} dice={_dice.IsBusy} fx=({_fx.DebugState()})");
            if (bannerGhost)
            {
                _turnBanner.Hide();
            }
            if (overlayGhost)
            {
                _prompt.Hide();
                _picker.Hide();
                _autoModalOpen = false;
            }
            _modalSignature = "";
            _modalRevision = -1;
            _renderedRevision = -1; // full re-render; MaybeShowModal runs fresh
        }

        /// <summary>[D]: dump every presentation gate to the console — the frozen-state
        /// forensics kit. Each line names a layer that can silently block the table.</summary>
        private void DumpDebugState()
        {
            var groups = CurrentGroups();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"revision={_session.Revision} rendered={_renderedRevision} " +
                $"modalRev={_modalRevision} wasMyTurn={_wasMyTurn} autoplay={_session.HumanAutoplay}");
            sb.AppendLine($"banner: open={_turnBanner.IsOpen} visible={_turnBanner.RootVisible}");
            sb.AppendLine($"prompt: open={_prompt.IsOpen} visible={_prompt.RootVisible}   " +
                $"picker: open={_picker.IsOpen} visible={_picker.RootVisible}");
            sb.AppendLine($"dice: busy={_dice.IsBusy}   fx: {_fx.DebugState()}");
            sb.AppendLine($"groups: isModal={groups?.IsModal} modal={groups?.ModalMoves.Count} " +
                $"bar={groups?.BarMoves.Count} hand={groups?.HandMoves.Count} " +
                $"supply={groups?.SupplyMoves.Count} market={groups?.MarketMoves.Count}");
            sb.AppendLine($"moves({_session.Moves.Count}): " + string.Join(" | ",
                _session.Moves.Take(8).Select(m => _session.LabelFor(m))));
            if (View != null)
            {
                sb.AppendLine($"view: stage={View.Stage} phase={View.Phase} " +
                    $"active={View.ActivePlayer} decisions=[{string.Join(",", View.MyDecisions.Select(d => d.Kind))}] " +
                    $"awaiting=[{string.Join(",", View.AwaitingResponse)}]");
            }
            Debug.Log("[LW DUMP]\n" + sb);
            _statusText.text = "debug state dumped to console";
        }

        private void BackToMenu(string status)
        {
            _session?.Dispose();
            _session = null;
            _remote = null;
            _screen = Screen.Menu;
            UiKit.Clear(_turnActionRow);
            _table.SetVisible(false);
            _prompt.Hide();
            _picker.Hide();
            _turnBanner.Hide();
            _dice.Clear();
            _fx.Clear();
            _lobby.ShowMenu(status);
        }

        /// <summary>Ping the server's /health while the menu is visible: the status dot.</summary>
        private System.Collections.IEnumerator ServerStatusLoop()
        {
            while (true)
            {
                if (_lobby.MenuVisible)
                {
                    string healthUrl = ToHealthUrl(_lobby.ServerUrl);
                    if (healthUrl != null)
                    {
                        using (var request = UnityEngine.Networking.UnityWebRequest.Get(healthUrl))
                        {
                            request.timeout = 3;
                            yield return request.SendWebRequest();
                            bool ok = request.result ==
                                UnityEngine.Networking.UnityWebRequest.Result.Success;
                            _lobby.SetServerStatus(ok, ok ? "" : "server unreachable");
                        }
                    }
                    else
                    {
                        _lobby.SetServerStatus(false, "invalid server url");
                    }
                }
                yield return new WaitForSeconds(3f);
            }
        }

        /// <summary>ws://host:port/ws -> http://host:port/health (and wss -> https).</summary>
        private static string ToHealthUrl(string wsUrl)
        {
            if (!System.Uri.TryCreate(wsUrl, System.UriKind.Absolute, out var uri))
            {
                return null;
            }
            string scheme = uri.Scheme == "wss" ? "https" : "http";
            return $"{scheme}://{uri.Authority}/health";
        }

        // ------------------------------------------------------------ input

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

        private void OpenHandMenu(int cardInstanceId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return;
            }
            // Reaction window: clicking the card in hand IS the response — the live
            // reaction panel already shows what it answers. (Tag routes through
            // bar-aiming via AttackTargets and never reaches this menu.)
            if (moves.Count == 1 && moves[0] is RespondToWindow)
            {
                Submit(moves[0]);
                return;
            }
            var card = View.Hand.FirstOrDefault(c => c.InstanceId == cardInstanceId);
            string cardName = _db.Lemon(card?.DefId ?? "").Name;
            var cardArt = _art.Lemon(card?.DefId ?? "");

            // Multi-variant plays collapse into one option that opens a guided flow,
            // instead of flooding the menu with one text row per combination.
            var bmTakes = moves.OfType<PlayLemonCard>()
                .Where(m => m.DiscardedBmInstanceId != null).Cast<GameAction>().ToList();
            var lemonTakes = moves.OfType<PlayLemonCard>()
                .Where(m => m.DiscardedLemonInstanceId != null).Cast<GameAction>().ToList();
            var equipSteals = moves.OfType<PlayLemonCard>()
                .Where(m => m.TargetEquippedInstanceId != null).Cast<GameAction>().ToList();
            var direct = moves
                .Where(m => !bmTakes.Contains(m) && !lemonTakes.Contains(m) &&
                            !equipSteals.Contains(m)).ToList();

            var options = ToOptions(direct);
            if (bmTakes.Count > 0)
            {
                options.Add(new Prompt.Option("Take a discarded Black Market card...",
                    () => OpenTakeFromDiscard(bmTakes, blackMarket: true)));
            }
            if (lemonTakes.Count > 0)
            {
                options.Add(new Prompt.Option("Take a discarded Lemon card...",
                    () => OpenTakeFromDiscard(lemonTakes, blackMarket: false)));
            }
            if (equipSteals.Count > 0)
            {
                options.Add(new Prompt.Option("Choose a player to target...",
                    () => OpenEquippedSteal(cardName, cardArt, equipSteals)));
            }

            if (direct.Count == 0 && options.Count == 1)
            {
                options[0].OnPick(); // a lone guided flow — skip the one-button menu
                return;
            }
            _prompt.Show(cardName, new[] { cardArt }, options, showCancel: true);
        }

        /// <summary>
        /// Two-step flow for cards that target an opponent's equipped Black Market card
        /// (Finders Keepers, That's Not Fair!): pick the victim, then pick the card off
        /// their board in the big picker — with BACK to reconsider the victim.
        /// </summary>
        /// <summary>That's Not Fair! discards its target instead of stealing it.</summary>
        private bool TargetsForDiscardOnly(IEnumerable<GameAction> moves) =>
            moves.OfType<PlayLemonCard>().Any(m =>
                View.Hand.FirstOrDefault(c => c.InstanceId == m.CardInstanceId)?.DefId
                    == "thats-not-fair");

        private void OpenEquippedSteal(string cardName, Texture2D cardArt, List<GameAction> steals)
        {
            var byVictim = steals.Cast<PlayLemonCard>()
                .GroupBy(m => m.TargetPlayerId ?? FindEquipOwner(m.TargetEquippedInstanceId.Value))
                .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());

            void ShowVictims()
            {
                _prompt.Show($"{cardName}: choose who to target", new[] { cardArt },
                    byVictim.Select(kv => new Prompt.Option(
                        $"{NameOf(kv.Key)} ({EquippedCardInfos(kv.Key, kv.Value).Count} card(s))",
                        () => ShowCards(kv.Key))).ToList(),
                    showCancel: true);
            }

            void ShowCards(int victim)
            {
                _prompt.Hide();
                var victimMoves = byVictim[victim];
                var byEquipped = victimMoves.Cast<PlayLemonCard>()
                    .GroupBy(m => m.TargetEquippedInstanceId.Value)
                    .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
                var cards = EquippedCardInfos(victim, victimMoves);
                // That's Not Fair! trashes the card; Finders Keepers takes it.
                bool trashes = TargetsForDiscardOnly(victimMoves);
                string verb = trashes ? "DISCARD" : "TAKE";
                _table.OpenDiscardPicker(
                    $"{cardName.ToUpperInvariant()}: {verb} FROM {NameOf(victim).ToUpperInvariant()}",
                    cards, blackMarket: true,
                    c => _db.BlackMarket(c.DefId).Name,
                    equippedId =>
                    {
                        var picked = cards.First(c => c.InstanceId == equippedId);
                        ResolveEquipDestination(byEquipped[equippedId],
                            _art.BlackMarket(picked.DefId, picked.Shape ?? Shape.Square));
                    },
                    onBack: ShowVictims, verb: verb);
            }

            ShowVictims();
        }

        /// <summary>
        /// The opponents this hand card can hit, when EVERY way to play it resolves to
        /// one victim — those cards aim at the player bars (click or drag) instead of
        /// opening a menu. Null means "not an attack": use the normal menu flow.
        /// </summary>
        private ISet<int> AttackTargets(int cardInstanceId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves) ||
                moves.Count == 0)
            {
                return null;
            }
            var targets = new HashSet<int>();
            foreach (var move in moves)
            {
                int victim = VictimOf(move);
                if (victim < 0)
                {
                    return null;
                }
                targets.Add(victim);
            }
            return targets;
        }

        /// <summary>
        /// The equipped Black Market cards this hand card can hit directly (That's Not
        /// Fair, Finders Keepers) — while aiming at the bars, the visible copies on
        /// the viewed board are precise targets of their own.
        /// </summary>
        private ISet<int> EquipAimTargets(int cardInstanceId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return null;
            }
            var targets = new HashSet<int>();
            foreach (var move in moves)
            {
                if (move is PlayLemonCard play && play.TargetEquippedInstanceId is int eq)
                {
                    targets.Add(eq);
                }
            }
            return targets.Count > 0 ? targets : null;
        }

        /// <summary>
        /// Aimed straight at an equipped card on someone's board: confirm the exact
        /// hit, then fall into the shared destination flow (which just submits when
        /// there's nothing left to choose).
        /// </summary>
        private void ConfirmEquipAttack(int cardInstanceId, int equippedId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return;
            }
            var variants = moves.OfType<PlayLemonCard>()
                .Where(p => p.TargetEquippedInstanceId == equippedId)
                .Cast<GameAction>()
                .ToList();
            var info = EquippedInfoOf(equippedId);
            if (variants.Count == 0 || info == null)
            {
                return;
            }
            string cardName = LemonName(
                View.Hand.FirstOrDefault(c => c.InstanceId == cardInstanceId)?.DefId ?? "");
            int victim = FindEquipOwner(equippedId);
            bool trashes = TargetsForDiscardOnly(variants);
            var art = _art.BlackMarket(info.DefId, info.Shape ?? Shape.Square);
            _prompt.Show(
                $"Play {cardName} on {NameOf(victim)}'s {BlackMarketName(info.DefId)}?",
                new[] { art },
                new List<Prompt.Option>
                {
                    new Prompt.Option(trashes ? "Discard it!" : "Take it!",
                        () => ResolveEquipDestination(variants, art)),
                },
                showCancel: true);
        }

        private GameAction RefreshMove() =>
            CurrentGroups()?.BarMoves.FirstOrDefault(m => m is RefreshMarket);

        private GameAction DrawMove() =>
            CurrentGroups()?.BarMoves.FirstOrDefault(m => m is DrawLemonCard);

        /// <summary>
        /// Market slots a hand card can take from (Connections), or null when the card
        /// doesn't reach the shelf — the aiming layer treats null as "not mine".
        /// </summary>
        private ISet<int> MarketTargets(int cardInstanceId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves) ||
                moves.Count == 0)
            {
                return null;
            }
            var slots = new HashSet<int>();
            foreach (var move in moves)
            {
                if (!(move is PlayLemonCard play) || !(play.MarketIndex is int index))
                {
                    return null; // any non-market variant: this is not a shelf card
                }
                slots.Add(index);
            }
            return slots;
        }

        /// <summary>
        /// Connections picked a market card: place it exactly like a bought one —
        /// straight in when there's one legal slot, else aim at your board.
        /// </summary>
        private void ResolveMarketTake(int cardInstanceId, int marketIndex)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return;
            }
            var taken = moves
                .Where(m => m is PlayLemonCard play && play.MarketIndex == marketIndex)
                .ToList();
            if (taken.Count == 0)
            {
                return;
            }
            var card = View.Market[marketIndex];
            ResolveEquipDestination(taken, _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square));
        }

        /// <summary>The opponent a play ultimately hits, or -1 when it isn't aimed at one.</summary>
        private int VictimOf(GameAction move)
        {
            // Reactions from the hand aim at bars too: Tag's "victim" is the redirect
            // target; Rubber Glue's is the attacker the play bounces back onto.
            if (move is RespondToWindow respond)
            {
                if (respond.RedirectTargetId is int redirect && redirect != View.ViewerId)
                {
                    return redirect;
                }
                var reactionCard = View.Hand.FirstOrDefault(c => c.InstanceId == respond.CardInstanceId);
                if (reactionCard?.DefId == "im-rubber-youre-glue" && View.StackTop != null &&
                    View.StackTop.OwnerId != View.ViewerId)
                {
                    return View.StackTop.OwnerId;
                }
                return -1;
            }
            if (!(move is PlayLemonCard play))
            {
                return -1;
            }
            if (play.TargetPlayerId is int victim)
            {
                return victim == View.ViewerId ? -1 : victim;
            }
            if (play.TargetEquippedInstanceId is int equipped)
            {
                int owner = FindEquipOwner(equipped);
                return owner == View.ViewerId ? -1 : owner;
            }
            return -1;
        }

        /// <summary>
        /// An attack was aimed and released on a player bar: submit it outright, or walk
        /// the remaining choices for that victim — which equipped card to hit (board
        /// picker, BACK re-aims) or which tantrum to hand off (small menu).
        /// </summary>
        private void ResolveAttackOnPlayer(int cardInstanceId, int victimId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return;
            }
            var onVictim = moves.Where(m => VictimOf(m) == victimId).ToList();
            if (onVictim.Count == 0)
            {
                return;
            }

            var card = View.Hand.FirstOrDefault(c => c.InstanceId == cardInstanceId);
            string cardName = _db.Lemon(card?.DefId ?? "").Name;
            if (onVictim.Count == 1 && onVictim[0] is RespondToWindow)
            {
                // A window response aimed at a bar (Tag redirect): the deliberate
                // aim-and-click is the confirmation — a blur prompt here would sit on
                // top of the live reaction panel and could be eaten by online updates.
                Submit(onVictim[0]);
                return;
            }
            if (onVictim.Count == 1)
            {
                // Point of no return: confirm before the attack actually fires.
                _prompt.Show($"Play {cardName} on {NameOf(victimId)}?",
                    new[] { _art.Lemon(card?.DefId ?? "") },
                    new List<Prompt.Option>
                    {
                        new Prompt.Option("YES!", () => Submit(onVictim[0])),
                    },
                    showCancel: true);
                return;
            }
            if (onVictim.Cast<PlayLemonCard>().All(m => m.TargetEquippedInstanceId != null))
            {
                var byEquipped = onVictim.Cast<PlayLemonCard>()
                    .GroupBy(m => m.TargetEquippedInstanceId.Value)
                    .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
                var cards = EquippedCardInfos(victimId, onVictim);
                // That's Not Fair! trashes what it points at; Finders Keepers takes it.
                string verb = TargetsForDiscardOnly(onVictim) ? "DISCARD" : "TAKE";
                _table.OpenDiscardPicker(
                    $"{cardName.ToUpperInvariant()}: {verb} FROM {NameOf(victimId).ToUpperInvariant()}",
                    cards, blackMarket: true,
                    c => _db.BlackMarket(c.DefId).Name,
                    equippedId =>
                    {
                        var picked = cards.First(c => c.InstanceId == equippedId);
                        ResolveEquipDestination(byEquipped[equippedId],
                            _art.BlackMarket(picked.DefId, picked.Shape ?? Shape.Square));
                    },
                    onBack: () => _table.RestartAttackTargeting(cardInstanceId), verb: verb);
                return;
            }
            _prompt.Show($"{cardName} → {NameOf(victimId)}",
                new[] { _art.Lemon(card?.DefId ?? "") }, ToOptions(onVictim), showCancel: true);
        }

        /// <summary>The seat whose turf/stand carries this equipped instance.</summary>
        private int FindEquipOwner(int equippedInstanceId)
        {
            foreach (var player in View.Players)
            {
                if (player.TurfEquipped.Any(c => c.InstanceId == equippedInstanceId) ||
                    player.Stands.Any(st => st.Equipped.Any(c => c.InstanceId == equippedInstanceId)))
                {
                    return player.PlayerId;
                }
            }
            return -1;
        }

        /// <summary>CardInfos for the equipped instances these moves target, in board order.</summary>
        private List<PlayerView.CardInfo> EquippedCardInfos(int victim, List<GameAction> moves)
        {
            var wanted = moves.Cast<PlayLemonCard>()
                .Select(m => m.TargetEquippedInstanceId.Value).ToHashSet();
            var player = View.Players[victim];
            return player.TurfEquipped
                .Concat(player.Stands.SelectMany(st => st.Equipped))
                .Where(c => wanted.Contains(c.InstanceId))
                .ToList();
        }

        /// <summary>Choose which discard to take via the discard-pile picker overlay.</summary>
        private void OpenTakeFromDiscard(List<GameAction> takes, bool blackMarket)
        {
            _prompt.Hide();
            var byCard = takes.Cast<PlayLemonCard>()
                .GroupBy(m => (blackMarket ? m.DiscardedBmInstanceId : m.DiscardedLemonInstanceId).Value)
                .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
            var pool = (blackMarket ? View.BlackMarketDiscard : View.LemonDiscard)
                .Where(c => byCard.ContainsKey(c.InstanceId)).ToList();

            _table.OpenDiscardPicker(
                blackMarket ? "TAKE A DISCARDED BLACK MARKET CARD" : "TAKE A DISCARDED LEMON CARD",
                pool, blackMarket,
                c => blackMarket ? _db.BlackMarket(c.DefId).Name : _db.Lemon(c.DefId).Name,
                instanceId =>
                {
                    var picked = pool.First(c => c.InstanceId == instanceId);
                    var texture = blackMarket
                        ? _art.BlackMarket(picked.DefId, picked.Shape ?? Shape.Square)
                        : _art.Lemon(picked.DefId);
                    ResolveEquipDestination(byCard[instanceId], texture);
                });
        }

        /// <summary>
        /// Finish a play whose variants differ only by equip destination: one candidate
        /// submits directly; several open the aim-at-your-board targeting (float card +
        /// dashed arrow), with a replace-prompt when the chosen slot is full.
        /// </summary>
        private void ResolveEquipDestination(List<GameAction> candidates, Texture2D texture)
        {
            if (candidates.Count == 1)
            {
                Submit(candidates[0]);
                return;
            }
            var byDest = candidates.Cast<PlayLemonCard>()
                .GroupBy(m => m.EquipStandInstanceId)
                .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
            System.Action<int?> pickDestination = destId =>
            {
                var atDest = byDest[destId];
                if (atDest.Count == 1)
                {
                    Submit(atDest[0]);
                }
                else
                {
                    _prompt.Show("That slot is full — replace which card?",
                        new[] { texture }, ToOptions(atDest), showCancel: true);
                }
            };
            if (byDest.Count == 1)
            {
                // Only one valid target: apply without the selector.
                pickDestination(byDest.Keys.First());
                return;
            }
            // The destinations are YOUR stands: if the steal was aimed on the victim's
            // board, snap home first (synchronously — the targeting layer must open
            // over the board it points at, and a later render would dismiss it).
            if (_table.ViewedBoardPlayer >= 0)
            {
                _table.ViewedBoardPlayer = -1;
                _renderedRevision = -1;
                RenderIfChanged();
            }
            _table.BeginEquipTargeting(texture,
                new HashSet<int?>(byDest.Keys), pickDestination, () => { });
        }

        private void OnMarketDragStart(int marketIndex)
        {
            _preview.SetDragging(true);
            var groups = CurrentGroups();
            if (groups == null || !groups.MarketMoves.TryGetValue(marketIndex, out var moves))
            {
                return;
            }
            var valid = new HashSet<int?>(moves.OfType<BuyBlackMarket>()
                .Select(m => m.TargetStandInstanceId));
            _table.SetValidDropTargets(valid);
        }

        private void OnMarketDrop(int marketIndex, int? standInstanceId)
        {
            _table.ClearDropHighlights();
            var groups = CurrentGroups();
            if (groups == null || !groups.MarketMoves.TryGetValue(marketIndex, out var moves))
            {
                return;
            }
            var matching = moves.OfType<BuyBlackMarket>()
                .Where(m => m.TargetStandInstanceId == standInstanceId)
                .Cast<GameAction>()
                .ToList();
            if (matching.Count == 1)
            {
                Submit(matching[0]);
            }
            else if (matching.Count > 1)
            {
                var card = View.Market[marketIndex];
                _prompt.Show("That slot is full — replace which card?",
                    new[] { _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square) },
                    ToOptions(matching), showCancel: true);
            }
        }

        private void OnSupplyDrop(string standTypeId, int insertIndex)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.SupplyMoves.TryGetValue(standTypeId, out var moves))
            {
                return;
            }
            var pick = moves.FirstOrDefault(m =>
                    (m as BuyStand)?.InsertIndex == insertIndex ||
                    (m as InitialBuyStand)?.InsertIndex == insertIndex)
                ?? moves[moves.Count - 1];
            Submit(pick);
        }

        private List<Prompt.Option> ToOptions(IEnumerable<GameAction> moves)
        {
            return moves
                .Select(m => new Prompt.Option(_session.LabelFor(m), () => Submit(m), ArtForMove(m)))
                .ToList();
        }

        /// <summary>
        /// The card an option would play, so response buttons ("Respond with Tantrum")
        /// can show what the card actually does. Null for card-less moves (Pass, buys).
        /// </summary>
        private Texture2D ArtForMove(GameAction move)
        {
            switch (move)
            {
                case RespondToWindow respond when respond.EquippedInstanceId is int eq:
                    return EquippedArt(eq);
                case RespondToWindow respond:
                    return HandArt(respond.CardInstanceId);
                case UseTurnAbility ability:
                    return EquippedArt(ability.EquippedInstanceId);
                case PlayLemonCard play when play.TargetEquippedInstanceId is int loot:
                    // Steal variants: show the LOOT, not the (identical) attack card.
                    return EquippedArt(loot);
                case PlayLemonCard play:
                    return HandArt(play.CardInstanceId);
                case SubmitRetarget retarget when retarget.TargetEquippedInstanceId is int newLoot:
                    return EquippedArt(newLoot);
                case SubmitAbilityChoice choice when choice.EquippedInstanceId is int copy:
                    return EquippedArt(copy);
                default:
                    return null;
            }
        }

        private Texture2D HandArt(int instanceId)
        {
            var card = View.Hand.FirstOrDefault(c => c.InstanceId == instanceId);
            return card == null ? null : _art.Lemon(card.DefId);
        }

        private Texture2D EquippedArt(int instanceId)
        {
            var info = EquippedInfoOf(instanceId);
            return info == null ? null : _art.BlackMarket(info.DefId, info.Shape ?? Shape.Square);
        }

        private PlayerView.CardInfo EquippedInfoOf(int instanceId)
        {
            foreach (var panel in View.Players)
            {
                foreach (var card in panel.TurfEquipped)
                {
                    if (card.InstanceId == instanceId)
                    {
                        return card;
                    }
                }
                foreach (var stand in panel.Stands)
                {
                    foreach (var card in stand.Equipped)
                    {
                        if (card.InstanceId == instanceId)
                        {
                            return card;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>"Juice Box Joey: do the thing" when the ability's source card is known.</summary>
        private string Prefixed(PlayerView.DecisionInfo decision, string action)
        {
            string source = SourceCardName(decision);
            return source != null
                ? $"{source}: {action}"
                : char.ToUpperInvariant(action[0]) + action.Substring(1);
        }

        /// <summary>"Juice Box Joey" for a decision raised by an equipped ability; null otherwise.</summary>
        private string SourceCardName(PlayerView.DecisionInfo decision)
        {
            if (!(decision?.SourceInstanceId is int src))
            {
                return null;
            }
            var info = EquippedInfoOf(src);
            return info == null ? null : BlackMarketName(info.DefId);
        }

        // ----------------------------------------------------------- render

        private void RenderIfChanged()
        {
            if (View == null)
            {
                return;
            }
            int revision = _session.Revision;
            if (revision == _renderedRevision)
            {
                return;
            }
            _renderedRevision = revision;

            var groups = CurrentGroups();

            // A window/decision can dissolve underneath an open modal — the engine
            // auto-passes players who become ineligible when a window recomputes, and
            // others' responses can resolve the whole stack. Window prompts have no
            // Cancel, so a stale one soft-locks the player: dismiss it.
            if (_autoModalOpen && (_prompt.IsOpen || _picker.IsOpen) &&
                (groups == null || !groups.IsModal || groups.ModalMoves.Count == 0))
            {
                _prompt.Hide();
                _picker.Hide();
                _autoModalOpen = false;
                _modalSignature = "";
            }

            try
            {
                RenderStatus();
                RenderBanner();
                _table.Render(View, _db, groups);
                _table.SetLog(_actionLog);
                RenderActionBar(groups);
                RenderTurnActions(groups);
            }
            catch (System.Exception e)
            {
                // A render throw must never silently brick the frame (stale action
                // bar, half-drawn table). Name the culprit where the player looks.
                Debug.LogException(e);
                _statusText.text = $"render error: {e.GetType().Name} — check the console";
            }
            MaybeAnnounceTurn();
            // A finished player-pick (or a state that moved past it) must drop the
            // aiming overlay, or the arrow lingers over a resolved decision.
            if (_table.PlayerPickActive)
            {
                var decision = View.MyDecisions.FirstOrDefault();
                if (groups == null || !groups.IsModal ||
                    decision == null || decision.Kind != DecisionKind.AbilityVictim)
                {
                    _table.EndPlayerPick();
                }
            }
            // Same for a resolved response window: the reaction panel must not
            // outlive the stack item it was answering.
            if (_reaction.IsOpen && !ReactionWindowDue(groups))
            {
                _reaction.Hide();
            }
            if (_turnBanner.IsOpen || _dice.IsBusy || _fx.IsBusy)
            {
                return; // decisions wait behind the ONWARD! button / the die / effects
            }
            MaybeShowModal(groups, revision);
        }

        /// <summary>Show the YOUR TURN interstitial when the turn passes to the viewer.</summary>
        private void MaybeAnnounceTurn()
        {
            bool myTurn = IsMyTurn();
            if (myTurn && !_wasMyTurn && (_dice.IsBusy || _fx.IsBusy))
            {
                // Let the die and effects finish first; their OnFinished forces a
                // re-render that lands back here with the turn edge still unconsumed.
                return;
            }
            bool becameMyTurn = myTurn && !_wasMyTurn;
            _wasMyTurn = myTurn;
            if (!becameMyTurn || _session.HumanAutoplay || _turnBanner.IsOpen)
            {
                return;
            }
            // Anything left open from the previous turn yields to the banner; the
            // forced re-render on dismiss re-opens whatever is still relevant.
            _prompt.Hide();
            _picker.Hide();
            _reaction.Hide();
            // Your turn starts at YOUR board: snap back from wherever you were
            // snooping, so ONWARD! lands on a table you can act on.
            if (_table.ViewedBoardPlayer >= 0)
            {
                _table.ViewedBoardPlayer = -1;
                _renderedRevision = -1;
            }
            Sfx.Play(Sfx.YourTurn); // played, not queued: it announces the banner itself
            _turnBanner.Show(TurnSubtitle());
        }

        private static string Actions(int count) => count == 1 ? "1 action" : $"{count} actions";

        private bool IsMyTurn()
        {
            if (View.Stage == GameStage.InitialBuys)
            {
                return View.CurrentInitialBuyer == View.ViewerId;
            }
            return (View.Stage == GameStage.Playing || View.Stage == GameStage.FinalRound)
                && View.ActivePlayer == View.ViewerId;
        }

        private string TurnSubtitle()
        {
            if (View.Stage == GameStage.InitialBuys)
            {
                return "Setup draft — pick out your stands";
            }
            if (View.Stage == GameStage.FinalRound)
            {
                return "Final round — make it count!";
            }
            return $"{Actions(View.ActionsRemaining)} — the market awaits";
        }

        private void RenderStatus()
        {
            // VP/cash live on your player panel now; the top-left keeps utility info.
            var me = View.Players[View.ViewerId];
            string status;
            if (View.Stage == GameStage.Finished)
            {
                status = $"GAME OVER — winner: {string.Join(", ", View.Winners.Select(NameOf))}" +
                         (_session is LocalGameSession ? "   [N] new game" : "");
            }
            else
            {
                // Tantrum counts moved onto the player bars, where everyone's shows.
                status = (_session.HumanAutoplay ? "AUTOPILOT [B]" : "[B] autopilot") +
                         (View.WhiniestBabyHolder == View.ViewerId ? "  |  WHINIEST BABY" : "") +
                         (View.SpoiledRottenHolder == View.ViewerId ? "  |  SPOILED ROTTEN" : "") +
                         (_remote != null ? $"  |  room {_remote.Room.Code}" : "");
                // The table is stalled on someone else: say who, so a quiet moment
                // (their response window, their discard) never reads as a hang.
                if (!View.ActingPlayers.Contains(View.ViewerId) && View.ActingPlayers.Count > 0)
                {
                    string verb = View.StackTop != null ? " to respond" : "";
                    status += $"  |  WAITING ON {string.Join(", ", View.ActingPlayers.Select(NameOf)).ToUpperInvariant()}{verb}";
                }
            }
            _statusText.text = status;
        }

        private void RenderBanner()
        {
            string banner;
            if (_reaction.IsOpen)
            {
                // The response panel's headline lives HERE while it is open: the bar
                // was saying nearly the same thing, and the freed band keeps showing
                // the die (or the attack) you are responding to.
                banner = _reactionBanner;
            }
            else if (View.Stage == GameStage.Finished)
            {
                banner = "Thanks for playing!";
            }
            else if (View.StackTop != null)
            {
                // Someone's card is resolving but OUR response panel isn't open
                // (nothing to respond with, or it's someone else's window). Name
                // the play — "Dex's turn — Play" told bystanders nothing.
                banner = StackHeadline();
            }
            else if (View.PendingRollValue is int roll)
            {
                string what = RollPurposeName(View.PendingRollPurpose) ?? "Sale";
                banner = $"{what.ToUpperInvariant()} ROLL: {roll}";
            }
            else if (View.Stage == GameStage.ChoosingLemonLords)
            {
                banner = "Choose your secret Lemon Lord titles";
            }
            else if (View.Stage == GameStage.InitialBuys)
            {
                banner = $"Setup draft — {NameOf(View.CurrentInitialBuyer ?? 0)} is buying";
            }
            else if (View.ActivePlayer == View.ViewerId)
            {
                banner = $"Your turn — {View.Phase} ({Actions(View.ActionsRemaining)} left)";
            }
            else
            {
                banner = $"{NameOf(View.ActivePlayer)}'s turn — {View.Phase}";
            }
            _topBanner.text = banner;
        }

        /// <summary>
        /// Third-person headline for the yellow bar while a card sits on the stack:
        /// plain plays ("Dex plays Automation"), attacks ("Dex plays Slander on Kim"),
        /// and reactions (tags, reflections, tantrums) all name the card instead of
        /// falling back to the generic "X's turn — Phase" label.
        /// </summary>
        private string StackHeadline()
        {
            var top = View.StackTop;
            // CardName, not LemonName: purchases and some responses carry
            // Black Market ids, and CardName checks both catalogs.
            string card = CardName(top.DefId);
            bool self = top.OwnerId == View.ViewerId;
            string owner = self ? "You" : NameOf(top.OwnerId);
            string plays = self ? "play" : "plays";
            if (top.AttackTargetId is int t)
            {
                string victim = t == View.ViewerId ? "you" : NameOf(t);
                return $"{owner} {plays} {card} on {victim}";
            }
            // Reactions name WHAT they answer, mirroring the response panel.
            string answered = null;
            if (!string.IsNullOrEmpty(top.RespondingToDefId))
            {
                answered = top.RespondingToIsPurchase
                    ? $"the {CardName(top.RespondingToDefId)} purchase"
                    : CardName(top.RespondingToDefId);
                if (top.RespondingToOwnerId is int by && !top.RespondingToIsPurchase)
                {
                    answered = (by == View.ViewerId ? "your " : NameOf(by) + "'s ") + answered;
                }
            }
            if (top.DefId == "tag-youre-it" && top.RedirectTargetId is int redirect)
            {
                string who = redirect == View.ViewerId ? "you" : NameOf(redirect);
                return $"{owner} {plays} {card} — {answered ?? "the attack"} goes to {who}";
            }
            if (top.DefId == "im-rubber-youre-glue" && answered != null)
            {
                return $"{owner} {plays} {card} — {answered} is reflected back";
            }
            if (answered != null)
            {
                return $"{owner} {plays} {card} on {answered}";
            }
            if (top.IsPurchase)
            {
                return self ? $"You buy {card}" : $"{owner} buys {card}";
            }
            return $"{owner} {plays} {card}";
        }

        /// <summary>
        /// The two "advance the game" buttons, pinned top-right in the status bar:
        /// short labels, dark chips against the yellow.
        /// </summary>
        private void RenderTurnActions(MoveGroups groups)
        {
            UiKit.Clear(_turnActionRow);
            if (groups == null || groups.IsModal)
            {
                return;
            }
            foreach (var move in groups.BarMoves)
            {
                string label = move is EndTurn ? "END TURN (SELL)"
                    : move is InitialBuyEnd ? "FINISH DRAFT"
                    : null;
                if (label == null)
                {
                    continue;
                }
                var captured = move;
                // End Turn stays silent: the die's own roll sound covers it.
                var button = UiKit.CreateButton(_turnActionRow, label, 16,
                    () => Submit(captured), dark: true,
                    clickSound: captured is EndTurn ? null : Sfx.ButtonClick);
                var element = button.GetComponent<LayoutElement>();
                element.minWidth = 190;
                element.minHeight = 36;
                var text = button.GetComponentInChildren<TMPro.TMP_Text>();
                text.alignment = TMPro.TextAlignmentOptions.Center;
                UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one);
            }
        }

        private void RenderActionBar(MoveGroups groups)
        {
            UiKit.Clear(_table.ActionBar);
            if (groups == null || groups.IsModal)
            {
                return;
            }
            foreach (var move in groups.BarMoves)
            {
                if (move is RefreshMarket || move is DrawLemonCard ||
                    move is EndTurn || move is InitialBuyEnd)
                {
                    continue; // these live on the table / in the top bar now
                }
                var captured = move;
                // Buttons whose ACTION has a sound stay silent, rather than clicking
                // over their own tumble a frame later.
                bool ownsItsSound = captured is EndTurn;
                var button = UiKit.CreateButton(_table.ActionBar,
                    _session.LabelFor(captured), 15, () => Submit(captured),
                    clickSound: ownsItsSound ? null : Sfx.ButtonClick);
                button.GetComponent<LayoutElement>().minWidth = 150;
            }
            if (groups.BarMoves.Count == 0 && groups.SupplyMoves.Count > 0)
            {
                // Draft visits before the mandatory stand: every move is a drag, so
                // the empty bar says what to do instead of looking broken.
                var hint = UiKit.CreateText(_table.ActionBar,
                    "Drag a stand from the shelf onto your row", 15,
                    TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.55f), body: true);
                hint.gameObject.AddComponent<LayoutElement>().minWidth = 320;
            }
        }

        // ------------------------------------------------------------ modal

        private void MaybeShowModal(MoveGroups groups, int revision)
        {
            if (groups == null || !groups.IsModal || groups.ModalMoves.Count == 0)
            {
                return;
            }
            // Online, harmless updates (a friend passing their window, bot pacing) bump
            // the revision while a modal is open. Rebuilding identical buttons under the
            // cursor eats the player's click — so if the same choice is already on
            // screen, leave it alone.
            string signature = ModalTitle() + "|" +
                string.Join("|", groups.ModalMoves.Select(m => _session.LabelFor(m)));
            if ((_prompt.IsOpen || _picker.IsOpen || _reaction.IsOpen) && signature == _modalSignature)
            {
                _modalRevision = revision;
                return;
            }
            if (_modalRevision == revision && (_prompt.IsOpen || _picker.IsOpen || _reaction.IsOpen))
            {
                return;
            }
            _modalRevision = revision;
            _modalSignature = signature;
            _autoModalOpen = true;
            if (TryShowPlayerPick(groups))
            {
                return;
            }
            if (TryShowPicker())
            {
                return;
            }
            bool passFirst(GameAction m) => m is SkipFreePlay || m is PassWindow;
            if (ReactionWindowDue(groups))
            {
                // A stack-top window: react from the log column with the table live.
                // The list is short here, so Pass sits at the BOTTOM — reactions read
                // first, "do nothing" is the fallback under them.
                var reactionMoves = groups.ModalMoves
                    .OrderBy(m => passFirst(m) ? 1 : 0)
                    .ToList();
                var kind = View.MyDecisions.FirstOrDefault()?.Kind;
                _prompt.Hide();
                _reactionBanner = ModalTitle();
                _reaction.Show(ModalCards(), GroupedPlayOptions(reactionMoves, tableLive: true),
                    kind == DecisionKind.AttackRetarget ? "PICK A NEW TARGET"
                    : kind == DecisionKind.BouncerAttack ? "STRIKE BACK"
                    : View.StackTop == null && View.PendingRollValue != null
                        ? "USE AN ABILITY?"
                        : "YOUR RESPONSE");
                return;
            }
            // Blur modals group play variants by card (Smear Campaign's free play
            // once listed dozens of rows): the escape hatch leads, so it never
            // scrolls off-screen.
            var orderedMoves = groups.ModalMoves
                .OrderBy(m => passFirst(m) ? 0 : 1)
                .ToList();
            _reaction.Hide();
            _prompt.Show(ModalTitle(), ModalCards(),
                GroupedPlayOptions(orderedMoves, tableLive: false), showCancel: false);
        }

        /// <summary>
        /// Options for a free-play window (Bouncer strike-back, Smear Campaign's
        /// offer): the engine enumerates one move per card x target, which reads as a
        /// wall of near-identical rows. Collapse to one option per CARD — picking it
        /// submits directly (single variant), arms the live-table aiming (reaction
        /// panel), or opens a target page (blur modal). Non-play moves pass through.
        /// </summary>
        private List<Prompt.Option> GroupedPlayOptions(IEnumerable<GameAction> moves, bool tableLive)
        {
            var all = moves.ToList();
            var options = new List<Prompt.Option>();
            var seenDefs = new HashSet<string>();
            foreach (var move in all)
            {
                if (!(move is PlayLemonCard play))
                {
                    options.Add(new Prompt.Option(_session.LabelFor(move),
                        () => Submit(move), ArtForMove(move)));
                    continue;
                }
                string defId = View.Hand.FirstOrDefault(c => c.InstanceId == play.CardInstanceId)?.DefId;
                if (defId == null || !seenDefs.Add(defId))
                {
                    continue; // copies of one card are one choice
                }
                var variants = all.OfType<PlayLemonCard>()
                    .Where(v => View.Hand.FirstOrDefault(
                        c => c.InstanceId == v.CardInstanceId)?.DefId == defId)
                    .ToList();
                var art = _art.Lemon(defId);
                if (variants.Count == 1)
                {
                    var only = variants[0];
                    options.Add(new Prompt.Option(_session.LabelFor(only), () => Submit(only), art));
                }
                else if (tableLive)
                {
                    int instanceId = play.CardInstanceId;
                    options.Add(new Prompt.Option($"Play {LemonName(defId)}...",
                        () => _table.ActivateHandCard(instanceId), art));
                }
                else
                {
                    string name = LemonName(defId);
                    options.Add(new Prompt.Option($"Play {name}...",
                        () => ShowVariantPage(name, art, variants), art));
                }
            }
            return options;
        }

        /// <summary>Second page of a blur-modal free play: this card's targets, with
        /// a way back to reconsider the card.</summary>
        private void ShowVariantPage(string name, Texture2D art, List<PlayLemonCard> variants)
        {
            var options = new List<Prompt.Option>
            {
                new Prompt.Option("< Back", () =>
                {
                    // Rewind the modal gate: the next tick re-renders and reopens the
                    // grouped first page (same trick as the turn banner's dismiss).
                    _modalRevision = -1;
                    _modalSignature = "";
                    _renderedRevision = -1;
                }),
            };
            options.AddRange(variants.Select(v => new Prompt.Option(
                _session.LabelFor(v), () => Submit(v),
                v.TargetEquippedInstanceId is int eq ? EquippedArt(eq) : null)));
            _prompt.Show($"Play {name}", new[] { art }, options, showCancel: false);
        }

        /// <summary>
        /// "Choose who to rob" aims at the player bars instead of opening a modal:
        /// the ability card floats mid-screen, the bars stay readable (money, hand
        /// size, VP are exactly the stats you pick a victim by), and the attack
        /// arrow language does the rest.
        /// </summary>
        private bool TryShowPlayerPick(MoveGroups groups)
        {
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null || decision.Kind != DecisionKind.AbilityVictim)
            {
                return false;
            }
            if (_table.PlayerPickActive)
            {
                return true; // already aiming this decision
            }
            var byVictim = new Dictionary<int, GameAction>();
            foreach (var move in groups.ModalMoves)
            {
                if (move is SubmitAbilityChoice choice && choice.TargetPlayerId is int victim)
                {
                    byVictim[victim] = move;
                }
            }
            if (byVictim.Count == 0)
            {
                return false;
            }
            var art = decision.SourceInstanceId is int src ? EquippedArt(src) : null;
            _table.BeginPlayerPick(art, new HashSet<int>(byVictim.Keys),
                picked => Submit(byVictim[picked]));
            return true;
        }

        /// <summary>Choose-N-cards moments route to the lift-and-glow picker.</summary>
        private bool TryShowPicker()
        {
            if (View.Stage == GameStage.ChoosingLemonLords)
            {
                var dealt = View.LemonLordDealt.ToList();
                _picker.Show($"Keep {_db.Config.LemonLordKept} secret Lemon Lord titles",
                    dealt.Select(id => _art.Title(id)).ToList(),
                    _db.Config.LemonLordKept,
                    picked => Submit(new ChooseLemonLords
                    {
                        KeepTitleIds = picked.Select(i => dealt[i]).ToList(),
                    }),
                    preview: false); // three near-full-size cards need no magnifier
                return true;
            }

            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null)
            {
                return false;
            }

            List<PlayerView.CardInfo> pool;
            int required;
            string title;
            System.Action<List<int>> accept;
            switch (decision.Kind)
            {
                case DecisionKind.DiscardToHandLimit:
                    pool = View.Hand.ToList();
                    required = decision.RequiredCount;
                    title = $"Timeout! Discard {required} card(s)";
                    accept = ids => Submit(new SubmitDiscard { InstanceIds = ids });
                    break;

                case DecisionKind.WhiniestBabyDiscard:
                {
                    // Restricted to the cards just drawn — and phrased positively:
                    // pick what you KEEP, the rest goes to the discard.
                    pool = decision.EligibleCardIds != null
                        ? View.Hand.Where(c => decision.EligibleCardIds.Contains(c.InstanceId)).ToList()
                        : View.Hand.ToList();
                    int keepCount = pool.Count - decision.RequiredCount;
                    if (keepCount > 0)
                    {
                        required = keepCount;
                        title = keepCount == 1
                            ? "Whiniest Baby: pick 1 new card to keep"
                            : $"Whiniest Baby: pick {keepCount} new cards to keep";
                        var drawnPool = pool;
                        accept = keptIds => Submit(new SubmitDiscard
                        {
                            InstanceIds = drawnPool.Select(c => c.InstanceId)
                                .Where(id => !keptIds.Contains(id)).ToList(),
                        });
                    }
                    else
                    {
                        // Thin deck: everything drawn must go — no keep choice exists.
                        required = decision.RequiredCount;
                        title = $"Whiniest Baby: discard {required} card(s)";
                        accept = ids => Submit(new SubmitDiscard { InstanceIds = ids });
                    }
                    break;
                }

                case DecisionKind.AbilityDiscard:
                    pool = View.Hand.ToList();
                    required = decision.RequiredCount;
                    title = Prefixed(decision, $"discard {required} card(s)");
                    accept = ids => Submit(new SubmitAbilityChoice { CardInstanceIds = ids });
                    break;

                case DecisionKind.AbilityPickCard:
                    pool = View.RevealedHand ?? new List<PlayerView.CardInfo>();
                    required = 1;
                    title = Prefixed(decision, $"pick a card from {NameOf(decision.ChosenPlayerId ?? 0)}'s hand");
                    accept = ids => Submit(new SubmitAbilityChoice { CardInstanceIds = ids });
                    break;

                case DecisionKind.AbilityGiveBack:
                    pool = View.Hand.Where(c => c.InstanceId != decision.StolenCardId).ToList();
                    required = 1;
                    title = Prefixed(decision, "give back a different card");
                    accept = ids => Submit(new SubmitAbilityChoice { CardInstanceIds = ids });
                    break;

                default:
                    return false;
            }

            var capturedPool = pool;
            _picker.Show(title,
                capturedPool.Select(c => _art.Lemon(c.DefId)).ToList(),
                required,
                picked => accept(picked.Select(i => capturedPool[i].InstanceId).ToList()));
            return true;
        }

        private string ModalTitle()
        {
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision != null)
            {
                // Name the equipped card behind an ability decision: "you're robbing
                // because of Juice Box Joey" beats a bare "choose who to rob".
                string source = SourceCardName(decision);
                switch (decision.Kind)
                {
                    case DecisionKind.TimeoutFine:
                        return $"Timeout! Pay the ${decision.RequiredMoney} fine " +
                               $"(you have ${View.Players[View.ViewerId].Money})";
                    case DecisionKind.AttackRetarget: return "Your attack was redirected — pick a new target";
                    case DecisionKind.FreePlayOffer: return "Smear Campaign: free play?";
                    case DecisionKind.ForcedPlay: return "Reverse Engineer: play the recovered card";
                    case DecisionKind.BouncerAttack: return "Bouncer: strike back?";
                    case DecisionKind.AbilityVictim:
                        return source != null ? $"{source}: choose who to rob" : "Choose who to rob";
                    case DecisionKind.InnovationCopy: return "Innovation: copy which ability?";
                    case DecisionKind.WordOfMouthStand: return "Word of Mouth: which stand sells?";
                    default: return decision.Kind.ToString();
                }
            }
            if (View.StackTop != null)
            {
                var top = View.StackTop;
                string cardName = top.IsPurchase
                    ? _db.BlackMarket(top.DefId).Name
                    : _db.Lemon(top.DefId).Name;
                if (top.AttackTargetId is int t)
                {
                    // The single attack moment: who hits whom, reactions right below.
                    string victim = t == View.ViewerId ? "you" : NameOf(t);
                    string title = $"{NameOf(top.OwnerId)} attacks {victim} with {cardName}!";
                    if (!string.IsNullOrEmpty(top.StolenDefId))
                    {
                        // Finders Keepers / That's Not Fair: name the loot.
                        title += $"  Target: {_db.BlackMarket(top.StolenDefId).Name}.";
                    }
                    return title;
                }
                // Responses name WHAT they answer — "Dex tantrums" alone caused real
                // misreads at the table (which thing is being tantrummed?).
                string answered = null;
                if (!string.IsNullOrEmpty(top.RespondingToDefId))
                {
                    answered = top.RespondingToIsPurchase
                        ? $"the {_db.BlackMarket(top.RespondingToDefId).Name} purchase"
                        : _db.Lemon(top.RespondingToDefId).Name;
                    if (top.RespondingToOwnerId is int owner && !top.RespondingToIsPurchase)
                    {
                        answered = (owner == View.ViewerId ? "your " : NameOf(owner) + "'s ") + answered;
                    }
                }
                if (top.DefId == "tantrum" && answered != null)
                {
                    return $"{NameOf(top.OwnerId)} tantrums {answered}! — respond?";
                }
                if (top.DefId == "tag-youre-it" && top.RedirectTargetId is int redirect)
                {
                    // Who it's going to, with the intel that decides a counter-tantrum.
                    var target = View.Players[redirect];
                    string who = redirect == View.ViewerId ? "YOU" : NameOf(redirect);
                    return $"{NameOf(top.OwnerId)} tags {answered ?? "the attack"} to {who} " +
                           $"(${target.Money}, {target.HandCount} cards) — respond?";
                }
                if (top.DefId == "im-rubber-youre-glue" && answered != null)
                {
                    string backAt = top.RespondingToOwnerId is int attacker
                        ? (attacker == View.ViewerId ? "YOU" : NameOf(attacker))
                        : "the attacker";
                    return $"{NameOf(top.OwnerId)} reflects {answered} back at {backAt}! — respond?";
                }
                string what = top.IsPurchase
                    ? $"{NameOf(top.OwnerId)} is buying {cardName}"
                    : $"{NameOf(top.OwnerId)} played {cardName}";
                return $"{what} — respond?";
            }
            if (View.PendingRollValue is int roll)
            {
                // Name the roll unless it is the ordinary turn sale — the common case
                // stays short, the surprising ones say what they are.
                return RollPurposeName(View.PendingRollPurpose) is string why
                    ? $"{why} roll: the die shows {roll} — respond?"
                    : $"The die shows {roll} — respond?";
            }
            if (View.TheftOnMe)
            {
                return "You were robbed — Profit Share?";
            }
            return "Your choice";
        }

        private List<Texture2D> ModalCards()
        {
            var cards = new List<Texture2D>();
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision?.SourceInstanceId is int src)
            {
                // The equipped card whose ability is asking — the "why" of this modal.
                var sourceArt = EquippedArt(src);
                if (sourceArt != null)
                {
                    cards.Add(sourceArt);
                }
            }
            if (View.StackTop != null)
            {
                var top = View.StackTop;
                cards.Add(top.IsPurchase
                    ? _art.BlackMarket(top.DefId, top.Shape ?? Shape.Square)
                    : _art.Lemon(top.DefId));
                if (!string.IsNullOrEmpty(top.StolenDefId))
                {
                    // Show what the steal is aimed at, right next to the attack.
                    cards.Add(_art.BlackMarket(top.StolenDefId, top.StolenShape ?? Shape.Square));
                }
                if (!string.IsNullOrEmpty(top.RespondingToDefId))
                {
                    // The card being tantrummed / tagged / reflected, beside the response.
                    cards.Add(top.RespondingToIsPurchase
                        ? _art.BlackMarket(top.RespondingToDefId, top.RespondingToShape ?? Shape.Square)
                        : _art.Lemon(top.RespondingToDefId));
                }
            }
            return cards;
        }
    }
}
