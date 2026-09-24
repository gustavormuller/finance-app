using System.Text.Json;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Ai;
using Finance.Api.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 009's <c>AnalysisJob</c> on its channel, on a scripted provider: the job halves of spec
/// tests 21 and 22, test 24's startup sweep, and the job half of tests 13 and 14 (a scope
/// acting for B cannot run A's analysis). Every <c>Error</c> is pt-BR copy, never the
/// exception's message.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AnalysisJobTests(PostgresFixture postgres)
{
    private const string Secret = "secret-model-id and an English stack message";

    [Fact]
    public async Task A_pending_analysis_runs_on_the_prompt_and_the_input_and_completes_with_the_content()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => new AiCompletion("  ## Resumo\n\nTudo certo.\n", 3000, 400));
        await using var factory = Scripted(provider);
        var user = await EnabledUserAsync(factory, ct);
        var id = await PendingAsync(postgres.ConnectionString, user, "2026-05", ct);

        Enqueue(factory, user, id);
        var analysis = await SettledAsync(postgres.ConnectionString, user, id, ct);

        Assert.Equal((AiAnalysisStatus.Completed, "## Resumo\n\nTudo certo.", null), (analysis.Status, analysis.Content, analysis.Error));
        Assert.Equal(MonthlyAnalysisPrompt.Current.Version, analysis.PromptVersion);
        Assert.True(analysis.StartedAt <= analysis.CompletedAt);
        var request = Assert.Single(provider.Requests);
        Assert.Equal((MonthlyAnalysisPrompt.Current.System, 8000), (request.System, request.MaxTokens));
        using (var input = JsonDocument.Parse(request.User))
        {
            Assert.Equal("2026-05", input.RootElement.GetProperty("month").GetString());
        }

        var usage = Assert.Single(await UsageAsync(user, ct));
        Assert.Equal((AiPurpose.Analysis, true, 3000), (usage.Purpose, usage.Succeeded, usage.InputTokens));
    }

    public static TheoryData<string, string> Failures => new()
    {
        { "provider", AiFailureText.ProviderFailed },
        { "timeout", AiFailureText.Timeout },
        { "bug", AiFailureText.Unexpected },
    };

    /// <summary>Spec test 22 at the job: failed, a pt-BR reason, and the call's usage recorded.</summary>
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_failing_call_fails_the_analysis_with_pt_br_copy_and_its_usage_is_recorded(string kind, string error)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Scripted(new ScriptedAiProvider(_ => throw (kind switch
        {
            "provider" => new AiProviderException(Secret, inputTokens: 1234),
            "timeout" => new AiProviderTimeoutException(Secret),
            _ => new InvalidOperationException(Secret),
        })));
        var user = await EnabledUserAsync(factory, ct);
        var id = await PendingAsync(postgres.ConnectionString, user, "2026-05", ct);

        Enqueue(factory, user, id);
        var analysis = await SettledAsync(postgres.ConnectionString, user, id, ct);

        Assert.Equal((AiAnalysisStatus.Failed, null, error), (analysis.Status, analysis.Content, analysis.Error));
        Assert.NotNull(analysis.CompletedAt);
        Assert.False(Assert.Single(await UsageAsync(user, ct)).Succeeded);
    }

    /// <summary>The gateway's own gates, checked again when the job runs: nothing is called or recorded.</summary>
    [Theory]
    [InlineData(false, AiFailureText.Disabled)]
    [InlineData(true, AiFailureText.BudgetExceeded)]
    public async Task Ai_turned_off_or_the_budget_spent_after_the_request_fails_it_without_a_call(bool enabled, string error)
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => throw new InvalidOperationException("not called"));
        await using var factory = Scripted(provider);
        var user = await EnabledUserAsync(factory, ct, enabled);
        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user))
        {
            var spent = AiPersistenceTests.AUsage(user);
            (spent.Month, spent.CostBrl) = (AiCost.MonthOf(DateTimeOffset.UtcNow), 15.00m);
            context.AiUsage.Add(spent);
            await context.SaveChangesAsync(ct);
        }

        var id = await PendingAsync(postgres.ConnectionString, user, "2026-05", ct);
        Enqueue(factory, user, id);

        var analysis = await SettledAsync(postgres.ConnectionString, user, id, ct);
        Assert.Equal((AiAnalysisStatus.Failed, error), (analysis.Status, analysis.Error));
        Assert.Empty(provider.Requests);
        Assert.Single(await UsageAsync(user, ct));
    }

    /// <summary>Tests 13 and 14 at the job: a scope acting for B does not see A's row, so nothing runs.</summary>
    [Fact]
    public async Task A_scope_acting_for_another_user_neither_runs_nor_touches_the_analysis()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => new AiCompletion("## Resumo", 10, 10));
        await using var factory = Scripted(provider);
        var a = await EnabledUserAsync(factory, ct);
        var b = await EnabledUserAsync(factory, ct);
        var foreign = await PendingAsync(postgres.ConnectionString, a, "2026-05", ct);
        var own = await PendingAsync(postgres.ConnectionString, b, "2026-05", ct);

        Enqueue(factory, b, foreign);
        Enqueue(factory, b, own);
        await SettledAsync(postgres.ConnectionString, b, own, ct);

        Assert.Single(provider.Requests);
        Assert.Equal(AiAnalysisStatus.Pending, (await AnalysisAsync(postgres.ConnectionString, a, foreign, ct)).Status);
        Assert.Empty(await UsageAsync(a, ct));
    }

    [Fact]
    public async Task An_analysis_no_longer_pending_is_not_run_again_when_it_is_dequeued_twice()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => new AiCompletion("## Resumo", 10, 10));
        await using var factory = Scripted(provider);
        var user = await EnabledUserAsync(factory, ct);
        var id = await PendingAsync(postgres.ConnectionString, user, "2026-05", ct);

        Enqueue(factory, user, id);
        Enqueue(factory, user, id);
        var marker = await PendingAsync(postgres.ConnectionString, user, "2026-04", ct);
        Enqueue(factory, user, marker);
        await SettledAsync(postgres.ConnectionString, user, marker, ct);

        Assert.Equal(2, provider.Requests.Count);
    }

    /// <summary>
    /// Spec test 24: at startup a <c>Pending</c> row older than 5 minutes is re-enqueued and a
    /// younger one is left to its request; a <c>Running</c> row past its timeout and 5 minutes
    /// was cut off by the restart and fails, since its call may already have been paid for.
    /// A fresh database, so the sweep sees no other test's rows.
    /// </summary>
    [Fact]
    public async Task The_startup_sweep_re_enqueues_stale_pending_rows_and_fails_interrupted_running_ones()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await postgres.CreateEmptyDatabaseAsync(ct);
        await using var users = new IdentityApiFactory(connection);
        await users.MigrateAsync(ct);
        var user = await EnabledUserAsync(users, ct, connection: connection);
        var now = DateTimeOffset.UtcNow;
        var stale = await PendingAsync(connection, user, "2026-05", ct, created: now.AddMinutes(-6));
        var young = await PendingAsync(connection, user, "2026-04", ct, created: now.AddMinutes(-1));
        var cutOff = await PendingAsync(connection, user, "2026-03", ct, created: now.AddHours(-1), started: now.AddMinutes(-8));
        var live = await PendingAsync(connection, user, "2026-02", ct, created: now.AddMinutes(-2), started: now.AddMinutes(-1));

        var provider = new ScriptedAiProvider(_ => new AiCompletion("## Resumo", 10, 10));
        await using var job = new IdentityApiFactory(connection, settings: new Dictionary<string, string?> { ["Ai:AnalysisSweep"] = "true" },
            services: services => services.AddSingleton<IAiProvider>(provider));
        _ = job.Services;

        Assert.Equal(AiAnalysisStatus.Completed, (await SettledAsync(connection, user, stale, ct)).Status);
        var interrupted = await SettledAsync(connection, user, cutOff, ct);
        Assert.Equal((AiAnalysisStatus.Failed, AiFailureText.Interrupted), (interrupted.Status, interrupted.Error));
        Assert.Equal(AiAnalysisStatus.Pending, (await AnalysisAsync(connection, user, young, ct)).Status);
        Assert.Equal(AiAnalysisStatus.Running, (await AnalysisAsync(connection, user, live, ct)).Status);
        Assert.Single(provider.Requests);
    }

    private IdentityApiFactory Scripted(IAiProvider provider) =>
        new(postgres.ConnectionString, services: services => services.AddSingleton(provider));

    private static void Enqueue(IdentityApiFactory factory, Guid user, Guid id) =>
        factory.Services.GetRequiredService<AnalysisQueue>().Enqueue(user, id);

    private async Task<Guid> EnabledUserAsync(IdentityApiFactory factory, CancellationToken ct, bool enabled = true, string? connection = null)
    {
        var id = (await factory.SignInNewUserAsync("analysis-job", ct)).Id;
        await using var context = TransactionsFixtures.ContextFor(connection ?? postgres.ConnectionString, null);
        await context.Users.Where(user => user.Id == id).ExecuteUpdateAsync(set => set.SetProperty(user => user.AiEnabled, enabled), ct);
        return id;
    }

    /// <summary>A row as <c>POST</c> writes it, or, with <paramref name="started"/>, as the job leaves it running.</summary>
    internal static async Task<Guid> PendingAsync(
        string connection, Guid user, string month, CancellationToken ct, DateTimeOffset? created = null, DateTimeOffset? started = null)
    {
        await using var context = TransactionsFixtures.ContextFor(connection, user);
        var row = new AiAnalysis
        {
            Id = Guid.NewGuid(),
            UserId = user,
            Month = month,
            Status = started is null ? AiAnalysisStatus.Pending : AiAnalysisStatus.Running,
            PromptVersion = "0",
            CreatedAt = created ?? DateTimeOffset.UtcNow,
            StartedAt = started,
        };
        context.AiAnalyses.Add(row);
        await context.SaveChangesAsync(ct);
        return row.Id;
    }

    /// <summary>The row once the job has finished with it, polled for up to 20 s.</summary>
    internal static async Task<AiAnalysis> SettledAsync(string connection, Guid user, Guid id, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var row = await AnalysisAsync(connection, user, id, ct);
            if (row.Status is AiAnalysisStatus.Completed or AiAnalysisStatus.Failed)
            {
                return row;
            }

            await Task.Delay(100, ct);
        }

        throw new TimeoutException($"Analysis {id} did not settle.");
    }

    private static async Task<AiAnalysis> AnalysisAsync(string connection, Guid user, Guid id, CancellationToken ct)
    {
        await using var context = TransactionsFixtures.ContextFor(connection, user);
        return await context.AiAnalyses.AsNoTracking().SingleAsync(row => row.Id == id, ct);
    }

    private async Task<List<AiUsage>> UsageAsync(Guid user, CancellationToken ct)
    {
        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user);
        return await context.AiUsage.ToListAsync(ct);
    }

    private sealed class ScriptedAiProvider(Func<AiRequest, AiCompletion> answer) : IAiProvider
    {
        public List<AiRequest> Requests { get; } = [];

        public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return Task.FromResult(answer(request));
        }
    }
}
