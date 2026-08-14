using System.Collections.Generic;
using LemonadeWars.Protocol;

namespace LemonadeWars.Unity
{
    /// <summary>Client-side snapshot of the online room: seats, your place, pacing.</summary>
    public sealed class RemoteRoomState
    {
        public string Code = "";
        public int YourSeat = -1;
        public string Token = "";
        public bool Started;
        public List<SeatInfo> Seats = new List<SeatInfo>();
        /// <summary>Room-wide bot pacing, server-confirmed: "slow" / "medium" / "fast".</summary>
        public string Speed = "medium";
    }
}
