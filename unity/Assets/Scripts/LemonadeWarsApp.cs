using System.Collections.Generic;
using System.IO;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
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
                cam.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
                // Neutral dark table until a proper game background exists.
                cam.GetComponent<Camera>().backgroundColor = new Color(0.06f, 0.075f, 0.10f);
            }
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
        private string _pendingName;
        private string _pendingCode;
        private string _pendingToken;
        private float _pendingSince;
        private const float ConnectTimeoutSeconds = 10f;

        private LobbyUi _lobby;
        private TableView _table;
        private Prompt _prompt;
        private CardPicker _picker;
        private CardPreview _preview;
        private TurnBanner _turnBanner;
        private DiceRoller _dice;
        private Text _statusText;
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

            var statusPanel = UiKit.CreatePanel(root, "Status", UiKit.PanelColor);
            UiKit.Anchor(statusPanel, new Vector2(0, 0.95f), new Vector2(1, 1));
            _statusText = UiKit.CreateText(statusPanel, "", 22, TextAnchor.MiddleLeft);
            UiKit.Anchor((RectTransform)_statusText.transform, Vector2.zero, Vector2.one,
                new Vector2(14, 0), new Vector2(-14, 0));

            _preview = new CardPreview(root);
            _table = new TableView(root, _art, _preview, this);
            _table.OnHandCard = OpenHandMenu;
            _table.CanBuyMarket = i => CurrentGroups()?.MarketMoves.ContainsKey(i) == true;
            _table.OnMarketDragStart = OnMarketDragStart;
            _table.OnMarketDragEnd = () =>
            {
                _preview.SetDragging(false);
                _table.ClearDropHighlights();
            };
            _table.OnMarketDrop = OnMarketDrop;
            _table.CanBuySupply = typeId => CurrentGroups()?.SupplyMoves.ContainsKey(typeId) == true;
            _table.OnSupplyDrop = OnSupplyDrop;
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
            _lobby.OnResume = () => ConnectRemote(
                PlayerPrefs.GetString("lw_server"), false,
                PlayerPrefs.GetString("lw_name"),
                PlayerPrefs.GetString("lw_code"),
                PlayerPrefs.GetString("lw_token"));
            _lobby.OnAddBot = () => _remote?.AddBot();
            _lobby.OnRemoveBotSeat = seat => _remote?.RemoveBot(seat);
            _lobby.OnStart = () => _remote?.StartGame();
            _lobby.OnReadyToggle = ready => _remote?.SetReady(ready);
            _lobby.OnLeave = () => BackToMenu("");
            _lobby.SetResumeInfo(PlayerPrefs.GetString("lw_code", ""));
            StartCoroutine(ServerStatusLoop());

            // Built last: overlays render on top.
            _prompt = new Prompt(root, this);
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
        }

        // ------------------------------------------------------------ flows

        private IReadOnlyList<string> _soloBotNames = new[] { "Benny", "Cleo", "Dex" };

        private void StartLocalGame(IReadOnlyList<string> botNames)
        {
            _soloBotNames = botNames.ToList();
            var names = new List<string> { _lobby.DisplayName };
            names.AddRange(_soloBotNames);

            _remote?.Dispose();
            _remote = null;
            _session?.Dispose();
            _session = new LocalGameSession(_db, names.ToArray(), 0,
                (ulong)System.DateTime.Now.Ticks);
            _session.EventEmitted += OnGameEvent;
            EnterGame();
        }

        private void ConnectRemote(string url, bool create, string name, string code, string token)
        {
            _session?.Dispose();
            _remote = RemoteGameSession.Connect(url);
            _session = _remote;
            _session.EventEmitted += OnGameEvent;
            _pendingSend = true;
            _pendingSince = Time.time;
            _pendingCreate = create;
            _pendingName = name;
            _pendingCode = code;
            _pendingToken = token;
            PlayerPrefs.SetString("lw_server", url);
            PlayerPrefs.SetString("lw_name", name);
            _screen = Screen.Lobby;
            _lobbyRevision = -1;
            _lobby.ShowLobby(_remote.Room, "Connecting to " + url + "...", false);
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
            _turnBanner.Hide();
            _dice.Clear();
            _renderedRevision = -1;
            _modalRevision = -1;
            _wasMyTurn = false;
        }

        /// <summary>Typed engine events drive presentation moments (dice, later: effects).</summary>
        private void OnGameEvent(GameEvent gameEvent)
        {
            if (_session == null || _session.HumanAutoplay)
            {
                return; // autopilot is for fast testing — no theatre
            }
            if (gameEvent is SaleRolled roll)
            {
                _dice.Enqueue(NameOf(roll.PlayerId), roll.Value, roll.PlayerId == _session.Seat);
            }
            else if (gameEvent is DieRerolled reroll)
            {
                _dice.EnqueueReroll(NameOf(reroll.ByPlayerId), reroll.NewValue,
                    reroll.ByPlayerId == _session.Seat);
            }
        }

        private void Update()
        {
            // Deferred lobby verb once the socket is open.
            if (_remote != null && _pendingSend)
            {
                if (_remote.Connected)
                {
                    _pendingSend = false;
                    if (_pendingCreate)
                    {
                        _remote.CreateRoom(_pendingName);
                    }
                    else
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

            if (_screen != Screen.Game)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.N) && _session is LocalGameSession)
            {
                StartLocalGame(_soloBotNames);
            }
            if (Input.GetKeyDown(KeyCode.B) && _session != null)
            {
                _session.HumanAutoplay = !_session.HumanAutoplay;
                _prompt.Hide();
                _picker.Hide();
                _turnBanner.Hide();
                _dice.Clear();
                _renderedRevision = -1;
                _modalRevision = -1;
            }

            _table.TickSupplyDrag(Input.mousePosition);
            _table.TickEquipTargeting(Input.mousePosition);
            _table.TickHandScroll(Input.mousePosition);
            _table.TickDiscardScroll(Input.mousePosition);
            _dice.Tick();
            RenderIfChanged();
        }

        private void BackToMenu(string status)
        {
            _session?.Dispose();
            _session = null;
            _remote = null;
            _screen = Screen.Menu;
            _table.SetVisible(false);
            _prompt.Hide();
            _picker.Hide();
            _turnBanner.Hide();
            _dice.Clear();
            _lobby.ShowMenu(status);
            _lobby.SetResumeInfo(PlayerPrefs.GetString("lw_code", ""));
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
                _table.OpenDiscardPicker($"{cardName.ToUpperInvariant()}: TAKE FROM {NameOf(victim).ToUpperInvariant()}",
                    cards, blackMarket: true,
                    c => _db.BlackMarket(c.DefId).Name,
                    equippedId =>
                    {
                        var picked = cards.First(c => c.InstanceId == equippedId);
                        ResolveEquipDestination(byEquipped[equippedId],
                            _art.BlackMarket(picked.DefId, picked.Shape ?? Shape.Square));
                    },
                    onBack: ShowVictims);
            }

            ShowVictims();
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
                .Select(m => new Prompt.Option(_session.LabelFor(m), () => Submit(m)))
                .ToList();
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

            RenderStatus();
            RenderBanner();
            _table.Render(View, _db, groups);
            _table.SetLog(_session.Log);
            RenderActionBar(groups);
            MaybeAnnounceTurn();
            if (_turnBanner.IsOpen || _dice.IsBusy)
            {
                return; // decisions wait behind the ONWARD! button / the die
            }
            MaybeShowModal(groups, revision);
        }

        /// <summary>Show the YOUR TURN interstitial when the turn passes to the viewer.</summary>
        private void MaybeAnnounceTurn()
        {
            bool myTurn = IsMyTurn();
            if (myTurn && !_wasMyTurn && _dice.IsBusy)
            {
                // Let the die finish first; OnFinished forces a re-render that lands
                // back here with the turn edge still unconsumed.
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
            _turnBanner.Show(TurnSubtitle());
        }

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
            return $"{View.ActionsRemaining} actions — the market awaits";
        }

        private void RenderStatus()
        {
            var me = View.Players[View.ViewerId];
            string status = View.Stage == GameStage.Finished
                ? $"GAME OVER — winner: {string.Join(", ", View.Winners.Select(NameOf))}" +
                  (_session is LocalGameSession ? "   [N] new game" : "")
                : $"{me.Name}: ${me.Money}  |  {me.InGameVictoryPoints} VP" +
                  (View.WhiniestBabyHolder == View.ViewerId ? "  |  WHINIEST BABY" : "") +
                  (View.SpoiledRottenHolder == View.ViewerId ? "  |  SPOILED ROTTEN" : "") +
                  (me.TantrumCount > 0 ? $"  |  {me.TantrumCount} tantrums" : "") +
                  (_session.HumanAutoplay ? "  |  AUTOPILOT [B]" : "  |  [B] autopilot") +
                  (_remote != null ? $"  |  room {_remote.Room.Code}" : "");
            _statusText.text = status;
        }

        private void RenderBanner()
        {
            string banner;
            if (View.Stage == GameStage.Finished)
            {
                banner = "Thanks for playing!";
            }
            else if (View.PendingRollValue is int roll)
            {
                banner = $"SALE ROLL: {roll}";
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
                banner = $"Your turn — {View.Phase} ({View.ActionsRemaining} actions left)";
            }
            else
            {
                banner = $"{NameOf(View.ActivePlayer)}'s turn — {View.Phase}";
            }
            _table.SetBanner(banner);
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
                var captured = move;
                var button = UiKit.CreateButton(_table.ActionBar,
                    _session.LabelFor(captured), 15, () => Submit(captured));
                button.GetComponent<LayoutElement>().minWidth = 150;
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
            if ((_prompt.IsOpen || _picker.IsOpen) && signature == _modalSignature)
            {
                _modalRevision = revision;
                return;
            }
            if (_modalRevision == revision && (_prompt.IsOpen || _picker.IsOpen))
            {
                return;
            }
            _modalRevision = revision;
            _modalSignature = signature;
            _autoModalOpen = true;
            if (TryShowPicker())
            {
                return;
            }
            _prompt.Show(ModalTitle(), ModalCards(), ToOptions(groups.ModalMoves), showCancel: false);
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
                    }));
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
                    title = $"Discard {required} card(s)";
                    accept = ids => Submit(new SubmitAbilityChoice { CardInstanceIds = ids });
                    break;

                case DecisionKind.AbilityPickCard:
                    pool = View.RevealedHand ?? new List<PlayerView.CardInfo>();
                    required = 1;
                    title = $"Pick a card from {NameOf(decision.ChosenPlayerId ?? 0)}'s hand";
                    accept = ids => Submit(new SubmitAbilityChoice { CardInstanceIds = ids });
                    break;

                case DecisionKind.AbilityGiveBack:
                    pool = View.Hand.Where(c => c.InstanceId != decision.StolenCardId).ToList();
                    required = 1;
                    title = "Give back a different card";
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
                switch (decision.Kind)
                {
                    case DecisionKind.TimeoutFine: return "Timeout! Pay your tantrum fine";
                    case DecisionKind.AttackRetarget: return "Your attack was redirected — pick a new target";
                    case DecisionKind.FreePlayOffer: return "Smear Campaign: free play?";
                    case DecisionKind.ForcedPlay: return "Reverse Engineer: play the recovered card";
                    case DecisionKind.BouncerAttack: return "Bouncer: strike back?";
                    case DecisionKind.AbilityVictim: return "Choose who to rob";
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
                string what = top.IsPurchase
                    ? $"{NameOf(top.OwnerId)} is buying {cardName}"
                    : $"{NameOf(top.OwnerId)} played {cardName}" +
                      (top.AttackTargetId is int t ? $" at {NameOf(t)}" : "");
                return $"{what} — respond?";
            }
            if (View.PendingRollValue is int roll)
            {
                return $"The die shows {roll} — respond?";
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
            if (View.StackTop != null)
            {
                var top = View.StackTop;
                cards.Add(top.IsPurchase
                    ? _art.BlackMarket(top.DefId, top.Shape ?? Shape.Square)
                    : _art.Lemon(top.DefId));
            }
            return cards;
        }
    }
}
