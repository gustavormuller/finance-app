using System.Net;
using System.Net.Http.Json;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 009's <c>/api/ai/analyses</c> and <c>/api/ai/usage</c> over HTTP: spec tests 13-14
/// (isolation), 15's analysis half, 16 for the analysis, and 21-23 (the lifecycle). The gates
/// run before any row is written. No test calls a real AI API.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AiAnalysisEndpointTests(PostgresFixture postgres)
{
    private sealed record Accepted(Guid AnalysisId);

    private sealed record Analysis(
        Guid Id, string Month, string Status, string? Content, string? Error, string PromptVersion,
        DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);

    private sealed record Usage(string Month, decimal SpentBrl, decimal BudgetBrl, int Calls);

    private sealed record Problem(string Title, string Detail, int Status);

    /// <summary>Spec test 21, on the fake: <c>202</c>, then <c>Completed</c> with the fake's pt-BR markdown.</summary>
    [Fact]
    public async Task A_post_is_a_202_and_the_job_completes_the_analysis_with_content()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString, settings: new Dictionary<string, string?> { ["Ai:FakeProvider"] = "true" });
        var user = await UserAsync(factory, "analysis-fake", ct);

        using var response = await user.Client.SendAsync(TransactionsFixtures.Post("/api/ai/analyses", new { month = "2026-05" }), ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<Accepted>(ct))!.AnalysisId;
        Assert.Equal($"/api/ai/analyses/{id}", response.Headers.Location?.OriginalString);
        var done = await SettledAsync(user, id, ct);
        Assert.Equal(("2026-05", "Completed", null), (done.Month, done.Status, done.Error));
        Assert.StartsWith("## Resumo", done.Content);
        Assert.Contains("2026-05", done.Content);
        Assert.Equal(1, (await UsageAsync(user, ct)).Calls);
    }

    /// <summary>Spec test 21's <c>Pending</c> row, held there while the one consumer is busy; and a second POST for a busy month is a 409.</summary>
    [Fact]
    public async Task A_row_waits_pending_behind_a_running_one_and_a_month_in_progress_cannot_be_posted_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new GatedAiProvider();
        await using var factory = Scripted(provider);
        var user = await UserAsync(factory, "analysis-gated", ct);

        var running = await PostAsync(user, "2026-05", ct);
        await provider.Called.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        var waiting = await PostAsync(user, "2026-04", ct);

        Assert.Equal("Running", (await GetAsync(user, running, ct)).Status);
        var pending = await GetAsync(user, waiting, ct);
        Assert.Equal(("Pending", (DateTimeOffset?)null), (pending.Status, pending.StartedAt));
        using (var again = await user.Client.SendAsync(TransactionsFixtures.Post("/api/ai/analyses", new { month = "2026-05" }), ct))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, ct);
        }

        provider.Release.SetResult();
        Assert.Equal("Completed", (await SettledAsync(user, running, ct)).Status);
        Assert.Equal("Completed", (await SettledAsync(user, waiting, ct)).Status);
    }

    /// <summary>Spec test 22 over HTTP: <c>Failed</c> with pt-BR copy, never the message, and the call counted.</summary>
    [Fact]
    public async Task A_failing_provider_fails_the_analysis_with_pt_br_copy_and_the_call_is_counted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Scripted(new ThrowingAiProvider());
        var user = await UserAsync(factory, "analysis-fail", ct);

        var done = await SettledAsync(user, await PostAsync(user, "2026-05", ct), ct);

        Assert.Equal(("Failed", null, AiFailureText.ProviderFailed), (done.Status, done.Content, done.Error));
        var usage = await UsageAsync(user, ct);
        Assert.Equal(1, usage.Calls);
        Assert.True(usage.SpentBrl > 0m);
    }

    /// <summary>Spec test 23: regenerating replaces the month's row; one remains, run again.</summary>
    [Fact]
    public async Task A_second_post_for_a_settled_month_replaces_it_and_one_row_remains()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Scripted(new ThrowingAiProvider(failFirst: true));
        var user = await UserAsync(factory, "analysis-again", ct);
        var first = await SettledAsync(user, await PostAsync(user, "2026-05", ct), ct);

        var second = await SettledAsync(user, await PostAsync(user, "2026-05", ct), ct);

        Assert.Equal(("Failed", "Completed"), (first.Status, second.Status));
        Assert.Null(second.Error);
        Assert.True(second.CreatedAt > first.CreatedAt);
        Assert.Equal([second.Id], (await ListAsync(user, "?month=2026-05", ct)).Select(analysis => analysis.Id));
        Assert.Equal(2, (await UsageAsync(user, ct)).Calls);
    }

    /// <summary>Spec test 15, the analysis half, and test 16's: refused before a row or a call.</summary>
    [Theory]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(true, HttpStatusCode.PaymentRequired)]
    public async Task Ai_off_is_a_403_and_the_budget_spent_a_402_with_no_row_no_call_and_no_new_usage(bool enabled, HttpStatusCode status)
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new GatedAiProvider();
        await using var factory = Scripted(provider);
        var user = await UserAsync(factory, "analysis-gate", ct, enabled);
        if (enabled)
        {
            await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id);
            var spent = AiPersistenceTests.AUsage(user.Id);
            (spent.Month, spent.CostBrl) = (AiCost.MonthOf(DateTimeOffset.UtcNow), 15.00m);
            context.AiUsage.Add(spent);
            await context.SaveChangesAsync(ct);
        }

        using var response = await user.Client.SendAsync(TransactionsFixtures.Post("/api/ai/analyses", new { month = "2026-05" }), ct);

        await AssertProblemAsync(response, status, ct);
        Assert.Empty(await ListAsync(user, "", ct));
        Assert.Equal(enabled ? 1 : 0, (await UsageAsync(user, ct)).Calls);
        Assert.False(provider.Called.Task.IsCompleted);
    }

    /// <summary>Spec tests 13 and 14 over HTTP.</summary>
    [Fact]
    public async Task One_users_analyses_and_usage_are_invisible_to_another_and_their_id_is_a_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString, settings: new Dictionary<string, string?> { ["Ai:FakeProvider"] = "true" });
        var a = await UserAsync(factory, "analysis-a", ct);
        var b = await UserAsync(factory, "analysis-b", ct);
        var id = (await SettledAsync(a, await PostAsync(a, "2026-05", ct), ct)).Id;

        using var foreign = await b.Client.GetAsync($"/api/ai/analyses/{id}", ct);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Empty(await ListAsync(b, "", ct));
        var usage = await UsageAsync(b, ct);
        Assert.Equal((0m, 0), (usage.SpentBrl, usage.Calls));
        Assert.Equal([id], (await ListAsync(a, "", ct)).Select(analysis => analysis.Id));
    }

    [Fact]
    public async Task Usage_is_the_months_spend_calls_and_budget_and_the_list_filters_by_month()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await UserAsync(factory, "analysis-usage", ct, enabled: false);
        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            foreach (var (month, cost, succeeded) in new[] { ("2026-05", 0.1234m, true), ("2026-05", 0.0100m, false), ("2026-04", 1m, true) })
            {
                var row = AiPersistenceTests.AUsage(user.Id);
                (row.Month, row.CostBrl, row.Succeeded) = (month, cost, succeeded);
                context.AiUsage.Add(row);
            }

            await context.SaveChangesAsync(ct);
            await AnalysisJobTests.PendingAsync(postgres.ConnectionString, user.Id, "2026-05", ct);
            await AnalysisJobTests.PendingAsync(postgres.ConnectionString, user.Id, "2026-04", ct);
        }

        Assert.Equal(new Usage("2026-05", 0.1334m, 15.00m, 2), await user.Client.GetFromJsonAsync<Usage>("/api/ai/usage?month=2026-05", ct));
        Assert.Equal(AiCost.MonthOf(DateTimeOffset.UtcNow), (await UsageAsync(user, ct)).Month);
        Assert.Equal(["2026-05", "2026-04"], (await ListAsync(user, "", ct)).Select(analysis => analysis.Month));
        Assert.Equal(["2026-04"], (await ListAsync(user, "?month=2026-04", ct)).Select(analysis => analysis.Month));
    }

    [Theory]
    [InlineData("/api/ai/usage?month=2026-13")]
    [InlineData("/api/ai/analyses?month=maio")]
    public async Task A_malformed_month_in_a_query_is_a_400(string url)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await UserAsync(factory, "analysis-query", ct);

        using var response = await user.Client.GetAsync(url, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-5")]
    [InlineData("2026-00")]
    [InlineData("2999-01")]
    public async Task A_missing_malformed_or_future_month_is_a_400_naming_the_field_and_writes_nothing(string? month)
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new GatedAiProvider();
        await using var factory = Scripted(provider);
        var user = await UserAsync(factory, "analysis-month", ct);

        using var response = await user.Client.SendAsync(TransactionsFixtures.Post("/api/ai/analyses", new { month }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["month"], await TransactionsFixtures.ProblemFieldsAsync(response, ct));
        Assert.Empty(await ListAsync(user, "", ct));
    }

    private IdentityApiFactory Scripted(IAiProvider provider) =>
        new(postgres.ConnectionString, services: services => services.AddSingleton(provider));

    private static async Task<SignedInUser> UserAsync(IdentityApiFactory factory, string prefix, CancellationToken ct, bool enabled = true)
    {
        var user = await factory.SignInNewUserAsync(prefix, ct);
        using var toggle = await user.Client.SendAsync(ImportFixtures.Patch("/api/auth/me", new { aiEnabled = enabled }), ct);
        toggle.EnsureSuccessStatusCode();
        return user;
    }

    private static async Task<Guid> PostAsync(SignedInUser user, string month, CancellationToken ct)
    {
        using var response = await user.Client.SendAsync(TransactionsFixtures.Post("/api/ai/analyses", new { month }), ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Accepted>(ct))!.AnalysisId;
    }

    private static async Task<Analysis> GetAsync(SignedInUser user, Guid id, CancellationToken ct) =>
        (await user.Client.GetFromJsonAsync<Analysis>($"/api/ai/analyses/{id}", ct))!;

    private static async Task<List<Analysis>> ListAsync(SignedInUser user, string query, CancellationToken ct) =>
        (await user.Client.GetFromJsonAsync<List<Analysis>>($"/api/ai/analyses{query}", ct))!;

    private static async Task<Usage> UsageAsync(SignedInUser user, CancellationToken ct) =>
        (await user.Client.GetFromJsonAsync<Usage>("/api/ai/usage", ct))!;

    /// <summary>What the card does: poll until the job has settled the row, here for up to 20 s.</summary>
    private static async Task<Analysis> SettledAsync(SignedInUser user, Guid id, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var analysis = await GetAsync(user, id, ct);
            if (analysis.Status is "Completed" or "Failed")
            {
                return analysis;
            }

            await Task.Delay(100, ct);
        }

        throw new TimeoutException($"Analysis {id} did not settle.");
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, CancellationToken ct)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Problem>(ct);
        Assert.Equal((int)status, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.DoesNotContain("AI ", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>Answers once released, so a test can hold the one consumer busy.</summary>
    private sealed class GatedAiProvider : IAiProvider
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Called.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new AiCompletion("## Resumo\n\nOk.", 100, 50);
        }
    }

    /// <summary>Fails every call, or only the first.</summary>
    private sealed class ThrowingAiProvider(bool failFirst = false) : IAiProvider
    {
        private int calls;

        public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct) =>
            Interlocked.Increment(ref calls) == 1 || !failFirst
                ? throw new AiProviderException("secret-model-id failed", inputTokens: 1000)
                : Task.FromResult(new AiCompletion("## Resumo\n\nDe novo.", 100, 50));
    }
}
