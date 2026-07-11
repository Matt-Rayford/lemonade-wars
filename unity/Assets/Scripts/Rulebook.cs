using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Searchable rulebook: faithful page images rendered from the PDF, with each
    /// page's extracted text as an invisible search layer. Type a term on the left,
    /// click a snippet, land on the page. Reachable from the main menu and the
    /// in-game pause menu.
    /// </summary>
    public sealed class RulebookViewer
    {
        private sealed class Page
        {
            public string Image;
            public string Flat; // whitespace-collapsed text, for search + snippets
            public float Width;
            public float Height;
            public List<(string Text, float X0, float Y0, float X1, float Y1)> Words;
        }

        private readonly string _imagesRoot;
        private readonly List<Page> _pages = new List<Page>();
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

        private readonly RectTransform _root;
        private readonly RawImage _pageImage;
        private readonly TMP_Text _pageLabel;
        private readonly TMP_InputField _search;
        private readonly RectTransform _resultList;
        private RectTransform _highlightHost;
        private string _query = "";
        private int _current;

        public bool IsOpen => _root.gameObject.activeSelf;

        public RulebookViewer(RectTransform canvasRoot, string streamingAssetsPath)
        {
            _imagesRoot = Path.Combine(streamingAssetsPath, "images");
            string manifestPath = Path.Combine(streamingAssetsPath, "game-data", "rulebook.json");
            if (File.Exists(manifestPath))
            {
                foreach (var entry in JObject.Parse(File.ReadAllText(manifestPath))["pages"]!)
                {
                    var words = new List<(string, float, float, float, float)>();
                    if (entry["words"] is JArray wordArray)
                    {
                        foreach (var word in wordArray)
                        {
                            words.Add(((string)word[0], (float)word[1], (float)word[2],
                                (float)word[3], (float)word[4]));
                        }
                    }
                    _pages.Add(new Page
                    {
                        Image = (string)entry["image"],
                        Flat = Regex.Replace((string)entry["text"] ?? "", @"\s+", " "),
                        Width = (float?)entry["w"] ?? 324f,
                        Height = (float?)entry["h"] ?? 540f,
                        Words = words,
                    });
                }
            }

            _root = UiKit.CreatePanel(canvasRoot, "Rulebook", new Color(0.05f, 0.07f, 0.10f, 0.985f));
            UiKit.Anchor(_root, Vector2.zero, Vector2.one);

            var title = UiKit.CreateText(_root, "RULEBOOK", 34, TextAnchor.MiddleCenter,
                new Color(0.98f, 0.83f, 0.10f));
            UiKit.Anchor((RectTransform)title.transform, new Vector2(0f, 0.93f), new Vector2(1f, 1f));

            var closeButton = UiKit.CreateButton(_root, "X", 22, Close);
            var closeRect = (RectTransform)closeButton.transform;
            UiKit.Anchor(closeRect, new Vector2(1f, 1f), new Vector2(1f, 1f));
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(46f, 46f);
            closeRect.anchoredPosition = new Vector2(-18f, -14f);
            CenterButtonLabel(closeButton);

            // A centered two-column block: search on the left, the page on the right,
            // page-turn controls in a row directly under the page.
            const float pageWidth = 480f;   // 324x540pt pages at 0.6 ratio
            const float pageHeight = 800f;
            const float gap = 28f;
            const float searchWidth = 440f;
            float half = (searchWidth + gap + pageWidth) / 2f;
            var center = new Vector2(0.5f, 0.50f);

            // ---- left column: search + snippet results ----
            var searchPanel = UiKit.CreatePanel(_root, "Search", new Color(0, 0, 0, 0));
            searchPanel.GetComponent<Image>().raycastTarget = false;
            searchPanel.anchorMin = searchPanel.anchorMax = center;
            searchPanel.pivot = new Vector2(0f, 0.5f);
            searchPanel.anchoredPosition = new Vector2(-half, 0f);
            searchPanel.sizeDelta = new Vector2(searchWidth, pageHeight);

            var searchLabel = UiKit.CreateText(searchPanel, "Search the rules", 16,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f), body: true);
            UiKit.Anchor((RectTransform)searchLabel.transform, new Vector2(0f, 0.955f), new Vector2(1f, 1f));
            _search = UiKit.CreateInput(searchPanel, "");
            UiKit.Anchor((RectTransform)_search.transform, new Vector2(0f, 0.895f), new Vector2(1f, 0.95f));
            _search.onValueChanged.AddListener(OnQueryChanged);

            var resultsHost = UiKit.CreatePanel(searchPanel, "Results", new Color(0, 0, 0, 0.35f));
            UiKit.Anchor(resultsHost, new Vector2(0f, 0f), new Vector2(1f, 0.878f));
            _resultList = UiKit.CreateScrollList(resultsHost);
            var resultLayout = _resultList.GetComponent<VerticalLayoutGroup>();
            resultLayout.spacing = 6;
            resultLayout.padding = new RectOffset(10, 10, 8, 8);

            // ---- right column: the page itself ----
            _pageImage = new GameObject("PageImage", typeof(RectTransform), typeof(RawImage))
                .GetComponent<RawImage>();
            _pageImage.transform.SetParent(_root, false);
            var pageRect = (RectTransform)_pageImage.transform;
            pageRect.anchorMin = pageRect.anchorMax = center;
            pageRect.pivot = new Vector2(1f, 0.5f);
            pageRect.anchoredPosition = new Vector2(half, 0f);
            pageRect.sizeDelta = new Vector2(pageWidth, pageHeight);

            // Search-hit highlights paint over the page in page-point coordinates.
            _highlightHost = new GameObject("Highlights", typeof(RectTransform))
                .GetComponent<RectTransform>();
            _highlightHost.SetParent(pageRect, false);
            UiKit.Anchor(_highlightHost, Vector2.zero, Vector2.one);

            // ---- below the page: prev / page counter / next ----
            float navTop = -pageHeight / 2f - 10f;
            float pageLeft = half - pageWidth;

            var prev = UiKit.CreateButton(_root, "<", 26, () => Step(-1));
            var prevRect = (RectTransform)prev.transform;
            prevRect.anchorMin = prevRect.anchorMax = center;
            prevRect.pivot = new Vector2(0f, 1f);
            prevRect.anchoredPosition = new Vector2(pageLeft, navTop);
            prevRect.sizeDelta = new Vector2(46f, 46f);
            CenterButtonLabel(prev);

            var next = UiKit.CreateButton(_root, ">", 26, () => Step(1));
            var nextRect = (RectTransform)next.transform;
            nextRect.anchorMin = nextRect.anchorMax = center;
            nextRect.pivot = new Vector2(1f, 1f);
            nextRect.anchoredPosition = new Vector2(half, navTop);
            nextRect.sizeDelta = new Vector2(46f, 46f);
            CenterButtonLabel(next);

            _pageLabel = UiKit.CreateText(_root, "", 20, TextAnchor.MiddleCenter,
                new Color(0.85f, 0.87f, 0.9f), body: true);
            var labelRect = (RectTransform)_pageLabel.transform;
            labelRect.anchorMin = labelRect.anchorMax = center;
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(pageLeft + pageWidth / 2f, navTop);
            labelRect.sizeDelta = new Vector2(300f, 46f);

            _root.gameObject.SetActive(false);
        }

        /// <summary>Square glyph buttons: kill CreateButton's left-aligned label inset.</summary>
        private static void CenterButtonLabel(Button button)
        {
            var text = button.GetComponentInChildren<TMP_Text>();
            text.alignment = TextAlignmentOptions.Center;
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one);
        }

        public void Open()
        {
            if (_pages.Count == 0)
            {
                return;
            }
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            ShowPage(_current);
            RenderResults();
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
        }

        public void Step(int delta) => ShowPage(_current + delta);

        private void ShowPage(int index)
        {
            _current = Mathf.Clamp(index, 0, _pages.Count - 1);
            _pageImage.texture = LoadPage(_pages[_current].Image);
            _pageLabel.text = $"Page {_current + 1} / {_pages.Count}";
            RenderHighlights();
        }

        /// <summary>
        /// Paint the search hits straight onto the page: word bounding boxes come
        /// from the PDF's text layer, scaled from page points to display pixels.
        /// </summary>
        private void RenderHighlights()
        {
            UiKit.Clear(_highlightHost);
            var page = _pages[_current];
            if (_query.Length < 2 || page.Words.Count == 0)
            {
                return;
            }

            string[] terms = _query.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
            {
                return;
            }
            var pageRect = (RectTransform)_pageImage.transform;
            float scaleX = pageRect.sizeDelta.x / page.Width;
            float scaleY = pageRect.sizeDelta.y / page.Height;

            for (int i = 0; i + terms.Length <= page.Words.Count; i++)
            {
                bool match = true;
                for (int t = 0; t < terms.Length; t++)
                {
                    if (page.Words[i + t].Text.IndexOf(terms[t],
                            System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        match = false;
                        break;
                    }
                }
                if (!match)
                {
                    continue;
                }
                for (int t = 0; t < terms.Length; t++)
                {
                    var word = page.Words[i + t];
                    var markGo = new GameObject("Mark", typeof(RectTransform), typeof(Image));
                    markGo.transform.SetParent(_highlightHost, false);
                    var mark = (RectTransform)markGo.transform;
                    mark.anchorMin = mark.anchorMax = new Vector2(0f, 1f);
                    mark.pivot = new Vector2(0f, 1f);
                    mark.anchoredPosition = new Vector2(word.X0 * scaleX - 2f, -(word.Y0 * scaleY) + 2f);
                    mark.sizeDelta = new Vector2(
                        (word.X1 - word.X0) * scaleX + 4f, (word.Y1 - word.Y0) * scaleY + 4f);
                    var image = markGo.GetComponent<Image>();
                    image.color = new Color(0.98f, 0.83f, 0.10f, 0.38f);
                    image.raycastTarget = false;
                }
            }
        }

        private Texture2D LoadPage(string relativePath)
        {
            if (_textures.TryGetValue(relativePath, out var cached))
            {
                return cached;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            string fullPath = Path.Combine(_imagesRoot, relativePath);
            if (File.Exists(fullPath))
            {
                texture.LoadImage(File.ReadAllBytes(fullPath));
            }
            _textures[relativePath] = texture;
            return texture;
        }

        private void OnQueryChanged(string query)
        {
            _query = (query ?? "").Trim();
            RenderResults();
            RenderHighlights();
        }

        private void RenderResults()
        {
            UiKit.Clear(_resultList);
            if (_query.Length < 2)
            {
                return;
            }

            bool any = false;
            for (int i = 0; i < _pages.Count; i++)
            {
                string flat = _pages[i].Flat;
                int at = flat.IndexOf(_query, System.StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                {
                    continue;
                }
                any = true;
                int hits = 0;
                for (int scan = at; scan >= 0;
                     scan = flat.IndexOf(_query, scan + 1, System.StringComparison.OrdinalIgnoreCase))
                {
                    hits++;
                }
                AddResultRow(i, hits, BuildSnippet(flat, at));
            }
            if (!any)
            {
                AddResultText($"No matches for \"{_query}\".");
            }
        }

        /// <summary>
        /// Context around the first hit — snapped to word boundaries (a snippet that
        /// opens mid-word reads like a rendering bug) with the match itself in yellow.
        /// </summary>
        private string BuildSnippet(string flat, int at)
        {
            // Back up to the start of the matched word, then whole words at a time
            // while the lead-in stays short.
            int start = at;
            while (start > 0 && flat[start - 1] != ' ')
            {
                start--;
            }
            while (start > 0)
            {
                int previousSpace = flat.LastIndexOf(' ', start - 2);
                if (previousSpace < 0 || at - previousSpace > 34)
                {
                    break;
                }
                start = previousSpace + 1;
            }
            int end = Mathf.Min(flat.Length, at + _query.Length + 64);
            if (end < flat.Length)
            {
                int lastSpace = flat.LastIndexOf(' ', end - 1);
                if (lastSpace > at + _query.Length)
                {
                    end = lastSpace;
                }
            }
            string snippet = flat.Substring(start, end - start).Replace("<", "‹");

            // Wrap every hit inside the snippet in the highlight colour.
            var builder = new System.Text.StringBuilder();
            int cursor = 0;
            for (int hit = snippet.IndexOf(_query, System.StringComparison.OrdinalIgnoreCase);
                 hit >= 0;
                 hit = snippet.IndexOf(_query, cursor, System.StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(snippet, cursor, hit - cursor);
                builder.Append("<color=#FFD84D><b>");
                builder.Append(snippet, hit, _query.Length);
                builder.Append("</b></color>");
                cursor = hit + _query.Length;
            }
            builder.Append(snippet, cursor, snippet.Length - cursor);
            return (start > 0 ? "…" : "") + builder + (end < flat.Length ? "…" : "");
        }

        private void AddResultText(string message)
        {
            var text = UiKit.CreateText(_resultList, message, 14, TextAnchor.MiddleLeft,
                new Color(0.7f, 0.72f, 0.76f), body: true);
            var element = text.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 40;
        }

        private void AddResultRow(int pageIndex, int hits, string snippet)
        {
            var row = new GameObject("Result", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(_resultList, false);
            var background = row.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            var idle = new Color(0, 0, 0, 0.35f);
            background.color = idle;
            row.GetComponent<LayoutElement>().minHeight = 74;

            // One rich-text block: yellow page tag line, then the snippet.
            string label = $"<size=17><b><color=#F7D02F>Page {pageIndex + 1}" +
                           (hits > 1 ? $"  ·  {hits} matches" : "") + "</color></b></size>\n" + snippet;
            var text = UiKit.CreateText(row.transform, label, 13, TextAnchor.UpperLeft,
                new Color(0.82f, 0.84f, 0.88f), body: true);
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 6), new Vector2(-10, -6));

            UiKit.AddHover(row,
                () => background.color = new Color(0.20f, 0.24f, 0.32f, 0.7f),
                () => background.color = idle);
            UiKit.AddClick(row, () => ShowPage(pageIndex));
        }
    }

    /// <summary>
    /// The Esc menu: pause the table (visually — the game itself keeps flowing for
    /// the other players), read the rules, toggle sound, or bail to the main menu.
    /// </summary>
    public sealed class PauseMenu
    {
        private const string SoundPref = "lw_sound";

        private readonly RectTransform _root;
        private readonly TMP_Text _soundLabel;

        public System.Action OnRulebook;
        public System.Action OnQuit;

        public bool IsOpen => _root.gameObject.activeSelf;

        public PauseMenu(RectTransform canvasRoot)
        {
            _root = UiKit.CreatePanel(canvasRoot, "PauseMenu", new Color(0.03f, 0.05f, 0.08f, 0.90f));
            UiKit.Anchor(_root, Vector2.zero, Vector2.one);
            UiKit.AddClick(_root.gameObject, Close); // click outside the column resumes

            var title = UiKit.CreateText(_root, "PAUSED", 52, TextAnchor.MiddleCenter,
                new Color(0.98f, 0.83f, 0.10f));
            title.raycastTarget = false;
            UiKit.Anchor((RectTransform)title.transform, new Vector2(0, 0.68f), new Vector2(1, 0.82f));
            UiKit.AddTextShadow(title);

            var column = new GameObject("PauseColumn", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(Image));
            column.transform.SetParent(_root, false);
            column.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f); // swallow the resume click
            UiKit.Anchor((RectTransform)column.transform, new Vector2(0.38f, 0.28f), new Vector2(0.62f, 0.64f));
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            Button Add(string label, UnityEngine.Events.UnityAction onClick)
            {
                var button = UiKit.CreateButton(column.transform, label, 22, onClick);
                button.gameObject.AddComponent<LayoutElement>().minHeight = 56;
                return button;
            }

            Add("Resume", Close);
            Add("Rulebook", () => OnRulebook?.Invoke());
            _soundLabel = Add("", ToggleSound).GetComponentInChildren<TMP_Text>();
            Add("Quit to main menu", () =>
            {
                Close();
                OnQuit?.Invoke();
            });
            RefreshSoundLabel();

            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
        }

        /// <summary>Restore the persisted mute state; call once at boot.</summary>
        public static void ApplySavedVolume()
        {
            AudioListener.volume = PlayerPrefs.GetInt(SoundPref, 1);
        }

        private void ToggleSound()
        {
            int on = PlayerPrefs.GetInt(SoundPref, 1) == 1 ? 0 : 1;
            PlayerPrefs.SetInt(SoundPref, on);
            PlayerPrefs.Save();
            AudioListener.volume = on;
            RefreshSoundLabel();
        }

        private void RefreshSoundLabel()
        {
            _soundLabel.text = PlayerPrefs.GetInt(SoundPref, 1) == 1 ? "Sound: ON" : "Sound: OFF";
        }
    }
}
