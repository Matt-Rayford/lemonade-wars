using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LemonadeWars.Unity
{
    public sealed partial class RulebookViewer
    {
        private const int MinQueryLength = 2;
        private const int SnippetLeadInChars = 34;
        private const int SnippetTailChars = 64;

        private void BuildSearchColumn()
        {
            var searchPanel = UiKit.CreatePanel(_root, "Search", new Color(0, 0, 0, 0));
            searchPanel.GetComponent<Image>().raycastTarget = false;
            searchPanel.anchorMin = searchPanel.anchorMax = BlockCenter;
            searchPanel.pivot = new Vector2(0f, 0.5f);
            searchPanel.anchoredPosition = new Vector2(-HalfBlockWidthPx, 0f);
            searchPanel.sizeDelta = new Vector2(SearchWidthPx, PageHeightPx);

            _search = UiKit.CreateInput(searchPanel, "");
            UiKit.Anchor((RectTransform)_search.transform, new Vector2(0f, 0.945f), new Vector2(1f, 1f));
            _search.onValueChanged.AddListener(OnQueryChanged);
            AddMagnifierToSearchField();

            var resultsHost = UiKit.CreatePanel(searchPanel, "Results", new Color(0, 0, 0, 0.35f));
            UiKit.Anchor(resultsHost, new Vector2(0f, 0f), new Vector2(1f, 0.928f));
            _resultList = UiKit.CreateScrollList(resultsHost);
            var resultLayout = _resultList.GetComponent<VerticalLayoutGroup>();
            resultLayout.spacing = 6;
            resultLayout.padding = new RectOffset(10, 10, 8, 8);
        }

        /// <summary>The magnifier is the field's label, so no line of chrome is spent saying "search".</summary>
        private void AddMagnifierToSearchField()
        {
            const float glyph = 22f;
            const float glyphInset = 14f;
            var magnifier = new GameObject("SearchIcon", typeof(RectTransform), typeof(Image));
            magnifier.transform.SetParent(_search.transform, false);
            var magnifierRect = (RectTransform)magnifier.transform;
            magnifierRect.anchorMin = magnifierRect.anchorMax = new Vector2(0f, 0.5f);
            magnifierRect.pivot = new Vector2(0f, 0.5f);
            magnifierRect.sizeDelta = new Vector2(glyph, glyph);
            magnifierRect.anchoredPosition = new Vector2(glyphInset, 0f);
            var magnifierImage = magnifier.GetComponent<Image>();
            magnifierImage.sprite = UiSprites.Magnifier;
            magnifierImage.color = UiKit.ButtonColor;
            magnifierImage.raycastTarget = false;

            // Inset the viewport, not the text: the caret rides the viewport.
            var textArea = (RectTransform)_search.textViewport;
            textArea.offsetMin = new Vector2(glyphInset + glyph + 10f, textArea.offsetMin.y);
        }

        private void OnQueryChanged(string query)
        {
            _query = (query ?? "").Trim();
            RenderResults();
            RenderHighlights();
        }

        // -------------------------------------------------------- results

        private void RenderResults()
        {
            UiKit.Clear(_resultList);
            if (_query.Length < MinQueryLength)
            {
                return;
            }

            bool any = false;
            for (int pageIndex = 0; pageIndex < _pages.Count; pageIndex++)
            {
                string pageText = _pages[pageIndex].SearchText;
                int firstHit = pageText.IndexOf(_query, System.StringComparison.OrdinalIgnoreCase);
                if (firstHit < 0)
                {
                    continue;
                }
                any = true;
                AddResultRow(pageIndex, CountHits(pageText, firstHit), BuildSnippet(pageText, firstHit));
            }
            if (!any)
            {
                AddResultText($"No matches for \"{_query}\".");
            }
        }

        private int CountHits(string pageText, int firstHit)
        {
            int hits = 0;
            for (int scan = firstHit; scan >= 0;
                 scan = pageText.IndexOf(_query, scan + 1, System.StringComparison.OrdinalIgnoreCase))
            {
                hits++;
            }
            return hits;
        }

        /// <summary>
        /// Context around the first hit, snapped to word boundaries (a snippet that opens
        /// mid-word reads like a rendering bug), with every match in yellow.
        /// </summary>
        private string BuildSnippet(string pageText, int firstHit)
        {
            int start = SnippetStart(pageText, firstHit);
            int end = SnippetEnd(pageText, firstHit);
            string snippet = pageText.Substring(start, end - start).Replace("<", "‹");
            return (start > 0 ? "…" : "") + HighlightHits(snippet) + (end < pageText.Length ? "…" : "");
        }

        /// <summary>The start of the matched word, then whole words back while the lead-in stays short.</summary>
        private static int SnippetStart(string pageText, int firstHit)
        {
            int start = firstHit;
            while (start > 0 && pageText[start - 1] != ' ')
            {
                start--;
            }
            while (start > 0)
            {
                int previousSpace = pageText.LastIndexOf(' ', start - 2);
                if (previousSpace < 0 || firstHit - previousSpace > SnippetLeadInChars)
                {
                    break;
                }
                start = previousSpace + 1;
            }
            return start;
        }

        private int SnippetEnd(string pageText, int firstHit)
        {
            int end = Mathf.Min(pageText.Length, firstHit + _query.Length + SnippetTailChars);
            if (end < pageText.Length)
            {
                int lastSpace = pageText.LastIndexOf(' ', end - 1);
                if (lastSpace > firstHit + _query.Length)
                {
                    end = lastSpace;
                }
            }
            return end;
        }

        private string HighlightHits(string snippet)
        {
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
            return builder.ToString();
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

        // ----------------------------------------------------- highlights

        /// <summary>
        /// Paints the hits onto the page: word boxes come from the PDF's text layer,
        /// scaled from page points to display pixels.
        /// </summary>
        private void RenderHighlights()
        {
            UiKit.Clear(_highlightHost);
            var page = _pages[_currentPage];
            if (_query.Length < MinQueryLength || page.Words.Count == 0)
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

            for (int firstWord = 0; firstWord + terms.Length <= page.Words.Count; firstWord++)
            {
                if (!WordsMatchTerms(page, firstWord, terms))
                {
                    continue;
                }
                for (int offset = 0; offset < terms.Length; offset++)
                {
                    AddHighlightMark(page.Words[firstWord + offset], scaleX, scaleY);
                }
            }
        }

        private static bool WordsMatchTerms(Page page, int firstWord, string[] terms)
        {
            for (int offset = 0; offset < terms.Length; offset++)
            {
                if (page.Words[firstWord + offset].Text.IndexOf(terms[offset],
                        System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        private void AddHighlightMark((string Text, float X0, float Y0, float X1, float Y1) word,
            float scaleX, float scaleY)
        {
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
