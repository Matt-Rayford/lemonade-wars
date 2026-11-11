using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>Builds every screen layer once at startup. Build order is draw order:
    /// each layer sits above the ones built before it.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private void BuildHud()
        {
            var canvas = UiKit.CreateCanvas();
            var root = (RectTransform)canvas.transform;

            _preview = new CardPreview(root);
            _table = new TableView(root, _art, _preview);
            BuildTableMessageStrip(root);
            WireTableCallbacks();
            _table.SetVisible(false);

            BuildLobby(root);
            BuildPlayTheatre(root);
            BuildEffectsLayer(root);
            BuildBlockingOverlays(root);
            BuildTurnAlertToast(root);
            BuildTurnActionRow(root);

            // Response windows use the non-modal reaction panel: card in the market
            // band, buttons in the log column, the table left live for scouting.
            _reaction = new ReactionPanel(root, _preview);

            BuildTopmostMenus(root);
        }

        /// <summary>The table's one line of words, in the clear strip between shelf and
        /// board; built after the table so it sits over the wallpaper.</summary>
        private void BuildTableMessageStrip(RectTransform root)
        {
            _tableMessage = UiKit.CreateText(root, "", 32, TextAnchor.MiddleCenter, UiKit.ButtonColor);
            _tableMessage.raycastTarget = false;
            UiKit.AddTextShadow(_tableMessage);
            var messageRect = (RectTransform)_tableMessage.transform;
            UiKit.Anchor(messageRect, new Vector2(0.21f, MessageStripBottom), new Vector2(0.79f, MessageStripTop));

            // The strip doubles as the home of the big End Turn button; the words step
            // aside while it is there. Anchored off the message, so nudging one nudges both.
            var tableActionsGo = new GameObject("TableActions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            tableActionsGo.transform.SetParent(root, false);
            _tableActionHost = (RectTransform)tableActionsGo.transform;
            UiKit.Anchor(_tableActionHost, messageRect.anchorMin, messageRect.anchorMax);
            var tableActionsLayout = tableActionsGo.GetComponent<HorizontalLayoutGroup>();
            tableActionsLayout.childAlignment = TextAnchor.MiddleCenter;
            tableActionsLayout.spacing = 12f;
            tableActionsLayout.childForceExpandWidth = false;
            tableActionsLayout.childForceExpandHeight = false;
            tableActionsLayout.childControlWidth = true;
            tableActionsLayout.childControlHeight = true;
        }

        private void WireTableCallbacks()
        {
            _table.BotLevelLookup = id => _session?.BotLevelOf(id);
            _table.UnshownMoney = id => _earningsInFlight.TryGetValue(id, out int amount) ? amount : 0;
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
            _table.OnBraggingDrop = BuyBraggingRights;
            _table.OnMarketDoubleClick = OnMarketDoubleClick;
            _table.OnSupplyDoubleClick = OnSupplyDoubleClick;
            _table.OnBraggingDoubleClick = OnBraggingDoubleClick;
            _table.OnBoardViewChanged = () => _renderedRevision = -1;
            _table.MarketCardHidden = instanceId => _hiddenMarketIds.Contains(instanceId);
            _table.HandCardInvisible = instanceId => _handCardsInFlight.Contains(instanceId);
            _table.EquippedCardInvisible = instanceId => _equippedInFlight.Contains(instanceId);
            _table.MarketStandIn = instanceId =>
                _marketStandIns.TryGetValue(instanceId, out var art) ? art : null;
        }

        private void BuildLobby(RectTransform root)
        {
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
        }

        /// <summary>Staged plays sit under the reveal scrim (ordered in
        /// <see cref="BuildEffectsLayer"/>), so a reveal still covers them.</summary>
        private void BuildPlayTheatre(RectTransform root)
        {
            _playTheatre = new PlayTheatre(root,
                playerId => _table.PlayerBarWorld(playerId),
                playerId => _table.PlayerAvatarWorld(playerId),
                playerId => _table.PurchaseShowcaseWorld(playerId),
                (playerId, on) => _table.SetVictimGlow(playerId, on),
                _preview,
                UiKit.LoadTextureSharp(Path.Combine(Application.streamingAssetsPath, "icons", "steal-blank.png")),
                NameOf);
            _playTheatre.OnIdle = ReopenHeldModals;
        }

        /// <summary>Above the table, below the modal overlays built after it.</summary>
        private void BuildEffectsLayer(RectTransform root)
        {
            // Queued reveals wait for a staged attack's arrival beat, not for its reaction window.
            _fx = new EffectsPlayer(root,
                () => _dice.IsBusy || _turnBanner.IsOpen || _playTheatre.IsBusy,
                playerId => _table.PlayerBarWorld(playerId),
                playerId => _table.PlayerCashWorld(playerId),
                buyerId => _table.PurchaseShowcaseWorld(buyerId));
            _fx.OnFinished = ReopenHeldModals;
            // A contested purchase's bills land at the showcase under its staged card.
            _playTheatre.DrawAbove(_fx.Layer);
        }

        private void BuildBlockingOverlays(RectTransform root)
        {
            _prompt = new Prompt(root, this, _preview);
            _picker = new CardPicker(root, _preview, this);
            // The die may tumble across the message strip but never rests on it.
            _dice = new DiceRoller(root, landingCeilingViewportY: MessageStripBottom);
            _dice.OnFinished = ReopenHeldModals;
            _turnBanner = new TurnBanner(root, this);
            _turnBanner.OnDismiss = ReopenHeldModals;
        }

        /// <summary>Forces a re-render so a modal held back behind theatre, a roll or the
        /// turn banner opens now that it has finished.</summary>
        private void ReopenHeldModals()
        {
            _renderedRevision = -1;
            _modalRevision = -1;
        }

        /// <summary>Built after every game layer, so it floats over every screen.</summary>
        private void BuildTurnAlertToast(RectTransform root)
        {
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
        }

        /// <summary>Turn actions live top-right, on the strip of bare table between the
        /// shelf and the board.</summary>
        private void BuildTurnActionRow(RectTransform root)
        {
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
        }

        /// <summary>Built last: the pause menu and rulebook draw above every screen.</summary>
        private void BuildTopmostMenus(RectTransform root)
        {
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
    }
}
