using System.Net;
using Finance.Api.Application.Ai;
using Finance.Api.Infrastructure.Ai;
using static Finance.Api.Tests.Unit.AiProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// The Anthropic Messages API adapter against hand-written fixtures
/// (<c>Fixtures/Ai/README.md</c>). The network is never hit; the key goes in a header only.
/// </summary>
public sealed class AnthropicAiProviderTests
{
    [Fact]
    public async Task The_text_blocks_are_joined_thinking_blocks_skipped_and_the_billed_tokens_read()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("anthropic-messages-end-turn.json"));

        var completion = await Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken);

        Assert.StartsWith("## Resumo\n\nVoc", completion.Text);
        Assert.EndsWith("em agosto.\n\n## Onde o dinheiro foi\n\nMercado: R$ 1.320,00.", completion.Text);
        Assert.Equal((2095, 503), (completion.InputTokens, completion.OutputTokens));
    }

    [Fact]
    public async Task The_request_posts_to_v1_messages_with_the_key_and_version_in_headers_only()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("anthropic-messages-end-turn.json"));

        await Provider(handler).CompleteAsync(Request("claude-opus-5"), TestContext.Current.CancellationToken);

        var sent = handler.Single();
        Assert.Equal((HttpMethod.Post, "https://api.anthropic.com/v1/messages"), (sent.Method, sent.Uri.ToString()));
        Assert.Equal(AnthropicKey, sent.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", sent.Headers["anthropic-version"]);
        Assert.Equal("application/json", sent.MediaType);
        Assert.False(sent.Headers.ContainsKey("authorization"));
        Assert.DoesNotContain(AnthropicKey, sent.Uri.ToString());
        Assert.DoesNotContain(AnthropicKey, sent.Body);
    }

    /// <summary>Opus 5 answers 400 to sampling parameters, a thinking budget and an assistant prefill.</summary>
    [Fact]
    public async Task The_body_is_model_max_tokens_system_and_one_user_message_and_nothing_else()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("anthropic-messages-end-turn.json"));

        await Provider(handler).CompleteAsync(Request("claude-opus-5"), TestContext.Current.CancellationToken);

        var body = handler.Single().Json;
        Assert.Equal(["model", "max_tokens", "system", "messages"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(("claude-opus-5", 1024, "Responda em pt-BR."),
            (body.GetProperty("model").GetString(), body.GetProperty("max_tokens").GetInt32(), body.GetProperty("system").GetString()));
        var message = Assert.Single(body.GetProperty("messages").EnumerateArray());
        Assert.Equal(("user", "{\"mes\":\"2026-08\"}"), (message.GetProperty("role").GetString(), message.GetProperty("content").GetString()));
    }

    [Fact]
    public async Task A_max_tokens_stop_is_a_truncated_answer_and_fails_with_its_tokens()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("anthropic-messages-max-tokens.json"));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal((812, 64), (error.InputTokens, error.OutputTokens));
        Assert.Contains("max_tokens", error.Message);
    }

    [Fact]
    public async Task A_refusal_with_a_200_fails_with_its_tokens()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("anthropic-messages-refusal.json"));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal((1540, 12), (error.InputTokens, error.OutputTokens));
        Assert.Contains("refusal", error.Message);
    }

    [Fact]
    public async Task An_end_turn_with_no_text_block_fails_with_its_tokens()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, """
            {"type":"message","content":[{"type":"thinking","thinking":"","signature":"x"}],
             "stop_reason":"end_turn","usage":{"input_tokens":90,"output_tokens":7}}
            """);

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal((90, 7), (error.InputTokens, error.OutputTokens));
    }

    /// <summary>A 4xx is refused before inference, so nothing was billed; a 5xx is unknown and estimated.</summary>
    [Theory]
    [InlineData(400, "invalid_request_error", 0)]
    [InlineData(401, "authentication_error", 0)]
    [InlineData(402, "billing_error", 0)]
    [InlineData(403, "permission_error", 0)]
    [InlineData(404, "not_found_error", 0)]
    [InlineData(429, "rate_limit_error", 0)]
    [InlineData(500, "api_error", null)]
    [InlineData(529, "overloaded_error", null)]
    public async Task An_error_status_fails_with_its_type_and_no_key(int status, string type, int? inputTokens)
    {
        var handler = new AiHttpHandler((HttpStatusCode)status,
            $$"""{"type":"error","error":{"type":"{{type}}","message":"echo {{AnthropicKey}}"},"request_id":"req_011"}""");

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal((inputTokens, 0), (error.InputTokens, error.OutputTokens));
        Assert.Contains(type, error.Message);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message);
        Assert.DoesNotContain(AnthropicKey, error.Message);
    }

    [Fact]
    public async Task A_404_names_the_model_so_a_wrong_id_is_plain()
    {
        var handler = new AiHttpHandler(HttpStatusCode.NotFound,
            """{"type":"error","error":{"type":"not_found_error","message":"model: gpt-4o"}}""");

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request("gpt-4o"), TestContext.Current.CancellationToken));

        Assert.Contains("'gpt-4o'", error.Message);
        Assert.Contains("Ai:Provider", error.Message);
    }

    [Fact]
    public async Task An_error_that_is_not_json_still_fails_as_a_provider_error()
    {
        var handler = new AiHttpHandler(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>");

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Null(error.InputTokens);
        Assert.Contains("502", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"content\":[")]
    [InlineData("{}")]
    [InlineData("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn"}""")]
    [InlineData("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":"1","output_tokens":2}}""")]
    public async Task A_200_that_cannot_be_read_fails_with_unknown_tokens(string body)
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, body);

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Null(error.InputTokens);
    }

    [Fact]
    public async Task An_empty_key_fails_the_call_before_any_request_and_bills_nothing()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("anthropic-messages-end-turn.json"));

        var error = await Assert.ThrowsAsync<AiProviderException>(() =>
            new AnthropicAiProvider(handler.Client(), Options(anthropicKey: "")).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
        Assert.Equal(0, error.InputTokens);
        Assert.Contains("Ai:Anthropic:ApiKey", error.Message);
    }

    [Fact]
    public async Task A_dropped_connection_fails_with_unknown_tokens()
    {
        var handler = new AiHttpHandler(() => throw new HttpRequestException("Connection reset by peer"));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(handler).CompleteAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Null(error.InputTokens);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    /// <summary>The gateway tells its own timeout from the caller's cancellation, so the adapter leaves both alone.</summary>
    [Fact]
    public async Task A_cancelled_call_stays_cancelled()
    {
        var handler = new AiHttpHandler(HttpStatusCode.OK, Fixture("anthropic-messages-end-turn.json"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Provider(handler).CompleteAsync(Request(), new CancellationToken(canceled: true)));
    }

    private static AnthropicAiProvider Provider(AiHttpHandler handler) => new(handler.Client(), Options());
}
