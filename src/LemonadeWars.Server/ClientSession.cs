using System.Net.WebSockets;
using System.Text;
using LemonadeWars.Protocol;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Server;

/// <summary>One connected socket: routes messages to a room until the socket closes.</summary>
public sealed class ClientSession
{
    private const int MaxMessageBytes = 64 * 1024;
    private const int MaxActiveGamesPerPlayer = 5;
    /// <summary>Every new key mints a registry record, so a hello flood would grow it forever.</summary>
    private const int MaxHellosPerConnection = 5;

    /// <summary>MAX_ROOMS: the cap for clients that never say hello, whom the per-player cap cannot bind.</summary>
    private static int MaxRooms =>
        int.TryParse(Environment.GetEnvironmentVariable("MAX_ROOMS"), out int maxRooms) ? maxRooms : 2000;

    /// <summary>
    /// RATE_LIMIT_PER_SEC; 0 disables (tests play whole games through one socket). Read
    /// per connection, not at type load, so test env setup cannot race a static initializer.
    /// </summary>
    private static double MessagesPerSecond =>
        double.TryParse(Environment.GetEnvironmentVariable("RATE_LIMIT_PER_SEC"), out double rate)
            ? rate
            : 10;

    private readonly WebSocket _socket;
    private readonly Connection _connection;
    private readonly RoomManager _rooms;
    private readonly PlayerRegistry _players;
    private readonly ConnectionRegistry _connections;
    private readonly RateLimiter _rateLimiter = new(MessagesPerSecond);

    private Room? _room;
    private PlayerRegistry.Player? _identity;
    private int _helloCount;

    private ClientSession(WebSocket socket, RoomManager rooms, PlayerRegistry players,
        ConnectionRegistry connections)
    {
        _socket = socket;
        _connection = new Connection(socket);
        _rooms = rooms;
        _players = players;
        _connections = connections;
    }

    public static Task RunAsync(WebSocket socket, RoomManager rooms,
        PlayerRegistry players, ConnectionRegistry connections) =>
        new ClientSession(socket, rooms, players, connections).RunUntilClosedAsync();

