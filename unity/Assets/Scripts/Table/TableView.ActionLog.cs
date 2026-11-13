using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Action log: a yellow tab on the screen's right edge. Clicking it slides the recent history
    // out, so fast bot turns can be read back; the tab rides out on the panel's leading corner,
    // where clicking it again tucks the log away.
    public sealed partial class TableView
    {
        private const float LogSlideSeconds = 0.3f;
        private const float LogTabWidthPx = 54f;
        private const float LogTabHeightPx = 48f;
        private const float LogTabIconPx = 32f;
        /// <summary>The panel, and the tab while it rests at the edge, run this far past the screen's edge,
        /// hiding their right-hand rounded corners: both read as attached to the edge, rounded on the left only.</summary>
        private const float LogEdgeOverhangPx = 18f;
        private static readonly Color LogHeaderTextColor = new Color(1f, 0.92f, 0.55f);
        private static readonly Color LogPanelColor = new Color(0.05f, 0.07f, 0.11f, 0.93f);
        private static readonly Color LogTabHoverColor = new Color(1f, 0.90f, 0.32f);

        private RectTransform _logZone;
        private RectTransform _logPanel;
        private RectTransform _logList;
        private RectTransform _logTab;
        private Vector2 _logPanelOpenPosition;
        private Texture2D _actionLogIcon;
        private readonly List<string> _logLines = new List<string>();
        private bool _logExpanded;
        /// <summary>0 = tucked away off-screen, 1 = fully out.</summary>
        private float _logSlide;
        private string _logSignature = "";

        private void BuildActionLog()
        {
            _logZone = UiKit.CreatePanel(Root, "ActionLog", new Color(0, 0, 0, 0));
            _logZone.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(_logZone, new Vector2(LogLeft, 0.24f), new Vector2(1f, 0.695f),
                new Vector2(4, 0), new Vector2(0, -4));

            BuildLogPanel();
            BuildLogTab(); // after the panel: the tab draws over the panel's corner
        }

        private void BuildLogPanel()
        {
            _logPanel = UiKit.CreatePanel(_logZone, "LogPanel", LogPanelColor);
            var surface = _logPanel.GetComponent<Image>();
            surface.sprite = UiSprites.RoundedRect;
            surface.type = Image.Type.Sliced;
            UiKit.Anchor(_logPanel, Vector2.zero, Vector2.one,
                new Vector2(0, 0), new Vector2(LogEdgeOverhangPx, 0));
            _logPanelOpenPosition = _logPanel.anchoredPosition;

            // Centred on the panel as seen: the overhang past the screen's edge does not count.
            var headerText = UiKit.CreateText(_logPanel, "ACTION LOG", 15, TextAnchor.MiddleCenter,
                LogHeaderTextColor);
            headerText.raycastTarget = false;
            UiKit.Anchor((RectTransform)headerText.transform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -LogTabHeightPx), new Vector2(-LogEdgeOverhangPx, 0));

            var listHost = UiKit.CreatePanel(_logPanel, "LogLines", new Color(0, 0, 0, 0));
            listHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(listHost, Vector2.zero, Vector2.one,
                new Vector2(0, 6), new Vector2(-LogEdgeOverhangPx, -LogTabHeightPx));
            _logList = UiKit.CreateScrollList(listHost);
            // The panel is the surface: strip the scroll list's own inset box and tint.
            var scrollNode = (RectTransform)_logList.parent.parent;
            UiKit.Anchor(scrollNode, Vector2.zero, Vector2.one);
            scrollNode.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var logLayout = _logList.GetComponent<VerticalLayoutGroup>();
            logLayout.spacing = 2;
            logLayout.padding = new RectOffset(12, 8, 2, 5);
            _logPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Measured from the zone's right edge by its own left edge, so it can rest against the screen's
        /// edge before the zone has a width, and travel to the panel's leading corner once it has.
        /// </summary>
        private void BuildLogTab()
        {
            _logTab = UiKit.CreatePanel(_logZone, "LogTab", UiKit.ButtonColor);
            var tabSurface = _logTab.GetComponent<Image>();
            tabSurface.sprite = UiSprites.RoundedRect;
            tabSurface.type = Image.Type.Sliced;
            _logTab.anchorMin = _logTab.anchorMax = new Vector2(1f, 1f);
            _logTab.pivot = new Vector2(0f, 1f);
            PlaceLogTab(easedSlide: 0f);
            UiKit.AddClick(_logTab.gameObject, ToggleLog);
            UiKit.AddHover(_logTab.gameObject,
                () => tabSurface.color = LogTabHoverColor,
                () => tabSurface.color = UiKit.ButtonColor);

            // Held to the tab's left end, which is the part on screen at rest.
            RectTransform face;
            if (_actionLogIcon == null)
            {
                var fallback = UiKit.CreateText(_logTab, "LOG", 14, TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
                fallback.raycastTarget = false;
                face = (RectTransform)fallback.transform;
                face.sizeDelta = new Vector2(LogTabWidthPx, LogTabHeightPx);
            }
            else
            {
                var iconGo = new GameObject("LogIcon", typeof(RectTransform), typeof(RawImage));
                iconGo.transform.SetParent(_logTab, false);
                var iconImage = iconGo.GetComponent<RawImage>();
                iconImage.texture = _actionLogIcon;
                iconImage.color = UiKit.ButtonTextColor; // the art is white on clear, so it takes the tint exactly
                iconImage.raycastTarget = false;
                face = (RectTransform)iconGo.transform;
                face.sizeDelta = new Vector2(LogTabIconPx, LogTabIconPx);
            }
            face.anchorMin = face.anchorMax = new Vector2(0f, 0.5f);
            face.pivot = new Vector2(0.5f, 0.5f);
            face.anchoredPosition = new Vector2(LogTabWidthPx / 2f, 0f);
        }

        /// <summary>
        /// At rest the tab hangs off the screen's edge by the overhang; fully out it is a plain rounded
        /// square on the panel's top-left corner. In between it leads the panel out.
        /// </summary>
        private void PlaceLogTab(float easedSlide)
        {
            float leftEdgeAtRest = -LogTabWidthPx;
            float leftEdgeFullyOut = -_logZone.rect.width;
            _logTab.anchoredPosition = new Vector2(Mathf.Lerp(leftEdgeAtRest, leftEdgeFullyOut, easedSlide), 0f);
            _logTab.sizeDelta = new Vector2(
                LogTabWidthPx + (1f - easedSlide) * LogEdgeOverhangPx, LogTabHeightPx);
        }

        private void ToggleLog()
        {
            _logExpanded = !_logExpanded;
            EndBoardBandAtOpenLog();
            RenderLog();
        }

        /// <summary>
        /// Call every frame: slides the panel and its tab toward wherever the last toggle sent them. Placed
        /// every frame, moving or not, because the zone's width follows the window.
        /// </summary>
        public void TickActionLog()
        {
            _logSlide = Mathf.MoveTowards(_logSlide, _logExpanded ? 1f : 0f,
                Time.unscaledDeltaTime / LogSlideSeconds);
            float easedSlide = Mathf.SmoothStep(0f, 1f, _logSlide);
            float tuckedAwayPx = _logZone.rect.width + LogEdgeOverhangPx;
            _logPanel.anchoredPosition = _logPanelOpenPosition + new Vector2((1f - easedSlide) * tuckedAwayPx, 0f);
            _logPanel.gameObject.SetActive(_logSlide > 0f);
            PlaceLogTab(easedSlide);
        }

        /// <summary>
        /// The open log would cover the board's last stands, so the band stops at the log's edge:
        /// the row fades out there and edge-scrolls (see TickBoardScroll) to reach what is past it.
        /// </summary>
        private void EndBoardBandAtOpenLog()
        {
            _boardPanel.anchorMax = new Vector2(_logExpanded ? LogLeft : BoardRight, BoardTop);
        }

        /// <param name="lines">Oldest first.</param>
        public void SetLog(IEnumerable<string> lines)
        {
            _logLines.Clear();
            if (lines != null)
            {
                _logLines.AddRange(lines);
            }
            string signature = _logLines.Count + "|" +
                (_logLines.Count > 0 ? _logLines[_logLines.Count - 1] : "");
            if (signature == _logSignature)
            {
                return;
            }
            _logSignature = signature;
            RenderLog();
        }

        /// <summary>Newest line first. Lines are only built while the panel is out; a closing panel keeps what it showed.</summary>
        private void RenderLog()
        {
            if (!_logExpanded)
            {
                return;
            }
            UiKit.Clear(_logList);
            for (int i = _logLines.Count - 1; i >= 0; i--)
            {
                var line = UiKit.CreateText(_logList, _logLines[i], 13, TextAnchor.MiddleLeft,
                    i == _logLines.Count - 1 ? Color.white : new Color(0.78f, 0.80f, 0.84f),
                    body: true);
                line.gameObject.AddComponent<LayoutElement>().minHeight = 18;
            }
        }
    }
}
