using System.Collections.Generic;
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

        private sealed class StagedResponse
        {
            public RectTransform Chip;
            public SlapMotion Motion;
            public int OwnerId;
            public string DefId;
            public float TurnDegrees;
        }

        private readonly List<StagedResponse> _responses = new List<StagedResponse>();
        private float _slapUntil;
        /// <summary>0 = piled where slapped, 1 = fanned out for reading.</summary>
        private float _spreadBlend;

        /// <summary>
        /// A reaction played at the staged card, by anyone (the viewer included): it
        /// slaps down on top of the pile from the responder's bar. Ignored when nothing
        /// is staged — the caller shows a reveal instead.
        /// </summary>
        public bool AddResponse(Texture2D art, string defId, int responderId)
        {
            var bar = _barWorld(responderId);
            bool pileOpen = _phase == Phase.Arriving || _phase == Phase.Holding;
            if (!pileOpen || art == null || bar == null)
            {
                return false; // nothing on the table to land on (or it is already leaving)
            }
            var chip = BuildChip(art);
            PinPlayerBadge(chip, responderId, remember: false);
            chip.SetAsLastSibling();
            float turnDegrees = Random.Range(-ResponseTurnDegrees, ResponseTurnDegrees);
            chip.localEulerAngles = new Vector3(0, 0, turnDegrees);
            var motion = chip.gameObject.AddComponent<SlapMotion>();
            motion.From = Local(bar.Value);
            // Where the staged card IS, not where it was sent: the pile sits on the card.
            motion.To = _chip.anchoredPosition + new Vector2(
                Random.Range(-ResponseScatterPx, ResponseScatterPx),
                Random.Range(-ResponseScatterPx, ResponseScatterPx));
            chip.anchoredPosition = motion.From;
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

        /// <summary>True while a reaction is still coming down.</summary>
        private bool Slapping => Time.time < _slapUntil;

        /// <summary>
        /// Fan the pile out while the pointer rests on any card of it (the staged
        /// play included), and let it fall back into its slapped mess otherwise.
        /// Only landed cards are placed here; one still slapping down is the slap's.
        /// </summary>
        private void TickSpread()
        {
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
            // A stolen card beside the play stays put: the fan is slapped on later, so
            // it simply draws over the loot until it folds back.
        }

        private bool PointerOverPile()
        {
            Vector2 pointer = Input.mousePosition;
            if (RectTransformUtility.RectangleContainsScreenPoint(_chip, pointer))
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

        /// <summary>
        /// The staged play is leaving: whatever was thrown at it fades where it fell.
        /// Returns true when there was a pile to fade — the card under it waits for
        /// that fade before moving, or it slides out from under the pile mid-fade.
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
