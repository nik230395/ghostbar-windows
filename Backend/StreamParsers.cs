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
