using LemonadeWars.Engine.Core;
using LemonadeWars.Protocol;

namespace LemonadeWars.Server;

public sealed partial class Room
{
    /// <summary>This room as a My Games entry; null if the player is not seated here.</summary>
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

    /// <summary>Lobbies and running games both count against the per-player cap.</summary>
    public bool IsActiveFor(string playerId)
    {
        lock (_sync)
        {
            return (_game == null || _game.State.Stage != GameStage.Finished) &&
                   _seats.Any(s => s.PlayerId == playerId);
        }
    }

    /// <summary>
    /// Anyone connected or any recent activity keeps a game alive. Atomic with joins, which
    /// a retired room refuses, so the sweeper cannot race a returning player.
    /// </summary>
    public bool TryRetire(TimeSpan lobbyTtl, TimeSpan finishedTtl, TimeSpan staleTtl)
    {
        lock (_sync)
        {
            if (_retired)
            {
                return true;
            }
            var idleFor = DateTime.UtcNow - LastActivityUtc;
            bool anyConnected = _seats.Any(s => !s.IsBot && s.Connected);
            bool isFinished = _game != null && _game.State.Stage == GameStage.Finished;
            bool isEmptyExpiredLobby = _game == null && !anyConnected && idleFor > lobbyTtl;
            bool isFinishedPastGrace = isFinished && idleFor > finishedTtl;
            bool isAbandonedGame = _game != null && !isFinished && !anyConnected && idleFor > staleTtl;
            if (!isEmptyExpiredLobby && !isFinishedPastGrace && !isAbandonedGame)
            {
                return false;
            }
            _retired = true;
            return true;
        }
    }
}
