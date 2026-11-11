using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The response pile: every reaction thrown at the staged play lands on top of it,
    // turned at a random angle and badged with its player, so a Tantrum on a Tantrum
    // on a Tag reads as the stack it is. Hovering the pile fans it out to the right
    // in play order, each card straightened and overlapping the last, to read it all.
    public sealed partial class PlayTheatre
    {
        private const float ResponseTurnDegrees = 25f;
        private const float ResponseScatterPx = 14f;
        /// <summary>After a reaction lands, the table holds this long so everyone sees it before the next move.</summary>
        private const float ReactionBreathSeconds = 0.75f;
        /// <summary>Fanned cards step this much of a card width apart: the badge and the left strip of each stay visible.</summary>
        private const float SpreadStepFraction = 0.7f;
        private const float SpreadSeconds = 0.18f;
        private const float PileReturnSeconds = 0.3f;

        private sealed class StagedResponse
        {
            public RectTransform Chip;
            public SlapMotion Motion;
            public int OwnerId;
            public string DefId;
            public float TurnDegrees;
            /// <summary>A card the viewer put on the pile themselves (a Rummage Sale pick); -1 for a reaction.</summary>
            public int Key = -1;
            /// <summary>The reaction has done its work: it leaves the pile once it has been seen to land.</summary>
            public bool FadeWhenSettled;
        }

        /// <summary>A keyed pile card was clicked: the owner decides whether it comes back off.</summary>
        public System.Action<int> OnPileCardClicked;

        private readonly List<StagedResponse> _responses = new List<StagedResponse>();
        private float _slapUntil;
        /// <summary>0 = piled where slapped, 1 = fanned out for reading.</summary>
        private float _spreadBlend;

        private bool IsPileOpen => _phase == Phase.Arriving || _phase == Phase.Holding;

        /// <summary>Covers the landing and the breath after it.</summary>
        private bool IsSlapping => Time.time < _slapUntil;

        /// <summary>
        /// A reaction played at the staged card, by anyone (the viewer included). False when
        /// nothing is staged to land on, so the caller shows a reveal instead.
        /// </summary>
        public bool AddResponse(Texture2D art, string defId, int responderId)
        {
            var bar = _barWorld(responderId);
            if (!IsPileOpen || art == null || bar == null)
            {
                return false;
            }
            var chip = BuildChip(art);
            PinPlayerBadge(chip, responderId, isStagedPlay: false);
            var motion = SlapOntoPile(chip, Local(bar.Value), out float turnDegrees);
            _responses.Add(new StagedResponse
            {
                Chip = chip,
                Motion = motion,
                OwnerId = responderId,
                DefId = defId,
                TurnDegrees = turnDegrees,
            });
            _slapUntil = Time.time + SlapMotion.SlapSeconds + ReactionBreathSeconds;
            return true;
        }

        /// <summary>
        /// The viewer's own pick slapped on from a spot on the board (a Rummage Sale card leaving its stand):
        /// no badge, clickable, and keyed so it can be sent back. False when nothing is staged to land on.
        /// </summary>
        public bool AddPileCard(Texture2D art, int key, Vector3 fromWorld)
        {
            if (!IsPileOpen || art == null)
            {
                return false;
            }
            var chip = BuildChip(art);
            var motion = SlapOntoPile(chip, Local(fromWorld), out float turnDegrees);
            // The art is the raycast target (and already relays hover for the
            // magnifier), so the click handler must sit on it, not on the frame.
            UiKit.AddClick(chip.GetComponentInChildren<RawImage>().gameObject,
                () => OnPileCardClicked?.Invoke(key));
            _responses.Add(new StagedResponse
            {
                Chip = chip,
                Motion = motion,
                OwnerId = -1,
                DefId = "",
                TurnDegrees = turnDegrees,
                Key = key,
            });
            return true;
        }

        /// <param name="toWorld">Where the card flies home to; null removes it on the spot.</param>
        public void ReturnPileCard(int key, Vector3? toWorld)
        {
            for (int i = _responses.Count - 1; i >= 0; i--)
            {
                var response = _responses[i];
                if (response.Key != key)
                {
                    continue;
                }
                _responses.RemoveAt(i);
                if (response.Chip == null)
                {
                    return;
                }
                if (toWorld == null)
                {
                    Object.Destroy(response.Chip.gameObject);
                    return;
                }
                FlyPileCardHome(response, toWorld.Value);
                return;
            }
        }

        public void ReturnAllPileCards(System.Func<int, Vector3?> homeWorld)
        {
            foreach (var key in _responses.Where(r => r.Key >= 0).Select(r => r.Key).ToList())
            {
                ReturnPileCard(key, homeWorld(key));
            }
        }

        /// <summary>
        /// A reaction that resolved while the play under it lives on (a Tantrum that stopped a Tantrum, a Tag
        /// that moved the attack) is spent: it fades off the pile after its landing beat.
        /// </summary>
        public void SettleResponse(int ownerId, string defId)
        {
            for (int i = _responses.Count - 1; i >= 0; i--)
            {
                var response = _responses[i];
                if (response.OwnerId == ownerId && response.DefId == defId && !response.FadeWhenSettled)
                {
                    response.FadeWhenSettled = true;
                    return;
                }
            }
        }

        /// <summary>A reaction that was itself cancelled (a counter-Tantrum) fades off the pile.</summary>
        public void CancelResponse(int ownerId, string defId)
        {
            for (int i = _responses.Count - 1; i >= 0; i--)
            {
                var response = _responses[i];
                if (response.OwnerId == ownerId && response.DefId == defId && response.Motion != null)
                {
                    response.Motion.BeginFade();
                    _responses.RemoveAt(i);
                    return;
                }
            }
        }

        public bool PointerOverPile()
        {
            Vector2 pointer = Input.mousePosition;
            if (_chip != null && RectTransformUtility.RectangleContainsScreenPoint(_chip, pointer))
            {
                return true;
            }
            foreach (var response in _responses)
            {
                if (response.Chip != null && RectTransformUtility.RectangleContainsScreenPoint(response.Chip, pointer))
                {
                    return true;
                }
            }
            return false;
        }

        private SlapMotion SlapOntoPile(RectTransform chip, Vector2 from, out float turnDegrees)
        {
            chip.SetAsLastSibling();
            turnDegrees = Random.Range(-ResponseTurnDegrees, ResponseTurnDegrees);
            chip.localEulerAngles = new Vector3(0, 0, turnDegrees);
            var motion = chip.gameObject.AddComponent<SlapMotion>();
            motion.From = from;
            // Where the staged card IS, not where it was sent: the pile sits on the card.
            motion.To = _chip.anchoredPosition + new Vector2(
                Random.Range(-ResponseScatterPx, ResponseScatterPx),
                Random.Range(-ResponseScatterPx, ResponseScatterPx));
            chip.anchoredPosition = motion.From;
            return motion;
        }

        private void FlyPileCardHome(StagedResponse response, Vector3 toWorld)
        {
            _preview.Hide();
            Object.Destroy(response.Motion);
            response.Chip.localEulerAngles = Vector3.zero;
            response.Chip.GetComponent<CanvasGroup>().blocksRaycasts = false;
            var flight = response.Chip.gameObject.AddComponent<FlightMotion>();
            flight.From = response.Chip.anchoredPosition;
            flight.To = Local(toWorld);
            flight.Duration = PileReturnSeconds;
            flight.ArcHeight = 0f;
            flight.VisibleWhileWaiting = true;
            flight.FadeOutFrom = 0.7f;
        }

        private void FadeSettledResponses()
        {
            if (IsSlapping)
            {
                return; // the landing and its breath belong to the card
            }
            for (int i = _responses.Count - 1; i >= 0; i--)
            {
                var response = _responses[i];
                if (response.FadeWhenSettled && response.Motion != null && response.Motion.Landed)
                {
                    response.Motion.BeginFade();
                    _responses.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Fans the pile out while the pointer rests on any card of it (the staged play included).
        /// Only landed cards are placed here; one still slapping down belongs to its slap.
        /// </summary>
        private void TickSpread()
        {
            FadeSettledResponses();
            bool wantsSpread = _responses.Count > 0 && _phase == Phase.Holding && PointerOverPile();
            _spreadBlend = Mathf.MoveTowards(_spreadBlend, wantsSpread ? 1f : 0f, Time.deltaTime / SpreadSeconds);
            float eased = Mathf.SmoothStep(0f, 1f, _spreadBlend);
            float step = CardWidth * SpreadStepFraction;
            for (int i = 0; i < _responses.Count; i++)
            {
                var response = _responses[i];
                if (response.Chip == null || response.Motion == null || !response.Motion.Landed)
                {
                    continue;
                }
                var fanned = _chip.anchoredPosition + new Vector2(step * (i + 1), 0f);
                response.Chip.anchoredPosition = Vector2.Lerp(response.Motion.To, fanned, eased);
                response.Chip.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(response.TurnDegrees, 0f, eased));
            }
            // Loot staged beside the play stays put: the fan simply draws over it until it folds back.
        }

        /// <summary>
        /// The staged play is leaving: whatever was thrown at it fades where it fell. True when there was a
        /// pile to fade; the card under it waits for that, or it slides out from under the pile mid-fade.
        /// </summary>
        private bool FadeResponses()
        {
            bool any = _responses.Count > 0;
            foreach (var response in _responses)
            {
                if (response.Motion != null)
                {
                    response.Motion.BeginFade();
                }
            }
            _responses.Clear();
            return any;
        }

        private void DestroyResponses()
        {
            foreach (var response in _responses)
            {
                if (response.Chip != null)
                {
                    Object.Destroy(response.Chip.gameObject);
                }
            }
            _responses.Clear();
        }
    }
}
