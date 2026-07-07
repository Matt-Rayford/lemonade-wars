using System.Collections.Generic;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// One-shot sound effects, loaded from Resources/sounds (synced from
    /// game-assets/sound-effects). Clip names are the file names without extension.
    /// Everything is fire-and-forget through a single hidden AudioSource.
    /// </summary>
    public static class Sfx
    {
        public const string CardDraw = "card-draw";
        public const string TitleClaim = "title-claim";
        public const string AttackCard = "attack-card";

        private static AudioSource _source;
        private static readonly Dictionary<string, AudioClip> Clips =
            new Dictionary<string, AudioClip>();

        public static void Play(string name, float volume = 1f)
        {
            if (_source == null)
            {
                var go = new GameObject("Sfx", typeof(AudioSource));
                Object.DontDestroyOnLoad(go);
                _source = go.GetComponent<AudioSource>();
                _source.playOnAwake = false;
                if (Object.FindFirstObjectByType<AudioListener>() == null)
                {
                    go.AddComponent<AudioListener>(); // code-built scene may lack one
                }
            }
            if (!Clips.TryGetValue(name, out var clip))
            {
                Clips[name] = clip = Resources.Load<AudioClip>("sounds/" + name);
            }
            if (clip != null)
            {
                _source.PlayOneShot(clip, volume);
            }
        }
    }
}
