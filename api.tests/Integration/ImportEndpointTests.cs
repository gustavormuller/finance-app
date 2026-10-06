using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Finance.Api.Domain.Import;
using Finance.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec integration tests 40-45 (lifecycle), 47-53 (correctness), 54-55 (limits)
/// and 56-57 (suggestion), over HTTP against a real PostgreSQL.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ImportEndpointTests(PostgresFixture postgres)
{
    private static string ThreeRows() => ImportFixtures.Ofx(ImportFixtures.OfxRows(3));

    private async Task<(SignedInUser User, Guid AccountId)> NewUserWithAccountAsync(IdentityApiFactory factory, string prefix, CancellationToken cancellationToken)
    {
        var user = await factory.SignInNewUserAsync(prefix, cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);

        return (user, accountId);
    }

    /// <summary>Spec integration test 40.</summary>
    [Fact]
    public async Task Upload_stages_the_file_and_answers_with_the_counts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "upload", cancellationToken);

        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports", Encoding.UTF8.GetBytes(ThreeRows()), "setembro.ofx", ImportFixtures.OfxFields(accountId)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var staged = (await response.Content.ReadFromJsonAsync<ImportFixtures.UploadResult>(cancellationToken))!;

        Assert.Equal($"/api/imports/{staged.BatchId}", response.Headers.Location?.ToString());
        Assert.Equal((3, 3, 0, 0), (staged.RowCount, staged.Ready, staged.Duplicates, staged.Invalid));

        var detail = await user.GetBatchAsync(staged.BatchId, cancellationToken);
        Assert.Equal("Staged", detail.Batch.Status);
        Assert.Equal("Ofx", detail.Batch.Source);
        Assert.Equal("setembro.ofx", detail.Batch.FileName);
        Assert.Equal("Nubank", detail.Batch.AccountName);
        Assert.Equal(3, detail.Counts.Ready);
        Assert.Equal(3, detail.Counts.Included);
        Assert.Equal([1, 2, 3], detail.Rows.Items.Select(row => row.RowNumber));
        Assert.All(detail.Rows.Items, row => Assert.Equal("Ready", row.Status));
        Assert.All(detail.Rows.Items, row => Assert.True(row.Included));
        Assert.All(detail.Rows.Items, row => Assert.NotNull(row.CategoryId));
        Assert.Equal("PAG*IFOOD 0 — Compra 0", detail.Rows.Items[0].RawDescription);
        Assert.Equal("FIT-0", detail.Rows.Items[0].ExternalId);
    }

    /// <summary>Spec integration tests 41 and 42.</summary>
    [Fact]
    public async Task A_second_upload_while_one_is_open_is_a_409_until_the_first_is_discarded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "open-batch", cancellationToken);

        var first = await user.UploadOfxAsync(accountId, ThreeRows(), cancellationToken);

        using var second = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports", Encoding.UTF8.GetBytes(ThreeRows()), "outro.ofx", ImportFixtures.OfxFields(accountId)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var problem = JsonDocument.Parse(await second.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(first.BatchId, problem.RootElement.GetProperty("openBatchId").GetGuid());

        using var discard = await user.Client.SendAsync(TransactionsFixtures.Delete($"/api/imports/{first.BatchId}"), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, discard.StatusCode);

        using var gone = await user.Client.GetAsync($"/api/imports/{first.BatchId}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        var third = await user.UploadOfxAsync(accountId, ThreeRows(), cancellationToken);
        Assert.NotEqual(first.BatchId, third.BatchId);
    }

    /// <summary>Spec integration tests 43 and 48.</summary>
    [Fact]
    public async Task Commit_writes_the_rows_marks_the_batch_and_clears_the_staging()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "commit", cancellationToken);

        var staged = await user.UploadOfxAsync(accountId, ThreeRows(), cancellationToken);
        var committed = await user.CommitAsync(staged.BatchId, cancellationToken);

        Assert.Equal((3, 0), (committed.Committed, committed.Skipped));

        var detail = await user.GetBatchAsync(staged.BatchId, cancellationToken);
        Assert.Equal("Committed", detail.Batch.Status);
        Assert.Equal(3, detail.Batch.CommittedCount);
        Assert.NotNull(detail.Batch.CommittedAt);
        Assert.Equal(0, detail.Rows.Total);

        var transactions = await user.ListTransactionsAsync(cancellationToken);
        Assert.Equal(3, transactions.Total);
        Assert.Contains(transactions.Items, item => item.Description == "PAG*IFOOD 0 — Compra 0" && item.Amount == -10.00m);

        // Filtered to the batch, which is what the done step links to.
        Assert.Equal(3, (await user.ListTransactionsAsync(cancellationToken, $"importBatchId={staged.BatchId}")).Total);
        Assert.Equal(0, (await user.ListTransactionsAsync(cancellationToken, $"importBatchId={Guid.NewGuid()}")).Total);

        // Test 48, past the API: the three columns the import fills.
        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id);
        var rows = await context.Transactions.OrderBy(transaction => transaction.ExternalId).ToListAsync(cancellationToken);

        Assert.All(rows, row => Assert.Equal(staged.BatchId, row.ImportBatchId));
        Assert.Equal(["FIT-0", "FIT-1", "FIT-2"], rows.Select(row => row.ExternalId));
        Assert.All(rows, row => Assert.Equal("PAG IFOOD — COMPRA", row.NormalizedDescription));

        using var again = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{staged.BatchId}/commit"), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    /// <summary>Spec integration tests 44 and 45.</summary>
    [Fact]
    public async Task Undo_deletes_exactly_the_batchs_transactions_even_after_some_went_by_hand()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "undo", cancellationToken);

        // A manual transaction that must survive.
        var categories = await user.CategoriesAsync(cancellationToken);
        var manualId = await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categories["Alimentação"].Id, description: "Manual"),
            cancellationToken);

        var first = await user.UploadOfxAsync(accountId, ThreeRows(), cancellationToken);
        await user.CommitAsync(first.BatchId, cancellationToken);

        using var undoStaged = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{Guid.NewGuid()}/undo"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, undoStaged.StatusCode);

        // Test 44.
        using var undo = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{first.BatchId}/undo"), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        Assert.Equal(3, (await undo.Content.ReadFromJsonAsync<ImportFixtures.UndoResult>(cancellationToken))!.Deleted);

        var after = await user.ListTransactionsAsync(cancellationToken);
        Assert.Equal(manualId, Assert.Single(after.Items).Id);
        Assert.Empty((await user.Client.GetFromJsonAsync<List<ImportFixtures.BatchItem>>("/api/imports", cancellationToken))!);

        // Test 45: the same, after one row was deleted by hand.
        var second = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(ImportFixtures.OfxRows(3, "AGAIN")), cancellationToken);
        await user.CommitAsync(second.BatchId, cancellationToken);

        var imported = (await user.ListTransactionsAsync(cancellationToken, $"importBatchId={second.BatchId}")).Items;
        using var byHand = await user.Client.SendAsync(TransactionsFixtures.Delete($"/api/transactions/{imported[0].Id}"), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, byHand.StatusCode);

        using var undoAgain = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{second.BatchId}/undo"), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, undoAgain.StatusCode);
        Assert.Equal(2, (await undoAgain.Content.ReadFromJsonAsync<ImportFixtures.UndoResult>(cancellationToken))!.Deleted);

        Assert.Equal(1, (await user.ListTransactionsAsync(cancellationToken)).Total);
    }

    [Fact]
    public async Task A_staged_batch_cannot_be_undone_and_a_committed_one_cannot_be_discarded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "wrong-status", cancellationToken);

        var staged = await user.UploadOfxAsync(accountId, ThreeRows(), cancellationToken);

        using var undo = await user.Client.SendAsync(ImportFixtures.Post($"/api/imports/{staged.BatchId}/undo"), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, undo.StatusCode);

        await user.CommitAsync(staged.BatchId, cancellationToken);

        using var discard = await user.Client.SendAsync(TransactionsFixtures.Delete($"/api/imports/{staged.BatchId}"), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, discard.StatusCode);

        Assert.Equal(3, (await user.ListTransactionsAsync(cancellationToken)).Total);
    }

    /// <summary>Spec integration test 47, the decimal chain from the file to the JSON.</summary>
    [Fact]
    public async Task Committed_amounts_are_byte_identical_to_the_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "amounts", cancellationToken);

        string[] written = ["-1234.56", "0.01", "3000.00", "-0.10", "1234567890.12"];

        var ofx = ImportFixtures.Ofx(written.Select((amount, index) =>
            new ImportFixtures.OfxRow("20260901", amount, $"A-{index}", $"Linha {index}")));

        var staged = await user.UploadOfxAsync(accountId, ofx, cancellationToken);
        Assert.Equal(written.Length, staged.Ready);
        await user.CommitAsync(staged.BatchId, cancellationToken);

        var body = await user.Client.GetStringAsync("/api/transactions?pageSize=200", cancellationToken);

        using var document = JsonDocument.Parse(body);
        var serialised = document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("amount").GetRawText())
            .Order()
            .ToList();

        Assert.Equal(written.Select(amount => decimal.Parse(amount, CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture)).Order(), serialised);
    }

    /// <summary>Spec integration test 49.</summary>
    [Fact]
    public async Task A_long_description_is_kept_whole_in_staging_and_truncated_on_commit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "long-description", cancellationToken);

        var name = string.Join(' ', Enumerable.Repeat("PALAVRA", 50));
        Assert.True(name.Length > 300);

        var staged = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260901", "-1.00", "L-1", name)]),
            cancellationToken);

        var detail = await user.GetBatchAsync(staged.BatchId, cancellationToken);
        Assert.Equal(name, Assert.Single(detail.Rows.Items).RawDescription);

        await user.CommitAsync(staged.BatchId, cancellationToken);

        var description = Assert.Single((await user.ListTransactionsAsync(cancellationToken)).Items).Description;
        Assert.True(description.Length <= 300);
        Assert.StartsWith(description, name);
    }

    /// <summary>Spec integration tests 50 and 51.</summary>
    [Fact]
    public async Task Re_importing_marks_every_seen_row_duplicate_and_commit_writes_only_the_new_ones()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "reimport", cancellationToken);

        var rows = ImportFixtures.OfxRows(10).ToList();
        var first = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(rows), cancellationToken);
        await user.CommitAsync(first.BatchId, cancellationToken);

        // Test 50: the same file again.
        var same = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(rows), cancellationToken);
        Assert.Equal((10, 0, 10, 0), (same.RowCount, same.Ready, same.Duplicates, same.Invalid));

        var committed = await user.CommitAsync(same.BatchId, cancellationToken);
        Assert.Equal((0, 10), (committed.Committed, committed.Skipped));
        Assert.Equal(10, (await user.ListTransactionsAsync(cancellationToken)).Total);

        // Test 51: a later export overlapping by five rows.
        var overlapping = rows.Skip(5).Concat(ImportFixtures.OfxRows(5, "NEW", new DateOnly(2026, 10, 1))).ToList();
        var later = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(overlapping), cancellationToken);
        Assert.Equal((10, 5, 5, 0), (later.RowCount, later.Ready, later.Duplicates, later.Invalid));

        var detail = await user.GetBatchAsync(later.BatchId, cancellationToken, "status=Duplicate");
        Assert.Equal(5, detail.Rows.Total);
        Assert.All(detail.Rows.Items, row => Assert.False(row.Included));

        Assert.Equal(5, (await user.CommitAsync(later.BatchId, cancellationToken)).Committed);
        Assert.Equal(15, (await user.ListTransactionsAsync(cancellationToken)).Total);
    }

    /// <summary>
    /// Heuristic dedupe for a file without ids: the same date, amount and normalized
    /// description is a duplicate; a description that differs only in its digits is
    /// the same key.
    /// </summary>
    [Fact]
    public async Task Without_ids_the_heuristic_key_decides()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "heuristic", cancellationToken);

        var first = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260910", "-42.90", null, "PAG*IFOOD 10/09")]),
            cancellationToken);
        await user.CommitAsync(first.BatchId, cancellationToken);

        var second = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx(
            [
                new ImportFixtures.OfxRow("20260910", "-42.90", null, "PAG*IFOOD 99/99"),
                new ImportFixtures.OfxRow("20260911", "-42.90", null, "PAG*IFOOD 10/09"),
                new ImportFixtures.OfxRow("20260910", "-42.91", null, "PAG*IFOOD 10/09"),
            ]),
            cancellationToken);

        Assert.Equal((1, 2), (second.Duplicates, second.Ready));
    }

    /// <summary>A duplicate the user includes is written, and a colliding id is dropped rather than refused.</summary>
    [Fact]
    public async Task An_included_duplicate_is_committed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "include-duplicate", cancellationToken);

        var rows = ImportFixtures.OfxRows(2).ToList();
        var first = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(rows), cancellationToken);
        await user.CommitAsync(first.BatchId, cancellationToken);

        var again = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(rows), cancellationToken);
        var detail = await user.GetBatchAsync(again.BatchId, cancellationToken);

        using var include = await user.Client.SendAsync(
            ImportFixtures.Patch($"/api/imports/{again.BatchId}/rows/{detail.Rows.Items[0].Id}", new { include = true }),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, include.StatusCode);
        Assert.True((await include.Content.ReadFromJsonAsync<ImportFixtures.RowItem>(cancellationToken))!.Included);

        Assert.Equal(1, (await user.GetBatchAsync(again.BatchId, cancellationToken)).Counts.Included);

        var committed = await user.CommitAsync(again.BatchId, cancellationToken);
        Assert.Equal((1, 1), (committed.Committed, committed.Skipped));

        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id);
        var written = await context.Transactions.Where(transaction => transaction.ImportBatchId == again.BatchId).ToListAsync(cancellationToken);

        Assert.Null(Assert.Single(written).ExternalId);
        Assert.Equal(3, await context.Transactions.CountAsync(cancellationToken));
    }

    /// <summary>Spec integration test 52.</summary>
    [Fact]
    public async Task A_foreign_currency_row_is_invalid_and_the_others_still_import()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "currency", cancellationToken);

        var staged = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx(
            [
                new ImportFixtures.OfxRow("20260901", "-10.00", "C-1", "Em real"),
                new ImportFixtures.OfxRow("20260902", "-20.00", "C-2", "Em dólar", Currency: "USD"),
                new ImportFixtures.OfxRow("20260903", "-30.00", "C-3", "Em real também"),
            ]),
            cancellationToken);

        Assert.Equal((2, 1), (staged.Ready, staged.Invalid));

        var detail = await user.GetBatchAsync(staged.BatchId, cancellationToken, "status=Invalid");
        var invalid = Assert.Single(detail.Rows.Items);
        Assert.Equal("USD", invalid.Currency);
        Assert.Equal([RowIssues.CurrencyMismatch("USD")], invalid.Issues);
        Assert.False(invalid.Included);

        var committed = await user.CommitAsync(staged.BatchId, cancellationToken);
        Assert.Equal((2, 1), (committed.Committed, committed.Skipped));
        Assert.Equal(2, (await user.ListTransactionsAsync(cancellationToken)).Total);
    }

    /// <summary>Spec integration test 53: row-level, never file-level.</summary>
    [Fact]
    public async Task Three_bad_rows_out_of_twenty_leave_seventeen_committed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "three-bad", cancellationToken);

        var rows = ImportFixtures.OfxRows(17).Concat(
        [
            new ImportFixtures.OfxRow("20261399", "-1.00", "BAD-1", "Data impossível"),
            new ImportFixtures.OfxRow("20260901", "0.001", "BAD-2", "Arredonda para zero"),
            new ImportFixtures.OfxRow("20260901", "-1.00", "BAD-3", "12/03/2026"),
        ]);

        var staged = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(rows), cancellationToken);
        Assert.Equal((20, 17, 3), (staged.RowCount, staged.Ready, staged.Invalid));

        var invalid = (await user.GetBatchAsync(staged.BatchId, cancellationToken, "status=Invalid")).Rows.Items;
        Assert.Equal([18, 19, 20], invalid.Select(row => row.RowNumber));
        Assert.Equal([RowIssues.InvalidDate("20261399000000[-3:BRT]")], invalid[0].Issues);
        Assert.Equal([RowIssues.ZeroAmount], invalid[1].Issues);
        Assert.Equal([RowIssues.EmptyDescription], invalid[2].Issues);

        var committed = await user.CommitAsync(staged.BatchId, cancellationToken);
        Assert.Equal((17, 3), (committed.Committed, committed.Skipped));
    }

    /// <summary>Spec integration test 54.</summary>
    [Fact]
    public async Task A_file_over_two_megabytes_is_a_413()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "too-big", cancellationToken);

        var bytes = new byte[(int)(2.1 * 1024 * 1024)];
        Array.Fill(bytes, (byte)'x');

        using var upload = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports", bytes, "grande.ofx", ImportFixtures.OfxFields(accountId)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, upload.StatusCode);

        using var preview = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports/preview-csv", bytes, "grande.csv"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, preview.StatusCode);

        Assert.Empty((await user.Client.GetFromJsonAsync<List<ImportFixtures.BatchItem>>("/api/imports", cancellationToken))!);
    }

    /// <summary>Spec integration test 55.</summary>
    [Fact]
    public async Task A_file_with_more_than_five_thousand_rows_is_a_422()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "too-many", cancellationToken);

        var ofx = ImportFixtures.Ofx(ImportFixtures.OfxRows(ImportEndpoints.MaxRows + 1));
        Assert.True(ofx.Length < ImportEndpoints.MaxFileBytes);

        using var upload = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports", Encoding.UTF8.GetBytes(ofx), "muitos.ofx", ImportFixtures.OfxFields(accountId)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, upload.StatusCode);
        Assert.Empty((await user.Client.GetFromJsonAsync<List<ImportFixtures.BatchItem>>("/api/imports", cancellationToken))!);

        // And exactly five thousand is fine, inside the request.
        var full = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(ImportFixtures.OfxRows(ImportEndpoints.MaxRows)), cancellationToken);
        Assert.Equal(ImportEndpoints.MaxRows, full.Ready);
        Assert.Equal(ImportEndpoints.MaxRows, (await user.CommitAsync(full.BatchId, cancellationToken)).Committed);
    }

    /// <summary>
    /// Spec integration tests 56 and 57. The category picked by hand in the preview
    /// is what the next import of the same merchant suggests; a merchant never seen
    /// gets the sign default.
    /// </summary>
    [Fact]
    public async Task A_category_chosen_in_the_preview_is_suggested_next_time_and_the_sign_default_otherwise()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "suggest", cancellationToken);
        var categories = await user.CategoriesAsync(cancellationToken);

        var first = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260901", "-42.90", "S-1", "PAG*IFOOD 01/09")]),
            cancellationToken);

        // Test 57 first: nothing in history, so the expense default.
        var row = Assert.Single((await user.GetBatchAsync(first.BatchId, cancellationToken)).Rows.Items);
        Assert.Equal(categories["Outros"].Id, row.CategoryId);

        // A category of the wrong kind is refused, one of the right kind is kept.
        using var wrongKind = await user.Client.SendAsync(
            ImportFixtures.Patch($"/api/imports/{first.BatchId}/rows/{row.Id}", new { categoryId = categories["Salário"].Id }),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode);
        Assert.Contains("categoryId", await TransactionsFixtures.ProblemFieldsAsync(wrongKind, cancellationToken));

        using var corrected = await user.Client.SendAsync(
            ImportFixtures.Patch($"/api/imports/{first.BatchId}/rows/{row.Id}", new { categoryId = categories["Alimentação"].Id }),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);

        await user.CommitAsync(first.BatchId, cancellationToken);
        Assert.Equal(categories["Alimentação"].Id, Assert.Single((await user.ListTransactionsAsync(cancellationToken)).Items).CategoryId);

        // Test 56: same merchant, different day and id, and an income line for the other default.
        var second = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx(
            [
                new ImportFixtures.OfxRow("20260915", "-58.00", "S-2", "PAG*IFOOD 15/09"),
                new ImportFixtures.OfxRow("20260915", "3000.00", "S-3", "TED RECEBIDA"),
            ]),
            cancellationToken);

        var rows = (await user.GetBatchAsync(second.BatchId, cancellationToken)).Rows.Items;
        Assert.Equal(categories["Alimentação"].Id, rows[0].CategoryId);
        Assert.Equal(categories["Outras receitas"].Id, rows[1].CategoryId);
    }

    /// <summary>
    /// The spec's accepted debt, pinned: a row entered by hand carries no key and
    /// never feeds the suggestion or the dedupe.
    /// </summary>
    [Fact]
    public async Task A_transaction_entered_by_hand_does_not_feed_the_history()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "manual-history", cancellationToken);
        var categories = await user.CategoriesAsync(cancellationToken);

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categories["Alimentação"].Id, date: "2026-09-01", description: "PAG*IFOOD 01/09"),
            cancellationToken);

        var staged = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260901", "-42.90", null, "PAG*IFOOD 01/09")]),
            cancellationToken);

        var row = Assert.Single((await user.GetBatchAsync(staged.BatchId, cancellationToken)).Rows.Items);
        Assert.Equal("Ready", row.Status);
        Assert.Equal(categories["Outros"].Id, row.CategoryId);
    }

    [Fact]
    public async Task An_invalid_row_cannot_be_included()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "include-invalid", cancellationToken);

        var staged = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260901", "0.00", "Z-1", "Zero")]),
            cancellationToken);

        var row = Assert.Single((await user.GetBatchAsync(staged.BatchId, cancellationToken)).Rows.Items);
        Assert.Equal("Invalid", row.Status);

        using var include = await user.Client.SendAsync(
            ImportFixtures.Patch($"/api/imports/{staged.BatchId}/rows/{row.Id}", new { include = true }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, include.StatusCode);
        Assert.Contains("include", await TransactionsFixtures.ProblemFieldsAsync(include, cancellationToken));

        var committed = await user.CommitAsync(staged.BatchId, cancellationToken);
        Assert.Equal((0, 1), (committed.Committed, committed.Skipped));
    }

    [Fact]
    public async Task Something_that_is_not_ofx_is_a_400_naming_the_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "not-ofx", cancellationToken);

        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports", Encoding.UTF8.GetBytes("<html>sessão expirada</html>"), "extrato.ofx", ImportFixtures.OfxFields(accountId)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("file", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Staged_rows_are_paginated_and_filtered_by_status()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var (user, accountId) = await NewUserWithAccountAsync(factory, "paging", cancellationToken);

        var staged = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(ImportFixtures.OfxRows(120)), cancellationToken);

        var page2 = await user.GetBatchAsync(staged.BatchId, cancellationToken, "page=2&pageSize=50");
        Assert.Equal(120, page2.Rows.Total);
        Assert.Equal(50, page2.Rows.Items.Count);
        Assert.Equal(51, page2.Rows.Items[0].RowNumber);

        var none = await user.GetBatchAsync(staged.BatchId, cancellationToken, "status=Invalid");
        Assert.Equal(0, none.Rows.Total);
        Assert.Equal(120, none.Counts.Ready);
    }
}
