using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Shared setup for 004's tests: entities written directly for the persistence
/// tests, and multipart uploads, an OFX builder and the response shapes for the
/// endpoint tests.
/// </summary>
/// <remarks>
/// The response shapes are declared here rather than shared with the endpoints, so
/// a renamed field fails a test instead of being renamed on both sides at once.
/// </remarks>
internal static class ImportFixtures
{
    public static ImportBatch ABatch(
        Guid userId,
        Guid accountId,
        ImportBatchStatus status = ImportBatchStatus.Staged,
        string fileName = "extrato.ofx") => new()
    {
        UserId = userId,
        AccountId = accountId,
        Source = ImportSource.Ofx,
        FileName = fileName,
        Status = status,
        RowCount = 0,
        CreatedAt = DateTimeOffset.UtcNow,
        CommittedAt = status == ImportBatchStatus.Committed ? DateTimeOffset.UtcNow : null,
        CommittedCount = status == ImportBatchStatus.Committed ? 0 : null,
    };

    public static StagedTransaction AStagedRow(
        Guid userId,
        Guid batchId,
        int rowNumber = 1,
        decimal? amount = -42.90m,
        string? externalId = null,
        StagedRowStatus status = StagedRowStatus.Ready) => new()
    {
        UserId = userId,
        ImportBatchId = batchId,
        RowNumber = rowNumber,
        Date = new DateOnly(2026, 9, 10),
        Amount = amount,
        Currency = Account.DefaultCurrency,
        RawDescription = "PAG*IFOOD 10/09",
        NormalizedDescription = "PAG IFOOD",
        ExternalId = externalId,
        Status = status,
        Included = status == StagedRowStatus.Ready,
    };

    public static CsvTemplate ATemplate(Guid userId, string name = "Nubank conta") => new()
    {
        UserId = userId,
        Name = name,
        Delimiter = ',',
        HasHeader = true,
        Culture = "en-US",
        DateFormat = "dd/MM/yyyy",
        SignMode = SignMode.Signed,
        DateColumn = "Data",
        AmountColumn = "Valor",
        DescriptionColumns = "Descrição",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>An account and a category for <paramref name="userId"/>, written directly.</summary>
    public static async Task<(Guid AccountId, Guid CategoryId)> SeedAccountAndCategoryAsync(
        string connectionString,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var context = TransactionsFixtures.ContextFor(connectionString, userId);

        var account = TransactionsFixtures.AnAccount(userId, "Conta " + Guid.NewGuid().ToString("N")[..8]);
        var category = TransactionsFixtures.ACategory(userId, "Categoria " + Guid.NewGuid().ToString("N")[..8]);

        context.Accounts.Add(account);
        context.Categories.Add(category);
        await context.SaveChangesAsync(cancellationToken);

        return (account.Id, category.Id);
    }

    // ---- HTTP -------------------------------------------------------------------

    /// <summary>One STMTTRN. Null leaves the tag out entirely.</summary>
    public sealed record OfxRow(
        string Posted,
        string Amount,
        string? FitId,
        string? Name,
        string? Memo = null,
        string? Currency = null);

    /// <summary>A 1.x document the way a Brazilian bank writes one: header block, unclosed tags.</summary>
    public static string Ofx(IEnumerable<OfxRow> rows, string currency = Account.DefaultCurrency)
    {
        var builder = new StringBuilder(
            "OFXHEADER:100\nDATA:OFXSGML\nVERSION:102\nSECURITY:NONE\nENCODING:USASCII\nCHARSET:1252\n"
            + "COMPRESSION:NONE\nOLDFILEUID:NONE\nNEWFILEUID:NONE\n\n"
            + "<OFX>\n<BANKMSGSRSV1>\n<STMTTRNRS>\n<STMTRS>\n");

        builder.Append("<CURDEF>").Append(currency).Append("\n<BANKTRANLIST>\n");

        foreach (var row in rows)
        {
            builder.Append("<STMTTRN>\n<TRNTYPE>OTHER\n");
            builder.Append("<DTPOSTED>").Append(row.Posted).Append("000000[-3:BRT]\n");
            builder.Append("<TRNAMT>").Append(row.Amount).Append('\n');

            if (row.FitId is not null)
            {
                builder.Append("<FITID>").Append(row.FitId).Append('\n');
            }

            if (row.Name is not null)
            {
                builder.Append("<NAME>").Append(row.Name).Append('\n');
            }

            if (row.Memo is not null)
            {
                builder.Append("<MEMO>").Append(row.Memo).Append('\n');
            }

            if (row.Currency is not null)
            {
                builder.Append("<CURRENCY>\n<CURRATE>1\n<CURSYM>").Append(row.Currency).Append("\n</CURRENCY>\n");
            }

            builder.Append("</STMTTRN>\n");
        }

        builder.Append("</BANKTRANLIST>\n</STMTRS>\n</STMTTRNRS>\n</BANKMSGSRSV1>\n</OFX>\n");

        return builder.ToString();
    }

    /// <summary><paramref name="count"/> distinct, valid rows on consecutive days.</summary>
    public static IEnumerable<OfxRow> OfxRows(int count, string prefix = "FIT", DateOnly? from = null)
    {
        var start = from ?? new DateOnly(2026, 9, 1);

        return Enumerable.Range(0, count).Select(index => new OfxRow(
            start.AddDays(index % 28).ToString("yyyyMMdd"),
            (-10m - index).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            $"{prefix}-{index}",
            $"PAG*IFOOD {index}",
            $"Compra {index}"));
    }

    /// <summary>A multipart upload carrying the Origin header the CSRF check demands.</summary>
    public static HttpRequestMessage Upload(
        string url,
        byte[] file,
        string fileName,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        var content = new MultipartFormDataContent();

        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(part, "file", fileName);

        foreach (var (name, value) in fields ?? new Dictionary<string, string>())
        {
            content.Add(new StringContent(value), name);
        }

        return WithOrigin(new HttpRequestMessage(HttpMethod.Post, url) { Content = content });
    }

    public static HttpRequestMessage Patch(string url, object body) =>
        WithOrigin(new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(body) });

