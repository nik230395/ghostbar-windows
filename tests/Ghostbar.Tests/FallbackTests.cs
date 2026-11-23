using System.Text.Json.Nodes;
using Ghostbar.Backend;

namespace Ghostbar.Tests;

[Collection("serial")]
public class FallbackTests
{
    private static readonly JsonArray History = new(new JsonObject { ["role"] = "user", ["content"] = "hi" });

    private record Run(string Text, bool? IsError, List<string> Notices, List<string> Models);

    private static async Task<Run> Go(FakeServer server, string selected, IReadOnlyList<string> order, ModelRouter? router = null)
    {
        var text = new System.Text.StringBuilder();
        var notices = new List<string>();
        var models = new List<string>();
        bool? isError = null;
        await new ChatRunner(router ?? new ModelRouter()).RunAsync(History, selected, order, null,
            new BackendOptions(BackendType.OpenAI, server.Url, "k"), null,
            new ChatCallbacks(t => text.Append(t), notices.Add, models.Add, e => isError = e), CancellationToken.None);
        return new Run(text.ToString(), isError, notices, models);
    }

    private static List<string> Tried(FakeServer server) =>
        server.Bodies.Select(b => b!["model"]!.GetValue<string>()).ToList();

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(404)]
    [InlineData(400)]
    [InlineData(403)]
    public async Task ErrorBeforeText_SwitchesToNextModel(int status)
    {
        using var server = new FakeServer(FakeServer.ByModel(new()
        {
            ["a"] = FakeServer.Status(status, "quota"),
            ["b"] = FakeServer.Lines(FakeServer.Text("from b")),
        }));
        var router = new ModelRouter();
        var run = await Go(server, ModelRouter.Auto, ["a", "b"], router);

        Assert.Equal("from b", run.Text);
        Assert.False(run.IsError);
        Assert.Equal(["a", "b"], run.Models);
        Assert.Contains(run.Notices, n => n.Contains("switching to b"));
        Assert.True(router.IsBlocked("a"));
    }

    [Fact]
    public async Task Unauthorized_StopsWithoutTryingOtherModels()
    {
        using var server = new FakeServer(FakeServer.Status(401, "bad key"));
        var run = await Go(server, ModelRouter.Auto, ["a", "b", "c"]);

        Assert.True(run.IsError);
        Assert.Contains("HTTP 401", run.Text);
        Assert.Equal(["a"], Tried(server));
    }

    [Fact]
    public async Task ErrorAfterText_DoesNotSwitch()
    {
        using var server = new FakeServer(FakeServer.ByModel(new()
        {
            ["a"] = FakeServer.Lines(FakeServer.Text("half "), """data: {"error":{"code":503,"message":"overloaded"}}"""),
            ["b"] = FakeServer.Lines(FakeServer.Text("from b")),
        }));
        var run = await Go(server, ModelRouter.Auto, ["a", "b"]);

        Assert.True(run.IsError);
        Assert.StartsWith("half ", run.Text);
        Assert.Contains("HTTP 503", run.Text);
        Assert.Equal(["a"], Tried(server));
    }

    [Fact]
    public async Task ErrorEventInStream_BeforeText_Switches()
    {
        using var server = new FakeServer(FakeServer.ByModel(new()
        {
            ["a"] = FakeServer.Lines("""data: {"error":{"code":429,"message":"quota"}}"""),
            ["b"] = FakeServer.Lines(FakeServer.Text("ok")),
        }));
        var run = await Go(server, ModelRouter.Auto, ["a", "b"]);

        Assert.Equal("ok", run.Text);
        Assert.False(run.IsError);
    }

    [Fact]
    public async Task EmptyAnswer_SwitchesToNextModel()
    {
        using var server = new FakeServer(FakeServer.ByModel(new()
        {
            ["a"] = FakeServer.Status(200),
            ["b"] = FakeServer.Lines(FakeServer.Text("ok")),
        }));
        var run = await Go(server, ModelRouter.Auto, ["a", "b"]);

        Assert.Equal("ok", run.Text);
    }

    [Fact]
    public async Task AllModelsFail_ShowsLastErrorOnce()
    {
        using var server = new FakeServer(FakeServer.Status(429, "quota"));
        var run = await Go(server, ModelRouter.Auto, ["a", "b", "c"]);

        Assert.True(run.IsError);
        Assert.Equal(["a", "b", "c"], Tried(server));
        Assert.Contains("All 3 models failed.", run.Notices);
        Assert.Equal(1, run.Text.Split("HTTP 429").Length - 1);
    }

    [Fact]
    public async Task SelectedModel_IsTriedFirst_ThenFallsBack()
    {
        using var server = new FakeServer(FakeServer.ByModel(new()
        {
            ["c"] = FakeServer.Status(503),
            ["a"] = FakeServer.Lines(FakeServer.Text("from a")),
        }));
        var run = await Go(server, "c", ["a", "b", "c"]);

        Assert.Equal(["c", "a"], Tried(server));
        Assert.Equal("from a", run.Text);
    }

    [Fact]
    public async Task BlockedModel_IsSkippedOnNextMessage()
    {
        using var server = new FakeServer(FakeServer.ByModel(new()
        {
            ["a"] = FakeServer.Status(429),
            ["b"] = FakeServer.Lines(FakeServer.Text("ok")),
        }));
        var router = new ModelRouter();
        await Go(server, ModelRouter.Auto, ["a", "b"], router);
        server.Bodies.Clear();
        await Go(server, ModelRouter.Auto, ["a", "b"], router);

        Assert.Equal(["b"], Tried(server));
    }

    [Fact]
    public async Task NoModels_ExplainsWhatToDo()
    {
        using var server = new FakeServer(FakeServer.Status(200));
        var run = await Go(server, ModelRouter.Auto, []);

        Assert.True(run.IsError);
        Assert.Contains("No models available", run.Text);
    }
}
