using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec test 11. What the AddIdentity migration actually produced, read from the
/// catalogue rather than from the model — the model is the thing under test.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class IdentitySchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ai_enabled_exists_and_defaults_to_false()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Creating the client boots the host, and booting applies the migrations.
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var boot = await client.GetAsync("/health", cancellationToken);
        boot.EnsureSuccessStatusCode();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT data_type, column_default, is_nullable "
            + "FROM information_schema.columns "
            + "WHERE table_name = 'AspNetUsers' AND column_name = 'AiEnabled';",
            connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken), "AspNetUsers.AiEnabled does not exist");
        Assert.Equal("boolean", reader.GetString(0));

        // ADR-010: a row written by anything that does not know about this column
        // still has AI switched off.
        Assert.Equal("false", reader.GetString(1));
        Assert.Equal("NO", reader.GetString(2));
    }

    [Fact]
    public async Task No_role_tables_were_created()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var boot = await client.GetAsync("/health", cancellationToken);
        boot.EnsureSuccessStatusCode();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // IdentityUserContext instead of IdentityDbContext: four tables, not seven.
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables "
            + "WHERE table_schema = 'public' "
            + "AND table_name IN ('AspNetRoles', 'AspNetRoleClaims', 'AspNetUserRoles');",
            connection);

        Assert.Equal(0L, await command.ExecuteScalarAsync(cancellationToken));

        await using var expected = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables "
            + "WHERE table_schema = 'public' "
            + "AND table_name IN "
            + "('AspNetUsers', 'AspNetUserClaims', 'AspNetUserLogins', 'AspNetUserTokens');",
            connection);

        Assert.Equal(4L, await expected.ExecuteScalarAsync(cancellationToken));
    }
}
