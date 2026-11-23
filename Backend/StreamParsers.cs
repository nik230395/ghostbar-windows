using System.Text.Json.Nodes;

namespace Ghostbar.Backend;

public interface IStreamParser
{
    string? Token(string line);
}

static class SseLine
{
    public static string Payload(string line) =>
        line.StartsWith("data:") ? line[5..].Trim() : line;

    public static JsonNode? Parse(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    public static JsonNode? At(JsonNode? node, string key) => node is JsonObject o ? o[key] : null;

    public static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

public class OllamaStreamParser : IStreamParser
{
    public string? Token(string line)
    {
        var j = SseLine.Parse(line);
        return SseLine.Str(SseLine.At(SseLine.At(j, "message"), "content")) ?? SseLine.Str(SseLine.At(j, "response"));
    }
}

public class AnthropicStreamParser : IStreamParser
{
    public string? Token(string line)
    {
        var s = SseLine.Payload(line);
        if (s.Length == 0) return null;
        var j = SseLine.Parse(s);
        if (SseLine.Str(SseLine.At(j, "type")) != "content_block_delta") return null;
        if (SseLine.Str(SseLine.At(SseLine.At(j, "delta"), "type")) != "text_delta") return null;
        return SseLine.Str(SseLine.At(SseLine.At(j, "delta"), "text"));
    }
}

public class OpenAIStreamParser : IStreamParser
{
    public string? Token(string line)
    {
        var s = SseLine.Payload(line);
        if (s == "[DONE]" || s.Length == 0) return null;
        var choices = SseLine.At(SseLine.Parse(s), "choices") as JsonArray;
        var delta = choices is { Count: > 0 } ? SseLine.At(choices[0], "delta") : null;
        var content = SseLine.Str(SseLine.At(delta, "content"));
        if (!string.IsNullOrEmpty(content)) return content;
        return SseLine.Str(SseLine.At(delta, "reasoning_content"));
    }
}

static class StreamError
{
    public static bool TryParse(string line, out int status, out string body)
    {
        status = 0;
        body = SseLine.Payload(line);
        if (!body.StartsWith('{') && !body.StartsWith('[')) return false;
        var json = SseLine.Parse(body);
        if (json is JsonArray { Count: > 0 } array) json = array[0];
        if (SseLine.At(json, "error") is not JsonObject error) return false;
        var code = SseLine.At(error, "code");
        status = code is JsonValue v && v.TryGetValue<int>(out var n) && n >= 400 ? n : 500;
        return true;
    }
}

class ToolCallAccumulator
{
    private readonly SortedDictionary<int, JsonObject> calls = new();

    public void Add(string line)
    {
        var choices = SseLine.At(SseLine.Parse(SseLine.Payload(line)), "choices") as JsonArray;
        var delta = choices is { Count: > 0 } ? SseLine.At(choices[0], "delta") : null;
        if (SseLine.At(delta, "tool_calls") is not JsonArray deltas) return;

        foreach (var part in deltas.OfType<JsonObject>())
        {
            var index = SseLine.At(part, "index") is JsonValue v && v.TryGetValue<int>(out var i) ? i : calls.Count;
            if (!calls.TryGetValue(index, out var slot)) calls[index] = slot = new JsonObject();
            foreach (var (key, value) in part)
            {
                if (key == "index") continue;
                if (key == "function" && value is JsonObject function) MergeFunction(slot, function);
                else slot[key] = value?.DeepClone();
            }
        }
    }

    private static void MergeFunction(JsonObject slot, JsonObject function)
    {
        if (slot["function"] is not JsonObject target)
            slot["function"] = target = new JsonObject { ["name"] = "", ["arguments"] = "" };
        foreach (var (key, value) in function)
        {
            var text = SseLine.Str(value);
            if (key == "arguments") target["arguments"] = (SseLine.Str(target["arguments"]) ?? "") + text;
            else if (key == "name" && !string.IsNullOrEmpty(text)) target["name"] = text;
            else if (key != "name") target[key] = value?.DeepClone();
        }
    }

    public JsonArray Result => new(calls.Select(c =>
    {
        var call = (JsonObject)c.Value.DeepClone();
        if (string.IsNullOrEmpty(SseLine.Str(call["id"]))) call["id"] = $"call_{c.Key}";
        call["type"] = "function";
        if (call["function"] is JsonObject f && string.IsNullOrEmpty(SseLine.Str(f["arguments"]))) f["arguments"] = "{}";
        return (JsonNode)call;
    }).ToArray());
}
