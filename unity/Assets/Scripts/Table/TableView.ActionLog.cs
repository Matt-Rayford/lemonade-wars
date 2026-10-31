using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Action log: the right-hand column mirroring the player panels. Collapsed it's
    // one slim "latest thing that happened" chip; expanded it's the recent history —
    // fast bot turns can be replayed at a glance.
    public sealed partial class TableView
    {
        private RectTransform _logCollapsed;
        private RectTransform _logExpandedPanel;
        private RectTransform _logList;
        private TMP_Text _logLatest;
        private TMP_Text _logToggleHint;
        private readonly List<string> _logLines = new List<string>();
        private bool _logExpanded;
        private string _logSignature = "";

        private void BuildActionLog()
        {
            var zone = UiKit.CreatePanel(Root, "ActionLog", new Color(0, 0, 0, 0));
            zone.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(zone, new Vector2(BoardRight, 0.24f), new Vector2(1f, 0.695f),
                new Vector2(4, 0), new Vector2(-10, -4));

            // Header pinned at the TOP in both states — toggling swaps only the +/–
            // hint and what hangs beneath, so the control never jumps around the zone.
            var header = UiKit.CreatePanel(zone, "LogHeader", new Color(0, 0, 0, 0.45f));
            UiKit.Anchor(header, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -24), new Vector2(0, 0));
            var headerText = UiKit.CreateText(header, "ACTION LOG", 13, TextAnchor.MiddleLeft,
                new Color(1f, 0.92f, 0.55f));
            headerText.raycastTarget = false;
            UiKit.Anchor((RectTransform)headerText.transform, Vector2.zero, Vector2.one,
                new Vector2(8, 0), new Vector2(0, 0));
            _logToggleHint = UiKit.CreateText(header, "+", 15, TextAnchor.MiddleCenter,
                new Color(1f, 0.92f, 0.55f));
            _logToggleHint.raycastTarget = false;
            UiKit.Anchor((RectTransform)_logToggleHint.transform, new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(-24, 0), new Vector2(-4, 0));
            UiKit.AddClick(header.gameObject, ToggleLog);

            // Collapsed: the latest line on a slim chip directly beneath the header.
            _logCollapsed = UiKit.CreatePanel(zone, "LogCollapsed", new Color(0, 0, 0, 0.40f));
            UiKit.Anchor(_logCollapsed, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -50), new Vector2(0, -24));
            _logLatest = UiKit.CreateText(_logCollapsed, "", 13, TextAnchor.MiddleLeft,
                new Color(0.86f, 0.88f, 0.92f), body: true);
            _logLatest.raycastTarget = false;
            UiKit.Anchor((RectTransform)_logLatest.transform, Vector2.zero, Vector2.one,
                new Vector2(8, 0), new Vector2(-8, 0));
            UiKit.AddClick(_logCollapsed.gameObject, ToggleLog);
            _logCollapsed.gameObject.SetActive(false);

            // Expanded: the recent history below the header, newest at the top — its
            // first row sits exactly where the collapsed chip was.
            _logExpandedPanel = UiKit.CreatePanel(zone, "LogExpanded", new Color(0.05f, 0.07f, 0.11f, 0.93f));
            UiKit.Anchor(_logExpandedPanel, Vector2.zero, Vector2.one,
                new Vector2(0, 0), new Vector2(0, -24));
            var listHost = UiKit.CreatePanel(_logExpandedPanel, "LogLines", new Color(0, 0, 0, 0));
            listHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(listHost, Vector2.zero, Vector2.one);
            _logList = UiKit.CreateScrollList(listHost);
            // Flat: the expanded panel IS the surface. Strip the scroll list's own
            // inset box + tint so expanding just makes the box taller, and pad the
            // lines to line up with the collapsed chip's text.
            var scrollNode = (RectTransform)_logList.parent.parent;
            UiKit.Anchor(scrollNode, Vector2.zero, Vector2.one);
            scrollNode.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var logLayout = _logList.GetComponent<VerticalLayoutGroup>();
            logLayout.spacing = 2;
            logLayout.padding = new RectOffset(8, 8, 5, 5);
            _logExpandedPanel.gameObject.SetActive(false);
        }

        private void ToggleLog()
        {
            _logExpanded = !_logExpanded;
            _logExpandedPanel.gameObject.SetActive(_logExpanded);
            _logToggleHint.text = _logExpanded ? "–" : "+";
            RenderLog();
        }

        /// <summary>Feed the friendly action-log lines (oldest first).</summary>
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

        private void RenderLog()
        {
            _logLatest.text = _logLines.Count > 0 ? _logLines[_logLines.Count - 1] : "";
            _logCollapsed.gameObject.SetActive(!_logExpanded && _logLines.Count > 0);
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
