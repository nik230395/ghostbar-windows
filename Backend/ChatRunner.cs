using System.Text.Json.Nodes;

namespace Ghostbar.Backend;

public interface IToolExecutor
{
    JsonArray Definitions { get; }
    Task<string> ExecuteAsync(string name, string arguments, CancellationToken ct);
}

public record ChatCallbacks(Action<string> Token, Action<string> Notice, Action<string> ModelUsed, Action<bool> End);

public class ChatRunner(ModelRouter router)
{
    public int MaxToolRounds { get; init; } = 15;

    public async Task RunAsync(JsonArray history, string selected, IReadOnlyList<string> order, string? image,
        BackendOptions o, IToolExecutor? tools, ChatCallbacks cb, CancellationToken ct)
    {
        try
        {
            var msgs = BackendClient.PrepareMessages(history, image, o);
            var toolDefs = tools != null && BackendClient.SupportsTools(o.Type) ? tools.Definitions : null;
            for (var round = 0; ; round++)
            {
                var result = await RequestWithFallbackAsync(msgs, selected, order, o, toolDefs, cb, ct);
                if (result.Cancelled) return;
                if (!result.Ok)
                {
                    cb.Token(result.Error);
                    cb.End(true);
                    return;
                }
                if (result.ToolCalls.Count == 0 || tools == null)
                {
                    cb.End(false);
                    return;
                }
                if (round >= MaxToolRounds)
                {
                    cb.Notice($"Stopped after {MaxToolRounds} tool steps.");
                    cb.End(true);
                    return;
                }

                msgs.Add(new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = result.Text.Length > 0 ? result.Text : null,
                    ["tool_calls"] = result.ToolCalls.DeepClone(),
                });
                foreach (var call in result.ToolCalls.OfType<JsonObject>())
                {
                    var function = call["function"] as JsonObject;
                    var name = SseLine.Str(function?["name"]) ?? "";
                    var arguments = SseLine.Str(function?["arguments"]) ?? "{}";
                    var output = await tools.ExecuteAsync(name, arguments, ct);
                    msgs.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = SseLine.Str(call["id"]),
                        ["content"] = output,
                    });
                }
                selected = result.Model;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task<StreamResult> RequestWithFallbackAsync(JsonArray msgs, string selected, IReadOnlyList<string> order,
        BackendOptions o, JsonArray? tools, ChatCallbacks cb, CancellationToken ct)
    {
        var candidates = router.Candidates(selected, order);
        if (candidates.Count == 0)
            return new StreamResult(false, 0, "No models available. Open Settings and press Test.", "", "", [], false);

        StreamResult? last = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var model = candidates[i];
            cb.ModelUsed(model);
            var result = await BackendClient.StreamOnceAsync(msgs, model, o, tools, cb.Token, ct);
            if (result.Cancelled) return result;
            if (result.Ok && (result.Text.Length > 0 || result.ToolCalls.Count > 0))
            {
                router.Succeed(model);
                return result with { Model = model };
            }

            var empty = result.Ok;
            if (empty) result = result with { Ok = false, Error = "The backend answered but sent no text. Check the model name." };
            if (result.Text.Length > 0) return result;

            var failure = empty
                ? new ModelFailure(FailureAction.Switch, TimeSpan.Zero, "empty answer")
                : result.Status > 0
                    ? router.Classify(result.Status, result.Body)
                    : new ModelFailure(FailureAction.Stop, TimeSpan.Zero, "connection failed");
            BackendClient.Log($"model {model} failed: {failure.Reason} action={failure.Action}");
            if (failure.Action == FailureAction.Stop) return result;

            router.Fail(model, failure);
            last = result;
            if (i + 1 < candidates.Count) cb.Notice($"{model}: {failure.Reason}, switching to {candidates[i + 1]}");
        }

        if (candidates.Count > 1) cb.Notice($"All {candidates.Count} models failed.");
        return last!;
    }
}
