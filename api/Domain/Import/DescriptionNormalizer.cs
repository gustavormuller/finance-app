namespace Finance.Api.Domain.Import;

/// <summary>
/// Reduces a bank's description to the key that dedupe and ADR-012's history lookup
/// share. <c>PAG*IFOOD 12/03</c> and <c>PAG*IFOOD  15/04</c> both become
/// <c>PAG IFOOD</c>: same merchant, different day, one key.
/// </summary>
public static class DescriptionNormalizer
{
    /// <summary>Matches the <c>NormalizedDescription</c> column.</summary>
    public const int MaxLength = 300;

    public static string Normalize(string? raw) => throw new NotImplementedException();
}
