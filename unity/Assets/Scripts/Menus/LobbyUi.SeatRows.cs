using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public sealed partial class LobbyUi
    {
        private const float ReadyDotInsetPx = 26f;
        private static readonly Color RemoveButtonIdle = new Color(0.72f, 0.24f, 0.20f, 0.95f);

        public static string NextLevel(string level) =>
            level == "easy" ? "medium"
            : level == "medium" ? "hard"
            : level == "hard" ? "wambulance"
            : "easy";

        private static RectTransform CreateSeatList(RectTransform parent, Vector2 min, Vector2 max)
        {
            var go = new GameObject("SeatList", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            UiKit.Anchor((RectTransform)go.transform, min, max);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            return (RectTransform)go.transform;
        }

        /// <summary>
        /// A bot's difficulty chip is clickable only when <paramref name="onCycleLevel"/> is given.
        /// <paramref name="ready"/> draws the status dot; null = no dot (solo seats, the games list).
        /// </summary>
        private static void AddSeatRow(RectTransform list, string label, System.Action onRemove,
            string botLevel = null, System.Action onCycleLevel = null, bool? ready = null,
            bool isYou = false)
        {
            // The dot owns the row's right edge; chip and remove button shift left of it.
            float dotInset = ready != null ? ReadyDotInsetPx : 0f;
            var row = new GameObject("SeatRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(list, false);
            var background = row.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.color = new Color(0, 0, 0, 0.30f);
            row.GetComponent<LayoutElement>().minHeight = 48;
            if (isYou)
            {
                AddYourSeatRim(row);
            }

            var text = UiKit.CreateText(row.transform, label, 20, TextAnchor.MiddleLeft);
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(16, 2), new Vector2((botLevel != null ? -182 : -56) - dotInset, -2));

            if (ready is bool isReady)
            {
                AddReadyDot(row, isReady);
            }
            if (botLevel != null)
            {
                AddBotLevelChip(row, botLevel, onCycleLevel,
                    rightEdgeX: (onRemove != null ? -49f : -7f) - dotInset);
            }
            if (onRemove != null)
            {
                AddRemoveSeatButton(row, onRemove, rightEdgeX: -7f - dotInset);
            }
        }

        private static void AddYourSeatRim(GameObject row)
        {
            var rimGo = new GameObject("YouRim", typeof(RectTransform), typeof(Image));
            rimGo.transform.SetParent(row.transform, false);
            UiKit.Anchor((RectTransform)rimGo.transform, Vector2.zero, Vector2.one,
                new Vector2(-2, -2), new Vector2(2, 2));
            var rim = rimGo.GetComponent<Image>();
            rim.sprite = UiSprites.RoundedOutline;
            rim.type = Image.Type.Sliced;
            rim.color = UiKit.ButtonColor;
            rim.raycastTarget = false;
        }

        private static void AddReadyDot(GameObject row, bool isReady)
        {
            var dotGo = new GameObject("ReadyDot", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(row.transform, false);
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.anchorMin = dotRect.anchorMax = new Vector2(1f, 0.5f);
            dotRect.pivot = new Vector2(1f, 0.5f);
            // 16px = exactly 1/8 of the 128px circle: a whole mip level, no blend.
            dotRect.sizeDelta = new Vector2(16f, 16f);
            dotRect.anchoredPosition = new Vector2(-9f, 0);
            var dotImage = dotGo.GetComponent<Image>();
            dotImage.sprite = UiSprites.Circle;
            dotImage.type = Image.Type.Simple;
            dotImage.preserveAspect = true;
            dotImage.color = isReady ? ReadyGreen : NotReadyRed;
            dotImage.raycastTarget = false;
        }

        private static void AddBotLevelChip(GameObject row, string botLevel, System.Action onCycleLevel,
            float rightEdgeX)
        {
            var chipGo = new GameObject("Level", typeof(RectTransform), typeof(Image));
            chipGo.transform.SetParent(row.transform, false);
            var chipRect = (RectTransform)chipGo.transform;
            chipRect.anchorMin = chipRect.anchorMax = new Vector2(1f, 0.5f);
            chipRect.pivot = new Vector2(1f, 0.5f);
            chipRect.sizeDelta = new Vector2(148f, 34f); // WAMBULANCE with 15px of air each side
            chipRect.anchoredPosition = new Vector2(rightEdgeX, 0);
            var chipImage = chipGo.GetComponent<Image>();
            chipImage.sprite = UiSprites.RoundedRect;
            chipImage.type = Image.Type.Sliced;
            var chipIdle = new Color(0.24f, 0.30f, 0.40f, 0.95f);
            chipImage.color = chipIdle;
            var levelText = UiKit.CreateText(chipGo.transform,
                botLevel.ToUpperInvariant(), 15, TextAnchor.MiddleCenter,
                botLevel == "wambulance" ? new Color(0.55f, 0.78f, 1f)
                : botLevel == "hard" ? new Color(1f, 0.62f, 0.45f)
                : botLevel == "easy" ? new Color(0.62f, 0.90f, 0.62f)
                : new Color(0.85f, 0.88f, 0.92f), body: true);
            levelText.raycastTarget = false;
            UiKit.Anchor((RectTransform)levelText.transform, Vector2.zero, Vector2.one);
            if (onCycleLevel != null)
            {
                UiKit.AddHover(chipGo,
                    () => chipImage.color = new Color(0.34f, 0.42f, 0.56f, 1f),
                    () => chipImage.color = chipIdle);
                UiKit.AddClick(chipGo, () => onCycleLevel());
            }
        }

        private static void AddRemoveSeatButton(GameObject row, System.Action onRemove, float rightEdgeX)
        {
            var removeGo = new GameObject("Remove", typeof(RectTransform), typeof(Image));
            removeGo.transform.SetParent(row.transform, false);
            var removeRect = (RectTransform)removeGo.transform;
            removeRect.anchorMin = removeRect.anchorMax = new Vector2(1f, 0.5f);
            removeRect.pivot = new Vector2(1f, 0.5f);
            removeRect.sizeDelta = new Vector2(36f, 36f);
            removeRect.anchoredPosition = new Vector2(rightEdgeX, 0);
            var removeImage = removeGo.GetComponent<Image>();
            removeImage.sprite = UiSprites.RoundedRect;
            removeImage.type = Image.Type.Sliced;
            removeImage.color = RemoveButtonIdle;
            var removeGlyph = UiKit.CreateText(removeGo.transform, "X", 18,
                TextAnchor.MiddleCenter, Color.white);
            removeGlyph.raycastTarget = false;
            UiKit.Anchor((RectTransform)removeGlyph.transform, Vector2.zero, Vector2.one);
            UiKit.AddHover(removeGo,
                () => removeImage.color = new Color(0.88f, 0.32f, 0.26f, 1f),
                () => removeImage.color = RemoveButtonIdle);
            UiKit.AddClick(removeGo, () => onRemove());
        }
    }
}
