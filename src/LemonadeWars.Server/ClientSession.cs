using System.Net.WebSockets;
using System.Text;
using LemonadeWars.Engine.Data;
using LemonadeWars.Protocol;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Server;

/// <summary>Finds game-data/ via env var (containers) or by walking up (dev/tests).</summary>
public static class GameDataLocator
{
    public static CardDatabase LoadDatabase()
    {
        string? configured = Environment.GetEnvironmentVariable("GAME_DATA_DIR");
        if (!string.IsNullOrEmpty(configured))
        {
            return CardDatabase.Load(configured);
        }
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "game-data");
            if (Directory.Exists(candidate))
            {
                return CardDatabase.Load(candidate);
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "game-data not found; set GAME_DATA_DIR or run from within the repo.");
    }
}

/// <summary>One connected socket: routes messages to a room until the socket closes.</summary>
public static class ClientSession
{
    private const int MaxMessageBytes = 64 * 1024;
    /// <summary>Per-identity cap on unfinished games — create-room spam guard.</summary>
    private const int MaxActiveGamesPerPlayer = 5;
    /// <summary>Identity churn guard: hello floods would mint registry records forever.</summary>
    private const int MaxHellosPerConnection = 5;

    /// <summary>Global room ceiling (MAX_ROOMS env) — the anonymous-spam backstop:
    /// identity caps don't bind clients that never say hello, this does.</summary>
    private static int MaxRooms =>
        int.TryParse(Environment.GetEnvironmentVariable("MAX_ROOMS"), out int r) ? r : 2000;

    /// <summary>Sustained messages/second per socket (RATE_LIMIT_PER_SEC; 0 disables —
    /// tests hammer full games through a single socket). Read per connection, not at
    /// type load, so test env setup can never race the static initializer.</summary>
    private static double RatePerSecond =>
        double.TryParse(Environment.GetEnvironmentVariable("RATE_LIMIT_PER_SEC"), out double r)
            ? r
            : 10;

    public static async Task RunAsync(WebSocket socket, RoomManager rooms,
        PlayerRegistry players)
    {
        Room? room = null;
        PlayerRegistry.Player? identity = null;
        int hellos = 0;
        // Token bucket: bursts are fine, a firehose is not.
        double rate = RatePerSecond;
        double burst = rate * 3;
        double tokens = burst;
        DateTime lastRefill = DateTime.UtcNow;
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                string? text = await ReceiveTextAsync(socket);
                if (text == null)
                {
                    break;
                }

                if (rate > 0)
                {
                    var now = DateTime.UtcNow;
                    tokens = Math.Min(burst, tokens + (now - lastRefill).TotalSeconds * rate);
                    lastRefill = now;
                    if (tokens < 1)
                    {
                        await SendErrorAsync(socket, "Too many messages — slow down.");
                        continue;
                    }
                    tokens -= 1;
                }

                JObject message;
                try
                {
                    message = JObject.Parse(text);
                }
                catch
                {
                    await SendErrorAsync(socket, "Malformed JSON.");
                    continue;
                }

                switch ((string?)message["type"])
                {
                    case MessageType.Hello:
                    {
                        if (++hellos > MaxHellosPerConnection)
                        {
                            await SendErrorAsync(socket, "Too many identity changes.");
                            break;
                        }
                        identity = players.Identify(
                            (string?)message["playerKey"], (string?)message["name"]);
                        if (identity == null)
                        {
                            await SendErrorAsync(socket, "Invalid player key.");
                            break;
                        }
                        await SendAsync(socket, new WelcomeMessage
                        {
                            PlayerId = identity.PlayerId,
                            Name = identity.Name,
                            GamesList = rooms.GamesFor(identity.PlayerId),
                        });
                        break;
                    }
                    case MessageType.ListGames when identity != null:
                    {
                        await SendAsync(socket, new GamesMessage
                        {
                            GamesList = rooms.GamesFor(identity.PlayerId),
                        });
                        break;
                    }
                    case MessageType.CreateRoom:
                    {
                        if (identity != null &&
                            rooms.ActiveGameCount(identity.PlayerId) >= MaxActiveGamesPerPlayer)
                        {
                            await SendErrorAsync(socket,
                                "You have too many games going — finish or abandon some first.");
                            break;
                        }
                        if (rooms.RoomCount >= MaxRooms)
                        {
                            await SendErrorAsync(socket,
                                "The server is full right now — try again later.");
                            break;
                        }
                        room = rooms.Create();
                        room.Join((string?)message["name"] ?? "", null,
                            identity?.PlayerId, socket, out _);
                        break;
                    }
                    case MessageType.JoinRoom:
                    {
                        var found = rooms.Find((string?)message["code"] ?? "");
                        if (found == null)
                        {
                            await SendErrorAsync(socket, "No room with that code.");
                            break;
                        }
                        var seat = found.Join((string?)message["name"] ?? "",
                            (string?)message["token"], identity?.PlayerId, socket,
                            out string error);
                        if (seat == null)
                        {
                            await SendErrorAsync(socket, error);
                            break;
                        }
                        room = found;
                        break;
                    }
                    case MessageType.Ready when room != null:
                    {
                        room.SetReady(room.SeatIndexOf(socket),
                            (bool?)message["ready"] ?? true);
                        break;
                    }
                    case MessageType.AddBot when room != null:
                    {
                        string error = room.AddBot(room.SeatIndexOf(socket));
                        if (error.Length > 0)
                        {
                            await SendErrorAsync(socket, error);
                        }
                        break;
                    }
                    case MessageType.RemoveBot when room != null:
                    {
                        string error = room.RemoveBot(room.SeatIndexOf(socket),
                            (int?)message["seat"] ?? -1);
                        if (error.Length > 0)
                        {
                            await SendErrorAsync(socket, error);
                        }
                        break;
                    }
                    case MessageType.StartGame when room != null:
                    {
                        string error = room.Start(room.SeatIndexOf(socket));
                        if (error.Length > 0)
                        {
                            await SendErrorAsync(socket, error);
                        }
                        break;
                    }
                    case MessageType.Action when room != null:
                    {
                        if (message["action"] is JObject actionJson)
                        {
                            await room.HandleActionAsync(room.SeatIndexOf(socket), actionJson);
                        }
                        break;
                    }
                    default:
                        await SendErrorAsync(socket, "Unknown or out-of-order message.");
                        break;
                }
            }
        }
        catch (WebSocketException)
        {
            // Client vanished; fall through to detach.
        }
        finally
        {
            room?.Detach(socket);
        }
    }

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

    private static Task SendErrorAsync(WebSocket socket, string message) =>
        SendAsync(socket, new ErrorMessage { Message = message });

    private static Task SendAsync(WebSocket socket, object message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(
            Newtonsoft.Json.JsonConvert.SerializeObject(message, WireCodec.Settings));
        return socket.State == WebSocketState.Open
            ? socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None)
            : Task.CompletedTask;
    }
}
