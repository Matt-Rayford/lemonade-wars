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
                cam.GetComponent<Camera>().backgroundColor = new Color(0.93f, 0.85f, 0.25f);
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
        private string _pendingVerb; // create:<name> or join:<name>:<code>, sent once connected

        private LobbyUi _lobby;
        private TableView _table;
        private Prompt _prompt;
        private CardPicker _picker;
        private CardPreview _preview;
        private Text _statusText;
        private int _renderedRevision = -1;
        private int _modalRevision = -1;

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

            var statusPanel = UiKit.CreatePanel(root, "Status", UiKit.PanelColor);
            UiKit.Anchor(statusPanel, new Vector2(0, 0.95f), new Vector2(1, 1));
            _statusText = UiKit.CreateText(statusPanel, "", 22, TextAnchor.MiddleLeft);
            UiKit.Anchor((RectTransform)_statusText.transform, Vector2.zero, Vector2.one,
                new Vector2(14, 0), new Vector2(-14, 0));

            _preview = new CardPreview(root);
            _table = new TableView(root, _art, _preview);
            _table.OnHandCard = OpenHandMenu;
            _table.CanBuyMarket = i => CurrentGroups()?.MarketMoves.ContainsKey(i) == true;
            _table.OnMarketDragStart = OnMarketDragStart;
            _table.OnMarketDragEnd = () => _table.ClearDropHighlights();
            _table.OnMarketDrop = OnMarketDrop;
            _table.CanBuySupply = typeId => CurrentGroups()?.SupplyMoves.ContainsKey(typeId) == true;
            _table.OnSupplyDrop = OnSupplyDrop;
            _table.SetVisible(false);

            _lobby = new LobbyUi(root);
            _lobby.OnPlayLocal = StartLocalGame;
            _lobby.OnHost = (url, name) => ConnectRemote(url, $"create:{name}");
            _lobby.OnJoin = (url, name, code) => ConnectRemote(url, $"join:{name}:{code}");
            _lobby.OnAddBot = () => _remote?.AddBot();
            _lobby.OnStart = () => _remote?.StartGame();
            _lobby.OnReadyToggle = ready => _remote?.SetReady(ready);
            _lobby.OnLeave = () => BackToMenu("");

            // Built last: overlays render on top.
            _prompt = new Prompt(root, this);
            _picker = new CardPicker(root, _preview, this);
        }

        // ------------------------------------------------------------ flows

        private void StartLocalGame()
        {
            _remote?.Dispose();
            _remote = null;
            _session?.Dispose();
            _session = new LocalGameSession(_db,
                new[] { "You", "Benny", "Cleo", "Dex" }, 0,
                (ulong)System.DateTime.Now.Ticks);
            EnterGame();
        }

        private void ConnectRemote(string url, string verb)
        {
            _session?.Dispose();
            _remote = RemoteGameSession.Connect(url);
            _session = _remote;
            _pendingVerb = verb;
            _screen = Screen.Lobby;
            _lobbyRevision = -1;
            _lobby.ShowLobby(_remote.Room, "Connecting to " + url + "...", false);
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
        }

        private void EnterGame()
        {
            _screen = Screen.Game;
            _lobby.HideAll();
            _table.SetVisible(true);
            _prompt.Hide();
            _picker.Hide();
            _renderedRevision = -1;
            _modalRevision = -1;
        }

        private void Update()
        {
            // Deferred lobby verb once the socket is open.
            if (_remote != null && _pendingVerb != null)
            {
                if (_remote.Connected)
                {
                    var parts = _pendingVerb.Split(new[] { ':' }, 3);
                    if (parts[0] == "create")
                    {
                        _remote.CreateRoom(parts[1]);
                    }
                    else
                    {
                        _remote.JoinRoom(parts[2], parts[1]);
                    }
                    _pendingVerb = null;
                }
                else if (_remote.ConnectionError.Length > 0)
                {
                    _pendingVerb = null;
                    BackToMenu(_remote.ConnectionError);
                }
            }

            _session?.Tick();
            TickLobby();

            if (_screen != Screen.Game)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.N) && _session is LocalGameSession)
            {
                StartLocalGame();
            }
            if (Input.GetKeyDown(KeyCode.B) && _session != null)
            {
                _session.HumanAutoplay = !_session.HumanAutoplay;
                _prompt.Hide();
                _picker.Hide();
                _renderedRevision = -1;
                _modalRevision = -1;
            }

            _table.TickSupplyDrag(Input.mousePosition);
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
            _lobby.ShowMenu(status);
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
            _prompt.Show(_db.Lemon(card?.DefId ?? "").Name,
                new[] { _art.Lemon(card?.DefId ?? "") },
                ToOptions(moves), showCancel: true);
        }

        private void OnMarketDragStart(int marketIndex)
        {
            _preview.Hide();
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
            RenderStatus();
            RenderBanner();
            _table.Render(View, _db, groups);
            _table.SetLog(_session.Log);
            RenderActionBar(groups);
            MaybeShowModal(groups, revision);
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
            else
            {
                banner = $"{NameOf(View.ActivePlayer)}'s turn — {View.Phase}" +
                         (View.ActivePlayer == View.ViewerId
                             ? $" ({View.ActionsRemaining} actions left)"
                             : "");
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
            if (_modalRevision == revision && (_prompt.IsOpen || _picker.IsOpen))
            {
                return;
            }
            _modalRevision = revision;
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
                case DecisionKind.WhiniestBabyDiscard:
                    pool = View.Hand.ToList();
                    required = decision.RequiredCount;
                    title = decision.Kind == DecisionKind.DiscardToHandLimit
                        ? $"Timeout! Discard {required} card(s)"
                        : "Whiniest Baby: discard 1 card";
                    accept = ids => Submit(new SubmitDiscard { InstanceIds = ids });
                    break;

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
