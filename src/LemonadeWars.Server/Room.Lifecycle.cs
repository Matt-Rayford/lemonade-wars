using LemonadeWars.Engine.Core;
using LemonadeWars.Protocol;

namespace LemonadeWars.Server;

// Per-player summaries and end-of-life: My Games entries, activity checks, retirement.
public sealed partial class Room
{
    /// <summary>This room as a My Games entry for one player; null if they're not in it.</summary>
    public GameSummary? SummaryFor(string playerId)
    {
        lock (_sync)
        {
            var seat = _seats.FirstOrDefault(s => s.PlayerId == playerId);
            if (seat == null)
            {
                return null;
            }
            bool finished = _game != null && _game.State.Stage == GameStage.Finished;
            var acting = _game != null && !finished
                ? _game.ActingPlayers()
                : Array.Empty<int>() as IReadOnlyList<int>;
            int turnPlayer = _game != null && !finished ? _game.State.ActivePlayer : -1;
            return new GameSummary
            {
                Code = Code,
                Players = _seats.Select(s => s.Name).ToList(),
                YourSeat = seat.Index,
                Started = _game != null,
                Finished = finished,
                YourTurn = acting.Contains(seat.Index),
                TurnPlayerName = turnPlayer >= 0 && turnPlayer < _seats.Count
                    ? _seats[turnPlayer].Name
                    : "",
            };
        }
    }

    /// <summary>Not finished (lobby or running) — counts against the per-player cap.</summary>
    public bool IsActiveFor(string playerId)
    {
        lock (_sync)
        {
            return (_game == null || _game.State.Stage != GameStage.Finished) &&
                   _seats.Any(s => s.PlayerId == playerId);
        }
    }

    /// <summary>
    /// Atomically mark this room dead if it qualifies: an empty lobby past its TTL, a
    /// finished game past its grace, or an abandoned running game past the stale window.
    /// A live async game — anyone connected, or any recent activity — never qualifies.
    /// Once retired, joins are refused, so the sweeper can't race a returning player.
    /// </summary>
    public bool TryRetire(TimeSpan lobbyTtl, TimeSpan finishedTtl, TimeSpan staleTtl)
    {
        lock (_sync)
        {
            if (_retired)
            {
                return true;
            }
            var age = DateTime.UtcNow - LastActivityUtc;
            bool anyConnected = _seats.Any(s => !s.IsBot && s.Connected);
            bool eligible =
                (_game == null && !anyConnected && age > lobbyTtl) ||
                (_game != null && _game.State.Stage == GameStage.Finished && age > finishedTtl) ||
                (_game != null && _game.State.Stage != GameStage.Finished &&
                 !anyConnected && age > staleTtl);
            if (!eligible)
            {
                return false;
            }
            _retired = true;
            return true;
        }
    }
}
