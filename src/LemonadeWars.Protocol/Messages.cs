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
        public const string Hello = "hello";
        public const string CreateRoom = "create_room";
        public const string JoinRoom = "join_room";
        public const string AddBot = "add_bot";
        public const string RemoveBot = "remove_bot";
        public const string SetBotLevel = "set_bot_level";
        public const string Ready = "ready";
        public const string StartGame = "start_game";
        public const string Action = "action";
        public const string ListGames = "list_games";

        // server -> client
        public const string Welcome = "welcome";
        public const string Room = "room";
        public const string Update = "update";
        public const string Games = "games";
        public const string TurnAlert = "turn_alert";
        public const string Error = "error";
    }

    public sealed class SeatInfo
    {
        public int Seat { get; set; }
        public string Name { get; set; } = "";
        public bool IsBot { get; set; }
        /// <summary>easy / medium / hard for bot seats; empty for humans.</summary>
        public string BotLevel { get; set; } = "";
        public bool Connected { get; set; }
        public bool Ready { get; set; }
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

    /// <summary>One of the caller's games, for the My Games list.</summary>
    public sealed class GameSummary
    {
        public string Code { get; set; } = "";
        public List<string> Players { get; set; } = new List<string>();
        public int YourSeat { get; set; }
        public bool Started { get; set; }
        public bool Finished { get; set; }
        /// <summary>The game is waiting on YOUR input (turn or response window).</summary>
        public bool YourTurn { get; set; }
        /// <summary>Whose turn it is right now (empty pre-start / post-finish).</summary>
        public string TurnPlayerName { get; set; } = "";
    }

    /// <summary>Reply to hello: your durable identity and everything you're playing.</summary>
    public sealed class WelcomeMessage
    {
        public string Type { get; set; } = MessageType.Welcome;
        public string PlayerId { get; set; } = "";
        public string Name { get; set; } = "";
        public List<GameSummary> GamesList { get; set; } = new List<GameSummary>();
    }

    /// <summary>Reply to list_games: a fresh My Games snapshot.</summary>
    public sealed class GamesMessage
    {
        public string Type { get; set; } = MessageType.Games;
        public List<GameSummary> GamesList { get; set; } = new List<GameSummary>();
    }

    /// <summary>
    /// A game you're NOT currently watching needs your input. Sent once per turn edge
    /// to every other live connection of that player.
    /// </summary>
    public sealed class TurnAlertMessage
    {
        public string Type { get; set; } = MessageType.TurnAlert;
        public string Code { get; set; } = "";
    }
}
