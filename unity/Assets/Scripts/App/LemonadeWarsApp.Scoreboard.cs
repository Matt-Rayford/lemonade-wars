using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public sealed partial class LemonadeWarsApp
    {
        private const float ScoreRowMaxHeightPx = 140f;
        private const float ScoreRowMinHeightPx = 80f;
        private const float ScoreRowSpacingPx = 10f;
        private const float ScoreCardAspect = 5f / 7f;
        private const float ScoreCardGapPx = 8f;
        private const float ScoreNameColumnPx = 210f;
        private const float ScoreRowPaddingPx = 10f;

        private bool _endShown;

        private void MaybeShowEndSummary()
        {
            if (View != null && View.Stage == GameStage.Finished && !_endShown &&
                !_fx.IsBusy && !_dice.IsBusy && !_turnBanner.IsOpen && !_picker.IsOpen &&
                !_playTheatre.IsBusy)
            {
                _endShown = true;
                ShowEndSummary();
            }
        }

        private void ShowEndSummary()
        {
            _prompt.Hide();
            string winners = string.Join(" & ", View.Winners.Select(NameOf));
            string verb = View.Winners.Count > 1 ? "WIN" : "WINS";
            _prompt.ShowBody($"{winners.ToUpperInvariant()} {verb} LEMONADE WARS!", BuildScoreboard,
                new[]
                {
                    new Prompt.Option("Return to Board", () => { }),
                    new Prompt.Option("Leave Game", () => BackToMenu("")),
                },
                titleColor: UiKit.ButtonColor);
        }

        /// <summary>
        /// Everyone ranked by score (money breaks ties). Rows shrink to fit the table, so five
        /// players with full hands of titles still fit on one screen.
        /// </summary>
        private void BuildScoreboard(RectTransform host)
        {
            var standings = View.Players
                .Select(p => (Player: p, Total: p.InGameVictoryPoints + p.FinishedLords.Count(l => l.Met)))
                .OrderByDescending(s => s.Total)
                .ThenByDescending(s => s.Player.Money)
                .ToList();

            float available = host.rect.height > 0f ? host.rect.height : 800f;
            float rowHeight = Mathf.Clamp(
                (available - (standings.Count - 1) * ScoreRowSpacingPx) / standings.Count,
                ScoreRowMinHeightPx, ScoreRowMaxHeightPx);
            float cardHeight = rowHeight - 2f * ScoreRowPaddingPx;

            var listGo = new GameObject("Standings", typeof(RectTransform), typeof(VerticalLayoutGroup));
            listGo.transform.SetParent(host, false);
            UiKit.Anchor((RectTransform)listGo.transform, Vector2.zero, Vector2.one);
            var list = listGo.GetComponent<VerticalLayoutGroup>();
            list.spacing = ScoreRowSpacingPx;
            list.childAlignment = TextAnchor.UpperCenter;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            list.childControlWidth = true;
            list.childControlHeight = true;

            foreach (var (player, total) in standings)
            {
                BuildScoreRow((RectTransform)listGo.transform, player, total, rowHeight, cardHeight);
            }
        }

        private void BuildScoreRow(RectTransform list, PlayerView.PlayerPanel player, int total,
            float rowHeight, float cardHeight)
        {
            var rowGo = new GameObject("Score", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(list, false);
            var row = rowGo.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 14f;
            row.padding = new RectOffset((int)ScoreRowPaddingPx * 2, (int)ScoreRowPaddingPx * 2, 0, 0);
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = true;
            row.childControlHeight = true;
            rowGo.GetComponent<LayoutElement>().preferredHeight = rowHeight;

            AddScoreRowAvatar(rowGo.transform, player, cardHeight);
            AddScoreRowLabels(rowGo.transform, player, total);
            AddScoreRowCards(rowGo.transform, player, cardHeight);
        }

        private static void AddScoreRowAvatar(Transform row, PlayerView.PlayerPanel player, float cardHeight)
        {
            float badge = Mathf.Min(64f, cardHeight * 0.6f);
            var avatar = TableView.CreatePlayerBadge(row, player.Name, badge);
            var avatarElement = avatar.gameObject.AddComponent<LayoutElement>();
            avatarElement.preferredWidth = badge;
            avatarElement.preferredHeight = badge;
        }

        private static void AddScoreRowLabels(Transform row, PlayerView.PlayerPanel player, int total)
        {
            var labelsGo = new GameObject("Labels", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(LayoutElement));
            labelsGo.transform.SetParent(row, false);
            var labels = labelsGo.GetComponent<VerticalLayoutGroup>();
            labels.spacing = 2f;
            labels.childAlignment = TextAnchor.MiddleLeft;
            // Force-expanding children would make this block flexible and push the cards to the far edge.
            labels.childForceExpandWidth = false;
            labels.childForceExpandHeight = false;
            labels.childControlWidth = true;
            labels.childControlHeight = true;
            var labelsElement = labelsGo.GetComponent<LayoutElement>();
            labelsElement.preferredWidth = ScoreNameColumnPx;
            labelsElement.flexibleWidth = 0f;

            var name = UiKit.CreateText(labelsGo.transform, player.Name.ToUpperInvariant(), 22,
                TextAnchor.MiddleLeft, Color.white);
            name.raycastTarget = false;
            UiKit.AddTextShadow(name);
            name.gameObject.AddComponent<LayoutElement>().minHeight = 28f;

            string points = total == 1 ? "1 VP" : $"{total} VP";
            var score = UiKit.CreateText(labelsGo.transform, $"{points}  ·  ${player.Money}", 17,
                TextAnchor.MiddleLeft, UiKit.ButtonColor, body: true);
            score.raycastTarget = false;
            UiKit.AddTextShadow(score);
            score.gameObject.AddComponent<LayoutElement>().minHeight = 22f;
        }

        /// <summary>Every card that scored, side by side; hover (or Alt) reads any of them.</summary>
        private void AddScoreRowCards(Transform row, PlayerView.PlayerPanel player, float cardHeight)
        {
            var cardsGo = new GameObject("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            cardsGo.transform.SetParent(row, false);
            var cards = cardsGo.GetComponent<HorizontalLayoutGroup>();
            cards.spacing = ScoreCardGapPx;
            cards.childAlignment = TextAnchor.MiddleLeft;
            cards.childForceExpandWidth = false;
            cards.childForceExpandHeight = false;
            cards.childControlWidth = true;
            cards.childControlHeight = true;
            float cardWidth = cardHeight * ScoreCardAspect;
            foreach (var art in ScoreCardArts(player))
            {
                var image = UiKit.CreateCardImage(cardsGo.transform, art, cardWidth, cardHeight);
                _preview.Attach(image.gameObject, art);
            }
        }

        /// <summary>In order: First Dibs, Bragging Rights, then the secret Lemon Lords actually met.</summary>
        private List<Texture2D> ScoreCardArts(PlayerView.PlayerPanel player)
        {
            var arts = new List<Texture2D>();
            foreach (string titleId in player.FirstDibsClaimed)
            {
                arts.Add(_art.Title(titleId));
            }
            for (int i = 0; i < player.BraggingRights; i++)
            {
                arts.Add(_art.BraggingRights(i) ?? _art.Back("braggingRights"));
            }
            foreach (var lord in player.FinishedLords)
            {
                if (lord.Met)
                {
                    arts.Add(_art.Title(lord.TitleId));
                }
            }
            return arts;
        }
    }
}
