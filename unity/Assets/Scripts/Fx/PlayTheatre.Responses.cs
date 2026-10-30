using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The response pile: every reaction thrown at the staged play lands on top of it,
    // turned at a random angle and badged with its player, so a Tantrum on a Tantrum
    // on a Tag reads as the stack it is.
    public sealed partial class PlayTheatre
    {
        private const float ResponseTurnDegrees = 25f;
        private const float ResponseScatterPx = 14f;
        /// <summary>After a reaction lands, the table holds this long so everyone sees it before the next move.</summary>
        private const float ReactionBreathSeconds = 0.75f;

        private readonly List<(RectTransform Chip, SlapMotion Motion, int OwnerId, string DefId)> _responses =
            new List<(RectTransform, SlapMotion, int, string)>();
        private float _slapUntil;

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
            chip.localEulerAngles = new Vector3(0, 0, Random.Range(-ResponseTurnDegrees, ResponseTurnDegrees));
            var motion = chip.gameObject.AddComponent<SlapMotion>();
            motion.From = Local(bar.Value);
            // Where the staged card IS, not where it was sent: the pile sits on the card.
            motion.To = _chip.anchoredPosition + new Vector2(
                Random.Range(-ResponseScatterPx, ResponseScatterPx),
                Random.Range(-ResponseScatterPx, ResponseScatterPx));
            chip.anchoredPosition = motion.From;
            _responses.Add((chip, motion, responderId, defId));
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
