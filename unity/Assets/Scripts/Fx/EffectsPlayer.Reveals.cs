using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        /// <summary>Long enough to read both the title and the art of a card played on you; 1.7s was not.</summary>
        private const float RevealHoldSeconds = 2.0f;
        private const float RevealCardWidth = 230f;
        private const float RevealCardHeight = 322f;
        private static readonly Vector2 RevealCardCenter = new Vector2(0, 60f);

        private const float ClaimHoldSeconds = 1.0f;
        private const float ClaimFlySeconds = 0.35f;
        /// <summary>A claimed card lands at the lord fan's card width.</summary>
        private const float ClaimLandingWidth = 190f;

        private void StartReveal(RevealFx reveal)
        {
            UiKit.Clear(_revealCardHost);
            _revealTitle.text = reveal.Title;

            var image = UiKit.CreateCardImage(_revealCardHost, reveal.Art, RevealCardWidth, RevealCardHeight);
            image.raycastTarget = false;
            var frame = (RectTransform)image.transform.parent;
            frame.GetComponent<Image>().raycastTarget = false;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(RevealCardWidth, RevealCardHeight);
            frame.anchoredPosition = RevealCardCenter;
            frame.gameObject.AddComponent<PopIn>();
            var shadow = frame.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(0, -6f);

            _revealRoot.gameObject.SetActive(true);
            _busyUntil = Time.time + RevealHoldSeconds;
            _endActive = () => _revealRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// The busy window covers the hold and the flight, so the deferred re-render that
        /// adds the persistent card runs right as the flight lands.
        /// </summary>
        private void StartClaim(ClaimFx claim)
        {
            StartReveal(new RevealFx { Art = claim.Art, Title = claim.Title });

            var chip = BuildFlightChip(claim.Art, RevealCardWidth, RevealCardHeight);
            var deliver = FiringOnce(claim.OnDelivered);

            var motion = chip.gameObject.AddComponent<FlightMotion>();
            motion.From = RevealCardCenter;
            motion.Delay = ClaimHoldSeconds;
            motion.Duration = ClaimFlySeconds;
            motion.ArcHeight = 40f;
            motion.EndScale = ClaimLandingWidth / RevealCardWidth;
            motion.OnStart = () =>
            {
                motion.To = _layer.InverseTransformPoint(claim.Destination());
                _revealRoot.gameObject.SetActive(false); // the chip takes over seamlessly
            };
            motion.OnArrive = deliver;

            _busyUntil = Time.time + ClaimHoldSeconds + ClaimFlySeconds;
            _endActive = () =>
            {
                _revealRoot.gameObject.SetActive(false);
                if (chip != null)
                {
                    Object.Destroy(chip.gameObject); // skip-click: no orphaned flight
                }
                deliver(); // a skip still delivers: the card must never stay hidden
            };
        }
    }
}
