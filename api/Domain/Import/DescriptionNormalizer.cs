using System.Text;

namespace Finance.Api.Domain.Import;

/// <summary>
/// Reduces a bank's description to the key that dedupe and ADR-012's history lookup
/// share. <c>PAG*IFOOD 12/03</c> and <c>PAG*IFOOD  15/04</c> both become
/// <c>PAG IFOOD</c>: same merchant, different day, one key.
/// </summary>
/// <remarks>
/// The spec lists six steps — uppercase, strip accents, digit runs to a space,
/// strip <c>* # - / . : ,</c>, collapse whitespace, truncate. They are applied in
/// one pass because none of them can produce input for an earlier one, and the
/// result is the same as running them in sequence. Idempotent by construction: the
/// output contains only letters and single spaces, which every step leaves alone.
/// <para>
/// Accents are stripped with an explicit table rather than <c>FormD</c>
/// normalization, because the API runs with <c>InvariantGlobalization</c> and
/// <see cref="string.Normalize()"/> is a no-op there. The table covers the whole
/// Latin-1 uppercase range, which is every accented letter Portuguese has.
/// </para>
/// </remarks>
public static class DescriptionNormalizer
{
    /// <summary>Matches the <c>NormalizedDescription</c> column.</summary>
    public const int MaxLength = 300;

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var upper = raw.ToUpperInvariant();
        var builder = new StringBuilder(Math.Min(upper.Length, MaxLength));
        var pendingSpace = false;

        foreach (var character in upper)
        {
            // Digits, the listed punctuation and whitespace all become "a space goes
            // here", and a run of them becomes one space.
            if (char.IsDigit(character) || char.IsWhiteSpace(character) || IsStripped(character))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(StripAccent(character));

            if (builder.Length >= MaxLength)
            {
                break;
            }
        }

        return builder.Length > MaxLength
            ? builder.ToString(0, MaxLength).TrimEnd()
            : builder.ToString().TrimEnd();
    }

    private static bool IsStripped(char character) =>
        character is '*' or '#' or '-' or '/' or '.' or ':' or ',';

    /// <summary>
    /// Latin-1 Supplement, uppercase half, to its unaccented letter. The literals
    /// are why this file carries a BOM (CLAUDE.md).
    /// </summary>
    private static char StripAccent(char character) => character switch
    {
        >= 'À' and <= 'Å' => 'A', // A with grave, acute, circumflex, tilde, diaeresis, ring
        'Ç' => 'C',                    // C with cedilla
        >= 'È' and <= 'Ë' => 'E', // E with grave, acute, circumflex, diaeresis
        >= 'Ì' and <= 'Ï' => 'I', // I with grave, acute, circumflex, diaeresis
        'Ñ' => 'N',                    // N with tilde
        >= 'Ò' and <= 'Ö' => 'O', // O with grave, acute, circumflex, tilde, diaeresis
        'Ø' => 'O',                    // O with stroke
        >= 'Ù' and <= 'Ü' => 'U', // U with grave, acute, circumflex, diaeresis
        'Ý' => 'Y',                    // Y with acute
        _ => character,
    };
}
