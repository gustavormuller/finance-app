using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Finance.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec integration tests 36-39 and 46 at the storage layer, plus the constraints
/// the spec's data model asks for: the partial unique index on ExternalId, the
/// cascade from a batch to its staged rows, and the RESTRICT from a batch to its
/// committed transactions.
/// </summary>
/// <remarks>
/// The HTTP versions are in ImportSecurityTests. These exist so isolation is the
/// storage layer's guarantee and not each endpoint's courtesy.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ImportPersistenceTests(PostgresFixture postgres)
{
    /// <summary>PostgreSQL's SQLSTATE for foreign_key_violation.</summary>
    private const string ForeignKeyViolation = "23503";

    /// <summary>Spec integration tests 36 and 37, at the context.</summary>
    [Fact]
    public async Task A_users_batch_and_staged_rows_are_invisible_to_another_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var userA = await factory.SignInNewUserAsync("import-iso-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("import-iso-b", cancellationToken);
        var (accountId, _) = await ImportFixtures.SeedAccountAndCategoryAsync(postgres.ConnectionString, userA.Id, cancellationToken);

        Guid batchId;

        await using (var asA = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            var batch = ImportFixtures.ABatch(userA.Id, accountId);
            asA.ImportBatches.Add(batch);
            await asA.SaveChangesAsync(cancellationToken);

            asA.StagedTransactions.Add(ImportFixtures.AStagedRow(userA.Id, batch.Id, 1));
            asA.StagedTransactions.Add(ImportFixtures.AStagedRow(userA.Id, batch.Id, 2));
            await asA.SaveChangesAsync(cancellationToken);
            batchId = batch.Id;
        }

        await using (var asB = TransactionsFixtures.ContextFor(postgres.ConnectionString, userB.Id))
        {
            Assert.Null(await asB.ImportBatches.SingleOrDefaultAsync(batch => batch.Id == batchId, cancellationToken));
            Assert.Equal(0, await asB.StagedTransactions.CountAsync(row => row.ImportBatchId == batchId, cancellationToken));

            // The rows exist; the filter is what hides them.
            Assert.Equal(2, await asB.StagedTransactions.IgnoreQueryFilters()
                .CountAsync(row => row.ImportBatchId == batchId, cancellationToken));
        }

        await using (var asA = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            Assert.NotNull(await asA.ImportBatches.SingleOrDefaultAsync(batch => batch.Id == batchId, cancellationToken));
            Assert.Equal(2, await asA.StagedTransactions.CountAsync(row => row.ImportBatchId == batchId, cancellationToken));
        }
    }

    /// <summary>
    /// Spec integration test 38, at the context: even a bulk update that names A's
    /// batch id goes through the filter and touches nothing.
    /// </summary>
    [Fact]
    public async Task A_bulk_update_through_another_users_context_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var userA = await factory.SignInNewUserAsync("import-bulk-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("import-bulk-b", cancellationToken);
        var (accountId, _) = await ImportFixtures.SeedAccountAndCategoryAsync(postgres.ConnectionString, userA.Id, cancellationToken);

        Guid batchId;

        await using (var asA = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            var batch = ImportFixtures.ABatch(userA.Id, accountId);
            asA.ImportBatches.Add(batch);
            await asA.SaveChangesAsync(cancellationToken);
            batchId = batch.Id;
        }

        await using (var asB = TransactionsFixtures.ContextFor(postgres.ConnectionString, userB.Id))
        {
            var affected = await asB.ImportBatches
                .Where(batch => batch.Id == batchId)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(batch => batch.Status, ImportBatchStatus.Committed),
                    cancellationToken);

            Assert.Equal(0, affected);
        }

        await using (var asA = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            var batch = await asA.ImportBatches.SingleAsync(entity => entity.Id == batchId, cancellationToken);

            Assert.Equal(ImportBatchStatus.Staged, batch.Status);
        }
    }

    /// <summary>Spec integration test 39, at the context.</summary>
    [Fact]
    public async Task Csv_templates_are_isolated_per_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var userA = await factory.SignInNewUserAsync("template-iso-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("template-iso-b", cancellationToken);

        await using (var asA = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            asA.CsvTemplates.Add(ImportFixtures.ATemplate(userA.Id));
            await asA.SaveChangesAsync(cancellationToken);
        }

        await using (var asB = TransactionsFixtures.ContextFor(postgres.ConnectionString, userB.Id))
        {
            Assert.Empty(await asB.CsvTemplates.ToListAsync(cancellationToken));

            // The same name under another user is not a collision: the unique index
            // is scoped to the user.
            asB.CsvTemplates.Add(ImportFixtures.ATemplate(userB.Id));
            await asB.SaveChangesAsync(cancellationToken);
        }

        await using (var asA = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            Assert.Single(await asA.CsvTemplates.ToListAsync(cancellationToken));
        }
    }

    /// <summary>
    /// Spec integration test 46. The partial unique index, not a pre-check, is what
    /// keeps a user to one open batch: a second Staged one is refused by the
    /// database, a Committed one beside a Staged one is fine, and another user's
    /// Staged batch is nobody's business.
    /// </summary>
    [Fact]
    public async Task The_database_allows_one_staged_batch_per_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var userA = await factory.SignInNewUserAsync("one-open-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("one-open-b", cancellationToken);
        var (accountA, _) = await ImportFixtures.SeedAccountAndCategoryAsync(postgres.ConnectionString, userA.Id, cancellationToken);
        var (accountB, _) = await ImportFixtures.SeedAccountAndCategoryAsync(postgres.ConnectionString, userB.Id, cancellationToken);

        await using var asA = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id);

        asA.ImportBatches.Add(ImportFixtures.ABatch(userA.Id, accountA, ImportBatchStatus.Committed, "antigo.ofx"));
        var open = ImportFixtures.ABatch(userA.Id, accountA);
        asA.ImportBatches.Add(open);
        await asA.SaveChangesAsync(cancellationToken);

        await using (var asB = TransactionsFixtures.ContextFor(postgres.ConnectionString, userB.Id))
        {
            asB.ImportBatches.Add(ImportFixtures.ABatch(userB.Id, accountB));
            await asB.SaveChangesAsync(cancellationToken);
        }

        await using (var second = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            second.ImportBatches.Add(ImportFixtures.ABatch(userA.Id, accountA, fileName: "segundo.ofx"));

            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync(cancellationToken));

            Assert.True(failure.IsDuplicate());
        }

        // Once the open one is committed, the next upload can be staged.
        open.Status = ImportBatchStatus.Committed;
        open.CommittedAt = DateTimeOffset.UtcNow;
        asA.ImportBatches.Add(ImportFixtures.ABatch(userA.Id, accountA, fileName: "terceiro.ofx"));
        await asA.SaveChangesAsync(cancellationToken);

        Assert.Equal(3, await asA.ImportBatches.CountAsync(cancellationToken));
    }

    /// <summary>
    /// The other partial index: an ExternalId is unique per account where present.
    /// Manual rows and CSV imports have none and are not constrained at all.
    /// </summary>
    [Fact]
    public async Task External_ids_are_unique_per_account_only_where_present()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var user = await factory.SignInNewUserAsync("external-id", cancellationToken);
        var (accountId, categoryId) = await ImportFixtures.SeedAccountAndCategoryAsync(postgres.ConnectionString, user.Id, cancellationToken);
        var (otherAccountId, _) = await ImportFixtures.SeedAccountAndCategoryAsync(postgres.ConnectionString, user.Id, cancellationToken);

        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id);

        context.Transactions.Add(WithExternalId(TransactionsFixtures.ATransaction(user.Id, accountId, categoryId), "FIT-1"));
        context.Transactions.Add(WithExternalId(TransactionsFixtures.ATransaction(user.Id, otherAccountId, categoryId), "FIT-1"));
        context.Transactions.Add(WithExternalId(TransactionsFixtures.ATransaction(user.Id, accountId, categoryId), null));
        context.Transactions.Add(WithExternalId(TransactionsFixtures.ATransaction(user.Id, accountId, categoryId), null));
        await context.SaveChangesAsync(cancellationToken);

        await using var again = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id);

        again.Transactions.Add(WithExternalId(TransactionsFixtures.ATransaction(user.Id, accountId, categoryId), "FIT-1"));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync(cancellationToken));

        Assert.True(failure.IsDuplicate());
    }

    [Fact]
    public async Task Deleting_a_batch_takes_its_staged_rows_but_not_its_committed_transactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var user = await factory.SignInNewUserAsync("batch-delete", cancellationToken);
        var (accountId, categoryId) = await ImportFixtures.SeedAccountAndCategoryAsync(postgres.ConnectionString, user.Id, cancellationToken);

        Guid stagedId;
        Guid committedId;

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var staged = ImportFixtures.ABatch(user.Id, accountId);
            var committed = ImportFixtures.ABatch(user.Id, accountId, ImportBatchStatus.Committed, "antigo.ofx");
            context.ImportBatches.AddRange(staged, committed);
            await context.SaveChangesAsync(cancellationToken);

            context.StagedTransactions.Add(ImportFixtures.AStagedRow(user.Id, staged.Id));

            var transaction = TransactionsFixtures.ATransaction(user.Id, accountId, categoryId);
            transaction.ImportBatchId = committed.Id;
            context.Transactions.Add(transaction);
            await context.SaveChangesAsync(cancellationToken);

            stagedId = staged.Id;
            committedId = committed.Id;
        }

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            context.ImportBatches.Remove(await context.ImportBatches.SingleAsync(batch => batch.Id == stagedId, cancellationToken));
            await context.SaveChangesAsync(cancellationToken);

            Assert.Equal(0, await context.StagedTransactions.CountAsync(row => row.ImportBatchId == stagedId, cancellationToken));
        }

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            context.ImportBatches.Remove(await context.ImportBatches.SingleAsync(batch => batch.Id == committedId, cancellationToken));

            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));

            Assert.Equal(ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }
    }

    /// <summary>Principle 4, at the staging table as well.</summary>
    [Fact]
    public async Task Staged_amounts_are_numeric()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT data_type, numeric_precision, numeric_scale FROM information_schema.columns "
            + "WHERE table_name = 'StagedTransactions' AND column_name = 'Amount'",
            connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));
        Assert.Equal("numeric", reader.GetString(0));
        Assert.Equal(18, reader.GetInt32(1));
        Assert.Equal(2, reader.GetInt32(2));
    }

    private static Transaction WithExternalId(Transaction transaction, string? externalId)
    {
        transaction.ExternalId = externalId;
        transaction.NormalizedDescription = "SUPERMARKET";

        return transaction;
    }
}
