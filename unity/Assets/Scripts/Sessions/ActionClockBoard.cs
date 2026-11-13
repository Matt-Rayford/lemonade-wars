using System.Collections.Generic;
using LemonadeWars.Protocol;

namespace LemonadeWars.Unity
{
    /// <summary>The room's action timers as last heard from the server, counted down locally in between.</summary>
    public sealed class ActionClockBoard
    {
        private struct Clock
        {
            public bool Running;
            /// <summary>On the caller's clock; only meaningful while running.</summary>
            public float ExpiresAtSeconds;
        }

        private readonly Dictionary<int, Clock> _clocks = new Dictionary<int, Clock>();

        /// <summary>The server update the clocks belong to.</summary>
        public int StateVersion { get; private set; }

        /// <summary>
        /// Messages can overtake one another: clocks older than the ones held are dropped, and
        /// among clocks of the same update a countdown that has started is never un-started.
        /// </summary>
        public void Hear(int stateVersion, IReadOnlyList<ActionClockInfo> clocks, float nowSeconds)
        {
            if (stateVersion < StateVersion)
            {
                return;
            }
            bool isSameUpdate = stateVersion == StateVersion;
            var heard = new Dictionary<int, Clock>();
            foreach (var info in clocks)
            {
                bool alreadyRunning = isSameUpdate &&
                    _clocks.TryGetValue(info.Seat, out var held) && held.Running;
                heard[info.Seat] = alreadyRunning && !info.Running
                    ? _clocks[info.Seat]
                    : new Clock
                    {
                        Running = info.Running,
                        ExpiresAtSeconds = nowSeconds + (float)info.SecondsLeft,
                    };
            }
            _clocks.Clear();
            foreach (var entry in heard)
            {
                _clocks[entry.Key] = entry.Value;
            }
            StateVersion = stateVersion;
        }

        public void Clear()
        {
            _clocks.Clear();
            StateVersion = 0;
        }

        /// <summary>Null unless the seat's countdown is running.</summary>
        public float? SecondsLeft(int seat, float nowSeconds) =>
            _clocks.TryGetValue(seat, out var clock) && clock.Running
                ? System.Math.Max(0f, clock.ExpiresAtSeconds - nowSeconds)
                : (float?)null;

        /// <summary>The seat has a clock the server is holding until its table settles.</summary>
        public bool IsHeldForSettling(int seat) =>
            _clocks.TryGetValue(seat, out var clock) && !clock.Running;
    }
}
