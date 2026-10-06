using Npgsql;
using Testcontainers.PostgreSql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Starts one real PostgreSQL container per test run. Shared by every integration
/// test through <see cref="PostgresCollection"/> so the image is pulled once.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Every test database keeps an Npgsql pool whose idle connections outlive the test by
    // up to five minutes, so the run holds far more connections than any one test uses.
    // PostgreSQL's default of 100 ran out ("53300: too many clients") once the suite grew.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("financas")
        .WithUsername("dev")
        .WithPassword("dev")
        .WithCommand("-c", "max_connections=300")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    /// <summary>
    /// Creates an empty database on the same container and returns a connection
    /// string for it. The query-filter test builds its schema with
    /// <c>EnsureCreated</c>, which does nothing on a database that already has
    /// tables, so it needs one of its own.
    /// </summary>
    public async Task<string> CreateEmptyDatabaseAsync(CancellationToken cancellationToken)
    {
        var name = "filter_" + Guid.NewGuid().ToString("N");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // CREATE DATABASE takes no parameters. The name is a generated identifier,
        // never input.
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\";", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
