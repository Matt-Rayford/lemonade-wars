using System.Security.Cryptography;
using LemonadeWars.Engine.Ai;

namespace LemonadeWars.Server;

public sealed class Seat
{
    /// <summary>Mutable only pre-game: removing a lobby seat re-indexes the ones after it.</summary>
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Token { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    /// <summary>Durable public identity (PlayerRegistry); null for bots and legacy clients.</summary>
    public string? PlayerId { get; set; }
    public bool IsBot { get; set; }
    /// <summary>A BotFactory level; only read for bot seats.</summary>
    public string BotLevel { get; set; } = BotFactory.Medium;
    /// <summary>Bots are always ready; humans toggle in the lobby.</summary>
    public bool Ready { get; set; }
    public Connection? Client { get; set; }

    public bool Connected => Client is { Open: true };

    public Task SendAsync(object message) =>
        Client?.SendAsync(message) ?? Task.CompletedTask;
}
