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

    public IReadOnlyList<RuleViolation> Validate(CsvTable table) => throw new NotImplementedException();

    public static int? ResolveColumn(string? reference, CsvTable table) => throw new NotImplementedException();
}

/// <summary>
/// Applies a validated <see cref="CsvMapping"/> to a <see cref="CsvTable"/>, one
/// <see cref="ParsedRow"/> per record.
/// </summary>
public static class CsvRowInterpreter
{
    public static IReadOnlyList<ParsedRow> Interpret(CsvTable table, CsvMapping mapping) =>
        throw new NotImplementedException();
}
