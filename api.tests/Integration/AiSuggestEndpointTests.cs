using System.Net;
using System.Net.Http.Json;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Ai;
using Finance.Api.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 009 spec integration tests 15-20 for <c>POST /api/imports/{id}/suggest</c>: the gates on
/// a scripted provider, the categorisation on <see cref="FakeAiProvider"/>. Every refusal is
/// a pt-BR problem detail that never echoes the exception's message. The analysis endpoint
/// tests cover test 15's analysis half.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AiSuggestEndpointTests(PostgresFixture postgres)
{
    private const string Secret = "secret-model-id and an English stack message";

    private sealed record SuggestCounts(int Suggested, int Skipped);

    private sealed record Problem(string Title, string Detail, int Status);

    /// <summary>Spec tests 18 and 19.</summary>
    [Fact]
    public async Task Default_rows_get_the_fakes_categories_and_a_remembered_row_is_not_sent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = FakeOn();
        var (user, batchId) = await StagedAsync(factory, "suggest-fake", ["PAG*IFOOD 15/09", "PADARIA REAL", "POSTO SHELL"], ct);
        var categories = await user.CategoriesAsync(ct);

        using var response = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{batchId}/suggest"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new SuggestCounts(2, 0), await response.Content.ReadFromJsonAsync<SuggestCounts>(ct));
        var rows = (await user.GetBatchAsync(batchId, ct)).Rows.Items;
        Assert.Equal(
            [("History", categories["Lazer"].Id), ("Ai", categories["Alimentação"].Id), ("Ai", categories["Alimentação"].Id)],
            rows.Select(row => (row.CategorySource!, row.CategoryId!.Value)));
    }

    /// <summary>Spec test 20.</summary>
    [Fact]
    public async Task A_garbage_answer_is_a_200_with_nothing_suggested_and_nothing_changed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = FakeOn();
        var (user, batchId) = await StagedAsync(factory, "suggest-garbage", [$"LOJA {FakeAiProvider.GarbageMarker}", "POSTO SHELL"], ct);
        var before = await CategoriesOfRowsAsync(user, batchId, ct);

        using var response = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{batchId}/suggest"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new SuggestCounts(0, 2), await response.Content.ReadFromJsonAsync<SuggestCounts>(ct));
        Assert.Equal(before, await CategoriesOfRowsAsync(user, batchId, ct));
    }

    /// <summary>Spec test 15, the suggest half.</summary>
    [Fact]
    public async Task With_ai_off_it_is_a_403_and_no_usage_is_recorded()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => throw new InvalidOperationException("not called"));
        await using var factory = Scripted(provider);
        var (user, batchId) = await StagedAsync(factory, "suggest-off", ["POSTO SHELL"], ct, aiEnabled: false);

        using var response = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{batchId}/suggest"), ct);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, ct);
        Assert.Empty(provider.Requests);
        Assert.Empty(await UsageAsync(user.Id, ct));
    }

    /// <summary>Spec test 16.</summary>
    [Fact]
    public async Task Over_the_budget_it_is_a_402_and_the_provider_is_not_called()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => throw new InvalidOperationException("not called"));
        await using var factory = Scripted(provider);
        var (user, batchId) = await StagedAsync(factory, "suggest-budget", ["POSTO SHELL"], ct);
        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var spent = AiPersistenceTests.AUsage(user.Id);
            (spent.Month, spent.CostBrl) = (AiCost.MonthOf(DateTimeOffset.UtcNow), 15.00m);
            context.AiUsage.Add(spent);
            await context.SaveChangesAsync(ct);
        }

        using var response = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{batchId}/suggest"), ct);

        await AssertProblemAsync(response, HttpStatusCode.PaymentRequired, ct);
        Assert.Empty(provider.Requests);
        Assert.Single(await UsageAsync(user.Id, ct));
    }

    /// <summary>Spec test 17 over HTTP: a 502, recorded with the input tokens the provider reported.</summary>
    [Fact]
    public async Task A_provider_failure_is_a_502_and_its_usage_is_recorded_as_failed_with_its_tokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Scripted(new ScriptedAiProvider(_ => throw new AiProviderException(Secret, inputTokens: 1234)));
        var (user, batchId) = await StagedAsync(factory, "suggest-fail", ["POSTO SHELL"], ct);

        using var response = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{batchId}/suggest"), ct);

        await AssertProblemAsync(response, HttpStatusCode.BadGateway, ct);
        var usage = Assert.Single(await UsageAsync(user.Id, ct));
        Assert.Equal((false, 1234, AiPurpose.Categorisation), (usage.Succeeded, usage.InputTokens, usage.Purpose));
    }

    /// <summary>Decision 4's 30 s, past which the spec's 504; caught before its base class's 502.</summary>
    [Fact]
    public async Task A_provider_timeout_is_a_504()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Scripted(new ScriptedAiProvider(_ => throw new AiProviderTimeoutException(Secret)));
        var (user, batchId) = await StagedAsync(factory, "suggest-timeout", ["POSTO SHELL"], ct);

        using var response = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{batchId}/suggest"), ct);

        await AssertProblemAsync(response, HttpStatusCode.GatewayTimeout, ct);
        Assert.False(Assert.Single(await UsageAsync(user.Id, ct)).Succeeded);
    }

    [Fact]
    public async Task Another_users_batch_is_a_404_and_a_committed_one_a_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = FakeOn();
        var (owner, batchId) = await StagedAsync(factory, "suggest-owner", ["POSTO SHELL"], ct);
        var (other, _) = await StagedAsync(factory, "suggest-other", ["POSTO SHELL"], ct);

        using var foreign = await other.Client.SendAsync(ImportFixtures.Post($"/api/imports/{batchId}/suggest"), ct);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        await owner.CommitAsync(batchId, ct);
        using var committed = await owner.Client.SendAsync(ImportFixtures.Post($"/api/imports/{batchId}/suggest"), ct);
        await AssertProblemAsync(committed, HttpStatusCode.Conflict, ct);
    }

    private IdentityApiFactory FakeOn() =>
        new(postgres.ConnectionString, settings: new Dictionary<string, string?> { ["Ai:FakeProvider"] = "true" });

    private IdentityApiFactory Scripted(IAiProvider provider) =>
        new(postgres.ConnectionString, services: services => services.AddSingleton(provider));

    /// <summary>
    /// A user, AI turned on through <c>PATCH /api/auth/me</c> unless told otherwise, "PAG*IFOOD"
    /// committed as Lazer for history, then a batch of debits with these descriptions.
    /// </summary>
    private static async Task<(SignedInUser User, Guid BatchId)> StagedAsync(
        IdentityApiFactory factory, string prefix, string[] descriptions, CancellationToken ct, bool aiEnabled = true)
    {
        var user = await factory.SignInNewUserAsync(prefix, ct);
        using (var toggle = await user.Client.SendAsync(ImportFixtures.Patch("/api/auth/me", new { aiEnabled }), ct))
        {
            toggle.EnsureSuccessStatusCode();
        }

        var accountId = await user.CreateAccountAsync("Nubank", ct);
        var first = await user.UploadOfxAsync(
            accountId, ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260901", "-42.90", "S-0", "PAG*IFOOD 01/09")]), ct);
        var remembered = Assert.Single((await user.GetBatchAsync(first.BatchId, ct)).Rows.Items);
        using (var patch = await user.Client.SendAsync(
            ImportFixtures.Patch($"/api/imports/{first.BatchId}/rows/{remembered.Id}", new { categoryId = (await user.CategoriesAsync(ct))["Lazer"].Id }), ct))
        {
            patch.EnsureSuccessStatusCode();
        }

        await user.CommitAsync(first.BatchId, ct);
        var batch = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx(descriptions.Select((description, index) =>
                new ImportFixtures.OfxRow("20260915", $"-{index + 10}.00", $"S-{index + 1}", description))),
            ct);
        return (user, batch.BatchId);
    }

    private static async Task<List<(Guid Id, Guid? CategoryId, string? Source)>> CategoriesOfRowsAsync(
        SignedInUser user, Guid batchId, CancellationToken ct) =>
        [.. (await user.GetBatchAsync(batchId, ct)).Rows.Items.Select(row => (row.Id, row.CategoryId, row.CategorySource))];

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, CancellationToken ct)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AI budget", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not enabled", body, StringComparison.Ordinal);
        var problem = await response.Content.ReadFromJsonAsync<Problem>(ct);
        Assert.Equal((int)status, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    private async Task<List<AiUsage>> UsageAsync(Guid userId, CancellationToken ct)
    {
        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, userId);
        return await context.AiUsage.ToListAsync(ct);
    }

    private sealed class ScriptedAiProvider(Func<AiRequest, AiCompletion> answer) : IAiProvider
    {
        public List<AiRequest> Requests { get; } = [];

        public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }
}
