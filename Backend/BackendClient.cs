using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Ghostbar.Backend;

public static class BackendClient
{
    public const string AnthropicDefaultUrl = "https://api.anthropic.com/v1";
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static Action<string> Log { get; set; } = _ => { };
    public static TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public static string ModelsEndpoint(BackendOptions o) =>
        o.Type == BackendType.Ollama ? $"{BaseUrl(o)}/api/tags" : $"{BaseUrl(o)}/models";

    private static string BaseUrl(BackendOptions o) =>
        o.Type == BackendType.Anthropic && o.Url.Length == 0 ? AnthropicDefaultUrl : o.Url.TrimEnd('/');

    public static HttpRequestMessage ModelsRequest(BackendOptions o)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint(o));
        ApplyAuth(req, o);
        return req;
    }

    private static void ApplyAuth(HttpRequestMessage req, BackendOptions o)
    {
        if (o.Type == BackendType.Anthropic)
        {
            req.Headers.Add("x-api-key", o.ApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
        }
        else if (o.ApiKey.Length > 0)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
        }
    }

    public static string StatusHint(int code) => code switch
    {
        401 or 403 => "API key missing or rejected. Check the key in Settings.",
        404 => "Not found. Check the URL (it usually ends in /v1) and the model name.",
        429 => "Rate limit or quota exceeded.",
        >= 500 => "The server reported an internal error.",
        _ => "",
    };

    public static string DescribeException(Exception e, string url) => e switch
    {
        HttpRequestException { InnerException: SocketException { SocketErrorCode: SocketError.ConnectionRefused } } =>
            $"Connection refused at {url}. Is the server running?",
        HttpRequestException { InnerException: SocketException { SocketErrorCode: SocketError.HostNotFound } } =>
            $"Host not found: {url}. Check the URL and your internet connection.",
        HttpRequestException => $"Cannot reach {url}: {e.Message}",
        UriFormatException or InvalidOperationException => $"Invalid URL: {url}",
        OperationCanceledException => $"No answer from {url} (timed out).",
        _ => e.Message,
    };

    private static async Task<string> StatusMessage(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = (await resp.Content.ReadAsStringAsync(ct)).Trim();
        var detail = body.Length > 200 ? body[..200] : body;
        var code = (int)resp.StatusCode;
        var hint = StatusHint(code);
        var text = $"HTTP {code}";
        if (hint.Length > 0) text += $" - {hint}";
        if (detail.Length > 0) text += $"\n{detail}";
        return text;
    }

    public static async Task<List<string>> FetchModelsAsync(BackendOptions o)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var req = ModelsRequest(o);
        using var resp = await Http.SendAsync(req, cts.Token);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException(await StatusMessage(resp, cts.Token));
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
        if (o.Type == BackendType.Ollama)
            return json?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>()).OfType<string>().ToList() ?? [];
        return json?["data"]?.AsArray().Select(m => m?["id"]?.GetValue<string>()).OfType<string>().Order().ToList() ?? [];
    }

    public static async Task<string> TestAsync(BackendOptions o)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var req = ModelsRequest(o);
            using var resp = await Http.SendAsync(req, cts.Token);
            return resp.IsSuccessStatusCode ? "ok" : await StatusMessage(resp, cts.Token);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or InvalidOperationException or UriFormatException)
        {
            return DescribeException(e, o.Url);
        }
    }

    public static async Task StreamAsync(JsonArray messages, string model, string? image, BackendOptions o,
        Action<string> onToken, Action<bool> onEnd, CancellationToken ct)
    {
        var isError = false;
        try
        {
            var msgs = new JsonArray(messages.Select(m => m!.DeepClone()).ToArray());
            if (o.SystemPrompt.Length > 0)
                msgs.Insert(0, new JsonObject { ["role"] = "system", ["content"] = o.SystemPrompt });

            using var req = BuildChatRequest(msgs, model, image, o, out var parser);
            Log($"stream start backend={o.Type} model={model} image={image != null}");
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(IdleTimeout);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var message = await StatusMessage(resp, idle.Token);
                Log($"stream failed {message.Split('\n')[0]}");
                onToken(message);
                isError = true;
            }
            else
            {
                using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(idle.Token), Encoding.UTF8);
                var tokens = 0;
                while (true)
                {
                    idle.CancelAfter(IdleTimeout);
                    var line = await reader.ReadLineAsync(idle.Token);
                    if (line == null) break;
                    if (line.Length == 0) continue;
                    var token = parser.Token(line);
                    if (token == null) continue;
                    tokens++;
                    onToken(token);
                }
                Log($"stream done tokens={tokens}");
                if (tokens == 0)
                {
                    onToken("The backend answered but sent no text. Check the model name.");
                    isError = true;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Log("stream cancelled");
            return;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or InvalidOperationException or UriFormatException)
        {
            var message = DescribeException(e, o.Url);
            Log($"stream error {message}");
            onToken(message);
            isError = true;
        }
        onEnd(isError);
    }

    private static HttpRequestMessage BuildChatRequest(JsonArray msgs, string model, string? image, BackendOptions o, out IStreamParser parser)
    {
        switch (o.Type)
        {
            case BackendType.Ollama:
                parser = new OllamaStreamParser();
                return OllamaRequest(msgs, model, o, image);
            case BackendType.Anthropic:
                parser = new AnthropicStreamParser();
                return AnthropicRequest(msgs, model, o, image);
            default:
                parser = new OpenAIStreamParser();
                return OpenAIRequest(msgs, model, o, image);
        }
    }

    private static HttpRequestMessage Post(string url, JsonObject body) =>
        new(HttpMethod.Post, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpRequestMessage OllamaRequest(JsonArray msgs, string model, BackendOptions o, string? image)
    {
        if (image != null && msgs.Count > 0)
            msgs[^1]!["images"] = new JsonArray(image);
        return Post($"{BaseUrl(o)}/api/chat",
            new JsonObject { ["model"] = model, ["messages"] = msgs, ["stream"] = true });
    }

    private static HttpRequestMessage OpenAIRequest(JsonArray msgs, string model, BackendOptions o, string? image)
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
        var req = Post($"{BaseUrl(o)}/chat/completions",
            new JsonObject { ["model"] = model, ["stream"] = true, ["messages"] = msgs });
        ApplyAuth(req, o);
        return req;
    }

    private static HttpRequestMessage AnthropicRequest(JsonArray msgs, string model, BackendOptions o, string? image)
    {
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

        var req = Post($"{BaseUrl(o)}/messages", body);
        ApplyAuth(req, o);
        return req;
    }
}
