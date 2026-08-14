using System.Net.WebSockets;
using System.Text;
using LemonadeWars.Protocol;
using Newtonsoft.Json;

namespace LemonadeWars.Server;

/// <summary>
/// One client socket plus THE send lock for it. Rooms, session replies, and turn
/// alerts can all write to the same socket from different tasks; concurrent
/// WebSocket sends throw, so every write anywhere goes through this single gate.
/// </summary>
public sealed class Connection
{
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public Connection(WebSocket socket)
    {
        _socket = socket;
    }

    public bool Open => _socket.State == WebSocketState.Open;

    public async Task SendAsync(object message)
    {
        if (!Open)
        {
            return;
        }
        byte[] payload = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(message, WireCodec.Settings));
        await _sendLock.WaitAsync();
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(payload, WebSocketMessageType.Text, true,
                    CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // Connection died mid-send; the receive loop will clean up.
        }
        catch (ObjectDisposedException)
        {
            // Socket torn down under us — same story.
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
