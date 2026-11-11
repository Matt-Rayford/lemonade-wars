namespace LemonadeWars.Server;

public sealed partial class PlayerRegistry
{
    public sealed record Player(string PlayerId, string Name);
}
