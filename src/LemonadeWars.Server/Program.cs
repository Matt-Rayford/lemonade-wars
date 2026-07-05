using LemonadeWars.Server;

var builder = WebApplication.CreateBuilder(args);
// Predictable local port (matches the Unity client default); Railway injects PORT.
string port = Environment.GetEnvironmentVariable("PORT") ?? "5225";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
var app = builder.Build();

var db = GameDataLocator.LoadDatabase();
// DATA_DIR: room action logs (mount a Railway volume here to survive redeploys).
string dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
    ?? Path.Combine(AppContext.BaseDirectory, "rooms");
int botDelayMs = int.TryParse(Environment.GetEnvironmentVariable("BOT_DELAY_MS"), out int d)
    ? d
    : 600;
var rooms = new RoomManager(db, dataDir, botDelayMs);

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

app.MapGet("/", () => "Lemonade Wars server is up.");
app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await ClientSession.RunAsync(socket, rooms);
});

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program
{
}
