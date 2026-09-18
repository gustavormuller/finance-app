namespace Finance.Api.Domain.Import;

/// <summary>
/// One row of a statement after parsing, before validation, dedupe and suggestion.
/// The common output of the OFX and CSV parsers, and the input to everything after
/// them.
/// </summary>
/// <param name="RowNumber">1-based position in the file, for error messages.</param>
/// <param name="Date">Null when the text did not fit the declared format.</param>
/// <param name="Amount">Signed, as the file wrote it; null when unparseable. Not yet rounded.</param>
/// <param name="Currency">Null when the file does not say; the account's is assumed.</param>
/// <param name="RawDescription">Untruncated, as it came from the file.</param>
/// <param name="ExternalId">The OFX <c>FITID</c>; null for CSV.</param>
/// <param name="Issues">pt-BR, one per problem the parser found. Empty is not a promise of validity.</param>
public sealed record ParsedRow(
    int RowNumber,
    DateOnly? Date,
    decimal? Amount,
    string? Currency,
    string RawDescription,
    string? ExternalId,
    IReadOnlyList<string> Issues);

/// <summary>
/// Every message a staged row can carry. They are rendered verbatim in the preview,
/// so they are Portuguese (CLAUDE.md); the identifiers around them are not.
/// </summary>
public static class RowIssues
{
    public const string MissingAmount = "Valor ausente";

    public const string ZeroAmount = "Valor não pode ser zero";

    public const string DateOutOfRange = "Data fora do intervalo permitido";

    public const string DebitAndCredit = "Linha tem débito e crédito";

    public const string EmptyDescription = "Descrição vazia";

    public const string CategoryNotFound = "Categoria não encontrada";

    public static string InvalidDate(string text) => $"Data inválida: \"{text}\"";

    public static string InvalidAmount(string text) => $"Valor inválido: \"{text}\"";

    public static string CurrencyMismatch(string rowCurrency) => $"Moeda diferente da conta ({rowCurrency})";

    public static string RaggedRow(int actual, int expected) =>
        $"Linha com {actual} colunas, esperado {expected}";
}
