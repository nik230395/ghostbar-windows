using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Ghostbar.Tests;

delegate Task Handler(HttpListenerContext ctx, JsonNode? body, CancellationToken ct);

class FakeServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stop = new();

    public string Url { get; }
    public string? LastPath { get; private set; }
    public JsonNode? LastBody { get; private set; }
    public HttpListenerRequest? LastRequest { get; private set; }
    public ConcurrentQueue<JsonNode?> Bodies { get; } = new();

    public FakeServer(Handler handler)
    {
        var port = FreePort();
        Url = $"http://localhost:{port}";
        listener.Prefixes.Add($"{Url}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { return; }
                LastPath = ctx.Request.Url!.AbsolutePath;
                LastRequest = ctx.Request;
                JsonNode? body;
                using (var reader = new StreamReader(ctx.Request.InputStream))
                {
                    var text = await reader.ReadToEndAsync();
                    body = text.Length > 0 ? JsonNode.Parse(text) : null;
                }
                LastBody = body;
                Bodies.Enqueue(body);
                _ = Task.Run(async () =>
                {
                    try { await handler(ctx, body, stop.Token); }
                    catch (Exception e) when (e is OperationCanceledException or HttpListenerException or IOException) { }
                    finally { try { ctx.Response.Close(); } catch (Exception) { } }
                });
            }
        });
    }

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public static Handler Lines(params string[] lines) =>
        async (ctx, _, _) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            var bytes = Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
            await ctx.Response.OutputStream.WriteAsync(bytes);
        };

    public static Handler Status(int code, string body = "") =>
        async (ctx, _, _) =>
        {
            ctx.Response.StatusCode = code;
            await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body));
        };

    public static Handler Hang() =>
        (_, _, ct) => Task.Delay(Timeout.Infinite, ct);

    public static Handler ByModel(Dictionary<string, Handler> handlers) =>
        (ctx, body, ct) => handlers[body!["model"]!.GetValue<string>()](ctx, body, ct);

    public static Handler Sequence(params Handler[] handlers)
    {
        var next = 0;
        return (ctx, body, ct) => handlers[Math.Min(Interlocked.Increment(ref next) - 1, handlers.Length - 1)](ctx, body, ct);
    }

    public static string Text(string text) =>
        "data: " + new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { ["content"] = text } }) }.ToJsonString();

    public void Dispose()
    {
        stop.Cancel();
        listener.Abort();
    }
}
