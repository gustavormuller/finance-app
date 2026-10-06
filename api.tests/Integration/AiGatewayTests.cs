using Finance.Api.Application;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Ai;
using Finance.Api.Domain.MarketData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The database halves of spec 009 tests 1, 2 and 4, and decision 3, on the path a job takes
/// (a scope acting for the user, the query filters on). A scripted provider stands in; no test
/// calls a real AI API.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed partial class AiGatewayTests(PostgresFixture postgres)
{
    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Ai:MonthlyBudgetBrl"] = "15.00",
        ["Ai:UsdBrl"] = "5.40",
        ["Ai:Categorisation:Model"] = "test-cheap",
        ["Ai:Analysis:Model"] = "test-better",
        ["Ai:Pricing:test-cheap:InputPerMTokUsd"] = "1",
        ["Ai:Pricing:test-cheap:OutputPerMTokUsd"] = "5",
        ["Ai:Pricing:test-better:InputPerMTokUsd"] = "5",
        ["Ai:Pricing:test-better:OutputPerMTokUsd"] = "25",
    };

    [Fact]
    public async Task A_call_is_recorded_with_its_model_tokens_cost_at_the_fallback_and_its_utc_minus_3_month()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-01T02:30:00Z"));
        var provider = new ScriptedAiProvider(_ => new AiCompletion("## Resumo", 30_000, 800));
        await using var host = await HostAsync(provider, clock, ct);
        var user = await EnabledUserAsync(host, ct);

        var completion = await CallAsync(host, user, AiPurpose.Analysis, ct);

        Assert.Equal("## Resumo", completion.Text);
        var request = Assert.Single(provider.Requests);
        Assert.Equal(("test-better", "sistema", "dados", 1000), (request.Model, request.System, request.User, request.MaxTokens));
        var row = Assert.Single(await UsageAsync(host.Connection, user, ct));
        Assert.Equal(
            ("2026-08", AiPurpose.Analysis, "anthropic", "test-better", 30_000, 800, 0.9180m, true, clock.GetUtcNow()),
            (row.Month, row.Purpose, row.Provider, row.Model, row.InputTokens, row.OutputTokens, row.CostBrl, row.Succeeded, row.CreatedAt));
    }

    /// <summary>Spec test 4, at the database: the latest <c>USDBRL</c> by date, not the fallback.</summary>
    [Fact]
    public async Task The_cost_uses_the_latest_usdbrl_benchmark_when_one_is_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => new AiCompletion("ok", 30_000, 800));
        await using var host = await HostAsync(provider, Clock(), ct);
        var user = await EnabledUserAsync(host, ct);
        await using (var context = TransactionsFixtures.ContextFor(host.Connection, null))
        {
            context.Benchmarks.AddRange(
                new Benchmark { Code = "USDBRL", Date = new DateOnly(2026, 9, 14), Value = 5.00m },
                new Benchmark { Code = "USDBRL", Date = new DateOnly(2026, 9, 10), Value = 6.00m },
                new Benchmark { Code = "IVVB11", Date = new DateOnly(2026, 9, 15), Value = 400m });
            await context.SaveChangesAsync(ct);
        }

        await CallAsync(host, user, AiPurpose.Analysis, ct);

        Assert.Equal(0.8500m, Assert.Single(await UsageAsync(host.Connection, user, ct)).CostBrl); // 170000 * 5.00 / 1e6
    }

    /// <summary>Spec test 17's core: a failed call is recorded, with the input tokens the provider reported.</summary>
    [Fact]
    public async Task A_failed_call_is_recorded_as_not_succeeded_with_its_reported_tokens_and_the_error_propagates()
    {
        var ct = TestContext.Current.CancellationToken;
        var failure = new AiProviderException("provider error", inputTokens: 1234);
        await using var host = await HostAsync(new ScriptedAiProvider(_ => throw failure), Clock(), ct);
        var user = await EnabledUserAsync(host, ct);

        var thrown = await Assert.ThrowsAsync<AiProviderException>(() => CallAsync(host, user, AiPurpose.Categorisation, ct));

        Assert.Same(failure, thrown);
        var row = Assert.Single(await UsageAsync(host.Connection, user, ct));
        Assert.Equal(
            (false, "test-cheap", AiPurpose.Categorisation, 1234, 0, 0.0067m), // 1234 * 1 * 5.40 / 1e6 = 0.0066636
            (row.Succeeded, row.Model, row.Purpose, row.InputTokens, row.OutputTokens, row.CostBrl));
    }

    /// <summary>A timeout reports nothing: the input is estimated at four characters a token, rounded up.</summary>
    [Fact]
    public async Task A_failure_that_reports_no_tokens_records_an_estimate_of_the_input()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await HostAsync(new ScriptedAiProvider(_ => throw new TimeoutException()), Clock(), ct);
        var user = await EnabledUserAsync(host, ct);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            CallAsync(host, user, AiPurpose.Analysis, ct, system: new string('s', 4000), prompt: new string('u', 3999)));

        var row = Assert.Single(await UsageAsync(host.Connection, user, ct));
        Assert.Equal((false, 2000, 0, 0.0540m), (row.Succeeded, row.InputTokens, row.OutputTokens, row.CostBrl));
    }

    private static FakeTimeProvider Clock() => new(DateTimeOffset.Parse("2026-09-15T12:00:00Z"));

    /// <summary>
    /// Two hosts on one fresh database: users sign in on a real clock (the fake one would
    /// hand out a session cookie that has already expired), and the gateway runs on the
    /// fake clock and the scripted provider. A fresh database, because <c>Benchmarks</c> is
    /// shared and other tests store <c>USDBRL</c>.
    /// </summary>
    private async Task<Host> HostAsync(IAiProvider provider, TimeProvider clock, CancellationToken ct, params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?>(Settings);
        foreach (var (key, value) in extra)
        {
            settings[key] = value;
        }

        var connection = await postgres.CreateEmptyDatabaseAsync(ct);
        var users = new IdentityApiFactory(connection, settings: settings);
        await users.MigrateAsync(ct);
        var gateway = new IdentityApiFactory(connection, settings: settings, services: services =>
        {
            services.AddSingleton(provider);
            services.AddSingleton(clock);
        });
        return new Host(gateway, users, connection);
    }

    private static async Task<Guid> EnabledUserAsync(Host host, CancellationToken ct)
    {
        var id = (await host.Users.SignInNewUserAsync("ai-on", ct)).Id;
        await using var context = TransactionsFixtures.ContextFor(host.Connection, null);
        await context.Users.Where(user => user.Id == id).ExecuteUpdateAsync(set => set.SetProperty(user => user.AiEnabled, true), ct);
        return id;
    }

    private static async Task<AiCompletion> CallAsync(
        Host host, Guid user, AiPurpose purpose, CancellationToken ct, string system = "sistema", string prompt = "dados")
    {
        await using var scope = host.Gateway.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ActingUser>().ActAs(user);
        return await scope.ServiceProvider.GetRequiredService<AiGateway>().CompleteAsync(purpose, system, prompt, 1000, ct);
    }

    private static async Task<List<AiUsage>> UsageAsync(string connection, Guid user, CancellationToken ct)
    {
        await using var context = TransactionsFixtures.ContextFor(connection, user);
        return await context.AiUsage.ToListAsync(ct);
    }

    private static async Task SeedUsageAsync(string connection, Guid user, params (string Month, decimal Cost, bool Succeeded)[] rows)
    {
        await using var context = TransactionsFixtures.ContextFor(connection, user);
        foreach (var (month, cost, succeeded) in rows)
        {
            var row = AiPersistenceTests.AUsage(user);
            (row.Month, row.CostBrl, row.Succeeded) = (month, cost, succeeded);
            context.AiUsage.Add(row);
        }

        await context.SaveChangesAsync();
    }

    private sealed record Host(IdentityApiFactory Gateway, IdentityApiFactory Users, string Connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Gateway.DisposeAsync();
            await Users.DisposeAsync();
        }
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
