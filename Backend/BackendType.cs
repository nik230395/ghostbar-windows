namespace Ghostbar.Backend;

public enum BackendType { Ollama, OpenAI, Anthropic, OpenRouter }

public record BackendOptions(BackendType Type, string Url, string ApiKey, string SystemPrompt = "");
