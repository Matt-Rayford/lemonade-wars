using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Protocol
{
    /// <summary>
    /// Wire messages. Client -> server: create_room, join_room, add_bot, start_game, action.
    /// Server -> client: room, update, error. Everything is a JSON object with a "type" field.
    /// </summary>
    public static class MessageType
    {
        // client -> server
        public const string CreateRoom = "create_room";
        public const string JoinRoom = "join_room";
        public const string AddBot = "add_bot";
        public const string StartGame = "start_game";
        public const string Action = "action";

        // server -> client
        public const string Room = "room";
        public const string Update = "update";
        public const string Error = "error";
    }

    public sealed class SeatInfo
    {
        public int Seat { get; set; }
        public string Name { get; set; } = "";
        public bool IsBot { get; set; }
        public bool Connected { get; set; }
    }

    /// <summary>Room composition; sent on every lobby change and on (re)join.</summary>
    public sealed class RoomMessage
    {
        public string Type { get; set; } = MessageType.Room;
        public string Code { get; set; } = "";
        public int YourSeat { get; set; }
        /// <summary>Reconnect token — present only in messages to the seat it belongs to.</summary>
        public string Token { get; set; } = "";
        public bool Started { get; set; }
        public List<SeatInfo> Seats { get; set; } = new List<SeatInfo>();
    }

    /// <summary>Per-seat game state push: what happened, what you see, what you may do.</summary>
    public sealed class UpdateMessage
    {
        public string Type { get; set; } = MessageType.Update;
        public List<JObject> Events { get; set; } = new List<JObject>();
        /// <summary>The seat's PlayerView, serialized with WireCodec settings.</summary>
        public JObject View { get; set; }
        public List<MoveOption> Moves { get; set; } = new List<MoveOption>();
    }

    public sealed class MoveOption
    {
        public string Label { get; set; } = "";
        public JObject Action { get; set; }
    }

    public sealed class ErrorMessage
    {
        public string Type { get; set; } = MessageType.Error;
        public string Message { get; set; } = "";
    }
}
