using Finance.Api.Application.Ai;
using Finance.Api.Domain.Ai;

namespace Finance.Api.Tests.Integration;

/// <summary>The gates before a call: the budget (spec tests 1 and 2 at the database) and <c>ai_enabled</c>.</summary>
public sealed partial class AiGatewayTests
{
    /// <summary>
    /// Spec tests 1 and 2, at the database: the sum is the user's own month, failed calls
    /// included; at the budget nothing is sent and nothing is recorded.
    /// </summary>
    [Fact]
    public async Task Spent_14_99_is_allowed_and_spent_15_00_is_refused_before_the_provider_is_called()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => new AiCompletion("ok", 10, 10));
        await using var host = await HostAsync(provider, Clock(), ct);
        var under = await EnabledUserAsync(host, ct);
        var at = await EnabledUserAsync(host, ct);
        await SeedUsageAsync(host.Connection, under, ("2026-09", 10.00m, true), ("2026-09", 4.99m, false), ("2026-08", 100m, true));
        await SeedUsageAsync(host.Connection, at, ("2026-09", 14.00m, true), ("2026-09", 1.00m, false));

        await CallAsync(host, under, AiPurpose.Categorisation, ct);
        var refused = await Assert.ThrowsAsync<AiBudgetExceededException>(() => CallAsync(host, at, AiPurpose.Categorisation, ct));

        Assert.Single(provider.Requests);
        Assert.Equal((15.00m, 15.00m), (refused.SpentBrl, refused.BudgetBrl));
        Assert.Equal(4, (await UsageAsync(host.Connection, under, ct)).Count);
        Assert.Equal(2, (await UsageAsync(host.Connection, at, ct)).Count);
    }

    /// <summary>ADR-010: with <c>ai_enabled</c> off nothing is sent and nothing is recorded (spec test 15's core).</summary>
    [Fact]
    public async Task A_user_without_ai_enabled_is_refused_before_the_provider_is_called()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => new AiCompletion("ok", 10, 10));
        await using var host = await HostAsync(provider, Clock(), ct);
        var user = (await host.Users.SignInNewUserAsync("ai-off", ct)).Id;

        await Assert.ThrowsAsync<AiDisabledException>(() => CallAsync(host, user, AiPurpose.Analysis, ct));

        Assert.Empty(provider.Requests);
        Assert.Empty(await UsageAsync(host.Connection, user, ct));
    }
}
