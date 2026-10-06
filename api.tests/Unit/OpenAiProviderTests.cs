using System.Net;
using Finance.Api.Application.Ai;
using Finance.Api.Infrastructure.Ai;
using static Finance.Api.Tests.Unit.AiProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// The OpenAI Chat Completions adapter against hand-written fixtures
/// (<c>Fixtures/Ai/README.md</c>). The network is never hit; the key goes in a header only.
/// </summary>
public sealed class OpenAiProviderTests
{
    [Fact]
    public async Task The_first_choice_is_the_text_and_prompt_and_completion_tokens_are_the_billed_counts()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("openai-chat-completion-stop.json"));

        var completion = await Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken);

        Assert.StartsWith("## Resumo\n\nVoc", completion.Text);
        Assert.EndsWith("em agosto.", completion.Text);
        Assert.Equal((1117, 46), (completion.InputTokens, completion.OutputTokens));
    }

    [Fact]
    public async Task The_request_posts_to_v1_chat_completions_with_a_bearer_key_and_nothing_else_carries_it()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("openai-chat-completion-stop.json"));

        await Provider(handler).CompleteAsync(Request("gpt-test"), TestContext.Current.CancellationToken);

        var sent = handler.Single();
        Assert.Equal((HttpMethod.Post, "https://api.openai.com/v1/chat/completions"), (sent.Method, sent.Uri.ToString()));
        Assert.Equal($"Bearer {OpenAiKey}", sent.Headers["authorization"]);
        Assert.Equal("application/json", sent.MediaType);
        Assert.False(sent.Headers.ContainsKey("x-api-key"));
        Assert.DoesNotContain(OpenAiKey, sent.Uri.ToString());
        Assert.DoesNotContain(OpenAiKey, sent.Body);
    }

    [Fact]
    public async Task The_body_is_model_a_system_and_a_user_message_and_max_completion_tokens()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("openai-chat-completion-stop.json"));

        await Provider(handler).CompleteAsync(Request("gpt-test"), TestContext.Current.CancellationToken);

        var body = handler.Single().Json;
        Assert.Equal(["model", "messages", "max_completion_tokens"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(("gpt-test", 1024), (body.GetProperty("model").GetString(), body.GetProperty("max_completion_tokens").GetInt32()));
        Assert.Equal(
            [("system", "Responda em pt-BR."), ("user", "{\"mes\":\"2026-08\"}")],
            body.GetProperty("messages").EnumerateArray()
                .Select(message => (message.GetProperty("role").GetString(), message.GetProperty("content").GetString())));
    }

    [Theory]
    [InlineData("openai-chat-completion-length.json", 790, 64, "length")]
    [InlineData("openai-chat-completion-refusal.json", 1402, 10, "refus")]
    public async Task A_truncated_or_refused_answer_fails_with_its_tokens(string fixture, int input, int output, string reason)
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture(fixture));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal((input, output), (error.InputTokens, error.OutputTokens));
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public async Task A_content_filter_stop_fails_with_its_tokens()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, """
            {"choices":[{"index":0,"message":{"role":"assistant","content":"","refusal":null},"finish_reason":"content_filter"}],
             "usage":{"prompt_tokens":300,"completion_tokens":0,"total_tokens":300}}
            """);

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal((300, 0), (error.InputTokens, error.OutputTokens));
        Assert.Contains("content_filter", error.Message);
    }

    [Theory]
    [InlineData(400, "invalid_request_error", 0)]
    [InlineData(401, "invalid_request_error", 0)]
    [InlineData(403, "invalid_request_error", 0)]
    [InlineData(404, "invalid_request_error", 0)]
    [InlineData(429, "insufficient_quota", 0)]
    [InlineData(500, "server_error", null)]
    [InlineData(503, "server_error", null)]
    public async Task An_error_status_fails_with_its_type_and_no_key(int status, string type, int? inputTokens)
    {
        var handler = new AiHttpHandler((HttpStatusCode)status,
            $$$"""{"error":{"message":"Incorrect API key provided: {{{OpenAiKey}}}.","type":"{{{type}}}","param":null,"code":null}}""");

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal((inputTokens, 0), (error.InputTokens, error.OutputTokens));
        Assert.Contains(type, error.Message);
        Assert.DoesNotContain(OpenAiKey, error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"choices":[],"usage":{"prompt_tokens":1,"completion_tokens":1}}""")]
    [InlineData("""{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}""")]
    public async Task A_200_that_cannot_be_read_fails_with_unknown_tokens(string body)
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, body);

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Null(error.InputTokens);
    }

    [Fact]
    public async Task An_empty_key_fails_the_call_before_any_request_and_bills_nothing()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("openai-chat-completion-stop.json"));

        var error = await Assert.ThrowsAsync<AiProviderException>(() =>
            new OpenAiProvider(handler.Client(), Options(openAiKey: "")).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
        Assert.Equal(0, error.InputTokens);
        Assert.Contains("Ai:OpenAi:ApiKey", error.Message);
    }

    [Fact]
    public async Task A_dropped_connection_fails_with_unknown_tokens_and_a_cancelled_call_stays_cancelled()
    {
        var dropped = new AiHttpHandler(() => throw new HttpRequestException("Connection reset by peer"));
        var fine = new AiHttpHandler(HttpStatusCode.OK, Fixture("openai-chat-completion-stop.json"));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(dropped).CompleteAsync(Request(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(fine).CompleteAsync(Request(), new CancellationToken(canceled: true)));

        Assert.Null(error.InputTokens);
    }

    private static OpenAiProvider Provider(AiHttpHandler handler) => new(handler.Client(), Options());
}
