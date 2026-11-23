using System.Text.Json.Nodes;
using Ghostbar.Backend;
using Ghostbar.Tools;

namespace Ghostbar.Tests;

[Collection("serial")]
public class ToolLoopTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("ghostbar-loop").FullName;
    public void Dispose() => Directory.Delete(root, true);

    private static string ToolCallChunks(string id, string name, string args, bool signature = true)
    {
        var half = args.Length / 2;
        JsonNode Delta(JsonObject call) => new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { ["tool_calls"] = new JsonArray(call) } }),
        };
        var first = new JsonObject
        {
            ["index"] = 0, ["id"] = id, ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args[..half] },
        };
        if (signature) first["extra_content"] = new JsonObject { ["google"] = new JsonObject { ["thought_signature"] = "SIG" } };
        var second = new JsonObject { ["index"] = 0, ["function"] = new JsonObject { ["arguments"] = args[half..] } };
        return $"data: {Delta(first).ToJsonString()}\n\ndata: {Delta(second).ToJsonString()}\n\ndata: [DONE]";
    }

    private async Task<(string Text, bool? IsError, List<string> Notices)> Run(FakeServer server, IToolExecutor tools, int maxRounds = 15)
    {
        var text = new System.Text.StringBuilder();
        var notices = new List<string>();
        bool? isError = null;
        await new ChatRunner(new ModelRouter()) { MaxToolRounds = maxRounds }.RunAsync(
            new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "what is in a.txt?" }),
            "m", ["m"], null, new BackendOptions(BackendType.OpenAI, server.Url, "k"), tools,
            new ChatCallbacks(t => text.Append(t), notices.Add, _ => { }, e => isError = e), CancellationToken.None);
        return (text.ToString(), isError, notices);
    }

    [Fact]
    public async Task ToolCall_IsExecuted_AndResultSentBack()
    {
        File.WriteAllText(Path.Combine(root, "a.txt"), "secret content");
        using var server = new FakeServer(FakeServer.Sequence(
            FakeServer.Lines(ToolCallChunks("call_1", "read_file", """{"path":"a.txt"}""")),
            FakeServer.Lines(FakeServer.Text("It says secret content."))));
        var tools = new ProjectTools(root, (_, _) => Task.FromResult(true));

        var (text, isError, _) = await Run(server, tools);

        Assert.Equal("It says secret content.", text);
        Assert.False(isError);
        var bodies = server.Bodies.ToArray();
        Assert.Equal(2, bodies.Length);
        Assert.Equal("list_dir", bodies[0]!["tools"]![0]!["function"]!["name"]!.GetValue<string>());

        var messages = bodies[1]!["messages"]!.AsArray();
        var assistant = messages[^2]!;
        var call = assistant["tool_calls"]![0]!;
        Assert.Equal("assistant", assistant["role"]!.GetValue<string>());
        Assert.Equal("call_1", call["id"]!.GetValue<string>());
        Assert.Equal("""{"path":"a.txt"}""", call["function"]!["arguments"]!.GetValue<string>());
        Assert.Equal("SIG", call["extra_content"]!["google"]!["thought_signature"]!.GetValue<string>());

        var toolResult = messages[^1]!;
        Assert.Equal("tool", toolResult["role"]!.GetValue<string>());
        Assert.Equal("call_1", toolResult["tool_call_id"]!.GetValue<string>());
        Assert.Contains("secret content", toolResult["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolCallWithoutId_GetsGeneratedId()
    {
        File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        var chunk = """data: {"choices":[{"delta":{"tool_calls":[{"function":{"name":"read_file","arguments":"{\"path\":\"a.txt\"}"}}]}}]}""";
        using var server = new FakeServer(FakeServer.Sequence(FakeServer.Lines(chunk), FakeServer.Lines(FakeServer.Text("ok"))));

        await Run(server, new ProjectTools(root, (_, _) => Task.FromResult(true)));

        var messages = server.Bodies.ToArray()[1]!["messages"]!.AsArray();
        Assert.Equal("call_0", messages[^2]!["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("call_0", messages[^1]!["tool_call_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task EndlessToolCalls_StopAfterLimit()
    {
        using var server = new FakeServer(FakeServer.Lines(ToolCallChunks("c", "list_dir", "{}")));
        var (_, isError, notices) = await Run(server, new ProjectTools(root, (_, _) => Task.FromResult(true)), maxRounds: 3);

        Assert.True(isError);
        Assert.Contains("Stopped after 3 tool steps.", notices);
        Assert.Equal(4, server.Bodies.Count);
    }

    [Fact]
    public async Task ApprovedEdit_IsWritten_RejectedEdit_IsNot()
    {
        var file = Path.Combine(root, "a.txt");
        File.WriteAllText(file, "hello world");
        var args = """{"path":"a.txt","old_text":"world","new_text":"there"}""";

        foreach (var approve in new[] { false, true })
        {
            using var server = new FakeServer(FakeServer.Sequence(
                FakeServer.Lines(ToolCallChunks("e", "edit_file", args)),
                FakeServer.Lines(FakeServer.Text("done"))));
            var asked = 0;
            await Run(server, new ProjectTools(root, (_, _) => { asked++; return Task.FromResult(approve); }));

            Assert.Equal(1, asked);
            Assert.Equal(approve ? "hello there" : "hello world", File.ReadAllText(file));
            var toolResult = server.Bodies.ToArray()[1]!["messages"]!.AsArray()[^1]!["content"]!.GetValue<string>();
            Assert.Contains(approve ? "Edited a.txt" : "rejected", toolResult);
        }
    }

    [Fact]
    public async Task Tools_AreNotSent_ToBackendsWithoutToolSupport()
    {
        using var server = new FakeServer(FakeServer.Lines("""{"message":{"content":"hi"},"done":false}"""));
        await new ChatRunner(new ModelRouter()).RunAsync(
            new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "x" }), "m", ["m"], null,
            new BackendOptions(BackendType.Ollama, server.Url, ""), new ProjectTools(root, (_, _) => Task.FromResult(true)),
            new ChatCallbacks(_ => { }, _ => { }, _ => { }, _ => { }), CancellationToken.None);

        Assert.Null(server.LastBody!["tools"]);
    }
}
