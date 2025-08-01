using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var app = builder.Build();

app.UsePathBase("/Gemini");
app.UseHttpsRedirection();
// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(
options =>
{
    options.RoutePrefix = "swagger";
    // JSON endpoint 也會自動對應到 {pathBase}/swagger/v1/swagger.json
}
);


app.UseDefaultFiles();
app.UseStaticFiles();

app.UseWebSockets();

var connectedClients = new ConcurrentDictionary<string, WebSocket>();

// WebSocket endpoint
app.Map("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var connId = Guid.NewGuid().ToString();
        connectedClients[connId] = webSocket;

        Console.WriteLine($"Client connected: {connId}");

        var buffer = new byte[1024 * 4];

        while (webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine($"Client disconnected: {connId}");
                connectedClients.TryRemove(connId, out _);
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            else
            {
                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine($"Received: {msg}");

                var broadcastTasks = connectedClients.Values
                    .Where(s => s.State == WebSocketState.Open && s != webSocket) // Exclude the sender
                    .Select(socket =>
                        socket.SendAsync(
                            Encoding.UTF8.GetBytes(msg),
                            WebSocketMessageType.Text,
                            true,
                            CancellationToken.None
                        )
                    ).ToArray();

                await Task.WhenAll(broadcastTasks);
            }
        }
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

app.Run();