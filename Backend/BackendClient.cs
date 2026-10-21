using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Ghostbar.Config;

namespace Ghostbar.Backend;

class BackendClient
{
    private const string AnthropicDefaultUrl = "https://api.anthropic.com/v1";
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static string ModelsEndpoint(BackendType type, string url) =>
        type == BackendType.Ollama ? $"{url}/api/tags" : $"{url}/models";

    public static HttpRequestMessage ModelsRequest(BackendType type, string url, string apiKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint(type, url));
        if (type == BackendType.Anthropic)
        {
            if (apiKey.Length > 0) req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
        }
        else if (apiKey.Length > 0)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        return req;
    }

    public static async Task<List<string>> FetchModelsAsync(Settings cfg)
    {
        var url = cfg.BackendType == BackendType.Anthropic && cfg.BackendUrl.Length == 0
            ? AnthropicDefaultUrl : cfg.BackendUrl;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var req = ModelsRequest(cfg.BackendType, url, cfg.ApiKey);
        using var resp = await Http.SendAsync(req, cts.Token);
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
        if (cfg.BackendType == BackendType.Ollama)
            return json?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>()).OfType<string>().ToList() ?? [];
        return json?["data"]?.AsArray().Select(m => m?["id"]?.GetValue<string>()).OfType<string>().Order().ToList() ?? [];
    }

    public static async Task<string> TestAsync(BackendType type, string url, string apiKey)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var req = ModelsRequest(type, url, apiKey);
            using var resp = await Http.SendAsync(req, cts.Token);
            return resp.IsSuccessStatusCode ? "ok" : $"HTTP {(int)resp.StatusCode}";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return e.Message;
        }
    }

    public static async Task StreamAsync(JsonArray messages, string model, string? image,
        Action<string> onToken, Action<bool> onEnd, CancellationToken ct)
    {
        var cfg = Settings.Current;
        var msgs = new JsonArray(messages.Select(m => m!.DeepClone()).ToArray());
        if (cfg.SystemPrompt.Length > 0)
            msgs.Insert(0, new JsonObject { ["role"] = "system", ["content"] = cfg.SystemPrompt });

        var (req, parser) = cfg.BackendType switch
        {
            BackendType.Ollama => (OllamaRequest(msgs, model, cfg, image), (IStreamParser)new OllamaStreamParser()),
            BackendType.Anthropic => (AnthropicRequest(msgs, model, cfg, image), new AnthropicStreamParser()),
            _ => (OpenAIRequest(msgs, model, cfg, image), new OpenAIStreamParser()),
        };

        var isError = false;
        try
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(IdleTimeout);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var body = (await resp.Content.ReadAsStringAsync(idle.Token)).Trim();
                var detail = body.Length > 200 ? body[..200] : body;
                onToken(detail.Length == 0 ? $"HTTP {(int)resp.StatusCode}" : $"HTTP {(int)resp.StatusCode}: {detail}");
                isError = true;
            }
            else
            {
                using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(idle.Token), Encoding.UTF8);
                while (true)
                {
                    idle.CancelAfter(IdleTimeout);
                    var line = await reader.ReadLineAsync(idle.Token);
                    if (line == null) break;
                    if (line.Length == 0) continue;
                    var token = parser.Token(line);
                    if (token != null) onToken(token);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            onToken(e is OperationCanceledException ? "Timed out waiting for the backend." : e.Message);
            isError = true;
        }
        onEnd(isError);
    }

    private static HttpRequestMessage Post(string url, JsonObject body)
    {
        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private static HttpRequestMessage OllamaRequest(JsonArray msgs, string model, Settings cfg, string? image)
    {
        if (image != null && msgs.Count > 0)
            msgs[^1]!["images"] = new JsonArray(image);
        return Post($"{cfg.BackendUrl}/api/chat",
            new JsonObject { ["model"] = model, ["messages"] = msgs, ["stream"] = true });
    }

    private static HttpRequestMessage OpenAIRequest(JsonArray msgs, string model, Settings cfg, string? image)
    {
        if (image != null && msgs.Count > 0 && msgs[^1]!["content"] is JsonValue text)
        {
            msgs[^1]!["content"] = new JsonArray(
                new JsonObject { ["type"] = "text", ["text"] = text.GetValue<string>() },
                new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = $"data:image/png;base64,{image}" },
                });
        }
        var req = Post($"{cfg.BackendUrl}/chat/completions",
            new JsonObject { ["model"] = model, ["stream"] = true, ["messages"] = msgs });
        if (cfg.ApiKey.Length > 0)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
        return req;
    }

    private static HttpRequestMessage AnthropicRequest(JsonArray msgs, string model, Settings cfg, string? image)
    {
        var baseUrl = cfg.BackendUrl.Length == 0 ? AnthropicDefaultUrl : cfg.BackendUrl;
        var system = msgs.FirstOrDefault(m => m!["role"]?.GetValue<string>() == "system")?["content"]?.GetValue<string>();
        var userMsgs = new JsonArray(msgs.Where(m => m!["role"]?.GetValue<string>() != "system")
            .Select(m => m!.DeepClone()).ToArray());

        if (image != null && userMsgs.Count > 0 && userMsgs[^1]!["content"] is JsonValue text)
        {
            userMsgs[^1]!["content"] = new JsonArray(
                new JsonObject { ["type"] = "text", ["text"] = text.GetValue<string>() },
                new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject
                    {
                        ["type"] = "base64", ["media_type"] = "image/png", ["data"] = image,
                    },
                });
        }

        var body = new JsonObject
        {
            ["model"] = model, ["max_tokens"] = 8096, ["stream"] = true, ["messages"] = userMsgs,
        };
        if (!string.IsNullOrEmpty(system)) body["system"] = system;

        var req = Post($"{baseUrl}/messages", body);
        req.Headers.Add("x-api-key", cfg.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        return req;
    }
}
