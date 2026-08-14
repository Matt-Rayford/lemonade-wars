using LemonadeWars.Protocol;

namespace LemonadeWars.Server;

/// <summary>
/// Live identified connections, for cross-game turn alerts: a player at one table
/// (or idling on My Games) hears when a DIFFERENT game starts waiting on them.
/// </summary>
public sealed class ConnectionRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, List<Connection>> _byPlayer = new();

    public void Register(string playerId, Connection connection)
    {
        lock (_sync)
        {
            if (!_byPlayer.TryGetValue(playerId, out var list))
            {
                _byPlayer[playerId] = list = new List<Connection>();
            }
            if (!list.Contains(connection))
            {
                list.Add(connection);
            }
        }
    }

    public void Unregister(Connection connection)
    {
        lock (_sync)
        {
            foreach (var list in _byPlayer.Values)
            {
                list.Remove(connection);
            }
        }
    }

    /// <summary>Ping every live connection this player has; prune the dead ones.</summary>
    public void NotifyTurn(string playerId, string roomCode)
    {
        Connection[] targets;
        lock (_sync)
        {
            if (!_byPlayer.TryGetValue(playerId, out var list))
            {
                return;
            }
            list.RemoveAll(c => !c.Open);
            targets = list.ToArray();
        }
        foreach (var connection in targets)
        {
            _ = connection.SendAsync(new TurnAlertMessage { Code = roomCode });
        }
    }
}
