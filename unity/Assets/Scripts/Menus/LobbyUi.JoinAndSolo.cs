using System.Linq;
using TMPro;
using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class LobbyUi
    {
        private const int RoomCodeLength = 5;
        private const int RoomCodeFontSize = 52;

        // ---------------------------------------------------- join room

        private void BuildJoinRoom(RectTransform canvasRoot)
        {
            _joinRoot = UiKit.CreatePanel(canvasRoot, "JoinRoom", UiKit.TableColor);
            UiKit.Anchor(_joinRoot, Vector2.zero, Vector2.one);
            CreateScreenTitle(_joinRoot, "JOIN ROOM");

            var codeLabel = UiKit.CreateText(_joinRoot, "ROOM CODE", 24,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.8f));
            UiKit.Anchor((RectTransform)codeLabel.transform, new Vector2(0.3f, 0.60f), new Vector2(0.7f, 0.67f));

            _codeInput = UiKit.CreateInput(_joinRoot, "ABCDE");
            var codeRect = (RectTransform)_codeInput.transform;
            UiKit.Anchor(codeRect, new Vector2(0.37f, 0.46f), new Vector2(0.63f, 0.58f));
            _codeInput.characterLimit = RoomCodeLength;
            // Typed or pasted, each character lands upper-cased: what you see is what the server looks up.
            _codeInput.onValidateInput += (text, index, added) => char.ToUpperInvariant(added);
            var codeText = _codeInput.textComponent;
            codeText.fontSize = RoomCodeFontSize;
            codeText.alignment = TextAlignmentOptions.Center;
            if (_codeInput.placeholder is TMP_Text codePlaceholder)
            {
                codePlaceholder.fontSize = RoomCodeFontSize;
                codePlaceholder.alignment = TextAlignmentOptions.Center;
            }

            var joinActions = CreateButtonRow(_joinRoot, "JoinActions",
                new Vector2(0.34f, 0.30f), new Vector2(0.66f, 0.38f));
            UiKit.CreateButton(joinActions, "Back", 20, () => ShowMenu(""));
            UiKit.CreateButton(joinActions, "Join", 20,
                () => OnJoin?.Invoke(ServerUrl, DisplayName,
                    _codeInput.text.Trim().ToUpperInvariant()));
            _joinRoot.gameObject.SetActive(false);
        }

        public void ShowJoin()
        {
            ActivateOnly(_joinRoot);
            _codeInput.text = "";
            _codeInput.ActivateInputField();
        }

        // --------------------------------------------------- solo setup

        private void BuildSoloSetup(RectTransform canvasRoot)
        {
            _soloRoot = UiKit.CreatePanel(canvasRoot, "SoloSetup", UiKit.TableColor);
            UiKit.Anchor(_soloRoot, Vector2.zero, Vector2.one);
            CreateScreenTitle(_soloRoot, "PLAY VS BOTS");

            _soloSeatList = CreateSeatList(_soloRoot, new Vector2(0.32f, 0.18f), new Vector2(0.68f, 0.72f));

            var soloActions = CreateButtonRow(_soloRoot, "SoloActions",
                new Vector2(0.30f, 0.05f), new Vector2(0.70f, 0.13f));
            UiKit.CreateButton(soloActions, "Back", 20, () => ShowMenu(""));
            UiKit.CreateButton(soloActions, "Start game", 20,
                () => OnStartSolo?.Invoke(_soloBots.ToList()));
            _soloRoot.gameObject.SetActive(false);
        }

        public void ShowSoloSetup()
        {
            ActivateOnly(_soloRoot);
            RefreshSoloSeats();
        }

        private void RefreshSoloSeats()
        {
            UiKit.Clear(_soloSeatList);
            AddSeatRow(_soloSeatList, $"1. {DisplayName}", null, isYou: true, seat: 0);
            for (int i = 0; i < _soloBots.Count; i++)
            {
                int index = i;
                // The last remaining bot cannot be removed: 2 players minimum.
                AddSeatRow(_soloSeatList, $"{i + 2}. {_soloBots[i].Name}",
                    _soloBots.Count > 1
                        ? () =>
                        {
                            _soloBots.RemoveAt(index);
                            RefreshSoloSeats();
                        }
                        : (System.Action)null,
                    _soloBots[index].Level,
                    () =>
                    {
                        _soloBots[index] = (_soloBots[index].Name,
                            NextLevel(_soloBots[index].Level));
                        RefreshSoloSeats();
                    },
                    seat: index + 1);
            }
            if (_soloBots.Count < BotNames.Length)
            {
                UiKit.CreateButton(_soloSeatList, "+ Add bot", 18, () =>
                {
                    _soloBots.Add((BotNames.First(name => _soloBots.All(bot => bot.Name != name)),
                        "medium"));
                    RefreshSoloSeats();
                });
            }
        }
    }
}
