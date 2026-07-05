using System.Collections.Generic;
using System.IO;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The playable table: you (seat 0) vs three GreedyBots. Click cards to act — hand
    /// cards to play them, market cards to buy, supply piles for stands; windows and
    /// decisions appear as modal prompts. Spawns itself on Play in any scene.
    ///
    /// Keys: [B] autopilot for your seat, [N] new game.
    /// </summary>
    public sealed class LemonadeWarsApp : MonoBehaviour
    {
        private const int HumanSeat = 0;
        private const float BotStepSeconds = 0.35f;
        private static readonly string[] Names = { "You", "Benny", "Cleo", "Dex" };

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

        private CardDatabase _db;
        private CardArt _art;
        private Game _game;
        private Dictionary<int, IBot> _bots;
        private GreedyBot _autopilot;
        private bool _humanAutoplay;
        private float _nextBotStep;
        private readonly List<string> _log = new List<string>();

        private TableView _table;
        private Prompt _prompt;
        private CardPicker _picker;
        private CardPreview _preview;
        private Text _statusText;
        private int _renderedRevision = -1;
        private int _modalRevision = -1;

        private void Start()
        {
            _db = CardDatabase.Load(Path.Combine(Application.streamingAssetsPath, "game-data"));
            _art = new CardArt(Application.streamingAssetsPath);
            BuildHud();
            NewGame();
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
            _table.CanBuyMarket = i =>
                !_humanAutoplay && MoveGroups.For(_game, HumanSeat).MarketMoves.ContainsKey(i);
            _table.OnMarketDragStart = OnMarketDragStart;
            _table.OnMarketDragEnd = () => _table.ClearDropHighlights();
            _table.OnMarketDrop = OnMarketDrop;
            _table.CanBuySupply = typeId =>
                !_humanAutoplay && MoveGroups.For(_game, HumanSeat).SupplyMoves.ContainsKey(typeId);
            _table.OnSupplyDrop = OnSupplyDrop;
            // Built last: overlays render on top of the table.
            _prompt = new Prompt(root, _art);
            _picker = new CardPicker(root, _preview, this);
        }

        private void NewGame()
        {
            ulong seed = (ulong)System.DateTime.Now.Ticks;
            _game = Game.Create(_db, Names, seed);
            _bots = new Dictionary<int, IBot>();
            for (int i = 0; i < Names.Length; i++)
            {
                if (i != HumanSeat)
                {
                    _bots[i] = new GreedyBot();
                }
            }
            _autopilot = new GreedyBot();
            _log.Clear();
            Log($"New game — you're up against {Names[1]}, {Names[2]} and {Names[3]}.");
            _prompt?.Hide();
            _picker?.Hide();
            _renderedRevision = -1;
            _modalRevision = -1;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.N))
            {
                NewGame();
            }
            if (Input.GetKeyDown(KeyCode.B))
            {
                _humanAutoplay = !_humanAutoplay;
                _prompt.Hide();
                _picker.Hide();
                Log(_humanAutoplay ? "Autopilot ON." : "Autopilot off.");
                _renderedRevision = -1;
                _modalRevision = -1;
            }

            _table?.TickSupplyDrag(Input.mousePosition);
            StepBots();
            RenderIfChanged();
        }

        private void StepBots()
        {
            if (_game.State.Stage == GameStage.Finished || Time.time < _nextBotStep)
            {
                return;
            }
            var acting = _game.ActingPlayers();
            // Windows can await several players at once: step the first bot among them,
            // regardless of whether the human is also being waited on.
            foreach (int actor in acting)
            {
                bool isBot = actor != HumanSeat || _humanAutoplay;
                if (!isBot)
                {
                    continue;
                }
                _nextBotStep = Time.time + BotStepSeconds;
                var bot = actor == HumanSeat ? _autopilot : (GreedyBot)_bots[actor];
                ApplyAction(bot.Choose(_game, actor));
                return;
            }
        }

        private void ApplyAction(GameAction action)
        {
            _prompt.Hide();
            _picker.Hide();
            foreach (var gameEvent in _game.Apply(action))
            {
                Log(gameEvent.ToString());
            }
            _renderedRevision = -1;
        }

        private void Log(string line)
        {
            _log.Add(line);
            if (_log.Count > 18)
            {
                _log.RemoveAt(0);
            }
        }

        // ---------------------------------------------------------- contextual menus

        private void OpenHandMenu(int cardInstanceId)
        {
            var groups = MoveGroups.For(_game, HumanSeat);
            if (!groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return;
            }
            string defId = _game.State.LemonInstances[cardInstanceId].DefId;
            _prompt.Show(_db.Lemon(defId).Name,
                new[] { _art.Lemon(defId) },
                ToOptions(moves), showCancel: true);
        }

        private void OnMarketDragStart(int marketIndex)
        {
            _preview.Hide();
            if (!MoveGroups.For(_game, HumanSeat).MarketMoves.TryGetValue(marketIndex, out var moves))
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
            if (!MoveGroups.For(_game, HumanSeat).MarketMoves.TryGetValue(marketIndex, out var moves))
            {
                return;
            }
            var matching = moves.OfType<BuyBlackMarket>()
                .Where(m => m.TargetStandInstanceId == standInstanceId)
                .Cast<GameAction>()
                .ToList();
            if (matching.Count == 1)
            {
                ApplyAction(matching[0]);
            }
            else if (matching.Count > 1)
            {
                // The slot is full: the buyer picks which equipped card to replace.
                var instance = _game.State.BlackMarketInstances[_game.State.Market[marketIndex]];
                _prompt.Show("That slot is full — replace which card?",
                    new[] { _art.BlackMarket(instance.DefId, instance.Shape) },
                    ToOptions(matching), showCancel: true);
            }
        }

        private void OnSupplyDrop(string standTypeId, int insertIndex)
        {
            if (!MoveGroups.For(_game, HumanSeat).SupplyMoves.TryGetValue(standTypeId, out var moves))
            {
                return;
            }
            // Match the previewed row position; fall back to the rightmost slot.
            var pick = moves.FirstOrDefault(m =>
                    (m as BuyStand)?.InsertIndex == insertIndex ||
                    (m as InitialBuyStand)?.InsertIndex == insertIndex)
                ?? moves[moves.Count - 1];
            ApplyAction(pick);
        }

        private List<Prompt.Option> ToOptions(IEnumerable<GameAction> moves)
        {
            return moves
                .Select(m => new Prompt.Option(MoveDescriber.Describe(_game, m), () => ApplyAction(m)))
                .ToList();
        }

        // ------------------------------------------------------------------- render

        private void RenderIfChanged()
        {
            var s = _game.State;
            int revision = s.InteractionRevision * 100003 + s.LemonDeck.Count * 101 +
                           s.PendingDecisions.Count * 7 + (int)s.Stage + s.ActionsRemaining * 13 +
                           _log.Count * 3;
            if (revision == _renderedRevision)
            {
                return;
            }
            _renderedRevision = revision;

            var groups = _humanAutoplay ? null : MoveGroups.For(_game, HumanSeat);
            RenderStatus();
            RenderBanner();
            _table.Render(_game, HumanSeat, groups);
            _table.SetLog(_log);
            RenderActionBar(groups);
            MaybeShowModal(groups, revision);
        }

        private void RenderStatus()
        {
            var s = _game.State;
            var me = s.Players[HumanSeat];
            string status = s.Stage == GameStage.Finished
                ? $"GAME OVER — winner: {string.Join(", ", s.Winners.Select(w => Names[w]))}   [N] new game"
                : $"You: ${me.Money}  |  {me.InGameVictoryPoints} VP" +
                  (s.WhiniestBabyHolder == HumanSeat ? "  |  WHINIEST BABY" : "") +
                  (s.SpoiledRottenHolder == HumanSeat ? "  |  SPOILED ROTTEN" : "") +
                  (me.TantrumPile.Count > 0 ? $"  |  {me.TantrumPile.Count} tantrums" : "") +
                  (_humanAutoplay ? "  |  AUTOPILOT [B]" : "  |  [B] autopilot  [N] new game");
            _statusText.text = status;
        }

        private void RenderBanner()
        {
            var s = _game.State;
            string banner;
            if (s.Stage == GameStage.Finished)
            {
                banner = "Thanks for playing!";
            }
            else if (s.PendingRoll != null)
            {
                banner = $"SALE ROLL: {s.PendingRoll.Value}";
            }
            else if (s.Stage == GameStage.ChoosingLemonLords)
            {
                banner = "Choose your secret Lemon Lord titles";
            }
            else if (s.Stage == GameStage.InitialBuys)
            {
                int buyer = s.InitialBuyQueue.Count > 0 ? s.InitialBuyQueue[0] : 0;
                banner = $"Setup draft — {Names[buyer]} is buying";
            }
            else
            {
                banner = $"{Names[s.ActivePlayer]}'s turn — {s.Phase}" +
                         (s.ActivePlayer == HumanSeat ? $" ({s.ActionsRemaining} actions left)" : "");
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
                    MoveDescriber.Describe(_game, captured), 15, () => ApplyAction(captured));
                button.GetComponent<LayoutElement>().minWidth = 150;
            }
        }

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
            var s = _game.State;

            if (s.Stage == GameStage.ChoosingLemonLords)
            {
                var dealt = s.Players[HumanSeat].LemonLordDealt.ToList();
                _picker.Show($"Keep {_db.Config.LemonLordKept} secret Lemon Lord titles",
                    dealt.Select(id => _art.Title(id)).ToList(),
                    _db.Config.LemonLordKept,
                    picked => ApplyAction(new ChooseLemonLords
                    {
                        PlayerId = HumanSeat,
                        KeepTitleIds = picked.Select(i => dealt[i]).ToList(),
                    }));
                return true;
            }

            var decision = s.PendingDecisions.FirstOrDefault(d => d.PlayerId == HumanSeat);
            if (decision == null)
            {
                return false;
            }

            List<int> pool;
            int required;
            string title;
            System.Action<List<int>> accept;
            switch (decision.Kind)
            {
                case DecisionKind.DiscardToHandLimit:
                case DecisionKind.WhiniestBabyDiscard:
                    pool = s.Players[HumanSeat].Hand.ToList();
                    required = decision.RequiredCount;
                    title = decision.Kind == DecisionKind.DiscardToHandLimit
                        ? $"Timeout! Discard {required} card(s)"
                        : "Whiniest Baby: discard 1 card";
                    accept = ids => ApplyAction(new SubmitDiscard { PlayerId = HumanSeat, InstanceIds = ids });
                    break;

                case DecisionKind.AbilityDiscard:
                    pool = s.Players[HumanSeat].Hand.ToList();
                    required = decision.RequiredCount;
                    title = $"Discard {required} card(s)";
                    accept = ids => ApplyAction(new SubmitAbilityChoice { PlayerId = HumanSeat, CardInstanceIds = ids });
                    break;

                case DecisionKind.AbilityPickCard:
                    pool = s.Players[decision.ChosenPlayerId ?? 0].Hand.ToList();
                    required = 1;
                    title = $"Pick a card from {Names[decision.ChosenPlayerId ?? 0]}'s hand";
                    accept = ids => ApplyAction(new SubmitAbilityChoice { PlayerId = HumanSeat, CardInstanceIds = ids });
                    break;

                case DecisionKind.AbilityGiveBack:
                    pool = s.Players[HumanSeat].Hand.Where(id => id != decision.StolenCardId).ToList();
                    required = 1;
                    title = "Give back a different card";
                    accept = ids => ApplyAction(new SubmitAbilityChoice { PlayerId = HumanSeat, CardInstanceIds = ids });
                    break;

                default:
                    return false;
            }

            var capturedPool = pool;
            _picker.Show(title,
                capturedPool.Select(id => _art.Lemon(s.LemonInstances[id].DefId)).ToList(),
                required,
                picked => accept(picked.Select(i => capturedPool[i]).ToList()));
            return true;
        }

        private string ModalTitle()
        {
            var s = _game.State;
            if (s.Stage == GameStage.ChoosingLemonLords)
            {
                return "Pick 2 Lemon Lord titles";
            }
            var decision = s.PendingDecisions.FirstOrDefault(d => d.PlayerId == HumanSeat);
            if (decision != null)
            {
                switch (decision.Kind)
                {
                    case DecisionKind.DiscardToHandLimit: return "Timeout! Discard down to 10";
                    case DecisionKind.WhiniestBabyDiscard: return "Whiniest Baby: discard 1";
                    case DecisionKind.TimeoutFine: return "Timeout! Pay your tantrum fine";
                    case DecisionKind.AttackRetarget: return "Your attack was redirected — pick a new target";
                    case DecisionKind.FreePlayOffer: return "Smear Campaign: free play?";
                    case DecisionKind.ForcedPlay: return "Reverse Engineer: play the recovered card";
                    case DecisionKind.BouncerAttack: return "Bouncer: strike back?";
                    case DecisionKind.AbilityVictim: return "Choose who to rob";
                    case DecisionKind.AbilityPickCard: return "Pick a card from their hand";
                    case DecisionKind.AbilityGiveBack: return "Give back a different card";
                    case DecisionKind.AbilityDiscard: return "Discard a card";
                    case DecisionKind.InnovationCopy: return "Innovation: copy which ability?";
                    case DecisionKind.WordOfMouthStand: return "Word of Mouth: which stand sells?";
                    default: return decision.Kind.ToString();
                }
            }
            if (s.ResponseStack.Count > 0)
            {
                var top = s.ResponseStack[s.ResponseStack.Count - 1];
                string what = top.Kind == StackItemKind.BlackMarketPurchase
                    ? $"{Names[top.OwnerId]} is buying {_db.BlackMarket(s.BlackMarketInstances[top.BmInstanceId.Value].DefId).Name}"
                    : $"{Names[top.OwnerId]} played {_db.Lemon(top.LemonDefId).Name}" +
                      (top.AttackTargetId is int t ? $" at {Names[t]}" : "");
                return $"{what} — respond?";
            }
            if (s.PendingRoll != null)
            {
                return $"The die shows {s.PendingRoll.Value} — respond?";
            }
            if (s.TheftQueue.Count > 0)
            {
                return "You were robbed — Profit Share?";
            }
            return "Your choice";
        }

        private List<Texture2D> ModalCards()
        {
            var s = _game.State;
            var cards = new List<Texture2D>();
            if (s.Stage == GameStage.ChoosingLemonLords)
            {
                foreach (string id in s.Players[HumanSeat].LemonLordDealt)
                {
                    cards.Add(_art.Title(id));
                }
                return cards;
            }
            if (s.ResponseStack.Count > 0)
            {
                var top = s.ResponseStack[s.ResponseStack.Count - 1];
                cards.Add(top.Kind == StackItemKind.BlackMarketPurchase
                    ? _art.BlackMarket(s.BlackMarketInstances[top.BmInstanceId.Value].DefId,
                        s.BlackMarketInstances[top.BmInstanceId.Value].Shape)
                    : _art.Lemon(top.LemonDefId));
                return cards;
            }
            var pick = s.PendingDecisions.FirstOrDefault(d =>
                d.PlayerId == HumanSeat && d.Kind == DecisionKind.AbilityPickCard);
            if (pick?.ChosenPlayerId is int victim)
            {
                foreach (int id in s.Players[victim].Hand.Take(8))
                {
                    cards.Add(_art.Lemon(s.LemonInstances[id].DefId));
                }
            }
            return cards;
        }
    }
}
