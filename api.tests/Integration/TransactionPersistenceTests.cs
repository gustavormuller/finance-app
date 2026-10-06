using Finance.Api.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec integration tests 4 and 5, at the layer that decides them: what PostgreSQL
/// stores and hands back.
/// </summary>
/// <remarks>
/// The HTTP half — that the same values survive JSON serialisation in both
/// directions — is in TransactionEndpointTests. If a float were anywhere in this
/// half, no amount of care in the endpoint could repair it.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TransactionPersistenceTests(PostgresFixture postgres)
{
    /// <summary>
    /// Spec integration test 4. The two ends of the range that matter: a value wide
    /// enough to exhaust a float64's exact integer range, and one small enough that a
    /// rounding error would swallow it whole.
    /// </summary>
    [Theory]
    [InlineData("1234567890.12")]
    [InlineData("-0.01")]
    [InlineData("0.30")]
    [InlineData("-9999999999999999.99")]
    public async Task Amount_round_trips_byte_identical(string literal)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var user = await factory.SignInNewUserAsync("amount", cancellationToken);
        Guid transactionId;

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = TransactionsFixtures.AnAccount(user.Id);
            var category = TransactionsFixtures.ACategory(user.Id);
            context.Accounts.Add(account);
            context.Categories.Add(category);
            await context.SaveChangesAsync(cancellationToken);

            var transaction = TransactionsFixtures.ATransaction(
                user.Id, account.Id, category.Id, expected);

            context.Transactions.Add(transaction);
            await context.SaveChangesAsync(cancellationToken);
            transactionId = transaction.Id;
        }

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var stored = await context.Transactions.SingleAsync(
                entity => entity.Id == transactionId, cancellationToken);

            Assert.Equal(expected, stored.Money.Amount);
            Assert.Equal(Account.DefaultCurrency, stored.Money.Currency);
        }

        // Read as text straight out of PostgreSQL, bypassing the driver's own decimal
        // handling: this is the value that is actually on disk.
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """SELECT "Amount"::text, pg_typeof("Amount")::text FROM "Transactions" WHERE "Id" = @id;""",
            connection);

        command.Parameters.AddWithValue("id", transactionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        Assert.Equal(literal, reader.GetString(0));

        // The definition of done: no float, double or real anywhere in this path.
        Assert.Equal("numeric", reader.GetString(1));
    }

    /// <summary>
    /// Spec integration test 5. The column is <c>date</c>, so there is no instant to
    /// shift and no offset to apply — asserted with the two sessions fourteen and
    /// eleven hours either side of UTC, which is the widest a real server gets.
    /// </summary>
    [Fact]
    public async Task Date_round_trips_as_the_same_calendar_day_under_any_server_timezone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var date = new DateOnly(2026, 1, 1);

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var user = await factory.SignInNewUserAsync("date", cancellationToken);
        var farEast = postgres.ConnectionString.InTimeZone("Pacific/Kiritimati");
        var farWest = postgres.ConnectionString.InTimeZone("Pacific/Niue");
        Guid transactionId;

        // Written from a session fourteen hours ahead of UTC, on the first day of the
        // year: any conversion at all would land it in the previous one.
        await using (var context = TransactionsFixtures.ContextFor(farEast, user.Id))
        {
            var account = TransactionsFixtures.AnAccount(user.Id);
            var category = TransactionsFixtures.ACategory(user.Id);
            context.Accounts.Add(account);
            context.Categories.Add(category);
            await context.SaveChangesAsync(cancellationToken);

            var transaction = TransactionsFixtures.ATransaction(
                user.Id, account.Id, category.Id, date: date);

            context.Transactions.Add(transaction);
            await context.SaveChangesAsync(cancellationToken);
            transactionId = transaction.Id;
        }

        await using (var context = TransactionsFixtures.ContextFor(farWest, user.Id))
        {
            var stored = await context.Transactions.SingleAsync(
                entity => entity.Id == transactionId, cancellationToken);

            Assert.Equal(date, stored.Date);
        }

        await using var connection = new NpgsqlConnection(farWest);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT "Date"::text,
                   (SELECT data_type FROM information_schema.columns
                     WHERE table_name = 'Transactions' AND column_name = 'Date')
              FROM "Transactions" WHERE "Id" = @id;
            """,
            connection);

        command.Parameters.AddWithValue("id", transactionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        Assert.Equal("2026-01-01", reader.GetString(0));

        // The structural guarantee behind the assertion above: there is no instant
        // stored here for a timezone to move.
        Assert.Equal("date", reader.GetString(1));
    }
}
