using System;

namespace LemonadeWars.Engine.Core
{
    /// <summary>Thrown when a submitted action is illegal in the current state.</summary>
    public sealed class InvalidActionException : Exception
    {
        public InvalidActionException(string message) : base(message)
        {
        }
    }
}
