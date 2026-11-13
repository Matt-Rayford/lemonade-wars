using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Game lifecycle: solo game start, entering a game, game speed, and the
    /// return trip to the menu. Connecting to a server is in ServerConnection.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private IReadOnlyList<(string Name, string Level)> _soloBots =
            new[] { ("Benny", "medium"), ("Cleo", "medium"), ("Dex", "medium") };
        /// <summary>The last solo game's hand-picked First Dibs row, so [N] restarts with the same one; null = dealt at random.</summary>
        private IReadOnlyList<string> _soloFirstDibs;

        // ------------------------------------------------------- game speed

        private const string SpeedPref = "lw_game_speed";
        private static readonly string[] SpeedOrder = { "slow", "medium", "fast" };

        private static float LocalStepSeconds(string speed) =>
            speed == "slow" ? 2.0f : speed == "fast" ? 0.45f : 1.0f;

        /// <summary>Local games follow the saved preference; online rooms follow the server.</summary>
        private string CurrentSpeed() =>
            _remote != null && _screen == Screen.Game
                ? (string.IsNullOrEmpty(_remote.Room.Speed) ? "medium" : _remote.Room.Speed)
                : PlayerPrefs.GetString(SpeedPref, "medium");

        private void CycleSpeed()
        {
            string next = SpeedOrder[
                (System.Array.IndexOf(SpeedOrder, CurrentSpeed()) + 1) % SpeedOrder.Length];
            PlayerPrefs.SetString(SpeedPref, next);
            PlayerPrefs.Save();
            Sfx.Play(Sfx.BotSpeed(next));
            if (_session is LocalGameSession local)
            {
                local.BotStepSeconds = LocalStepSeconds(next);
            }
            else
            {
                _remote?.SetSpeed(next); // the room broadcast confirms + syncs everyone
            }
        }

        // ------------------------------------------------- start and finish

        private void StartLocalGame(IReadOnlyList<(string Name, string Level)> bots,
            IReadOnlyList<string> firstDibs)
        {
            _soloBots = bots.ToList();
            _soloFirstDibs = firstDibs;
            var names = new List<string> { _lobby.DisplayName };
            names.AddRange(_soloBots.Select(b => b.Name));

            _remote?.Dispose();
            _remote = null;
            _session?.Dispose();
            var localSession = new LocalGameSession(_db, names.ToArray(), 0,
                (ulong)System.DateTime.Now.Ticks,
                _soloBots.Select(b => b.Level).ToList(), firstDibs);
            localSession.BotStepSeconds = LocalStepSeconds(PlayerPrefs.GetString(SpeedPref, "medium"));
            localSession.HoldBots = () => _dice.IsBusy || _fx.IsBusy || _turnBanner.IsOpen || _playTheatre.IsBusy;
            _session = localSession;
            _session.EventEmitted += OnGameEvent;
            EnterGame();
        }

        private void EnterGame()
        {
            _screen = Screen.Game;
            SaveResume();
            _lobby.HideAll();
            _table.SetVisible(true);
            _prompt.Hide();
            _picker.Hide();
            _reaction.Hide();
            _turnBanner.Hide();
            _dice.Clear();
            _fx.Clear();
            _playTheatre.Clear();
            // The per-game reset checklist: add every new field that holds one game's state here.
            _earningsInFlight.Clear();
            _spendingHeld.Clear();
            _turnStartRollWaiting = false;
            _rollDraws.Clear();
            _rollEarnings = null;
            _table.ViewedBoardPlayer = -1;
            _table.SuppressWhiniestBaby = false; // a dropped mid-flight claim must not hide it forever
            _table.SuppressSpoiledRotten = false;
            _sale = null;
            _preStagedDefId = null;
            _lastShownBanner = null;
            _table.EndRummagePick();
            _table.StagedHandCardId = null;
            // Instance ids collide across games: a stale shelf hold would keep a market
            // slot empty forever, and a stale snapshot would diff garbage.
            _hiddenMarketIds.Clear();
            _liftedMarketIds.Clear();
            _handCardsInFlight.Clear();
            _equippedInFlight.Clear();
            _afterTurnBanner.Clear();
            _shelfDeparture = null;
            _timeoutCardInFlight = false;
            _timeoutCardShownAt = -100f;
            _liftedCardLanding = null;
            _marketStandIns.Clear();
            _marketSnap = null;
            _supplyTopSnap = null;
            _lastSpenderId = -1;
            _renderedRevision = -1;
            _modalRevision = -1;
            _wasMyTurn = false;
            _actionLog.Clear();
            _drawRunText = null;
            _endShown = false;
        }

        private void BackToMenu(string status)
        {
            if (_remote != null)
            {
                _remote.Leave(); // frees a lobby seat; disposes itself after sending
                if (string.IsNullOrEmpty(status))
                {
                    // A chosen exit has nothing to resume; an error exit keeps the record
                    // so a dropped game can be picked back up.
                    PlayerPrefs.DeleteKey(ResumeCodePref);
                    PlayerPrefs.DeleteKey(ResumeTokenPref);
                    PlayerPrefs.Save();
                }
            }
            else
            {
                _session?.Dispose();
            }
            _session = null;
            _remote = null;
            _screen = Screen.Menu;
            UiKit.Clear(_turnActionRow);
            _table.DropShelfGhosts();
            _table.HideTimeoutCard();
            _table.SetVisible(false);
            _prompt.Hide();
            _picker.Hide();
            _turnBanner.Hide();
            _dice.Clear();
            _fx.Clear();
            _playTheatre.Clear();
            _earningsInFlight.Clear();
            _spendingHeld.Clear();
            _turnStartRollWaiting = false;
            _rollDraws.Clear();
            _rollEarnings = null;
            _lobby.ShowMenu(status);
        }
    }
}
