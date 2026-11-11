using System;

namespace LemonadeWars.Engine.Core
{
    public sealed class InvalidActionException : Exception
    {
        public InvalidActionException(string message) : base(message)
        {
        }
    }
}
