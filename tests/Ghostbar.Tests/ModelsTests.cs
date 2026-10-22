using Ghostbar.Backend;

namespace Ghostbar.Tests;

public class ModelsTests
{
    [Fact]
    public async Task Ollama_ListsModelNames()
    {
        using var server = new FakeServer(FakeServer.Lines("""{"models":[{"name":"llama3:8b"},{"name":"gemma3:4b"}]}"""));
        var names = await BackendClient.FetchModelsAsync(new(BackendType.Ollama, server.Url, ""));

        Assert.Equal("/api/tags", server.LastPath);
        Assert.Equal(["llama3:8b", "gemma3:4b"], names);
    }

    [Fact]
    public async Task OpenAI_ListsModelIds_Sorted_WithBearer()
    {
        using var server = new FakeServer(FakeServer.Lines("""{"data":[{"id":"b"},{"id":"a"}]}"""));
        var names = await BackendClient.FetchModelsAsync(new(BackendType.OpenAI, server.Url, "sk"));

        Assert.Equal("/models", server.LastPath);
        Assert.Equal("Bearer sk", server.LastRequest!.Headers["Authorization"]);
        Assert.Equal(["a", "b"], names);
    }

    [Fact]
    public async Task Gemini_ModelsPrefix_IsRemoved()
    {
        using var server = new FakeServer(FakeServer.Lines("""{"data":[{"id":"models/gemini-flash"},{"id":"models/gemini-pro"}]}"""));
        var names = await BackendClient.FetchModelsAsync(new(BackendType.OpenAI, server.Url, "k"));

        Assert.Equal(["gemini-flash", "gemini-pro"], names);
    }

    [Fact]
    public async Task Anthropic_UsesApiKeyHeader()
    {
        using var server = new FakeServer(FakeServer.Lines("""{"data":[{"id":"claude-x"}]}"""));
        await BackendClient.FetchModelsAsync(new(BackendType.Anthropic, server.Url, "sk-ant"));

        Assert.Equal("sk-ant", server.LastRequest!.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", server.LastRequest.Headers["anthropic-version"]);
        Assert.Null(server.LastRequest.Headers["Authorization"]);
    }

    [Fact]
    public async Task Unauthorized_ThrowsWithHint()
    {
        using var server = new FakeServer(FakeServer.Status(401, "nope"));
        var e = await Assert.ThrowsAsync<HttpRequestException>(
            () => BackendClient.FetchModelsAsync(new(BackendType.OpenAI, server.Url, "bad")));

        Assert.Contains("HTTP 401", e.Message);
        Assert.Contains("API key", e.Message);
    }

    [Fact]
    public async Task Test_Ok()
    {
        using var server = new FakeServer(FakeServer.Lines("""{"data":[]}"""));
        Assert.Equal("ok", await BackendClient.TestAsync(new(BackendType.OpenAI, server.Url, "k")));
    }

    [Fact]
    public async Task Test_WrongKey_ExplainsKey()
    {
        using var server = new FakeServer(FakeServer.Status(401));
        var result = await BackendClient.TestAsync(new(BackendType.OpenAI, server.Url, "bad"));
        Assert.Contains("API key", result);
    }

    [Fact]
    public async Task Test_ServerDown_ExplainsConnection()
    {
        var result = await BackendClient.TestAsync(new(BackendType.Ollama, $"http://localhost:{FakeServer.FreePort()}", ""));
        Assert.Contains("Connection refused", result);
    }

    [Fact]
    public async Task Test_WrongPath_Explains404()
    {
        using var server = new FakeServer(FakeServer.Status(404));
        var result = await BackendClient.TestAsync(new(BackendType.OpenAI, server.Url, "k"));
        Assert.Contains("URL", result);
    }
}
