using Finance.Api.Application;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Ai;
using Finance.Api.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 009 checkpoint 2: <c>Ai:FakeProvider</c>, the switch the E2E run will use (spec tests
/// 28-29), built like 006's <c>MarketData:FakeProviders</c>: on in Development only, and
/// the boot is refused anywhere else. Its usage rows say <c>fake</c>.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AiFakeProviderTests(PostgresFixture postgres)
{
    private static readonly Dictionary<string, string?> FakeOn = new() { ["Ai:FakeProvider"] = "true" };

    [Fact]
    public async Task With_the_switch_calls_go_to_the_fake_and_are_recorded_as_fake()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString, settings: FakeOn);
        var user = (await factory.SignInNewUserAsync("ai-fake", ct)).Id;
        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, null))
        {
            await context.Users.Where(row => row.Id == user).ExecuteUpdateAsync(set => set.SetProperty(row => row.AiEnabled, true), ct);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            Assert.IsType<FakeAiProvider>(scope.ServiceProvider.GetRequiredService<IAiProvider>());
            scope.ServiceProvider.GetRequiredService<ActingUser>().ActAs(user);
            var completion = await scope.ServiceProvider.GetRequiredService<AiGateway>()
                .CompleteAsync(AiPurpose.Analysis, "sistema", "dados", 1000, ct);
            Assert.StartsWith("## Resumo", completion.Text);
        }

        await using var asUser = TransactionsFixtures.ContextFor(postgres.ConnectionString, user);
        var row = Assert.Single(await asUser.AiUsage.ToListAsync(ct));
        Assert.Equal(("fake", true), (row.Provider, row.Succeeded));
        Assert.True(row.InputTokens > 0 && row.CostBrl > 0m);
    }

    [Fact]
    public async Task Without_the_switch_the_fake_is_not_what_answers()
    {
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();

        // CP2 has no real adapter yet, so resolving fails; CP3 asserts the configured adapter here.
        var error = Assert.ThrowsAny<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAiProvider>());

        Assert.Contains("Ai:Provider", error.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task The_switch_fails_the_boot_outside_development(string environment)
    {
        await using var factory = new IdentityApiFactory(postgres.ConnectionString, environment: environment, settings: FakeOn);

        var error = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Ai:FakeProvider", AiBootTests.Flatten(error));
    }
}
