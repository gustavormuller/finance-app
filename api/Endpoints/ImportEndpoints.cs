using System.Text.Json;
using Finance.Api.Application;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Endpoints;

public static class ImportEndpoints
{
    /// <summary>Spec decision 12. Rejected at the request boundary, before parsing.</summary>
    public const long MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>Spec decision 12.</summary>
    public const int MaxRows = 5_000;

    private const int PreviewRows = 5;

    private const int DefaultPageSize = 50;

    private const int MaximumPageSize = 200;

    private sealed record CsvPreviewResponse(
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyList<string>> SampleRows,
        string? Delimiter,
        int SkippedRows,
        int RowCount);

    private sealed record UploadResponse(Guid BatchId, int RowCount, int Ready, int Duplicates, int Invalid);

    private sealed record BatchResponse(
        Guid Id,
        Guid AccountId,
        string AccountName,
        ImportSource Source,
        string FileName,
        ImportBatchStatus Status,
        int RowCount,
        int? CommittedCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CommittedAt);

    private sealed record RowResponse(
        Guid Id,
        int RowNumber,
        DateOnly? Date,
        decimal? Amount,
        string? Currency,
        string RawDescription,
        string? ExternalId,
        Guid? CategoryId,
        StagedRowStatus Status,
        bool Included,
        IReadOnlyList<string> Issues,
        CategorySource CategorySource);

    private sealed record RowCounts(int Ready, int Duplicates, int Invalid, int Included);

    private sealed record RowPage(IReadOnlyList<RowResponse> Items, int Page, int PageSize, int Total);

    private sealed record BatchDetail(BatchResponse Batch, RowCounts Counts, RowPage Rows);

    private sealed record RowPatch(Guid? CategoryId, bool? Include);

    private sealed record CommitResponse(int Committed, int Skipped);

    private sealed record UndoResponse(int Deleted);

