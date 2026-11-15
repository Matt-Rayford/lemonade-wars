using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// One voice's recorded lines, sorted by the card each belongs to. A file is named for its
    /// card's id, then any of these tags, dash-separated: "explicit", "male" or "female" (who the
    /// line is said to), and a number that tells takes of the same line apart.
    /// </summary>
    public sealed class VoiceLineBank
    {
        /// <summary>With explicit lines allowed, how often one is chosen over a clean line: a seasoning, not the meal.</summary>
        public const float ExplicitShare = 0.3f;
        /// <summary>Recordings still waiting for a card to belong to.</summary>
        private const string UnusedPrefix = "unused";
        private const string ExplicitTag = "explicit";
        private static readonly string[] SpokenToTags = { "male", "female" };

        private readonly struct Line
        {
            public readonly AudioClip Clip;
            public readonly bool IsExplicit;
            /// <summary>"male" / "female" when the line addresses one; null for a line that fits anyone.
            /// Nothing reads it yet: players have no gender until avatars arrive, so these are picked like any other take.</summary>
            public readonly string SpokenTo;

            public Line(AudioClip clip, bool isExplicit, string spokenTo)
            {
                Clip = clip;
                IsExplicit = isExplicit;
                SpokenTo = spokenTo;
            }
        }

        private readonly Dictionary<string, List<Line>> _linesByCard = new Dictionary<string, List<Line>>();

        public VoiceLineBank(IEnumerable<AudioClip> clips)
        {
            foreach (var clip in clips)
            {
                if (clip.name.StartsWith(UnusedPrefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string cardId = ReadFileName(clip.name, out bool isExplicit, out string spokenTo);
                if (!_linesByCard.TryGetValue(cardId, out var lines))
                {
                    _linesByCard[cardId] = lines = new List<Line>();
                }
                lines.Add(new Line(clip, isExplicit, spokenTo));
            }
        }

        /// <summary>
        /// A line for the card, every take equally likely. With explicit lines allowed they are chosen
        /// <see cref="ExplicitShare"/> of the time and the clean takes the rest. Null when the card has no line to say.
        /// </summary>
        public AudioClip PickFor(string cardId, bool allowExplicit)
        {
            if (!_linesByCard.TryGetValue(cardId, out var lines))
            {
                return null;
            }
            var explicitTakes = lines.Where(line => line.IsExplicit).ToList();
            bool goesExplicit = allowExplicit && explicitTakes.Count > 0 && Random.value < ExplicitShare;
            var takes = goesExplicit ? explicitTakes : lines.Where(line => !line.IsExplicit).ToList();
            return takes.Count == 0 ? null : takes[Random.Range(0, takes.Count)].Clip;
        }

        /// <summary>The card has a line that can always be said, whatever the listener's explicit setting.</summary>
        public bool HasCleanLineFor(string cardId) =>
            _linesByCard.TryGetValue(cardId, out var lines) && lines.Any(line => !line.IsExplicit);

        /// <summary>Any clean line at all, to let a player hear a voice before choosing it.</summary>
        public AudioClip PickSample()
        {
            var clean = _linesByCard.Values.SelectMany(lines => lines).Where(line => !line.IsExplicit).ToList();
            return clean.Count == 0 ? null : clean[Random.Range(0, clean.Count)].Clip;
        }

        /// <summary>Peels the tags off the end of a file name; what is left is the card's id.</summary>
        private static string ReadFileName(string fileName, out bool isExplicit, out string spokenTo)
        {
            isExplicit = false;
            spokenTo = null;
            var words = fileName.ToLowerInvariant().Split('-').ToList();
            while (words.Count > 1)
            {
                string last = words[words.Count - 1];
                if (last == ExplicitTag)
                {
                    isExplicit = true;
                }
                else if (SpokenToTags.Contains(last))
                {
                    spokenTo = last;
                }
                else if (!last.All(char.IsDigit))
                {
                    break;
                }
                words.RemoveAt(words.Count - 1);
            }
            return string.Join("-", words);
        }
    }
}