    public static HttpRequestMessage Post(string url) =>
        WithOrigin(new HttpRequestMessage(HttpMethod.Post, url));

    public static IReadOnlyDictionary<string, string> OfxFields(Guid accountId) =>
        new Dictionary<string, string> { ["accountId"] = accountId.ToString(), ["source"] = "Ofx" };

    public static IReadOnlyDictionary<string, string> CsvFields(
        Guid accountId,
        string culture,
        string dateFormat,
        string signMode,
        string dateColumn,
        string? amountColumn,
        string descriptionColumns,
        string? delimiter = null,
        string? debitColumn = null,
        string? creditColumn = null,
        bool hasHeader = true)
    {
        var fields = new Dictionary<string, string>
        {
            ["accountId"] = accountId.ToString(),
            ["source"] = "Csv",
            ["culture"] = culture,
            ["dateFormat"] = dateFormat,
            ["signMode"] = signMode,
            ["dateColumn"] = dateColumn,
            ["descriptionColumns"] = descriptionColumns,
            ["hasHeader"] = hasHeader ? "true" : "false",
        };

        if (amountColumn is not null)
        {
            fields["amountColumn"] = amountColumn;
        }

        if (delimiter is not null)
        {
            fields["delimiter"] = delimiter;
        }

        if (debitColumn is not null)
        {
            fields["debitColumn"] = debitColumn;
        }

        if (creditColumn is not null)
        {
            fields["creditColumn"] = creditColumn;
        }

        return fields;
    }

    public sealed record UploadResult(Guid BatchId, int RowCount, int Ready, int Duplicates, int Invalid);

    public sealed record BatchItem(
        Guid Id,
        Guid AccountId,
        string AccountName,
        string Source,
        string FileName,
        string Status,
        int RowCount,
        int? CommittedCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CommittedAt);

    public sealed record Counts(int Ready, int Duplicates, int Invalid, int Included);

    public sealed record RowItem(
        Guid Id,
        int RowNumber,
        DateOnly? Date,
        decimal? Amount,
        string? Currency,
        string RawDescription,
        string? ExternalId,
        Guid? CategoryId,
        string Status,
        bool Included,
        List<string> Issues,
        string? CategorySource = null);

    public sealed record RowPage(List<RowItem> Items, int Page, int PageSize, int Total);

    public sealed record BatchDetail(BatchItem Batch, Counts Counts, RowPage Rows);

    public sealed record CommitResult(int Committed, int Skipped);

    public sealed record UndoResult(int Deleted);

    public sealed record TemplateItem(
        Guid Id,
        string Name,
        string Delimiter,
        bool HasHeader,
        string Culture,
        string DateFormat,
        string SignMode,
        string DateColumn,
        string? AmountColumn,
        string? DebitColumn,
        string? CreditColumn,
        string DescriptionColumns);

    public static async Task<UploadResult> UploadOfxAsync(
        this SignedInUser user,
        Guid accountId,
        string ofx,
        CancellationToken cancellationToken,
        string fileName = "extrato.ofx")
    {
        using var response = await user.Client.SendAsync(
            Upload("/api/imports", Encoding.UTF8.GetBytes(ofx), fileName, OfxFields(accountId)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<UploadResult>(cancellationToken))!;
    }

    public static async Task<CommitResult> CommitAsync(this SignedInUser user, Guid batchId, CancellationToken cancellationToken)
    {
        using var response = await user.Client.SendAsync(Post($"/api/imports/{batchId}/commit"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CommitResult>(cancellationToken))!;
    }

    public static async Task<BatchDetail> GetBatchAsync(
        this SignedInUser user,
        Guid batchId,
        CancellationToken cancellationToken,
        string query = "pageSize=200") =>
        (await user.Client.GetFromJsonAsync<BatchDetail>($"/api/imports/{batchId}?{query}", cancellationToken))!;

    public static async Task<TransactionsFixtures.TransactionPage> ListTransactionsAsync(
        this SignedInUser user,
        CancellationToken cancellationToken,
        string query = "pageSize=200") =>
        (await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>($"/api/transactions?{query}", cancellationToken))!;

    /// <summary>The seeded categories, by name.</summary>
    public static async Task<Dictionary<string, TransactionsFixtures.CategoryItem>> CategoriesAsync(
        this SignedInUser user,
        CancellationToken cancellationToken)
    {
        var categories = await user.Client.GetFromJsonAsync<List<TransactionsFixtures.CategoryItem>>("/api/categories", cancellationToken);

        return categories!.ToDictionary(category => category.Name);
    }

    private static HttpRequestMessage WithOrigin(HttpRequestMessage request)
    {
        request.Headers.Add("Origin", IdentityApiFactory.AppOrigin);

        return request;
    }
}
