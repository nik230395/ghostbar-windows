using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Ghostbar.Backend;

public record StreamResult(bool Ok, int Status, string Error, string Body, string Text, JsonArray ToolCalls, bool Cancelled)
{
    public string Model { get; init; } = "";
}

public static class BackendClient
{
    public const string AnthropicDefaultUrl = "https://api.anthropic.com/v1";
    public const int TimeoutStatus = 504;
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static Action<string> Log { get; set; } = _ => { };
    public static TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public static string ModelsEndpoint(BackendOptions o) =>
        o.Type == BackendType.Ollama ? $"{BaseUrl(o)}/api/tags" : $"{BaseUrl(o)}/models";

    private static string BaseUrl(BackendOptions o) =>
        o.Type == BackendType.Anthropic && o.Url.Length == 0 ? AnthropicDefaultUrl : o.Url.TrimEnd('/');

    public static bool SupportsTools(BackendType type) => type is BackendType.OpenAI or BackendType.OpenRouter;

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

    public static string FormatStatus(int code, string body)
    {
        body = body.Trim();
        var detail = body.Length > 200 ? body[..200] : body;
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
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(FormatStatus((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(cts.Token)));
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
        if (o.Type == BackendType.Ollama)
            return json?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>()).OfType<string>().ToList() ?? [];
        return json?["data"]?.AsArray().Select(m => m?["id"]?.GetValue<string>()).OfType<string>().Select(StripModelsPrefix).Order().ToList() ?? [];
    }

    private static string StripModelsPrefix(string id) =>
        id.StartsWith("models/", StringComparison.Ordinal) ? id["models/".Length..] : id;

    public static async Task<string> TestAsync(BackendOptions o)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var req = ModelsRequest(o);
            using var resp = await Http.SendAsync(req, cts.Token);
            return resp.IsSuccessStatusCode
                ? "ok"
                : FormatStatus((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(cts.Token));
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or InvalidOperationException or UriFormatException)
        {
            return DescribeException(e, o.Url);
        }
    }

    public static Task StreamAsync(JsonArray messages, string model, string? image, BackendOptions o,
        Action<string> onToken, Action<bool> onEnd, CancellationToken ct) =>
        new ChatRunner(new ModelRouter()).RunAsync(messages, model, [model], image, o, null,
            new ChatCallbacks(onToken, _ => { }, _ => { }, onEnd), ct);

    public static JsonArray PrepareMessages(JsonArray history, string? image, BackendOptions o)
    {
        var msgs = new JsonArray(history.Select(m => m!.DeepClone()).ToArray());
        if (o.SystemPrompt.Length > 0)
            msgs.Insert(0, new JsonObject { ["role"] = "system", ["content"] = o.SystemPrompt });
        if (image != null && msgs.Count > 0) AttachImage(msgs[^1]!.AsObject(), image, o.Type);
        return msgs;
    }

    private static void AttachImage(JsonObject last, string image, BackendType type)
    {
        if (type == BackendType.Ollama)
        {
            last["images"] = new JsonArray(image);
            return;
        }
        if (last["content"] is not JsonValue text) return;
        JsonObject imagePart = type == BackendType.Anthropic
            ? new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = image },
            }
            : new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = $"data:image/png;base64,{image}" },
            };
        last["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text.GetValue<string>() }, imagePart);
    }

    public static async Task<StreamResult> StreamOnceAsync(JsonArray msgs, string model, BackendOptions o,
        JsonArray? tools, Action<string> onToken, CancellationToken ct)
    {
        var text = new StringBuilder();
        var toolCalls = new ToolCallAccumulator();
        StreamResult Fail(int status, string error, string body = "") =>
            new(false, status, error, body, text.ToString(), toolCalls.Result, false);

        try
        {
            using var req = BuildChatRequest(msgs, model, o, tools, out var parser);
            Log($"stream start backend={o.Type} model={model} tools={tools != null}");
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(IdleTimeout);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(idle.Token);
                var status = (int)resp.StatusCode;
                Log($"stream failed model={model} status={status}");
                return Fail(status, FormatStatus(status, body), body);
            }

            using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(idle.Token), Encoding.UTF8);
            while (true)
            {
                idle.CancelAfter(IdleTimeout);
                var line = await reader.ReadLineAsync(idle.Token);
                if (line == null) break;
                if (line.Length == 0) continue;
                if (StreamError.TryParse(line, out var errorStatus, out var errorBody))
                {
                    Log($"stream error event model={model} status={errorStatus}");
                    return Fail(errorStatus, FormatStatus(errorStatus, errorBody), errorBody);
                }
                if (o.Type != BackendType.Ollama && o.Type != BackendType.Anthropic)
                    toolCalls.Add(line);
                var token = parser.Token(line);
                if (token == null) continue;
                text.Append(token);
                onToken(token);
            }
            Log($"stream done model={model} chars={text.Length} toolCalls={toolCalls.Result.Count}");
            return new StreamResult(true, 200, "", "", text.ToString(), toolCalls.Result, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Log("stream cancelled");
            return new StreamResult(false, 0, "", "", text.ToString(), toolCalls.Result, true);
        }
        catch (OperationCanceledException e)
        {
            return Fail(TimeoutStatus, DescribeException(e, o.Url));
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or UriFormatException)
        {
            var message = DescribeException(e, o.Url);
            Log($"stream error {message}");
            return Fail(0, message);
        }
    }

    private static HttpRequestMessage BuildChatRequest(JsonArray msgs, string model, BackendOptions o, JsonArray? tools, out IStreamParser parser)
    {
        switch (o.Type)
        {
            case BackendType.Ollama:
                parser = new OllamaStreamParser();
                return Post($"{BaseUrl(o)}/api/chat",
                    new JsonObject { ["model"] = model, ["messages"] = msgs.DeepClone(), ["stream"] = true });
            case BackendType.Anthropic:
                parser = new AnthropicStreamParser();
                return AnthropicRequest(msgs, model, o);
            default:
                parser = new OpenAIStreamParser();
                var body = new JsonObject { ["model"] = model, ["stream"] = true, ["messages"] = msgs.DeepClone() };
                if (tools != null)
                {
                    body["tools"] = tools.DeepClone();
                    body["tool_choice"] = "auto";
                }
                var req = Post($"{BaseUrl(o)}/chat/completions", body);
                ApplyAuth(req, o);
                return req;
        }
    }

    private static HttpRequestMessage Post(string url, JsonObject body) =>
        new(HttpMethod.Post, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpRequestMessage AnthropicRequest(JsonArray msgs, string model, BackendOptions o)
    {
        var system = msgs.FirstOrDefault(m => m!["role"]?.GetValue<string>() == "system")?["content"]?.GetValue<string>();
        var userMsgs = new JsonArray(msgs.Where(m => m!["role"]?.GetValue<string>() != "system")
            .Select(m => m!.DeepClone()).ToArray());
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
