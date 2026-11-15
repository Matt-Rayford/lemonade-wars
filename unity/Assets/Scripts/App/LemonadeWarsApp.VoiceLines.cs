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

        /// <summary>Queued: a title often changes hands under a roll or a turn banner, and its line belongs with the card's own arrival.</summary>
        private void SpeakWhenTitleArrives(int newHolderId, string line)
        {
            var sessionAtQueueing = _session;
            string voice = _session.VoiceOf(newHolderId);
            _fx.QueueCallback(() =>
            {
                // Queued callbacks also fire when the queue is torn down: a game that was left says nothing.
                if (_session == sessionAtQueueing && _screen == Screen.Game)
                {
                    VoiceLines.Speak(voice, line);
                }
            });
        }

        /// <summary>
        /// The wail is the Baby's sound for a holder with nothing to say about it. One whose voice
        /// has a Whiniest Baby line says that instead: both at once is only noise.
        /// </summary>
        private void QueueWhiniestBabyWailFor(int holderId)
        {
            if (!VoiceLines.WillSpeak(_session.VoiceOf(holderId), WhiniestBabyLine))
            {
                _fx.QueueSound(Sfx.WhinyBaby);
            }
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
