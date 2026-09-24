using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 010 decision 2: in production the schema moves only in <c>deploy.sh</c>'s explicit
/// step, never as a side effect of a container starting. That step is the published
/// application run with the <c>migrate</c> argument, because the runtime image has no
/// SDK and no <c>dotnet ef</c>.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MigrationStepTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Production_boots_without_touching_the_schema()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await postgres.CreateEmptyDatabaseAsync(ct);

        await using var factory = new IdentityApiFactory(database, environment: "Production");
        _ = factory.Services;

        Assert.Equal(0, await AppliedMigrationsAsync(database, ct));
    }

    [Fact]
    public async Task The_migrate_argument_applies_every_migration_and_exits()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await postgres.CreateEmptyDatabaseAsync(ct);

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("migrate");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__Default"] = database;
        start.Environment["Google__ClientId"] = "test-client-id";
        start.Environment["Google__ClientSecret"] = "test-client-secret";
        start.Environment["App__Origin"] = "https://finance.example";
        start.Environment["DataProtection__KeysPath"] =
            Path.Combine(Path.GetTempPath(), "finance-app-tests", Guid.NewGuid().ToString("N"));

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("`migrate` did not exit: it went on to serve requests.\n" + await output);
        }

        Assert.True(process.ExitCode == 0, $"exit {process.ExitCode}\n{await output}\n{await errors}");
        await using var context = TransactionsFixtures.ContextFor(database, null);
        Assert.Equal(context.Database.GetMigrations().Count(), await AppliedMigrationsAsync(database, ct));
    }

    private static async Task<long> AppliedMigrationsAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var exists = new NpgsqlCommand(
            "SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL", connection);
        if (!(bool)(await exists.ExecuteScalarAsync(ct))!)
        {
            return 0;
        }

        await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM \"__EFMigrationsHistory\"", connection);
        return Convert.ToInt64(await count.ExecuteScalarAsync(ct));
    }
}
