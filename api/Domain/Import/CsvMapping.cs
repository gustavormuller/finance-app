using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Import;

/// <summary>
/// How to read one CSV layout: the part of a <c>CsvTemplate</c> that is about the
/// file rather than about who saved it. Column references are a header name, or a
/// 0-based index for files without a header.
/// </summary>
public sealed record CsvMapping(
    char Delimiter,
    bool HasHeader,
    string Culture,
    string DateFormat,
    SignMode SignMode,
    string DateColumn,
    string? AmountColumn,
    string? DebitColumn,
    string? CreditColumn,
    string DescriptionColumns)
{
    /// <summary>What the description columns are joined with, per spec decision 10.</summary>
    public const string DescriptionSeparator = " — ";

    public IReadOnlyList<string> DescriptionColumnList =>
        DescriptionColumns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Everything wrong with the mapping itself, before any row is looked at:
    /// unsupported culture, a format that cannot name a day, the columns a sign
    /// mode requires, and references that do not exist in the table. Field names
    /// match the request body, so the endpoint can report them as a 400.
    /// </summary>
    /// <param name="table">
    /// Null when there is no file yet — a template being saved — in which case the
    /// references are required but not resolved.
    /// </param>
    public IReadOnlyList<RuleViolation> Validate(CsvTable? table)
    {
        var violations = new List<RuleViolation>();

        if (!AmountParser.IsSupportedCulture(Culture))
        {
            violations.Add(new RuleViolation("culture", $"Cultura não suportada: \"{Culture}\"."));
        }

        if (!DateParser.IsValidFormat(DateFormat))
        {
            violations.Add(new RuleViolation("dateFormat", "O formato de data precisa conter dia, mês e ano."));
        }

        CheckColumn(violations, "dateColumn", DateColumn, table, required: true);

        var signed = SignMode is SignMode.Signed or SignMode.SignedInverted;
        CheckColumn(violations, "amountColumn", AmountColumn, table, required: signed);
        CheckColumn(violations, "debitColumn", DebitColumn, table, required: !signed);
        CheckColumn(violations, "creditColumn", CreditColumn, table, required: !signed);

        if (DescriptionColumnList.Count == 0)
        {
            violations.Add(new RuleViolation("descriptionColumns", "Escolha ao menos uma coluna de descrição."));
        }

        foreach (var column in DescriptionColumnList)
        {
            CheckColumn(violations, "descriptionColumns", column, table, required: true);
        }

        return violations;
    }

    /// <summary>
    /// A header name first, exact then case-insensitive, then a 0-based index. Null
    /// when neither resolves inside the table.
    /// </summary>
    public static int? ResolveColumn(string? reference, CsvTable table)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var wanted = reference.Trim();

        if (table.Headers is { } headers)
        {
            var exact = IndexOf(headers, wanted, StringComparison.Ordinal);

            if (exact >= 0)
            {
                return exact;
            }

            var loose = IndexOf(headers, wanted, StringComparison.OrdinalIgnoreCase);

            if (loose >= 0)
            {
                return loose;
            }
        }

        return int.TryParse(wanted, out var index) && index >= 0 && index < table.ColumnCount
            ? index
            : null;
    }

    private static int IndexOf(IReadOnlyList<string> headers, string wanted, StringComparison comparison)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (string.Equals(headers[index].Trim(), wanted, comparison))
            {
                return index;
            }
        }

        return -1;
    }

    private static void CheckColumn(
        List<RuleViolation> violations,
        string field,
        string? reference,
        CsvTable? table,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            if (required)
            {
                violations.Add(new RuleViolation(field, "Escolha a coluna."));
            }

            return;
        }

        if (table is not null && ResolveColumn(reference, table) is null)
        {
            violations.Add(new RuleViolation(field, $"Coluna \"{reference}\" não encontrada no arquivo."));
        }
    }
}

/// <summary>
/// Applies a validated <see cref="CsvMapping"/> to a <see cref="CsvTable"/>, one
/// <see cref="ParsedRow"/> per record. Pure: the same table and mapping always give
/// the same rows, which is what the mapping screen's live preview relies on.
/// </summary>
public static class CsvRowInterpreter
{
    public static IReadOnlyList<ParsedRow> Interpret(CsvTable table, CsvMapping mapping)
    {
        var dateColumn = Require(mapping.DateColumn, table);
        var amountColumn = CsvMapping.ResolveColumn(mapping.AmountColumn, table);
        var debitColumn = CsvMapping.ResolveColumn(mapping.DebitColumn, table);
        var creditColumn = CsvMapping.ResolveColumn(mapping.CreditColumn, table);
        var descriptionColumns = mapping.DescriptionColumnList.Select(column => Require(column, table)).ToList();

        var rows = new List<ParsedRow>(table.Records.Count);

        foreach (var record in table.Records)
        {
            if (record.Issue is { } ragged)
            {
                rows.Add(new ParsedRow(record.RowNumber, null, null, null, "", null, [ragged]));
                continue;
            }

            var issues = new List<string>();

            var dateText = record.Fields[dateColumn];
            DateOnly? date = DateParser.TryParseExact(dateText, mapping.DateFormat, out var parsedDate) ? parsedDate : null;

            if (date is null)
            {
                issues.Add(RowIssues.InvalidDate(dateText.Trim()));
            }

            var issuesBeforeAmounts = issues.Count;

            var resolution = SignResolver.Resolve(
                mapping.SignMode,
                ParseCell(record, amountColumn, mapping.Culture, issues),
                ParseCell(record, debitColumn, mapping.Culture, issues),
                ParseCell(record, creditColumn, mapping.Culture, issues));

            switch (resolution.Problem)
            {
                case SignProblem.DebitAndCredit:
                    issues.Add(RowIssues.DebitAndCredit);
                    break;

                // Only reported when no cell failed to parse: a cell that did is
                // already the reason there is no amount.
                case SignProblem.MissingAmount when issues.Count == issuesBeforeAmounts:
                    issues.Add(RowIssues.MissingAmount);
                    break;
            }

            var description = string.Join(
                CsvMapping.DescriptionSeparator,
                descriptionColumns
                    .Select(column => record.Fields[column].Trim())
                    .Where(part => part.Length > 0));

            rows.Add(new ParsedRow(record.RowNumber, date, resolution.Amount, null, description, null, issues));
        }

        return rows;
    }

    /// <summary>
    /// Null for an unmapped column or an empty cell, which <see cref="SignResolver"/>
    /// treats as "nothing on this side". A cell that has text that is not a number
    /// is an issue on the row and still null.
    /// </summary>
    private static decimal? ParseCell(CsvRecord record, int? column, string culture, List<string> issues)
    {
        if (column is not { } index)
        {
            return null;
        }

        var text = record.Fields[index];

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (AmountParser.TryParse(text, culture, out var amount))
        {
            return amount;
        }

        issues.Add(RowIssues.InvalidAmount(text.Trim()));

        return null;
    }

    private static int Require(string reference, CsvTable table) =>
        CsvMapping.ResolveColumn(reference, table)
        ?? throw new ArgumentException($"Column '{reference}' does not resolve; validate the mapping first.");
}
