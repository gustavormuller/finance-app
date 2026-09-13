using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Proves the Testcontainers lane end to end: pull the image, start the container,
/// hand back a connection string that actually connects. The EF Core in-memory
/// provider is deliberately not used anywhere in this repository, because it does
/// not enforce the global query filters and constraints that matter here.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PostgresContainerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Container_accepts_connections()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("SELECT 1;", connection);
        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Container_runs_postgres_16()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("SHOW server_version;", connection);
        var version = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(version);
        Assert.StartsWith("16.", version);
    }
}
