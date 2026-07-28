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
        public const string ButtonClick = "button-click";
        public const string CashRegister = "cash-register";
        public const string Coins = "coins";
        public const string WhinyBaby = "whiny-baby";
        public const string YourTurn = "your-turn-notification";
        public const string SaleRoll = "sale-roll";
        public const string RefreshMarket = "refresh-black-market";

        /// <summary>The spoken pace for a bot-speed setting ("slow"/"medium"/"fast").</summary>
        public static string BotSpeed(string speed) => "bot-speeds/bot-speed-" + speed;

        /// <summary>Slider step, in percent — the whole volume model moves in fives.</summary>
        public const int VolumeStep = 5;

        private const string VolumePref = "lw_sfx_volume";
        private const string LegacyMutePref = "lw_sound"; // pre-slider on/off toggle

        private static AudioSource _source;
        private static readonly Dictionary<string, AudioClip> Clips =
            new Dictionary<string, AudioClip>();

        /// <summary>Effects volume, 0-100 in steps of 5. Persisted and applied globally.</summary>
        public static int Volume
        {
            get
            {
                int stored = PlayerPrefs.GetInt(VolumePref,
                    PlayerPrefs.GetInt(LegacyMutePref, 1) == 0 ? 0 : 100);
                return Mathf.Clamp(Mathf.RoundToInt(stored / (float)VolumeStep) * VolumeStep, 0, 100);
            }
            set
            {
                int level = Mathf.Clamp(Mathf.RoundToInt(value / (float)VolumeStep) * VolumeStep, 0, 100);
                PlayerPrefs.SetInt(VolumePref, level);
                PlayerPrefs.Save();
                Apply();
            }
        }

        /// <summary>Push the saved level onto the listener; call once at boot.</summary>
        public static void Apply()
        {
            AudioListener.volume = Volume / 100f;
        }

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
