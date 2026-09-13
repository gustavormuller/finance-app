using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Drives the real request pipeline with a connection string of our choosing, so the
/// same endpoint can be exercised against a live container and against a dead port.
/// <paramref name="migrateOnStartup"/> exists for the unreachable-database case: the
/// application has to boot far enough to answer the request and report the failure,
/// which it cannot do if startup itself tries to migrate.
/// </summary>
internal sealed class HealthApiFactory(string connectionString, bool migrateOnStartup = true)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Database:MigrateOnStartup"] = migrateOnStartup ? "true" : "false",
            }));
    }
}

/// <summary>
/// Readiness endpoint against a real PostgreSQL. The point of these tests is the
/// round trip: an endpoint that answers "ok" from configuration alone must fail
/// <see cref="Reports_unreachable_when_the_database_is_down"/>.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class HealthEndpointTests(PostgresFixture postgres)
{
    private sealed record HealthResponse(string Status, string Database);

    [Fact]
    public async Task Reports_ok_when_the_database_answers()
    {
        await using var factory = new HealthApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.Equal("ok", body.Database);
    }

    [Fact]
    public async Task Reports_unreachable_when_the_database_is_down()
    {
        // Port 1 is closed, so the connection is refused immediately: deterministic
        // and fast. Stopping the shared container would be neither.
        await using var factory = new HealthApiFactory(
            "Host=127.0.0.1;Port=1;Database=financas;Username=dev;Password=dev;Timeout=2",
            migrateOnStartup: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal("degraded", body.Status);
        Assert.Equal("unreachable", body.Database);
    }

    [Fact]
    public async Task Initial_migration_is_recorded_in_the_history_table()
    {
        await using var factory = new HealthApiFactory(postgres.ConnectionString);

        // Creating the client is what boots the host, and booting is what applies
        // the migration. Nothing is sent over this client.
        using var client = factory.CreateClient();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            """SELECT count(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%_InitialCreate';""",
            connection);

        var applied = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1L, applied);
    }
}