    private async Task RunUntilClosedAsync()
    {
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                string? text = await ReceiveTextAsync(_socket);
                if (text == null)
                {
                    break;
                }

                if (!_rateLimiter.TryTake())
                {
                    await SendErrorAsync("Too many messages — slow down.");
                    continue;
                }

                if (!TryParseJson(text, out var message))
                {
                    await SendErrorAsync("Malformed JSON.");
                    continue;
                }

                await DispatchAsync(message);
            }
        }
        catch (WebSocketException)
        {
            // The client vanished; the finally block detaches it.
        }
        finally
        {
            _room?.Detach(_connection);
            _connections.Unregister(_connection);
        }
    }

    /// <summary>A message whose guard fails (no room yet, no identity yet) gets the out-of-order error.</summary>
    private async Task DispatchAsync(JObject message)
    {
        var room = _room;
        switch ((string?)message["type"])
        {
            case MessageType.Hello:
                await HandleHelloAsync(message);
                break;
            case MessageType.ListGames when _identity != null:
                await SendAsync(new GamesMessage { GamesList = _rooms.GamesFor(_identity.PlayerId) });
                break;
            case MessageType.CreateRoom:
                await HandleCreateRoomAsync(message);
                break;
            case MessageType.JoinRoom:
                await HandleJoinRoomAsync(message);
                break;
            case MessageType.LeaveRoom when room != null:
                room.Leave(_connection);
                _room = null;
                break;
            case MessageType.Ready when room != null:
                room.SetReady(room.SeatIndexOf(_connection), (bool?)message["ready"] ?? true);
                break;
            case MessageType.AddBot when room != null:
                await SendErrorIfAnyAsync(
                    room.AddBot(room.SeatIndexOf(_connection), (string?)message["level"]));
                break;
            case MessageType.SetBotLevel when room != null:
                await SendErrorIfAnyAsync(room.SetBotLevel(room.SeatIndexOf(_connection),
                    (int?)message["seat"] ?? -1, (string?)message["level"]));
                break;
            case MessageType.SetSpeed when room != null:
                await SendErrorIfAnyAsync(
                    room.SetSpeed(room.SeatIndexOf(_connection), (string?)message["speed"]));
                break;
            case MessageType.RemoveBot when room != null:
                await SendErrorIfAnyAsync(
                    room.RemoveBot(room.SeatIndexOf(_connection), (int?)message["seat"] ?? -1));
                break;
            case MessageType.StartGame when room != null:
                await SendErrorIfAnyAsync(room.Start(room.SeatIndexOf(_connection)));
                break;
            case MessageType.Action when room != null:
                if (message["action"] is JObject actionJson)
                {
                    await room.HandleActionAsync(room.SeatIndexOf(_connection), actionJson);
                }
                break;
            default:
                await SendErrorAsync("Unknown or out-of-order message.");
                break;
        }
    }

    private async Task HandleHelloAsync(JObject message)
    {
        if (++_helloCount > MaxHellosPerConnection)
        {
            await SendErrorAsync("Too many identity changes.");
            return;
        }
        var identity = _players.Identify((string?)message["playerKey"], (string?)message["name"]);
        if (identity == null)
        {
            await SendErrorAsync("Invalid player key.");
            return;
        }
        _identity = identity;
        // A re-hello under a different key moves this connection's turn alerts to that player.
        _connections.Unregister(_connection);
        _connections.Register(_identity.PlayerId, _connection);
        await SendAsync(new WelcomeMessage
        {
            PlayerId = _identity.PlayerId,
            Name = _identity.Name,
            GamesList = _rooms.GamesFor(_identity.PlayerId),
        });
    }

    private async Task HandleCreateRoomAsync(JObject message)
    {
        if (_identity != null &&
            _rooms.ActiveGameCount(_identity.PlayerId) >= MaxActiveGamesPerPlayer)
        {
            await SendErrorAsync("You have too many games going — finish or abandon some first.");
            return;
        }
        if (_rooms.RoomCount >= MaxRooms)
        {
            await SendErrorAsync("The server is full right now — try again later.");
            return;
        }
        LeaveCurrentRoom();
        _room = _rooms.Create();
        _room.Join((string?)message["name"] ?? "", null, _identity?.PlayerId, _connection, out _);
    }

    private async Task HandleJoinRoomAsync(JObject message)
    {
        var found = _rooms.Find((string?)message["code"] ?? "");
        if (found == null)
        {
            await SendErrorAsync("No room with that code.");
            return;
        }
        if (found == _room && found.SeatIndexOf(_connection) >= 0)
        {
            await SendErrorAsync("You are already in that room.");
            return;
        }
        var seat = found.Join((string?)message["name"] ?? "", (string?)message["token"],
            _identity?.PlayerId, _connection, out string error);
        if (seat == null)
        {
            await SendErrorAsync(error);
            return;
        }
        // Only once the new seat is taken: a refused join leaves you where you were.
        if (found != _room)
        {
            LeaveCurrentRoom();
        }
        _room = found;
    }

    /// <summary>A connection sits in one room; without this its old seat would stay shown as connected.</summary>
    private void LeaveCurrentRoom()
    {
        _room?.Leave(_connection);
        _room = null;
    }

    private static bool TryParseJson(string text, out JObject message)
    {
        try
        {
            message = JObject.Parse(text);
            return true;
        }
        catch
        {
            message = new JObject();
            return false;
        }
    }

    /// <summary>Null when the client closed the socket or sent an oversized message.</summary>
    private static async Task<string?> ReceiveTextAsync(WebSocket socket)
    {
        var buffer = new byte[8 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > MaxMessageBytes)
            {
                return null;
            }
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    /// <summary>Room commands report failure as a non-empty message.</summary>
    private Task SendErrorIfAnyAsync(string error) =>
        error.Length > 0 ? SendErrorAsync(error) : Task.CompletedTask;

    private Task SendErrorAsync(string message) =>
        _connection.SendAsync(new ErrorMessage { Message = message });

    private Task SendAsync(object message) => _connection.SendAsync(message);
}
