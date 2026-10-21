using System.Text.Json.Nodes;

namespace Ghostbar.Backend;

interface IStreamParser
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
}

class OllamaStreamParser : IStreamParser
{
    public string? Token(string line)
    {
        var j = SseLine.Parse(line);
        return j?["message"]?["content"]?.GetValue<string>() ?? j?["response"]?.GetValue<string>();
    }
}

class AnthropicStreamParser : IStreamParser
{
    public string? Token(string line)
    {
        var s = SseLine.Payload(line);
        if (s.Length == 0) return null;
        var j = SseLine.Parse(s);
        if (j?["type"]?.GetValue<string>() != "content_block_delta") return null;
        if (j["delta"]?["type"]?.GetValue<string>() != "text_delta") return null;
        return j["delta"]?["text"]?.GetValue<string>();
    }
}

class OpenAIStreamParser : IStreamParser
{
    public string? Token(string line)
    {
        var s = SseLine.Payload(line);
        if (s == "[DONE]" || s.Length == 0) return null;
        var delta = SseLine.Parse(s)?["choices"]?[0]?["delta"];
        var content = delta?["content"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(content)) return content;
        return delta?["reasoning_content"]?.GetValue<string>();
    }
}
