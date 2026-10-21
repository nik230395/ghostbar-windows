using System.Text.Json.Nodes;
using Ghostbar.Backend;

namespace Ghostbar.Tests;

[Collection("serial")]
public class StreamTests : IDisposable
{
    private static readonly JsonArray History = new(
        new JsonObject { ["role"] = "user", ["content"] = "hello" });

    public StreamTests() => BackendClient.IdleTimeout = TimeSpan.FromSeconds(5);
    public void Dispose() => BackendClient.IdleTimeout = TimeSpan.FromSeconds(30);

    private static async Task<(string text, bool? isError, bool ended)> Run(
        BackendOptions o, string? image = null, CancellationToken ct = default)
    {
        var text = new System.Text.StringBuilder();
        bool? isError = null;
        await BackendClient.StreamAsync(History, "m1", image, o, t => text.Append(t), e => isError = e, ct);
        return (text.ToString(), isError, isError != null);
    }

    [Fact]
    public async Task Ollama_StreamsTokens_AndSendsImage()
    {
        using var server = new FakeServer(FakeServer.Lines(
            """{"message":{"content":"Hel"},"done":false}""",
            """{"message":{"content":"lo"},"done":false}""",
            """{"done":true}"""));
        var (text, isError, _) = await Run(new(BackendType.Ollama, server.Url, ""), image: "IMG");

        Assert.Equal("Hello", text);
        Assert.False(isError);
        Assert.Equal("/api/chat", server.LastPath);
        Assert.Equal("m1", server.LastBody!["model"]!.GetValue<string>());
        Assert.True(server.LastBody["stream"]!.GetValue<bool>());
        Assert.Equal("IMG", server.LastBody["messages"]![0]!["images"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task OpenAI_StreamsTokens_SendsBearerSystemPromptAndImage()
    {
        using var server = new FakeServer(FakeServer.Lines(
            """data: {"choices":[{"delta":{"content":"A"}}]}""", "",
            """data: {"choices":[{"delta":{"content":"B"}}]}""", "",
            "data: [DONE]"));
        var (text, isError, _) = await Run(new(BackendType.OpenAI, server.Url + "/v1/", "sk-test", "be brief"), "IMG");

        Assert.Equal("AB", text);
        Assert.False(isError);
        Assert.Equal("/v1/chat/completions", server.LastPath);
        Assert.Equal("Bearer sk-test", server.LastRequest!.Headers["Authorization"]);
        var messages = server.LastBody!["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("be brief", messages[0]!["content"]!.GetValue<string>());
        var content = messages[1]!["content"]!.AsArray();
        Assert.Equal("hello", content[0]!["text"]!.GetValue<string>());
        Assert.Equal("data:image/png;base64,IMG", content[1]!["image_url"]!["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task OpenAI_WithoutKey_SendsNoAuthorizationHeader()
    {
        using var server = new FakeServer(FakeServer.Lines("""data: {"choices":[{"delta":{"content":"x"}}]}"""));
        await Run(new(BackendType.OpenAI, server.Url, ""));
        Assert.Null(server.LastRequest!.Headers["Authorization"]);
    }

    [Fact]
    public async Task Anthropic_StreamsTokens_SendsHeadersSystemAndImage()
    {
        using var server = new FakeServer(FakeServer.Lines(
            "event: message_start",
            """data: {"type":"message_start","message":{}}""",
            "event: content_block_delta",
            """data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"Hi"}}""",
            """data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"!"}}"""));
        var (text, isError, _) = await Run(new(BackendType.Anthropic, server.Url, "sk-ant", "be brief"), "IMG");

        Assert.Equal("Hi!", text);
        Assert.False(isError);
        Assert.Equal("/messages", server.LastPath);
        Assert.Equal("sk-ant", server.LastRequest!.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", server.LastRequest.Headers["anthropic-version"]);
        Assert.Equal("be brief", server.LastBody!["system"]!.GetValue<string>());
        Assert.Equal(8096, server.LastBody["max_tokens"]!.GetValue<int>());
        var messages = server.LastBody["messages"]!.AsArray();
        Assert.Single(messages);
        var content = messages[0]!["content"]!.AsArray();
        Assert.Equal("image/png", content[1]!["source"]!["media_type"]!.GetValue<string>());
        Assert.Equal("IMG", content[1]!["source"]!["data"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(401, "API key")]
    [InlineData(403, "API key")]
    [InlineData(404, "model name")]
    [InlineData(429, "quota")]
    [InlineData(500, "internal error")]
    public async Task HttpErrors_ShowStatusAndHint(int status, string hint)
    {
        using var server = new FakeServer(FakeServer.Status(status, """{"error":"details here"}"""));
        var (text, isError, _) = await Run(new(BackendType.OpenAI, server.Url, "k"));

        Assert.True(isError);
        Assert.Contains($"HTTP {status}", text);
        Assert.Contains(hint, text);
        Assert.Contains("details here", text);
    }

    [Fact]
    public async Task ConnectionRefused_SaysServerNotRunning()
    {
        var url = $"http://localhost:{FakeServer.FreePort()}";
        var (text, isError, _) = await Run(new(BackendType.Ollama, url, ""));

        Assert.True(isError);
        Assert.Contains("Connection refused", text);
        Assert.Contains(url, text);
    }

    [Fact]
    public async Task InvalidUrl_SaysInvalidUrl()
    {
        var (text, isError, _) = await Run(new(BackendType.OpenAI, "not a url", ""));

        Assert.True(isError);
        Assert.Contains("Invalid URL", text);
    }

    [Fact]
    public async Task EmptyAnswer_IsReportedAsError()
    {
        using var server = new FakeServer(FakeServer.Status(200));
        var (text, isError, _) = await Run(new(BackendType.OpenAI, server.Url, "k"));

        Assert.True(isError);
        Assert.Contains("no text", text);
    }

    [Fact]
    public async Task SilentServer_TimesOut()
    {
        BackendClient.IdleTimeout = TimeSpan.FromMilliseconds(300);
        using var server = new FakeServer(FakeServer.Hang());
        var (text, isError, _) = await Run(new(BackendType.OpenAI, server.Url, "k"));

        Assert.True(isError);
        Assert.Contains("timed out", text);
    }

    [Fact]
    public async Task Cancel_StopsWithoutEndCallback()
    {
        using var server = new FakeServer(FakeServer.Hang());
        using var cts = new CancellationTokenSource();
        var run = Run(new(BackendType.OpenAI, server.Url, "k"), ct: cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        var (_, isError, ended) = await run;

        Assert.False(ended);
        Assert.Null(isError);
    }
}
