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
    /// page's extracted text as an invisible search layer. Reachable from the main
    /// menu and the in-game pause menu.
    /// </summary>
    public sealed partial class RulebookViewer
    {
        private sealed class Page
        {
            public string Image;
            /// <summary>Whitespace-collapsed, so a snippet cut from it reads as one line.</summary>
            public string SearchText;
            public float Width;
            public float Height;
            public List<(string Text, float X0, float Y0, float X1, float Y1)> Words;
        }

        // Search on the left and the page on the right, centered as one block, with the
        // page-turn controls in a row directly under the page.
        private const float PageWidthPx = 480f;   // 324x540pt pages at 0.6 ratio
        private const float PageHeightPx = 800f;
        private const float ColumnGapPx = 28f;
        private const float SearchWidthPx = 440f;
        private const float HalfBlockWidthPx = (SearchWidthPx + ColumnGapPx + PageWidthPx) / 2f;
        private static readonly Vector2 BlockCenter = new Vector2(0.5f, 0.50f);

        private readonly string _imagesRoot;
        private readonly List<Page> _pages = new List<Page>();
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

        private readonly RectTransform _root;
        private RawImage _pageImage;
        private TMP_Text _pageLabel;
        private TMP_InputField _search;
        private RectTransform _resultList;
        private RectTransform _highlightHost;
        private string _query = "";
        private int _currentPage;

        public bool IsOpen => _root.gameObject.activeSelf;

        public RulebookViewer(RectTransform canvasRoot, string streamingAssetsPath)
        {
            _imagesRoot = Path.Combine(streamingAssetsPath, "images");
            LoadPages(Path.Combine(streamingAssetsPath, "game-data", "rulebook.json"));

            _root = UiKit.CreatePanel(canvasRoot, "Rulebook", UiKit.TableColor);
            UiKit.Anchor(_root, Vector2.zero, Vector2.one);
            UiKit.CreateWallpaper(_root, Vector2.one);

            BuildTitleAndCloseButton();
            BuildSearchColumn();
            BuildPageColumn();
            BuildPageTurnControls();

            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            if (_pages.Count == 0)
            {
                return;
            }
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            ShowPage(_currentPage);
            RenderResults();
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Turn a page, by button or arrow key. The sound lives here, not on the buttons,
        /// so the keys sound the same and the clamped ends stay silent.
        /// </summary>
        public void Step(int delta)
        {
            int before = _currentPage;
            ShowPage(_currentPage + delta);
            if (_currentPage != before)
            {
                Sfx.Play(Sfx.PageTurn);
            }
        }

        private void ShowPage(int index)
        {
            _currentPage = Mathf.Clamp(index, 0, _pages.Count - 1);
            _pageImage.texture = LoadPage(_pages[_currentPage].Image);
            _pageLabel.text = $"Page {_currentPage + 1} / {_pages.Count}";
            RenderHighlights();
        }

        // ------------------------------------------------------ page data

        private void LoadPages(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                return;
            }
            foreach (var entry in JObject.Parse(File.ReadAllText(manifestPath))["pages"]!)
            {
                _pages.Add(new Page
                {
                    Image = (string)entry["image"],
                    SearchText = Regex.Replace((string)entry["text"] ?? "", @"\s+", " "),
                    Width = (float?)entry["w"] ?? 324f,
                    Height = (float?)entry["h"] ?? 540f,
                    Words = ReadWords(entry),
                });
            }
        }

        private static List<(string, float, float, float, float)> ReadWords(JToken pageEntry)
        {
            var words = new List<(string, float, float, float, float)>();
            if (pageEntry["words"] is JArray wordArray)
            {
                foreach (var word in wordArray)
                {
                    words.Add(((string)word[0], (float)word[1], (float)word[2],
                        (float)word[3], (float)word[4]));
                }
            }
            return words;
        }

        private Texture2D LoadPage(string relativePath)
        {
            if (_textures.TryGetValue(relativePath, out var cached))
            {
                return cached;
            }
            // Sharp mips (tools/make_mips.py): pages are ~2250px wide but display around 600.
            string fullPath = Path.Combine(_imagesRoot, relativePath);
            var texture = UiKit.LoadTextureSharp(fullPath) ??
                new Texture2D(2, 2, TextureFormat.RGB24, false);
            _textures[relativePath] = texture;
            return texture;
        }

        // --------------------------------------------------------- chrome

        private void BuildTitleAndCloseButton()
        {
            var title = UiKit.CreateText(_root, "RULEBOOK", 34, TextAnchor.MiddleCenter,
                UiKit.ButtonColor);
            UiKit.Anchor((RectTransform)title.transform, new Vector2(0f, 0.93f), new Vector2(1f, 1f));

            var closeButton = UiKit.CreateButton(_root, "X", 22, Close);
            var closeRect = (RectTransform)closeButton.transform;
            UiKit.Anchor(closeRect, new Vector2(1f, 1f), new Vector2(1f, 1f));
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(46f, 46f);
            closeRect.anchoredPosition = new Vector2(-18f, -14f);
            CenterButtonLabel(closeButton);
        }

        private void BuildPageColumn()
        {
            _pageImage = new GameObject("PageImage", typeof(RectTransform), typeof(RawImage))
                .GetComponent<RawImage>();
            _pageImage.transform.SetParent(_root, false);
            var pageRect = (RectTransform)_pageImage.transform;
            pageRect.anchorMin = pageRect.anchorMax = BlockCenter;
            pageRect.pivot = new Vector2(1f, 0.5f);
            pageRect.anchoredPosition = new Vector2(HalfBlockWidthPx, 0f);
            pageRect.sizeDelta = new Vector2(PageWidthPx, PageHeightPx);

            // Search-hit highlights paint over the page in page-point coordinates.
            _highlightHost = new GameObject("Highlights", typeof(RectTransform))
                .GetComponent<RectTransform>();
            _highlightHost.SetParent(pageRect, false);
            UiKit.Anchor(_highlightHost, Vector2.zero, Vector2.one);
        }

        private void BuildPageTurnControls()
        {
            float navTop = -PageHeightPx / 2f - 10f;
            float pageLeft = HalfBlockWidthPx - PageWidthPx;

            var prev = UiKit.CreateButton(_root, "<", 26, () => Step(-1), clickSound: null);
            var prevRect = (RectTransform)prev.transform;
            prevRect.anchorMin = prevRect.anchorMax = BlockCenter;
            prevRect.pivot = new Vector2(0f, 1f);
            prevRect.anchoredPosition = new Vector2(pageLeft, navTop);
            prevRect.sizeDelta = new Vector2(46f, 46f);
            CenterButtonLabel(prev);

            var next = UiKit.CreateButton(_root, ">", 26, () => Step(1), clickSound: null);
            var nextRect = (RectTransform)next.transform;
            nextRect.anchorMin = nextRect.anchorMax = BlockCenter;
            nextRect.pivot = new Vector2(1f, 1f);
            nextRect.anchoredPosition = new Vector2(HalfBlockWidthPx, navTop);
            nextRect.sizeDelta = new Vector2(46f, 46f);
            CenterButtonLabel(next);

            _pageLabel = UiKit.CreateText(_root, "", 20, TextAnchor.MiddleCenter,
                new Color(0.85f, 0.87f, 0.9f), body: true);
            var labelRect = (RectTransform)_pageLabel.transform;
            labelRect.anchorMin = labelRect.anchorMax = BlockCenter;
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(pageLeft + PageWidthPx / 2f, navTop);
            labelRect.sizeDelta = new Vector2(300f, 46f);
        }

        /// <summary>Square glyph buttons: kill CreateButton's left-aligned label inset.</summary>
        private static void CenterButtonLabel(Button button)
        {
            var text = button.GetComponentInChildren<TMP_Text>();
            text.alignment = TextAlignmentOptions.Center;
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one);
        }
    }
}
