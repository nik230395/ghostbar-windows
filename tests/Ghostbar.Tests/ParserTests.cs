using Ghostbar.Backend;

namespace Ghostbar.Tests;

public class ParserTests
{
    [Fact]
    public void Ollama_ChatLine_ReturnsContent() =>
        Assert.Equal("Hi", new OllamaStreamParser().Token("""{"message":{"role":"assistant","content":"Hi"},"done":false}"""));

    [Fact]
    public void Ollama_GenerateLine_ReturnsResponse() =>
        Assert.Equal("Yo", new OllamaStreamParser().Token("""{"response":"Yo","done":false}"""));

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{"done":true}""")]
    public void Ollama_OtherLines_ReturnNull(string line) =>
        Assert.Null(new OllamaStreamParser().Token(line));

    [Fact]
    public void OpenAI_DeltaContent_ReturnsText() =>
        Assert.Equal("Hel", new OpenAIStreamParser().Token("""data: {"choices":[{"delta":{"content":"Hel"}}]}"""));

    [Fact]
    public void OpenAI_ReasoningContent_UsedWhenContentEmpty() =>
        Assert.Equal("think", new OpenAIStreamParser().Token("""data: {"choices":[{"delta":{"content":"","reasoning_content":"think"}}]}"""));

    [Theory]
    [InlineData("data: [DONE]")]
    [InlineData("data:")]
    [InlineData(": keep-alive")]
    [InlineData("""data: {"choices":[]}""")]
    public void OpenAI_NonTextLines_ReturnNull(string line) =>
        Assert.Null(new OpenAIStreamParser().Token(line));

    [Fact]
    public void Anthropic_TextDelta_ReturnsText() =>
        Assert.Equal("Hey", new AnthropicStreamParser().Token(
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hey"}}"""));

    [Theory]
    [InlineData("event: content_block_delta")]
    [InlineData("""data: {"type":"message_start","message":{}}""")]
    [InlineData("""data: {"type":"content_block_delta","delta":{"type":"input_json_delta","partial_json":"{"}}""")]
    public void Anthropic_OtherLines_ReturnNull(string line) =>
        Assert.Null(new AnthropicStreamParser().Token(line));
}

public class ParserRobustnessTests
{
    [Theory]
    [InlineData("""data: {"choices":[{"delta":{"content":null}}]}""")]
    [InlineData("""data: {"choices":[{"delta":{"content":5}}]}""")]
    [InlineData("""data: {"choices":"x"}""")]
    [InlineData("""data: {"usage":{"total_tokens":3},"choices":[]}""")]
    public void OpenAI_UnexpectedShapes_DoNotThrow(string line) =>
        Assert.Null(new OpenAIStreamParser().Token(line));

    [Theory]
    [InlineData("""{"message":{"content":5}}""")]
    [InlineData("""{"message":"x"}""")]
    public void Ollama_UnexpectedShapes_DoNotThrow(string line) =>
        Assert.Null(new OllamaStreamParser().Token(line));

    [Fact]
    public void Anthropic_UnexpectedShape_DoesNotThrow() =>
        Assert.Null(new AnthropicStreamParser().Token("""data: {"type":"content_block_delta","delta":"x"}"""));
}
