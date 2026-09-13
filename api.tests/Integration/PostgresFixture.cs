using Testcontainers.PostgreSql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Starts one real PostgreSQL container per test run. Shared by every integration
/// test through <see cref="PostgresCollection"/> so the image is pulled once.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("financas")
        .WithUsername("dev")
        .WithPassword("dev")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
