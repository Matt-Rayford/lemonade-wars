using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The player bars down the left edge: avatar, VP crowns, name, stat row, turn lemon,
    // hourglass, first-player tag, viewed-board rim and the attack-glow ring.
    public sealed partial class TableView
    {
        /// <summary>Player id to a bot seat's level name ("easy", "hard"...); null for humans.</summary>
        public System.Func<int, string> BotLevelLookup;

        /// <summary>True while the table is mid-theatre (effects, die, turn banner). The turn lemon and
        /// the hourglasses hold their last position until it ends, so the turn is seen to finish first.</summary>
        public System.Func<bool> TheatreBusy;
        /// <summary>Dollars the bar must not show yet: earnings still on their way, less any payment the table
        /// has not been shown leaving (so a negative value shows MORE than the player holds).</summary>
        public System.Func<int, int> UnshownMoney;
        private int _shownTurnHolder = -1;
        private List<int> _shownAwaited;

        private RectTransform _playersColumn;
        private Texture2D _lemonIcon;
        private Texture2D _vpIcon;
        private Texture2D _cashIcon;
        private Texture2D _tantrumIcon;
        private Texture2D _hourglassIcon;
        /// <summary>The cards' blank price tag with its pin: every bar wears one, numbered with the player's place in turn order.</summary>
        private Texture2D _pinnedTagIcon;

        private const float AvatarInsetPx = 12f;
        private const float AvatarDiameterPx = 56f;
        private const float TantrumStatColumnPx = 168f;
        private const float TantrumIconSizePx = 36f;

        private const float TurnOrderTagWidthPx = 36f;
        /// <summary>Counter-clockwise, so the tag hangs off its pin with its foot swung outward.</summary>
        private const float TurnOrderTagTiltDegrees = 15f;
        /// <summary>The tag's centre relative to the bar's top-right corner: over the corner, mostly outside it.</summary>
        private static readonly Vector2 TurnOrderTagOffsetPx = new Vector2(-4f, -16f);
        /// <summary>The turn lemon sits this far left of the bar's edge, out from under the tag.</summary>
        private const float TurnLemonTagClearancePx = 16f;
        private const int TurnOrderTagFontSize = 20;
        /// <summary>Thick, like the price lettering on the cards' own tags.</summary>
        private const float TurnOrderTagBorderPx = 2f;
        /// <summary>The navy the card art outlines everything in.</summary>
        private static readonly Color CardInkColor = new Color32(34, 13, 64, 255);

        /// <summary>
        /// Bars run in turn order from the player after you; yours is pinned at the bottom. The lemon marks
        /// whose turn it is; the hourglass marks whoever the table waits on, often someone else mid-window.
        /// </summary>
        private void RenderPlayers(PlayerView view)
        {
            UiKit.Clear(_playersColumn);
            _playerRows.Clear();
            _countdownGlasses.Clear();
            _cashIcons.Clear();
            _avatarBadges.Clear();
            int count = view.Players.Count;
            int viewedBoard = ViewedBoardPlayer >= 0 ? ViewedBoardPlayer : view.ViewerId;
            for (int offset = 1; offset <= count; offset++)
            {
                int playerId = (view.ViewerId + offset) % count;
                bool isMe = playerId == view.ViewerId;
                if (isMe)
                {
                    // A stretchy spacer pushes your own bar to the column's bottom.
                    var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
                    spacer.transform.SetParent(_playersColumn, false);
                    spacer.GetComponent<LayoutElement>().flexibleHeight = 1;
                }
                BuildPlayerRow(view, playerId, isMe, playerId == viewedBoard);
            }

            // Effects read bar anchors in the same frame as this render, before the layout group
            // would place the rows: settle them now, or a floater spawns at the column's centre.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_playersColumn);
        }

        /// <summary>Widgets are added in draw order: each one covers those before it.</summary>
        private void BuildPlayerRow(PlayerView view, int playerId, bool isMe, bool isViewedBoard)
        {
            var player = view.Players[playerId];
            var row = CreatePlayerBar(playerId);
            // The drafter holds the lemon during setup (ActivePlayer is frozen until play starts).
            int turnHolder = ShownTurnHolder(view);
            var awaited = ShownAwaited(view);
            bool wearsTurnOrderTag = _pinnedTagIcon != null;

            if (playerId == turnHolder && _lemonIcon != null)
            {
                AddTurnLemon(row, wearsTurnOrderTag);
            }
            var avatar = AddPlayerAvatar(row, playerId, player.Name);
            _avatarBadges[playerId] = avatar;
            bool isAwaited = awaited.Contains(playerId);
            if (isAwaited && ActionCountdownOf?.Invoke(playerId) != null)
            {
                AddCountdownGlass(avatar, playerId); // on your own avatar too: it is your clock
            }
            else if (isAwaited && !isMe)
            {
                AddWaitingHourglass(avatar);
            }
            AddVictoryCrowns(row, player);
            AddPlayerNameLabel(row, player.Name, playerId, isMe);
            AddPlayerStats(row, view, player, playerId);
            if (isViewedBoard)
            {
                AddViewedBoardRim(row);
            }
            var attackGlow = AddPlayerAttackGlow(row);
            _playerRows.Add((playerId, row, attackGlow));
            ShowBoardOnBarClick(row, playerId, view.ViewerId);
            if (wearsTurnOrderTag)
            {
                PinTurnOrderTag(row, TurnOrderOf(view, playerId)); // last: over everything else on the bar
            }
        }

        private RectTransform CreatePlayerBar(int playerId)
        {
            var row = new GameObject("PlayerRow", typeof(RectTransform), typeof(Image),
                typeof(LayoutElement));
            row.transform.SetParent(_playersColumn, false);
            var background = row.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.color = PlayerPalette.BarColorOf(playerId);
            row.GetComponent<LayoutElement>().minHeight = 82;
            return (RectTransform)row.transform;
        }

        private void AddTurnLemon(RectTransform row, bool wearsTurnOrderTag)
        {
            var lemonGo = new GameObject("TurnLemon", typeof(RectTransform), typeof(RawImage));
            lemonGo.transform.SetParent(row, false);
            var lemonRect = (RectTransform)lemonGo.transform;
            lemonRect.anchorMin = lemonRect.anchorMax = new Vector2(1f, 0.5f);
            lemonRect.pivot = new Vector2(1f, 0.5f);
            lemonRect.sizeDelta = new Vector2(38f, 38f);
            lemonRect.anchoredPosition = new Vector2(
                wearsTurnOrderTag ? -10f - TurnLemonTagClearancePx : -10f, 0);
            var lemonImage = lemonGo.GetComponent<RawImage>();
            lemonImage.texture = _lemonIcon;
            lemonImage.raycastTarget = false;
        }

        private static RectTransform AddPlayerAvatar(RectTransform row, int playerId, string playerName)
        {
            var avatar = CreatePlayerBadge(row, playerId, playerName, AvatarDiameterPx);
            avatar.anchorMin = avatar.anchorMax = new Vector2(0f, 0.5f);
            avatar.pivot = new Vector2(0f, 0.5f);
            avatar.anchoredPosition = new Vector2(AvatarInsetPx, 0);
            return avatar;
        }

        /// <summary>The spinning hourglass of a wait with no clock on it; several players can wait at once.</summary>
        private void AddWaitingHourglass(RectTransform avatar)
        {
            ShadeAvatar(avatar);
            var glassGo = new GameObject("Hourglass", typeof(RectTransform), typeof(RawImage),
                typeof(HourglassMotion));
            glassGo.transform.SetParent(avatar, false);
            var glassRect = (RectTransform)glassGo.transform;
            glassRect.anchorMin = glassRect.anchorMax = new Vector2(0.5f, 0.5f);
            // Half the avatar; HourglassMotion scales from here.
            glassRect.sizeDelta = new Vector2(AvatarDiameterPx / 2f, AvatarDiameterPx / 2f);
            var glass = glassGo.GetComponent<RawImage>();
            glass.texture = _hourglassIcon;
            glass.raycastTarget = false;
        }

        /// <summary>Greys the avatar so the hourglass drawn over it stands out.</summary>
        private static void ShadeAvatar(RectTransform avatar)
        {
            var shadeGo = new GameObject("TurnShade", typeof(RectTransform), typeof(Image));
            shadeGo.transform.SetParent(avatar, false);
            UiKit.Anchor((RectTransform)shadeGo.transform, Vector2.zero, Vector2.one);
            var shade = shadeGo.GetComponent<Image>();
            shade.sprite = UiSprites.Circle;
            shade.type = Image.Type.Simple;
            shade.preserveAspect = true;
            var grey = ModalBackdrop.Tint;
            shade.color = new Color(grey.r, grey.g, grey.b, 0.8f);
            shade.raycastTarget = false;
        }

        /// <summary>A crown per VP. Met Lemon Lords count once revealed (FinishedLords is empty until game
        /// end), so the crowns agree with the final standings.</summary>
        private void AddVictoryCrowns(RectTransform row, PlayerView.PlayerPanel player)
        {
            int crowns = Mathf.Min(
                player.InGameVictoryPoints + player.FinishedLords.Count(lord => lord.Met), 8);
            var avatarCenter = new Vector2(AvatarInsetPx + AvatarDiameterPx / 2f, 0f);
            for (int crown = 0; crown < crowns; crown++)
            {
                float degrees = 160f - crown * 32f;
                float radians = degrees * Mathf.Deg2Rad;
                var crownGo = new GameObject("Crown", typeof(RectTransform), typeof(RawImage));
                crownGo.transform.SetParent(row, false);
                var crownRect = (RectTransform)crownGo.transform;
                crownRect.anchorMin = crownRect.anchorMax = new Vector2(0f, 0.5f);
                crownRect.pivot = new Vector2(0.5f, 0.5f);
                crownRect.sizeDelta = new Vector2(22f, 22f);
                crownRect.anchoredPosition = avatarCenter +
                    new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 37f;
                var crownImage = crownGo.GetComponent<RawImage>();
                crownImage.texture = _vpIcon;
                crownImage.raycastTarget = false;
            }
        }

        private void AddPlayerNameLabel(RectTransform row, string playerName, int playerId, bool isMe)
        {
            string nameLabel = playerName + (isMe ? "  (you)" : "");
            string botLevel = BotLevelLookup?.Invoke(playerId);
            if (!string.IsNullOrEmpty(botLevel))
            {
                string levelColor = BotLevelColorHex(botLevel);
                nameLabel += $"  <size=13><color={levelColor}>{botLevel.ToUpperInvariant()}</color></size>";
            }
            var nameText = UiKit.CreateText(row,
                nameLabel, 19, TextAnchor.LowerLeft, Color.white);
            nameText.raycastTarget = false;
            UiKit.Anchor((RectTransform)nameText.transform, new Vector2(0, 0.56f), new Vector2(1, 1),
                new Vector2(80, 2), new Vector2(-52, -2));
        }

        /// <summary>The same colours as the lobby's difficulty chips.</summary>
        private static string BotLevelColorHex(string botLevel) =>
            botLevel == "wambulance" ? "#8CC7FF"
            : botLevel == "hard" ? "#FF9E73"
            : botLevel == "easy" ? "#9EE59E" : "#D9E0EB";

        /// <summary>Cash, hand size and tantrums under the name, in FIXED columns: a flowing layout let
        /// the icons drift with digit width, so the bars never lined up.</summary>
        private void AddPlayerStats(RectTransform row, PlayerView view, PlayerView.PlayerPanel player,
            int playerId)
        {
            var statsGo = new GameObject("Stats", typeof(RectTransform));
            statsGo.transform.SetParent(row, false);
            var stats = UiKit.Anchor((RectTransform)statsGo.transform,
                new Vector2(0, 0), new Vector2(1, 0.56f), new Vector2(80, 4), new Vector2(-52, -1));
            // Cash still in flight to the bar is not shown yet.
            int shownMoney = player.Money - (UnshownMoney?.Invoke(playerId) ?? 0);
            var cashIcon = AddStatAt(stats, _cashIcon, $"${shownMoney}", 0f, 36f);
            if (cashIcon != null)
            {
                _cashIcons[playerId] = cashIcon; // money floaters rise from the coin itself
            }
            AddStatAt(stats, _art.Back("lemon"), player.HandCount.ToString(), 92f, 26f);
            AddStatAt(stats, _tantrumIcon, player.TantrumCount.ToString(),
                TantrumStatColumnPx, TantrumIconSizePx);
            // Held back while the title-claim animation is still in flight, like the lord fan.
            if (view.WhiniestBabyHolder == playerId && !SuppressWhiniestBaby)
            {
                AddBabyTears(stats);
            }
        }

        /// <summary>The Whiniest Baby's tantrum icon bawls: tears drawn on the icon's exact rect.</summary>
        private static void AddBabyTears(RectTransform stats)
        {
            var tearsGo = new GameObject("BabyTears", typeof(RectTransform), typeof(Image));
            tearsGo.transform.SetParent(stats, false);
            var tearsRect = (RectTransform)tearsGo.transform;
            tearsRect.anchorMin = tearsRect.anchorMax = new Vector2(0f, 0.5f);
            tearsRect.sizeDelta = new Vector2(TantrumIconSizePx, TantrumIconSizePx);
            tearsRect.anchoredPosition = new Vector2(TantrumStatColumnPx + TantrumIconSizePx / 2f, 0f);
            var tearsImage = tearsGo.GetComponent<Image>();
            tearsImage.sprite = UiSprites.BabyTears;
            tearsImage.type = Image.Type.Simple;
            tearsImage.raycastTarget = false;
        }

        private static void AddViewedBoardRim(RectTransform row)
        {
            var borderGo = new GameObject("ViewedBorder", typeof(RectTransform), typeof(Image));
            borderGo.transform.SetParent(row, false);
            UiKit.Anchor((RectTransform)borderGo.transform, Vector2.zero, Vector2.one,
                new Vector2(-2, -2), new Vector2(2, 2));
            var borderImage = borderGo.GetComponent<Image>();
            borderImage.sprite = UiSprites.RoundedOutline;
            borderImage.type = Image.Type.Sliced;
            borderImage.color = UiKit.ButtonColor;
            borderImage.raycastTarget = false;
            // Behind the contents: a rim drawn across the avatar and crowns reads as a scratch on them.
            borderGo.transform.SetAsFirstSibling();
        }

        /// <summary>Shown while an attack is aimed at this player. The ring's rim hugs the bar's edge and
        /// fades outward, leaving the bar itself readable.</summary>
        private static GameObject AddPlayerAttackGlow(RectTransform row)
        {
            var attackGlowGo = new GameObject("AttackGlow", typeof(RectTransform), typeof(Image));
            attackGlowGo.transform.SetParent(row, false);
            attackGlowGo.transform.SetAsFirstSibling();
            float rimInset = UiSprites.GlowRingRimInset;
            UiKit.Anchor((RectTransform)attackGlowGo.transform, Vector2.zero, Vector2.one,
                new Vector2(-rimInset, -rimInset), new Vector2(rimInset, rimInset));
            var attackGlowImage = attackGlowGo.GetComponent<Image>();
            attackGlowImage.sprite = UiSprites.GlowRing;
            attackGlowImage.type = Image.Type.Sliced;
            attackGlowImage.color = AttackGlowColor;
            attackGlowImage.raycastTarget = false;
            attackGlowGo.SetActive(false);
            return attackGlowGo;
        }

        private void ShowBoardOnBarClick(RectTransform row, int playerId, int viewerId)
        {
            UiKit.AddClick(row.gameObject, () =>
            {
                ViewedBoardPlayer = playerId == viewerId ? -1 : playerId;
                OnBoardViewChanged?.Invoke();
            });
        }

        /// <summary>1 for the first player, counting up around the table in the order turns are taken.</summary>
        private static int TurnOrderOf(PlayerView view, int playerId)
        {
            int playerCount = view.Players.Count;
            return (playerId - view.FirstPlayer + playerCount) % playerCount + 1;
        }

        /// <summary>The cards' own price tag pinned over the bar's top-right corner at a tilt, numbered the way
        /// the cards letter their prices.</summary>
        private void PinTurnOrderTag(RectTransform row, int turnOrder)
        {
            var tagGo = new GameObject("TurnOrderTag", typeof(RectTransform), typeof(RawImage));
            tagGo.transform.SetParent(row, false);
            var tag = (RectTransform)tagGo.transform;
            tag.anchorMin = tag.anchorMax = new Vector2(1f, 1f);
            tag.pivot = new Vector2(0.5f, 0.5f);
            tag.sizeDelta = new Vector2(TurnOrderTagWidthPx,
                TurnOrderTagWidthPx * _pinnedTagIcon.height / _pinnedTagIcon.width);
            tag.anchoredPosition = TurnOrderTagOffsetPx;
            tag.localEulerAngles = new Vector3(0f, 0f, TurnOrderTagTiltDegrees);
            var image = tagGo.GetComponent<RawImage>();
            image.texture = _pinnedTagIcon;
            image.raycastTarget = false; // the bar under it still takes the click

            var number = UiKit.CreateOutlinedText(tag, turnOrder.ToString(), TurnOrderTagFontSize,
                Color.white, CardInkColor, TurnOrderTagBorderPx);
            // The paper below the pin, not the whole image: the pin overhangs the top-right,
            // so the paper's own centre sits left of and below the art's.
            UiKit.Anchor(number, new Vector2(0.03f, 0.04f), new Vector2(0.89f, 0.63f));
        }

        /// <summary>One icon-plus-count chip at a fixed column in the stat row. Returns the icon's rect,
        /// null when its texture is missing.</summary>
        private static RectTransform AddStatAt(RectTransform parent, Texture2D icon, string count,
            float columnX, float iconWidth)
        {
            RectTransform iconRect = null;
            if (icon != null)
            {
                var iconGo = new GameObject("StatIcon", typeof(RectTransform), typeof(RawImage));
                iconGo.transform.SetParent(parent, false);
                iconRect = (RectTransform)iconGo.transform;
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.sizeDelta = new Vector2(iconWidth, 36f);
                iconRect.anchoredPosition = new Vector2(columnX, 0f);
                var iconImage = iconGo.GetComponent<RawImage>();
                iconImage.texture = icon;
                iconImage.raycastTarget = false;
            }
            var text = UiKit.CreateText(parent, count, 18, TextAnchor.MiddleLeft,
                new Color(0.82f, 0.84f, 0.88f), body: true);
            text.raycastTarget = false;
            // "$20" must never wrap into "$2 / 0" when the row runs tight.
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = textRect.anchorMax = new Vector2(0f, 0.5f);
            textRect.pivot = new Vector2(0f, 0.5f);
            textRect.sizeDelta = new Vector2(60f, 34f);
            textRect.anchoredPosition = new Vector2(columnX + iconWidth + 5f, 0f);
            return iconRect;
        }

        /// <summary>A player's avatar: a circle in their stable name colour with their initial. The caller
        /// anchors it; the same badge marks their bar and the cards they play.</summary>
        /// <summary>A circle in the player's seat colour carrying their initial.</summary>
        public static RectTransform CreatePlayerBadge(Transform parent, int playerId, string name, float diameter)
        {
            var badgeGo = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(parent, false);
            var badgeRect = (RectTransform)badgeGo.transform;
            badgeRect.sizeDelta = new Vector2(diameter, diameter);
            var badgeImage = badgeGo.GetComponent<Image>();
            badgeImage.sprite = UiSprites.Circle;
            badgeImage.type = Image.Type.Simple;
            badgeImage.preserveAspect = true;
            badgeImage.color = PlayerPalette.ChipColorOf(playerId);
            badgeImage.raycastTarget = false;
            string initial = string.IsNullOrEmpty(name) ? "?" : char.ToUpperInvariant(name[0]).ToString();
            var letter = UiKit.CreateText(badgeGo.transform, initial, Mathf.RoundToInt(diameter * 30f / 56f),
                TextAnchor.MiddleCenter, Color.white);
            letter.raycastTarget = false;
            UiKit.Anchor((RectTransform)letter.transform, Vector2.zero, Vector2.one);
            return badgeRect;
        }

        /// <summary>World-space centre of a player's avatar chip and a point on its rim (for its radius in any space).</summary>
        public (Vector3 Center, Vector3 Rim)? PlayerAvatarWorld(int playerId)
        {
            if (!_avatarBadges.TryGetValue(playerId, out var badge) || badge == null)
            {
                return null;
            }
            var center = badge.TransformPoint(badge.rect.center);
            var rim = badge.TransformPoint(new Vector3(badge.rect.xMax, badge.rect.center.y, 0));
            return (center, rim);
        }

        public Vector3? PlayerCashWorld(int playerId) =>
            _cashIcons.TryGetValue(playerId, out var icon) && icon != null
                ? icon.TransformPoint(icon.rect.center)
                : (Vector3?)null;

        /// <summary>Who wears the hourglass this render: the players the engine waits on, except that the
        /// previous set keeps it while the theatre is still playing, so it moves together with the lemon.</summary>
        private IReadOnlyList<int> ShownAwaited(PlayerView view)
        {
            bool holdBack = TheatreBusy?.Invoke() == true && _shownAwaited != null &&
                            _shownAwaited.All(id => view.Players.Any(p => p.PlayerId == id));
            if (!holdBack)
            {
                _shownAwaited = view.ActingPlayers.ToList();
            }
            return _shownAwaited;
        }

        /// <summary>Whose lemon it is this render: the engine's turn holder, except that the previous holder
        /// keeps it while the theatre is still playing (the effects' OnFinished forces the handover render).</summary>
        private int ShownTurnHolder(PlayerView view)
        {
            int actual = view.CurrentInitialBuyer ?? view.ActivePlayer;
            bool holdBack = TheatreBusy?.Invoke() == true && _shownTurnHolder >= 0 &&
                            view.Players.Any(p => p.PlayerId == _shownTurnHolder);
            _shownTurnHolder = holdBack ? _shownTurnHolder : actual;
            return _shownTurnHolder;
        }

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
    }
}
