using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    /// <summary>When a player's chosen voice speaks: as they play a Lemon card, and as they are handed the Whiniest Baby or Spoiled Rotten.</summary>
    public sealed partial class LemonadeWarsApp
    {
        // Named for the voice files: these two cards are handed out, never played from a hand.
        private const string WhiniestBabyLine = "whiniest-baby";
        private const string SpoiledRottenLine = "spoiled-rotten";

        /// <summary>The card is slapped down the moment it is played, so its line is said then too, queue or no queue.</summary>
        private void SpeakPlayedCard(LemonCardPlayed played) =>
            VoiceLines.Speak(_session.VoiceOf(played.PlayerId), played.DefId);

        /// <summary>
        /// Queued: a title often changes hands under a roll or a turn banner, and its line belongs
        /// with the card's own arrival. <paramref name="soundIfUnsaid"/> plays when no line does
        /// (no voice, voices off, or another line still in the air): the Baby's wail, which a
        /// holder with a line of their own says in words instead.
        /// </summary>
        private void SpeakWhenTitleArrives(int newHolderId, string line, string soundIfUnsaid = null)
        {
            var sessionAtQueueing = _session;
            string voice = _session.VoiceOf(newHolderId);
            _fx.QueueCallback(() =>
            {
                // Queued callbacks also fire when the queue is torn down: a game that was left says nothing.
                if (_session != sessionAtQueueing || _screen != Screen.Game)
                {
                    return;
                }
                if (!VoiceLines.Speak(voice, line) && soundIfUnsaid != null)
                {
                    Sfx.Play(soundIfUnsaid);
                }
            });
        }

        /// <summary>
        /// Every voice at the table is loaded before the first card lands, not during it. Read off
        /// the room's seats: online, the game's first view has not arrived yet when the table opens.
        /// </summary>
        private void PreloadTableVoices()
        {
            if (_remote == null)
            {
                VoiceLines.Preload(VoiceLines.VoiceForTable);
                return;
            }
            foreach (var seat in _remote.Room.Seats)
            {
                VoiceLines.Preload(_remote.VoiceOf(seat.Seat));
            }
        }
    }
}
