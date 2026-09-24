using System.Net;
using System.Net.Http.Json;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 009 checkpoint 4b: <c>PATCH /api/auth/me { aiEnabled }</c>, the user's own switch
/// (ADR-010), and the preview's rows saying which rung chose their category, a pick by
/// hand included, for the "came from AI" marker.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AiToggleEndpointTests(PostgresFixture postgres)
{
    private sealed record Me(Guid Id, string Email, string? DisplayName, bool AiEnabled);

    [Fact]
    public async Task A_user_turns_their_own_ai_on_and_off()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("ai-toggle", ct);
        var other = await factory.SignInNewUserAsync("ai-toggle-other", ct);

        using var on = await user.Client.SendAsync(ImportFixtures.Patch("/api/auth/me", new { aiEnabled = true }), ct);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        Assert.True((await on.Content.ReadFromJsonAsync<Me>(ct))!.AiEnabled);
        Assert.True((await user.Client.GetFromJsonAsync<Me>("/api/auth/me", ct))!.AiEnabled);
        Assert.False((await other.Client.GetFromJsonAsync<Me>("/api/auth/me", ct))!.AiEnabled);

        using var off = await user.Client.SendAsync(ImportFixtures.Patch("/api/auth/me", new { aiEnabled = false }), ct);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False((await user.Client.GetFromJsonAsync<Me>("/api/auth/me", ct))!.AiEnabled);
    }

    [Fact]
    public async Task A_patch_without_ai_enabled_is_a_400_naming_the_field()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("ai-toggle-empty", ct);

        using var response = await user.Client.SendAsync(ImportFixtures.Patch("/api/auth/me", new { }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("aiEnabled", await TransactionsFixtures.ProblemFieldsAsync(response, ct));
    }

    [Fact]
    public async Task Signed_out_the_patch_is_a_401()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(ImportFixtures.Patch("/api/auth/me", new { aiEnabled = true }), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_preview_says_which_rung_chose_a_row_and_a_category_picked_by_hand_is_the_users()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("ai-source-user", ct);
        var categories = await user.CategoriesAsync(ct);
        var accountId = await user.CreateAccountAsync("Nubank", ct);
        var batch = await user.UploadOfxAsync(
            accountId, ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260915", "-19.90", "S-1", "PADARIA REAL")]), ct);
        var row = Assert.Single((await user.GetBatchAsync(batch.BatchId, ct)).Rows.Items);
        Assert.Equal("Default", row.CategorySource);

        using var patch = await user.Client.SendAsync(
            ImportFixtures.Patch($"/api/imports/{batch.BatchId}/rows/{row.Id}", new { categoryId = categories["Alimentação"].Id }), ct);

        Assert.Equal("User", (await patch.Content.ReadFromJsonAsync<ImportFixtures.RowItem>(ct))!.CategorySource);
        Assert.Equal("User", Assert.Single((await user.GetBatchAsync(batch.BatchId, ct)).Rows.Items).CategorySource);
    }
}
