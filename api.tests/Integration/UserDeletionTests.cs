using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Finance.Api.Domain;
using Finance.Api.Domain.Ai;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.MarketData;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Finance.Api.Tests.Integration.TransactionsFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 023's <c>DELETE /api/auth/me</c>: integration tests 1-3. The third one reads the user-owned
/// types from the EF model rather than from a list, so a table added later fails it until
/// this test seeds a row of it and the deletion removes that row.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class UserDeletionTests(PostgresFixture postgres)
{
    private static readonly MethodInfo CountOwnedMethod =
        typeof(UserDeletionTests).GetMethod(nameof(CountOwnedAsync), BindingFlags.Static | BindingFlags.NonPublic)!;

    private sealed record MeResponse(Guid Id);

    /// <summary>Spec test 1.</summary>
    [Fact]
    public async Task Deleting_needs_a_session_and_the_app_origin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);

        using var anonymous = factory.CreateApiClient();
        using var unauthenticated = await anonymous.SendAsync(Delete("/api/auth/me"), ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var user = await factory.SignInNewUserAsync("delete-origin", ct);
        using var foreign = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/me");
        foreign.Headers.Add("Origin", "https://evil.example");
        using var refused = await user.Client.SendAsync(foreign, ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var me = await user.Client.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        await using var context = ContextFor(postgres.ConnectionString, user.Id);
        Assert.Equal(DefaultCategories.All.Count, await context.Categories.CountAsync(ct));
    }

    /// <summary>Spec test 2.</summary>
    [Fact]
    public async Task Deleting_signs_out_the_old_cookie_reaches_nothing_and_the_same_address_starts_afresh()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"delete-session-{Guid.NewGuid():N}@example.com";
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var login = await client.SendAsync(AuthRequests.DevLogin(email, null), ct);
        var oldCookie = IdentityApiFactory.ReadSessionCookie(login);
        var oldId = (await client.GetFromJsonAsync<MeResponse>("/api/auth/me", ct))!.Id;

        using var deleted = await client.SendAsync(Delete("/api/auth/me"), ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Contains(
            deleted.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith(IdentityApiFactory.SessionCookieName + "=;", StringComparison.Ordinal)
                && header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

        using var after = await client.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        using var replayClient = factory.CreateApiClient();
        using var replay = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        replay.Headers.Add("Cookie", oldCookie);
        using var replayed = await replayClient.SendAsync(replay, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);

        using var again = await factory.CreateSignedInClientAsync(email, null, ct);
        var newId = (await again.GetFromJsonAsync<MeResponse>("/api/auth/me", ct))!.Id;
        Assert.NotEqual(oldId, newId);
        await using var context = ContextFor(postgres.ConnectionString, newId);
        Assert.Equal(DefaultCategories.All.Count, await context.Categories.CountAsync(ct));
        Assert.Empty(await context.Accounts.ToListAsync(ct));
    }

    /// <summary>Spec test 3: every user-owned table in the model, the Identity rows, and the shared data.</summary>
    [Fact]
    public async Task Every_row_the_user_owned_goes_and_every_row_of_anyone_else_stays()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = postgres.ConnectionString;
        await using var factory = new IdentityApiFactory(connection);
        var leaving = await factory.SignInNewUserAsync("delete-leaving", ct);
        var staying = await factory.SignInNewUserAsync("delete-staying", ct);
        var shared = await SeedSharedAsync(connection, ct);
        var seeded = await SeedEverythingAsync(connection, leaving.Id, shared.MarketAssetId, ct);
        await SeedEverythingAsync(connection, staying.Id, shared.MarketAssetId, ct);

        var owned = UserOwnedTypes(connection);
        Assert.All(owned, type => Assert.True(
            seeded.Contains(type),
            $"{type.Name} is user-owned, but this test seeds no row of it. Seed one, and make DELETE /api/auth/me remove it."));
        var stayingBefore = await CountsAsync(connection, owned, staying.Id, ct);
        Assert.All(await CountsAsync(connection, owned, leaving.Id, ct), count => Assert.True(count.Value > 0, count.Key.Name));

        using var deleted = await leaving.Client.SendAsync(Delete("/api/auth/me"), ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.All(await CountsAsync(connection, owned, leaving.Id, ct), count => Assert.True(count.Value == 0, $"{count.Key.Name}: {count.Value} left"));
        Assert.Equal(stayingBefore, await CountsAsync(connection, owned, staying.Id, ct));
        Assert.Equal((0, 0, 0, 0), await IdentityRowsAsync(connection, leaving.Id, ct));
        Assert.Equal((1, 1, 1, 1), await IdentityRowsAsync(connection, staying.Id, ct));

        await using var all = ContextFor(connection, null);
        Assert.True(await all.MarketAssets.AnyAsync(asset => asset.Id == shared.MarketAssetId, ct));
        Assert.Equal(1, await all.Prices.CountAsync(price => price.MarketAssetId == shared.MarketAssetId, ct));
        Assert.True(await all.Benchmarks.AnyAsync(benchmark => benchmark.Code == shared.BenchmarkCode, ct));
        Assert.True(await all.SyncRuns.AnyAsync(run => run.Id == shared.SyncRunId, ct));
    }

    private static IReadOnlyList<Type> UserOwnedTypes(string connection)
    {
        using var context = ContextFor(connection, null);
        return [.. context.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(type => typeof(IUserOwned).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)];
    }

    private static async Task<Dictionary<Type, int>> CountsAsync(string connection, IReadOnlyList<Type> types, Guid userId, CancellationToken ct)
    {
        await using var context = ContextFor(connection, null);
        var counts = new Dictionary<Type, int>();
        foreach (var type in types)
        {
            counts[type] = await (Task<int>)CountOwnedMethod.MakeGenericMethod(type).Invoke(null, [context, userId, ct])!;
        }

        return counts;
    }

    private static Task<int> CountOwnedAsync<TEntity>(AppDbContext context, Guid userId, CancellationToken ct)
        where TEntity : class, IUserOwned =>
        context.Set<TEntity>().IgnoreQueryFilters().CountAsync(entity => entity.UserId == userId, ct);

    private static async Task<(int Users, int Logins, int Claims, int Tokens)> IdentityRowsAsync(string connection, Guid userId, CancellationToken ct)
    {
        await using var context = ContextFor(connection, null);
        return (
            await context.Users.CountAsync(user => user.Id == userId, ct),
            await context.UserLogins.CountAsync(login => login.UserId == userId, ct),
            await context.UserClaims.CountAsync(claim => claim.UserId == userId, ct),
            await context.UserTokens.CountAsync(token => token.UserId == userId, ct));
    }

    private static async Task<(Guid MarketAssetId, string BenchmarkCode, Guid SyncRunId)> SeedSharedAsync(string connection, CancellationToken ct)
    {
        var symbol = "DEL" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var marketAsset = new MarketAsset
        {
            Id = Guid.NewGuid(),
            Ticker = symbol,
            Name = symbol + " name",
            Class = MarketAssetClass.StockBr,
            Currency = "BRL",
            Provider = ProviderKind.Brapi,
            ProviderSymbol = symbol,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            Trigger = SyncTrigger.Manual,
            Status = SyncRunStatus.Succeeded,
            Summary = "{}",
        };

        await using var context = ContextFor(connection, null);
        context.AddRange(
            marketAsset,
            new Price { MarketAssetId = marketAsset.Id, Date = new DateOnly(2026, 9, 1), Close = 10m },
            new Benchmark { Code = symbol, Date = new DateOnly(2026, 9, 1), Value = 1.5m },
            run);
        await context.SaveChangesAsync(ct);
        return (marketAsset.Id, symbol, run.Id);
    }

    /// <summary>
    /// One row of every user-owned type, wired through each RESTRICT key the deletion has to
    /// respect, plus an Identity claim and token. Returns the types it wrote.
    /// </summary>
    private static async Task<IReadOnlySet<Type>> SeedEverythingAsync(string connection, Guid userId, Guid marketAssetId, CancellationToken ct)
    {
        await using var context = ContextFor(connection, userId);

        var account = AnAccount(userId, "Conta a excluir");
        context.Add(account);
        var parent = ACategory(userId, "Casa de praia");
        context.Add(parent);
        var child = ACategory(userId, "Condominio da praia", parentId: parent.Id);
        context.Add(child);

        var committed = ImportFixtures.ABatch(userId, account.Id, ImportBatchStatus.Committed);
        context.Add(committed);
        var transaction = ATransaction(userId, account.Id, child.Id);
        transaction.ImportBatchId = committed.Id;
        context.Add(transaction);

        var staged = ImportFixtures.ABatch(userId, account.Id);
        context.Add(staged);
        var row = ImportFixtures.AStagedRow(userId, staged.Id);
        row.CategoryId = child.Id;
        context.Add(row);
        context.Add(ImportFixtures.ATemplate(userId));

        var asset = InvestmentsPersistenceTests.AnAsset(userId, marketAssetId);
        context.Add(asset);
        context.Add(InvestmentsPersistenceTests.ABuy(asset));
        context.Add(InvestmentsPersistenceTests.ADailyRow(asset, new DateOnly(2026, 9, 1)));

        context.Add(AiPersistenceTests.AUsage(userId));
        var analysis = AiPersistenceTests.AnAnalysis(userId, "2026-08");
        analysis.Status = AiAnalysisStatus.Running;
        context.Add(analysis);

        var types = context.ChangeTracker.Entries().Select(entry => entry.Metadata.ClrType).ToHashSet();

        context.UserClaims.Add(new IdentityUserClaim<Guid> { UserId = userId, ClaimType = "test", ClaimValue = "023" });
        context.UserTokens.Add(new IdentityUserToken<Guid> { UserId = userId, LoginProvider = "test", Name = "023", Value = "token" });

        await context.SaveChangesAsync(ct);
        return types;
    }
}
