using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Playable debug build: you (seat 0) vs three GreedyBots, rendered with a code-built
    /// HUD. No scene setup required — this spawns itself on Play in any scene.
    ///
    /// Keys: [B] toggle bot autoplay for your seat, [N] new game.
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

        // HUD containers rebuilt every action
        private Text _statusText;
        private Text _boardText;
        private Text _logText;
        private RectTransform _marketRow;
        private RectTransform _handRow;
        private RectTransform _moveList;
        private int _renderedRevision = -1;

        private void Start()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "game-data");
            _db = CardDatabase.Load(dataDir);
            _art = new CardArt(Application.streamingAssetsPath);
            BuildHud();
            NewGame();
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
            Log($"New game (seed {seed}). You are {Names[HumanSeat]}.");
            _renderedRevision = -1;
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
                Log(_humanAutoplay ? "Autopilot ON for your seat." : "Autopilot off.");
                _renderedRevision = -1;
            }

            if (_game.State.Stage != GameStage.Finished)
            {
                var acting = _game.ActingPlayers();
                if (acting.Count > 0)
                {
                    int actor = acting[0];
                    bool isBot = actor != HumanSeat || _humanAutoplay;
                    if (isBot && Time.time >= _nextBotStep)
                    {
                        _nextBotStep = Time.time + BotStepSeconds;
                        var bot = actor == HumanSeat ? _autopilot : (GreedyBot)_bots[actor];
                        ApplyAction(bot.Choose(_game, actor));
                    }
                }
            }

            RenderIfChanged();
        }

        private void ApplyAction(GameAction action)
        {
            foreach (var gameEvent in _game.Apply(action))
            {
                Log(gameEvent.ToString());
            }
            _renderedRevision = -1;
        }

        private void Log(string line)
        {
            _log.Add(line);
            if (_log.Count > 16)
            {
                _log.RemoveAt(0);
            }
        }

        // -------------------------------------------------------------- HUD

        private void BuildHud()
        {
            var canvas = UiKit.CreateCanvas();
            var root = (RectTransform)canvas.transform;

            // Status bar across the top.
            var statusPanel = UiKit.CreatePanel(root, "Status", UiKit.PanelColor);
            UiKit.Anchor(statusPanel, new Vector2(0, 0.94f), new Vector2(1, 1));
            _statusText = UiKit.CreateText(statusPanel, "", 24, TextAnchor.MiddleLeft);
            UiKit.Anchor((RectTransform)_statusText.transform, Vector2.zero, Vector2.one,
                new Vector2(14, 0), new Vector2(-14, 0));

            // Left: all players' boards.
            var boardPanel = UiKit.CreatePanel(root, "Boards", UiKit.PanelColor);
            UiKit.Anchor(boardPanel, new Vector2(0, 0.30f), new Vector2(0.36f, 0.94f),
                new Vector2(8, 4), new Vector2(-4, -6));
            _boardText = UiKit.CreateText(boardPanel, "", 19);
            UiKit.Anchor((RectTransform)_boardText.transform, Vector2.zero, Vector2.one,
                new Vector2(12, 8), new Vector2(-12, -8));

            // Left-bottom: event log.
            var logPanel = UiKit.CreatePanel(root, "Log", UiKit.PanelColor);
            UiKit.Anchor(logPanel, new Vector2(0, 0), new Vector2(0.36f, 0.30f),
                new Vector2(8, 8), new Vector2(-4, -4));
            _logText = UiKit.CreateText(logPanel, "", 16, TextAnchor.LowerLeft,
                new Color(0.8f, 0.9f, 0.8f));
            UiKit.Anchor((RectTransform)_logText.transform, Vector2.zero, Vector2.one,
                new Vector2(12, 8), new Vector2(-12, -8));

            // Center-top: market row.
            var marketPanel = UiKit.CreatePanel(root, "Market", UiKit.PanelColor);
            UiKit.Anchor(marketPanel, new Vector2(0.36f, 0.62f), new Vector2(0.72f, 0.94f),
                new Vector2(4, 4), new Vector2(-4, -6));
            _marketRow = UiKit.CreateCardRow(marketPanel, "MarketRow");

            // Center-bottom: your hand.
            var handPanel = UiKit.CreatePanel(root, "Hand", UiKit.PanelColor);
            UiKit.Anchor(handPanel, new Vector2(0.36f, 0), new Vector2(0.72f, 0.62f),
                new Vector2(4, 8), new Vector2(-4, -4));
            _handRow = UiKit.CreateCardRow(handPanel, "HandRow");

            // Right: legal move buttons.
            var movesPanel = UiKit.CreatePanel(root, "Moves", UiKit.PanelColor);
            UiKit.Anchor(movesPanel, new Vector2(0.72f, 0), new Vector2(1, 0.94f),
                new Vector2(4, 8), new Vector2(-8, -6));
            _moveList = UiKit.CreateScrollList(movesPanel);
        }

        private void RenderIfChanged()
        {
            int revision = _game.State.InteractionRevision * 100003 +
                           _game.State.LemonDeck.Count * 101 +
                           _game.State.PendingDecisions.Count * 7 +
                           (int)_game.State.Stage;
            if (revision == _renderedRevision)
            {
                return;
            }
            _renderedRevision = revision;

            RenderStatus();
            RenderBoards();
            RenderMarket();
            RenderHand();
            RenderMoves();
            _logText.text = string.Join("\n", _log);
        }

        private void RenderStatus()
        {
            var s = _game.State;
            var text = new StringBuilder();
            if (s.Stage == GameStage.Finished)
            {
                text.Append("GAME OVER — winner: ")
                    .Append(string.Join(", ", s.Winners.Select(w => Names[w])))
                    .Append("   [N] new game");
            }
            else
            {
                text.Append($"{s.Stage}")
                    .Append(s.Stage == GameStage.Playing || s.Stage == GameStage.FinalRound
                        ? $" | {Names[s.ActivePlayer]}'s turn ({s.Phase}, {s.ActionsRemaining} actions)"
                        : "");
                if (s.PendingRoll != null)
                {
                    text.Append($" | ROLL: {s.PendingRoll.Value}");
                }
                text.Append($" | Bragging Rights: ")
                    .Append(s.BraggingRightsSold < _db.Supporting.BraggingRightsPrices.Count
                        ? $"${_db.Supporting.BraggingRightsPrices[s.BraggingRightsSold]}"
                        : "sold out");
                text.Append(_humanAutoplay ? " | AUTOPILOT [B]" : " | [B] autopilot  [N] new game");
            }
            _statusText.text = text.ToString();
        }

        private void RenderBoards()
        {
            var s = _game.State;
            var text = new StringBuilder();
            foreach (var p in s.Players)
            {
                text.Append(p.PlayerId == s.ActivePlayer ? "> " : "  ")
                    .Append($"{p.Name}: ${p.Money}, {p.Hand.Count} cards, {p.InGameVictoryPoints} VP");
                if (s.WhiniestBabyHolder == p.PlayerId)
                {
                    text.Append(" [BABY]");
                }
                if (s.SpoiledRottenHolder == p.PlayerId)
                {
                    text.Append(" [SPOILED]");
                }
                if (p.TantrumPile.Count > 0)
                {
                    text.Append($" [{p.TantrumPile.Count} tantrums]");
                }
                text.AppendLine();
                text.Append("    Turf ")
                    .Append(string.Join(",", _game.PourNumbersOf(p)))
                    .Append(p.Turf.Equipped.Count > 0
                        ? ": " + string.Join(", ", p.Turf.Equipped.Select(id =>
                            _db.BlackMarket(s.BlackMarketInstances[id].DefId).Name))
                        : "");
                text.AppendLine();
                foreach (var stand in p.Stands)
                {
                    var type = _db.StandType(stand.StandTypeId);
                    text.Append($"    {type.Name} [{string.Join(",", _game.SaleNumbersOf(stand))}] ")
                        .Append($"${_game.StandEarnings(p, stand)}");
                    if (stand.Equipped.Count > 0)
                    {
                        text.Append(" + ").Append(string.Join(", ", stand.Equipped.Select(id =>
                            _db.BlackMarket(s.BlackMarketInstances[id].DefId).Name)));
                    }
                    text.AppendLine();
                }
                text.AppendLine();
            }
            _boardText.text = text.ToString();
        }

        private void RenderMarket()
        {
            UiKit.Clear(_marketRow);
            UiKit.CreateText(_marketRow, "MARKET", 18, TextAnchor.MiddleCenter)
                .GetComponent<RectTransform>().sizeDelta = new Vector2(40, 100);
            foreach (int id in _game.State.Market)
            {
                var instance = _game.State.BlackMarketInstances[id];
                UiKit.CreateCardImage(_marketRow, _art.BlackMarket(instance.DefId, instance.Shape), 150, 210);
            }
        }

        private void RenderHand()
        {
            UiKit.Clear(_handRow);
            var hand = _game.State.Players[HumanSeat].Hand;
            foreach (int id in hand.Take(9))
            {
                UiKit.CreateCardImage(_handRow, _art.Lemon(_game.State.LemonInstances[id].DefId), 150, 210);
            }
            if (hand.Count > 9)
            {
                UiKit.CreateText(_handRow, $"+{hand.Count - 9}", 26, TextAnchor.MiddleCenter)
                    .GetComponent<RectTransform>().sizeDelta = new Vector2(60, 100);
            }
        }

        private void RenderMoves()
        {
            UiKit.Clear(_moveList);
            if (_game.State.Stage == GameStage.Finished)
            {
                return;
            }

            var acting = _game.ActingPlayers();
            if (!acting.Contains(HumanSeat) || _humanAutoplay)
            {
                UiKit.CreateText(_moveList, acting.Count > 0
                    ? $"Waiting on {string.Join(", ", acting.Select(a => Names[a]))}..."
                    : "...", 18);
                return;
            }

            var moves = _game.LegalMovesFor(HumanSeat);
            UiKit.CreateText(_moveList, $"YOUR MOVE ({moves.Count} options)", 18);
            foreach (var move in moves
                .OrderBy(m => m is EndTurn || m is PassWindow ? 1 : 0)
                .ThenBy(m => m.GetType().Name)
                .Take(120))
            {
                var captured = move;
                UiKit.CreateButton(_moveList, MoveDescriber.Describe(_game, captured), 15,
                    () => ApplyAction(captured));
            }
        }
    }
}
