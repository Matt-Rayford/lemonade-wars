using System.Linq;
using LemonadeWars.Engine.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The player panels down the left edge: portrait badge, VP crowns, stat row,
    // first-player lemon, viewed-board rim and the attack-glow ring.
    public sealed partial class TableView
    {
        /// <summary>playerId -> "easy"/"medium"/"hard" for bot seats, null for humans.</summary>
        public System.Func<int, string> BotLevelLookup;

        /// <summary>Dark badge palette; a player's color is a stable hash of their name.</summary>
        private static readonly Color[] BadgeColors =
        {
            new Color(0.55f, 0.24f, 0.24f), new Color(0.23f, 0.42f, 0.58f),
            new Color(0.25f, 0.48f, 0.32f), new Color(0.56f, 0.40f, 0.16f),
            new Color(0.45f, 0.28f, 0.55f), new Color(0.20f, 0.48f, 0.48f),
            new Color(0.58f, 0.30f, 0.44f), new Color(0.38f, 0.42f, 0.20f),
            new Color(0.30f, 0.32f, 0.58f), new Color(0.50f, 0.34f, 0.24f),
        };

        private RectTransform _playersColumn;
        private Texture2D _lemonIcon;
        private Texture2D _vpIcon;
        private Texture2D _cashIcon;
        private Texture2D _tantrumIcon;
        private Texture2D _hourglassIcon;

        /// <summary>
        /// Player panels, Dune Imperium style: turn order top to bottom starting with
        /// the player after you; you sit pinned at the bottom. The lemon is the
        /// first-player token — it stays put all game (the banner and status line
        /// already say whose turn it is).
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

            // Effects read bar anchors in the SAME frame as this render (a purchase
            // floater spawns from the fx tick immediately after), but a layout group
            // does not place its children until end of frame — until then every fresh
            // row sits at the column's center, which dropped the "-$X" on whoever
            // happened to be sitting mid-column instead of on the buyer.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_playersColumn);
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

            // First-player lemon token, right edge — stays put for the whole game.
            if (playerId == view.FirstPlayer && _lemonIcon != null)
            {
                var lemonGo = new GameObject("FirstPlayerLemon", typeof(RectTransform), typeof(RawImage));
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

            // The table is waiting on this player — their turn, a reaction window,
            // a discard, a lord pick — so their badge greys out under the hourglass.
            // Several can wait at once (a window open to everyone); each gets one.
            if (view.ActingPlayers.Contains(playerId) && !isMe)
            {
                var shadeGo = new GameObject("TurnShade", typeof(RectTransform), typeof(Image));
                shadeGo.transform.SetParent(badgeGo.transform, false);
                UiKit.Anchor((RectTransform)shadeGo.transform, Vector2.zero, Vector2.one);
                var shade = shadeGo.GetComponent<Image>();
                shade.sprite = UiSprites.Circle;
                shade.type = Image.Type.Simple;
                shade.preserveAspect = true;
                var grey = ModalBackdrop.Tint;
                shade.color = new Color(grey.r, grey.g, grey.b, 0.8f);
                shade.raycastTarget = false;

                var glassGo = new GameObject("Hourglass", typeof(RectTransform), typeof(RawImage),
                    typeof(HourglassMotion));
                glassGo.transform.SetParent(badgeGo.transform, false);
                var glassRect = (RectTransform)glassGo.transform;
                glassRect.anchorMin = glassRect.anchorMax = new Vector2(0.5f, 0.5f);
                glassRect.sizeDelta = new Vector2(28f, 28f); // 50% of the badge; the motion scales from here
                var glass = glassGo.GetComponent<RawImage>();
                glass.texture = _hourglassIcon;
                glass.raycastTarget = false;
            }

            // Victory points: a crown per VP, arcing over the portrait left-to-right —
            // frees the stat row below for the money/hand/tantrum counts. Met Lemon
            // Lords count once revealed (FinishedLords is empty until game end), so
            // the crowns agree with the final standings, not just the in-game score.
            int crowns = Mathf.Min(
                player.InGameVictoryPoints + player.FinishedLords.Count(l => l.Met), 8);
            var badgeCenter = new Vector2(12f + 28f, 0f);
            for (int crown = 0; crown < crowns; crown++)
            {
                float degrees = 160f - crown * 32f;
                float radians = degrees * Mathf.Deg2Rad;
                var crownGo = new GameObject("Crown", typeof(RectTransform), typeof(RawImage));
                crownGo.transform.SetParent(row.transform, false);
                var crownRect = (RectTransform)crownGo.transform;
                crownRect.anchorMin = crownRect.anchorMax = new Vector2(0f, 0.5f);
                crownRect.pivot = new Vector2(0.5f, 0.5f);
                crownRect.sizeDelta = new Vector2(22f, 22f);
                crownRect.anchoredPosition = badgeCenter +
                    new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 37f;
                var crownImage = crownGo.GetComponent<RawImage>();
                crownImage.texture = _vpIcon;
                crownImage.raycastTarget = false;
            }

            // Name on top, VP / Cash below. Bot seats wear their difficulty in the
            // same colors as the lobby chips.
            string nameLabel = player.Name + (isMe ? "  (you)" : "");
            string botLevel = BotLevelLookup?.Invoke(playerId);
            if (!string.IsNullOrEmpty(botLevel))
            {
                string levelColor = botLevel == "wambulance" ? "#8CC7FF"
                    : botLevel == "hard" ? "#FF9E73"
                    : botLevel == "easy" ? "#9EE59E" : "#D9E0EB";
                nameLabel += $"  <size=13><color={levelColor}>{botLevel.ToUpperInvariant()}</color></size>";
            }
            var nameText = UiKit.CreateText(row.transform,
                nameLabel, 19, TextAnchor.LowerLeft, Color.white);
            nameText.raycastTarget = false;
            UiKit.Anchor((RectTransform)nameText.transform, new Vector2(0, 0.56f), new Vector2(1, 1),
                new Vector2(80, 2), new Vector2(-52, -2));
            // Icon stats under the name: cash, hand size, and tantrums (VP crowns arc
            // over the portrait). FIXED columns — flowing layouts made the icons drift
            // with digit width, so bars never lined up.
            var statsGo = new GameObject("Stats", typeof(RectTransform));
            statsGo.transform.SetParent(row.transform, false);
            var stats = UiKit.Anchor((RectTransform)statsGo.transform,
                new Vector2(0, 0), new Vector2(1, 0.56f), new Vector2(80, 4), new Vector2(-52, -1));
            AddStatAt(stats, _cashIcon, $"${player.Money}", 0f, 36f);
            // Hand size is targeting intel: fewer cards = fewer possible reactions.
            AddStatAt(stats, _art.Back("lemon"), player.HandCount.ToString(), 92f, 26f);
            AddStatAt(stats, _tantrumIcon, player.TantrumCount.ToString(), 168f, 36f);
            // The Whiniest Baby's tantrum icon visibly bawls: two fat tears over the
            // eyes, drawn on the icon's exact rect. Held back while the title-claim
            // animation is still mid-flight, like the lord fan.
            if (view.WhiniestBabyHolder == playerId && !SuppressWhiniestBaby)
            {
                var tearsGo = new GameObject("BabyTears", typeof(RectTransform), typeof(Image));
                tearsGo.transform.SetParent(stats, false);
                var tearsRect = (RectTransform)tearsGo.transform;
                tearsRect.anchorMin = tearsRect.anchorMax = new Vector2(0f, 0.5f);
                tearsRect.sizeDelta = new Vector2(36f, 36f);
                tearsRect.anchoredPosition = new Vector2(168f + 18f, 0f); // icon center
                var tearsImage = tearsGo.GetComponent<Image>();
                tearsImage.sprite = UiSprites.BabyTears;
                tearsImage.type = Image.Type.Simple;
                tearsImage.raycastTarget = false;
            }

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
                // Behind the contents: the rim crosses the portrait, and a line drawn
                // through the VP crowns reads as a scratch on them.
                borderGo.transform.SetAsFirstSibling();
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

        /// <summary>One icon-plus-count chip at a fixed column offset in the stat row.</summary>
        private static void AddStatAt(RectTransform parent, Texture2D icon, string count,
            float columnX, float iconWidth)
        {
            if (icon != null)
            {
                var iconGo = new GameObject("StatIcon", typeof(RectTransform), typeof(RawImage));
                iconGo.transform.SetParent(parent, false);
                var iconRect = (RectTransform)iconGo.transform;
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
    }
}
