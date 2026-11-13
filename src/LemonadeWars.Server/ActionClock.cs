namespace LemonadeWars.Server;

/// <summary>One awaited seat's countdown. Time is held as what is left, not a deadline, so a room nobody is watching simply stops spending it.</summary>
internal sealed class ActionClock
{
    public TimeSpan Remaining { get; set; }
    /// <summary>How much longer a connected seat may take to report its table settled before the countdown starts regardless.</summary>
    public TimeSpan SettleGraceLeft { get; set; }
    public bool Running { get; set; }
    /// <summary>The moves the seat was offered when this clock was armed: a different offer is a new wait.</summary>
    public string WaitSignature { get; set; } = "";
}
