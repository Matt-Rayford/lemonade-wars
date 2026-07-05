using LemonadeWars.Server;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var db = GameDataLocator.LoadDatabase();
var rooms = new RoomManager(db);

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
