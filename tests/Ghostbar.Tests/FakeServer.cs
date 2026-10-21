using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Ghostbar.Tests;

class FakeServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stop = new();

    public string Url { get; }
    public string? LastPath { get; private set; }
    public JsonNode? LastBody { get; private set; }
    public HttpListenerRequest? LastRequest { get; private set; }

    public FakeServer(Func<HttpListenerContext, CancellationToken, Task> handler)
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
                using (var reader = new StreamReader(ctx.Request.InputStream))
                {
                    var text = await reader.ReadToEndAsync();
                    LastBody = text.Length > 0 ? JsonNode.Parse(text) : null;
                }
                _ = Task.Run(async () =>
                {
                    try { await handler(ctx, stop.Token); }
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

    public static Func<HttpListenerContext, CancellationToken, Task> Lines(params string[] lines) =>
        async (ctx, _) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            var bytes = Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
            await ctx.Response.OutputStream.WriteAsync(bytes);
        };

    public static Func<HttpListenerContext, CancellationToken, Task> Status(int code, string body = "") =>
        async (ctx, _) =>
        {
            ctx.Response.StatusCode = code;
            await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body));
        };

    public static Func<HttpListenerContext, CancellationToken, Task> Hang() =>
        (_, ct) => Task.Delay(Timeout.Infinite, ct);

    public void Dispose()
    {
        stop.Cancel();
        listener.Abort();
    }
}
