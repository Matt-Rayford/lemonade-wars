using System.Collections.Generic;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Event-driven presentation effects (money floaters, more to come). Spawns queue up
    /// and are released only when the blocking theatre (dice, turn banner) has finished,
    /// so payout floaters don't play hidden under the dice overlay they belong after.
    /// Same-batch floaters on one player stack upward with a small cascade delay.
    /// </summary>
    public sealed class EffectsPlayer
    {
        private static readonly Color GainColor = new Color(0.45f, 0.95f, 0.50f);
        private static readonly Color LossColor = new Color(1f, 0.42f, 0.36f);

        private readonly RectTransform _layer;
        private readonly System.Func<bool> _held;
        private readonly System.Func<int, Vector3?> _anchorFor; // player id -> world pos
        private readonly Queue<(int PlayerId, int Amount)> _money =
            new Queue<(int, int)>();

        public EffectsPlayer(RectTransform canvasRoot, System.Func<bool> held,
            System.Func<int, Vector3?> anchorFor)
        {
            _held = held;
            _anchorFor = anchorFor;
            var go = new GameObject("Effects", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            _layer = (RectTransform)go.transform;
            UiKit.Anchor(_layer, Vector2.zero, Vector2.one);
        }

        public void QueueMoney(int playerId, int amount)
        {
            if (amount != 0)
            {
                _money.Enqueue((playerId, amount));
            }
        }

        /// <summary>Drop queued spawns (screen change, autopilot). Live floaters fade out.</summary>
        public void Clear()
        {
            _money.Clear();
        }

        /// <summary>Release pending effects once nothing is holding them; call every frame.</summary>
        public void Tick()
        {
            if (_money.Count == 0 || _held())
            {
                return;
            }
            var stacked = new Dictionary<int, int>();
            while (_money.Count > 0)
            {
                var (playerId, amount) = _money.Dequeue();
                stacked.TryGetValue(playerId, out int index);
                stacked[playerId] = index + 1;
                SpawnMoney(playerId, amount, index);
            }
        }

        private void SpawnMoney(int playerId, int amount, int stackIndex)
        {
            var world = _anchorFor(playerId);
            if (world == null)
            {
                return;
            }
            var text = UiKit.CreateText(_layer,
                (amount > 0 ? "+$" : "-$") + Mathf.Abs(amount), 26,
                TextAnchor.MiddleCenter, amount > 0 ? GainColor : LossColor, body: true);
            text.raycastTarget = false;
            text.fontStyle = TMPro.FontStyles.Bold;
            UiKit.AddTextShadow(text);

            var rect = (RectTransform)text.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(220, 40);
            Vector2 local = _layer.InverseTransformPoint(world.Value);
            rect.anchoredPosition = local + new Vector2(0, 8f + stackIndex * 26f);

            var motion = text.gameObject.AddComponent<FloaterMotion>();
            motion.Delay = stackIndex * 0.12f;
        }
    }

    /// <summary>Rise, linger, fade, self-destruct. Optional start delay for stacks.</summary>
    public sealed class FloaterMotion : MonoBehaviour
    {
        public float Delay;

        private const float RiseSpeed = 55f;
        private const float FadeStart = 0.75f;
        private const float Lifetime = 1.25f;

        private RectTransform _rect;
        private CanvasGroup _group;
        private float _elapsed;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed - Delay;
            if (t < 0f)
            {
                return;
            }
            _group.alpha = t < 0.15f
                ? t / 0.15f
                : 1f - Mathf.Clamp01((t - FadeStart) / (Lifetime - FadeStart));
            _rect.anchoredPosition += new Vector2(0, RiseSpeed * Time.deltaTime);
            if (t >= Lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
