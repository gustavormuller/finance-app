using System.Net;
using System.Net.Http.Json;
using Finance.Api.Domain.Transactions;
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
}
