using Finance.Api.Application.Ai;
using Finance.Api.Infrastructure.Ai;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 009 checkpoint 2: the E2E run's provider. Fixed, network-free and deterministic; its
/// token counts follow the request, so the budget and usage paths see real numbers.
/// </summary>
public sealed class FakeAiProviderTests
{
    [Fact]
    public async Task It_answers_the_same_request_the_same_way_in_pt_br_markdown()
    {
        var request = new AiRequest("any-model", "sistema", "dados", 1000);

        var first = await new FakeAiProvider().CompleteAsync(request, TestContext.Current.CancellationToken);
        var second = await new FakeAiProvider().CompleteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.StartsWith("## Resumo", first.Text);
    }

    [Fact]
    public async Task Its_tokens_are_four_characters_each_rounded_up()
    {
        var request = new AiRequest("any-model", new string('s', 10), new string('u', 7), 1000);

        var completion = await new FakeAiProvider().CompleteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(5, completion.InputTokens); // 17 characters
        Assert.Equal((completion.Text.Length + 3) / 4, completion.OutputTokens);
    }

    [Fact]
    public async Task A_cancelled_call_is_cancelled()
    {
        var request = new AiRequest("any-model", "sistema", "dados", 1000);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FakeAiProvider().CompleteAsync(request, new CancellationToken(canceled: true)));
    }
}
