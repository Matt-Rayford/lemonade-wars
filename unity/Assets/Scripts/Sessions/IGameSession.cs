using System;
using System.Collections.Generic;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// What the UI talks to: local play wraps an in-process Game (<see cref="LocalGameSession"/>),
    /// remote play speaks the server protocol (<see cref="RemoteGameSession"/>). Either way the
    /// UI renders from PlayerView + typed legal moves.
    /// </summary>
    public interface IGameSession : IDisposable
    {
        /// <summary>Null before the game starts.</summary>
        PlayerView View { get; }
        IReadOnlyList<GameAction> Moves { get; }
        IReadOnlyList<string> Log { get; }
        /// <summary>Bumped on every change; the UI re-renders when it moves.</summary>
        int Revision { get; }
        int Seat { get; }
        bool HumanAutoplay { get; set; }
        event Action<GameEvent> EventEmitted;

        string LabelFor(GameAction move);
        void Submit(GameAction action);
        /// <summary>Advances bots / pumps the socket. Call every frame.</summary>
        void Tick();
        /// <summary>The BotFactory level name for a bot seat; null for humans.</summary>
        string BotLevelOf(int playerId);
    }
}
