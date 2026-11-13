namespace LemonadeWars.Unity
{
    /// <summary>Something under the pointer that could be picked up right now; the cursor opens its hand over it.</summary>
    public interface IDragCue
    {
        bool InvitesDrag { get; }
    }
}