    private sealed record SuggestResponse(int Suggested, int Skipped);

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder routes)
    {
        var imports = routes.MapGroup("/api/imports").RequireAuthorization();

        // The two multipart routes opt out of the framework's antiforgery token check:
        // CSRF is covered by the Origin check every mutating /api request goes
        // through, which runs before these are reached.
        imports.MapPost("/preview-csv", PreviewCsvAsync).DisableAntiforgery();
        imports.MapPost("/", UploadAsync).DisableAntiforgery();

        imports.MapGet("/", async (AppDbContext database, CancellationToken cancellationToken) =>
            Results.Ok(await Describe(database, only: null).ToListAsync(cancellationToken)));

        imports.MapGet("/{id:guid}", GetBatchAsync);
        imports.MapPatch("/{id:guid}/rows/{rowId:guid}", PatchRowAsync);

        imports.MapPost("/{id:guid}/commit", async (Guid id, ImportCommands commands, CancellationToken cancellationToken) =>
        {
            var result = await commands.CommitAsync(id, cancellationToken);

            return result.Problem switch
            {
                ImportCommandProblem.NotFound => Results.NotFound(),
                ImportCommandProblem.WrongStatus => Problems.Conflict("Este lote já foi confirmado."),
                _ => Results.Ok(new CommitResponse(result.Committed, result.Skipped)),
            };
        });

        // Rung 3 on the rows the sign default filed. Synchronous (009, decision 4); the
        // gateway times the call out at Ai:Categorisation:TimeoutSeconds.
        imports.MapPost("/{id:guid}/suggest", async (
            Guid id, CategorisationCascade cascade, ILoggerFactory loggers, CancellationToken cancellationToken) =>
        {
            SuggestResult result;
            try
            {
                result = await cascade.SuggestAsync(id, cancellationToken);
            }
            catch (Exception failure) when (Problems.IsAiFailure(failure))
            {
                // The reason stays in the log (keys redacted by the adapters), never in the answer.
                loggers.CreateLogger(nameof(ImportEndpoints)).LogWarning(failure, "AI suggestion for batch {BatchId} refused or failed", id);
                return Problems.Ai(failure);
            }

            return result.Problem switch
            {
                ImportCommandProblem.NotFound => Results.NotFound(),
                ImportCommandProblem.WrongStatus => Problems.Conflict("Este lote já foi confirmado e não pode mais ser editado."),
                _ => Results.Ok(new SuggestResponse(result.Suggested, result.Skipped)),
            };
        });

        imports.MapDelete("/{id:guid}", async (Guid id, ImportCommands commands, CancellationToken cancellationToken) =>
            await commands.DiscardAsync(id, cancellationToken) switch
            {
                ImportCommandProblem.NotFound => Results.NotFound(),
                ImportCommandProblem.WrongStatus => Problems.Conflict(
                    "Um lote confirmado não pode ser descartado. Use desfazer para remover os lançamentos."),
                _ => Results.NoContent(),
            });

        imports.MapPost("/{id:guid}/undo", async (Guid id, ImportCommands commands, CancellationToken cancellationToken) =>
        {
            var result = await commands.UndoAsync(id, cancellationToken);

            return result.Problem switch
            {
                ImportCommandProblem.NotFound => Results.NotFound(),
                ImportCommandProblem.WrongStatus => Problems.Conflict(
                    "Este lote ainda não foi confirmado. Descarte-o em vez de desfazer."),
                _ => Results.Ok(new UndoResponse(result.Deleted)),
            };
        });

        return routes;
    }

    /// <summary>
    /// The first rows of a CSV as text, so the mapping screen can show real headers
    /// before anything is persisted. The delimiter is detected unless the form names
    /// one; the first elected row is returned as the headers whether or not the user
    /// ends up declaring a header, and the screen decides how to read it.
    /// </summary>
    private static async Task<IResult> PreviewCsvAsync(IFormFile? file, IFormCollection form, CancellationToken cancellationToken)
    {
        if (await ReadUploadAsync(file, cancellationToken) is not { } upload)
        {
            return Problems.Validation("file", "Envie um arquivo.");
        }

        if (upload.TooLarge is { } tooLarge)
        {
            return tooLarge;
        }

        CsvTable table;
        string? delimiter = null;

        if (SpreadsheetStatementReader.IsSpreadsheet(upload.Bytes) || IsSpreadsheetName(upload.FileName))
        {
            // Typed cells are written the way the mapping will declare them, so the
            // live preview shows what the upload will stage (spec 011, decision 7).
            var read = SpreadsheetStatementReader.Read(upload.Bytes, form["culture"], form["dateFormat"], hasHeader: false);

            if (read.Problem is { } problem)
            {
                return SpreadsheetProblemResult(problem);
            }

            table = read.Table!;
        }
        else
        {
            var separator = ReadDelimiter(form["delimiter"]) ?? CsvStatementParser.DetectDelimiter(upload.Text);
            table = CsvStatementParser.Parse(upload.Text, separator, hasHeader: false);
            delimiter = separator.ToString();
        }

        if (table.Records.Count == 0)
        {
            return Problems.Validation("file", "O arquivo não tem linhas legíveis.");
        }

        return Results.Ok(new CsvPreviewResponse(
            table.Records[0].Fields,
            table.Records.Skip(1).Take(PreviewRows).Select(record => record.Fields).ToList(),
            delimiter,
            table.SkippedRows,
            table.Records.Count));
    }

    private static async Task<IResult> UploadAsync(
        IFormFile? file,
        IFormCollection form,
        AppDbContext database,
        ImportStaging staging,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (await ReadUploadAsync(file, cancellationToken) is not { } upload)
        {
            return Problems.Validation("file", "Envie um arquivo.");
        }

        if (upload.TooLarge is { } tooLarge)
        {
            return tooLarge;
        }

        if (!Guid.TryParse(form["accountId"], out var accountId))
        {
            return Problems.Validation("accountId", "Conta não encontrada.");
        }

        // Through the query filter: somebody else's account does not resolve, and the
        // answer is the same 400 a made-up id gets (003, rule 4).
        var account = await database.Accounts.SingleOrDefaultAsync(entity => entity.Id == accountId, cancellationToken);

        if (account is null)
        {
            return Problems.Validation("accountId", "Conta não encontrada.");
        }

        if (!Enum.TryParse<ImportSource>(form["source"], ignoreCase: true, out var source))
        {
            return Problems.Validation("source", "A origem precisa ser Ofx, Csv ou Spreadsheet.");
        }

        var parsed = source == ImportSource.Ofx
            ? ParseOfx(upload.Text)
            : await ParseTableAsync(upload, source, form, database, cancellationToken);

        if (parsed.Problem is { } invalid)
        {
            return invalid;
        }

        var rows = parsed.Rows!;

        if (rows.Count > MaxRows)
        {
            return Results.Problem(
                title: "Arquivo com linhas demais",
                detail: $"O arquivo tem {rows.Count} lançamentos; o limite é {MaxRows}. Exporte um período menor.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (rows.Count == 0)
        {
            return Problems.Validation("file", "O arquivo não tem lançamentos.");
        }

        var outcome = await staging.StageAsync(currentUser.Id!.Value, account, source, upload.FileName, rows, cancellationToken);

        if (outcome.OpenBatchId is { } openBatchId)
        {
            return Results.Problem(
                title: "Conflito",
                detail: "Já existe uma importação em andamento. Confirme ou descarte-a antes de enviar outro arquivo.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["openBatchId"] = openBatchId });
        }

        var summary = outcome.Summary!;

        return Results.Created(
            $"/api/imports/{summary.BatchId}",
            new UploadResponse(summary.BatchId, summary.RowCount, summary.Ready, summary.Duplicates, summary.Invalid));
    }

    private static (IReadOnlyList<ParsedRow>? Rows, IResult? Problem) ParseOfx(string text)
    {
        var result = OfxParser.Parse(text);

        return result.Error is { } error
            ? (null, Problems.Validation("file", error))
            : (result.Statement!.Rows, null);
    }

    /// <summary>
    /// A CSV or a spreadsheet, through a saved template by id or the mapping fields
    /// inline. Either way the mapping is validated against the real headers of this
    /// file before a row is read. A spreadsheet has no delimiter; its typed cells are
    /// written in the mapping's culture and date format (spec 011, decision 5).
    /// </summary>
    private static async Task<(IReadOnlyList<ParsedRow>? Rows, IResult? Problem)> ParseTableAsync(
        Upload upload,
        ImportSource source,
        IFormCollection form,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        CsvMapping mapping;

        if (Guid.TryParse(form["templateId"], out var templateId))
        {
            var template = await database.CsvTemplates.SingleOrDefaultAsync(entity => entity.Id == templateId, cancellationToken);

            if (template is null)
            {
                return (null, Problems.Validation("templateId", "Modelo não encontrado."));
            }

            mapping = template.ToMapping();
        }
        else
        {
            if (!Enum.TryParse<SignMode>(form["signMode"], ignoreCase: true, out var signMode))
            {
                return (null, Problems.Validation("signMode", "O modo de sinal precisa ser Signed, SignedInverted ou DebitCredit."));
            }

            mapping = new CsvMapping(
                ReadDelimiter(form["delimiter"]) ?? (source == ImportSource.Csv ? CsvStatementParser.DetectDelimiter(upload.Text) : ';'),
                !string.Equals(form["hasHeader"], "false", StringComparison.OrdinalIgnoreCase),
                form["culture"].ToString(),
                form["dateFormat"].ToString(),
                signMode,
                form["dateColumn"].ToString(),
                Optional(form["amountColumn"]),
                Optional(form["debitColumn"]),
                Optional(form["creditColumn"]),
                form["descriptionColumns"].ToString());
        }

        CsvTable table;

        if (source == ImportSource.Spreadsheet)
        {
            var read = SpreadsheetStatementReader.Read(upload.Bytes, mapping.Culture, mapping.DateFormat, mapping.HasHeader);

            if (read.Problem is { } problem)
            {
                return (null, SpreadsheetProblemResult(problem));
            }

            table = read.Table!;
        }
        else
        {
            table = CsvStatementParser.Parse(upload.Text, mapping.Delimiter, mapping.HasHeader);
        }

        var violations = mapping.Validate(table);

        if (violations.Count > 0)
        {
            return (null, Problems.Validation([.. violations.Select(violation => (RuleViolation?)violation)]));
        }

        return (CsvRowInterpreter.Interpret(table, mapping), null);
    }

    private static async Task<IResult> GetBatchAsync(
        Guid id,
        AppDbContext database,
        CancellationToken cancellationToken,
        StagedRowStatus? status = null,
        int page = 1,
        int pageSize = DefaultPageSize)
    {
        var batch = await Describe(database, only: id).SingleOrDefaultAsync(cancellationToken);

        if (batch is null)
        {
            return Results.NotFound();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);

        var all = database.StagedTransactions.Where(row => row.ImportBatchId == id);

        var counts = new RowCounts(
            await all.CountAsync(row => row.Status == StagedRowStatus.Ready, cancellationToken),
            await all.CountAsync(row => row.Status == StagedRowStatus.Duplicate, cancellationToken),
            await all.CountAsync(row => row.Status == StagedRowStatus.Invalid, cancellationToken),
            await all.CountAsync(row => row.Included, cancellationToken));

        var filtered = status is { } only ? all.Where(row => row.Status == only) : all;
        var total = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderBy(row => row.RowNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Results.Ok(new BatchDetail(
            batch,
            counts,
            new RowPage(items.Select(Describe).ToList(), page, pageSize, total)));
    }

    /// <summary>
    /// The two things the preview lets a person change: the category, and whether a
    /// duplicate goes in. An Invalid row cannot be included, and a category has to
    /// agree with the row's sign or the commit would refuse it anyway (003, rule 3).
    /// </summary>
    private static async Task<IResult> PatchRowAsync(
        Guid id,
        Guid rowId,
        RowPatch patch,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        var batch = await database.ImportBatches.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (batch is null)
        {
            return Results.NotFound();
        }

        if (batch.Status != ImportBatchStatus.Staged)
        {
            return Problems.Conflict("Este lote já foi confirmado e não pode mais ser editado.");
        }

        var row = await database.StagedTransactions
            .SingleOrDefaultAsync(entity => entity.Id == rowId && entity.ImportBatchId == id, cancellationToken);

        if (row is null)
        {
            return Results.NotFound();
        }

        if (patch.Include is true && row.Status == StagedRowStatus.Invalid)
        {
            return Problems.Validation("include", "Uma linha inválida não pode ser incluída.");
        }

        if (patch.CategoryId is { } categoryId)
        {
            var category = await database.Categories.SingleOrDefaultAsync(entity => entity.Id == categoryId, cancellationToken);

            if (category is null)
            {
                return Problems.Validation("categoryId", "Categoria não encontrada.");
            }

            if (row.Amount is { } amount && TransactionRules.ValidateSign(amount, category.Kind) is { } violation)
            {
                return Problems.Validation("categoryId", violation.Message);
            }

            row.CategoryId = categoryId;
            row.CategorySource = CategorySource.User;
        }

        if (patch.Include is { } include)
        {
            row.Included = include;
        }

        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(Describe(row));
    }

    /// <summary>The uploaded bytes; <see cref="Text"/> decodes them once, for the text formats.</summary>
    private sealed record Upload(string FileName, byte[] Bytes, IResult? TooLarge)
    {
        private string? text;

        public string Text => text ??= StatementText.Decode(Bytes);
    }

    /// <summary>Null when there is no file; otherwise its bytes, or the 413 it earned.</summary>
    private static async Task<Upload?> ReadUploadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        var fileName = Path.GetFileName(file.FileName);

        if (file.Length > MaxFileBytes)
        {
            return new Upload(fileName, [], Results.Problem(
                title: "Arquivo muito grande",
                detail: $"O arquivo tem {file.Length / 1024.0m / 1024.0m:0.0} MB; o limite é {MaxFileBytes / 1024 / 1024} MB.",
                statusCode: StatusCodes.Status413PayloadTooLarge));
        }

        var bytes = new byte[file.Length];

        await using var stream = file.OpenReadStream();
        await stream.ReadExactlyAsync(bytes, cancellationToken);

        return new Upload(fileName, bytes, null);
    }

    private static bool IsSpreadsheetName(string fileName) =>
        fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);

    /// <summary>Spec 011, decisions 10 and 11. Rendered verbatim.</summary>
    private static IResult SpreadsheetProblemResult(SpreadsheetProblem problem) => problem switch
    {
        SpreadsheetProblem.TooLarge => Results.Problem(
            title: "Arquivo muito grande",
            detail: "A planilha é grande demais depois de descompactada; o limite é 20 MB. Exporte um período menor.",
            statusCode: StatusCodes.Status413PayloadTooLarge),
        SpreadsheetProblem.PasswordProtected => Problems.Validation(
            "file", "A planilha está protegida por senha. Remova a senha no Excel e envie de novo."),
        _ => Problems.Validation(
            "file", "O arquivo não é uma planilha do Excel (.xls ou .xlsx) válida. Se o banco oferece CSV ou OFX, exporte nesse formato."),
    };

    private static char? ReadDelimiter(string? value) =>
        value is { Length: 1 } ? value[0] : value?.ToLowerInvariant() switch
        {
            "tab" or "\\t" => '\t',
            _ => null,
        };

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The id filter is applied before the projection: EF cannot see through a
    /// constructor-only projection to filter by one of its arguments.
    /// </summary>
    private static IQueryable<BatchResponse> Describe(AppDbContext database, Guid? only) =>
        from batch in database.ImportBatches
        where only == null || batch.Id == only
        join account in database.Accounts on batch.AccountId equals account.Id
        orderby batch.CreatedAt descending
        select new BatchResponse(
            batch.Id,
            account.Id,
            account.Name,
            batch.Source,
            batch.FileName,
            batch.Status,
            batch.RowCount,
            batch.CommittedCount,
            batch.CreatedAt,
            batch.CommittedAt);

    private static RowResponse Describe(StagedTransaction row) => new(
        row.Id,
        row.RowNumber,
        row.Date,
        row.Amount,
        row.Currency,
        row.RawDescription,
        row.ExternalId,
        row.CategoryId,
        row.Status,
        row.Included,
        row.Issues is null ? [] : JsonSerializer.Deserialize<List<string>>(row.Issues) ?? [],
        row.CategorySource);
}
