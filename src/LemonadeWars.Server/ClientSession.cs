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

    public static async Task RunAsync(WebSocket socket, RoomManager rooms)
    {
        Room? room = null;
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                string? text = await ReceiveTextAsync(socket);
                if (text == null)
                {
                    break;
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
                    case MessageType.CreateRoom:
                    {
                        room = rooms.Create();
                        room.Join((string?)message["name"] ?? "", null, socket, out _);
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
                            (string?)message["token"], socket, out string error);
                        if (seat == null)
                        {
                            await SendErrorAsync(socket, error);
                            break;
                        }
                        room = found;
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

    private static Task SendErrorAsync(WebSocket socket, string message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(
            Newtonsoft.Json.JsonConvert.SerializeObject(
                new ErrorMessage { Message = message }, WireCodec.Settings));
        return socket.State == WebSocketState.Open
            ? socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None)
            : Task.CompletedTask;
    }
}
