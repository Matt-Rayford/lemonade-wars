using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The voices players' cards speak in, loaded from Resources/voices/&lt;voice&gt; (synced from
    /// game-assets/audio by tools/sync_unity.sh), and this player's settings for them: whether
    /// voices are on at all, which voice their own cards use, how loud voices are here, and
    /// whether explicit lines may play.
    /// </summary>
    public static class VoiceLines
    {
        /// <summary>The voice setting for a player who speaks with none.</summary>
        public const string NoVoice = "";

        private const string HearVoicesPref = "lw_voices_on";
        private const string MyVoicePref = "lw_voice";
        private const string ExplicitPref = "lw_voice_explicit";
        private const string VolumePref = "lw_voice_volume";
        /// <summary>In percent, like the Sound Effects level.</summary>
        public const int VolumeStep = 5;
        /// <summary>One voice id per line: Resources folders cannot be listed at runtime.</summary>
        private const string IndexResource = "voices/index";

        /// <summary>After a line ends, the table stays quiet of new ones this long: cards land in bursts, and line after line is noise.</summary>
        private const float QuietAfterLineSeconds = 3f;

        private static List<string> _available;
        private static AudioSource _channel;
        /// <summary>When the line now in the air, and the quiet owed after it, will be over.</summary>
        private static float _quietUntil = float.NegativeInfinity;
        private static readonly Dictionary<string, VoiceLineBank> Banks = new Dictionary<string, VoiceLineBank>();

        /// <summary>Every voice that shipped with this build, by id (its folder name).</summary>
        public static IReadOnlyList<string> Available
        {
            get
            {
                if (_available == null)
                {
                    var index = Resources.Load<TextAsset>(IndexResource);
                    _available = (index == null ? "" : index.text)
                        .Split('\n')
                        .Select(line => line.Trim())
                        .Where(line => line.Length > 0)
                        .ToList();
                }
                return _available;
            }
        }

        /// <summary>
        /// The master switch, off until the player turns it on: whether they hear anyone's lines,
        /// and whether their own voice goes out to the table at all.
        /// </summary>
        public static bool HearVoices
        {
            get => PlayerPrefs.GetInt(HearVoicesPref, 0) != 0;
            set => SaveFlag(HearVoicesPref, value);
        }

        /// <summary>What the table is told this player sounds like: their chosen voice, or none while they have voices off.</summary>
        public static string VoiceForTable => HearVoices ? MyVoice : NoVoice;

        /// <summary>
        /// The voice this player has chosen for their cards; <see cref="NoVoice"/> until they pick
        /// one. A saved voice this build no longer ships counts as none. Kept while voices are
        /// switched off, so switching them back on restores it.
        /// </summary>
        public static string MyVoice
        {
            get
            {
                string saved = PlayerPrefs.GetString(MyVoicePref, NoVoice);
                return Available.Contains(saved) ? saved : NoVoice;
            }
            set
            {
                PlayerPrefs.SetString(MyVoicePref, value ?? NoVoice);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Lets the explicit takes into the mix for this listener. Off until they ask for it.</summary>
        public static bool ExplicitMode
        {
            get => PlayerPrefs.GetInt(ExplicitPref, 0) != 0;
            set => SaveFlag(ExplicitPref, value);
        }

        /// <summary>
        /// 0-100 in steps of <see cref="VolumeStep"/>: how loud every voice is on this client only,
        /// under the Sound Effects level. A line already being said changes with it.
        /// </summary>
        public static int Volume
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(VolumePref, 100) / VolumeStep * VolumeStep, 0, 100);
            set
            {
                PlayerPrefs.SetInt(VolumePref, Mathf.Clamp(value, 0, 100));
                PlayerPrefs.Save();
                if (_channel != null)
                {
                    _channel.volume = Volume / 100f;
                }
            }
        }

        /// <summary>A line is being said right now.</summary>
        public static bool IsSpeaking => _channel != null && _channel.isPlaying;

        /// <summary>"kevin" as the settings screen spells it.</summary>
        public static string DisplayName(string voice) =>
            string.IsNullOrEmpty(voice) ? "None" : char.ToUpperInvariant(voice[0]) + voice.Substring(1);

        /// <summary>
        /// Says the voice's line for a card, if this listener hears voices and the voice has one,
        /// and no line is still being said or ended in the last few seconds: this one then goes
        /// unsaid, not said late.
        /// </summary>
        /// <returns>Whether a line was said, for sounds a spoken line stands in for.</returns>
        public static bool Speak(string voice, string cardId)
        {
            bool isTooSoon = Time.unscaledTime < _quietUntil;
            if (!HearVoices || isTooSoon || !(BankOf(voice) is VoiceLineBank bank))
            {
                return false;
            }
            var line = bank.PickFor(cardId, ExplicitMode);
            Say(line);
            return line != null;
        }

        /// <summary>Plays any one clean line in place of whatever is being said, so a voice can be judged as it is chosen.</summary>
        public static void Audition(string voice)
        {
            if (BankOf(voice) is VoiceLineBank bank)
            {
                if (_channel != null)
                {
                    _channel.Stop(); // samples follow clicks: they must not pile up
                }
                Say(bank.PickSample());
            }
        }

        /// <summary>Lines overlap when players talk over each other, as they would. Null says nothing.</summary>
        private static void Say(AudioClip line)
        {
            if (line == null)
            {
                return;
            }
            if (_channel == null)
            {
                _channel = Sfx.CreateChannel("Voices");
            }
            _channel.volume = Volume / 100f;
            _channel.PlayOneShot(line);
            _quietUntil = Time.unscaledTime + line.length + QuietAfterLineSeconds;
        }

        /// <summary>Loads a voice's clips ahead of its first line, which would otherwise hitch as it is said.</summary>
        public static void Preload(string voice)
        {
            if (HearVoices)
            {
                BankOf(voice);
            }
        }

        /// <summary>Null for no voice, and for one this build does not ship (a newer client's, say).</summary>
        private static VoiceLineBank BankOf(string voice)
        {
            if (string.IsNullOrEmpty(voice) || !Available.Contains(voice))
            {
                return null;
            }
            if (!Banks.TryGetValue(voice, out var bank))
            {
                Banks[voice] = bank = new VoiceLineBank(Resources.LoadAll<AudioClip>("voices/" + voice));
            }
            return bank;
        }

        private static void SaveFlag(string pref, bool isOn)
        {
            PlayerPrefs.SetInt(pref, isOn ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
