using System.Text;

namespace Finance.Api.Domain.Import;

/// <summary>
/// Bytes of an uploaded statement to text. Brazilian banks export in whichever
/// encoding their mainframe had — Windows-1252 is as common as UTF-8 — and a file
/// that is decoded wrong turns every accented description into mojibake that the
/// normalizer then cannot match against history.
/// </summary>
/// <remarks>
/// A BOM wins. Otherwise strict UTF-8: it is the one encoding a file can be
/// <em>proven</em> to be in, because random Latin-1 bytes almost never form valid
/// UTF-8 sequences. Only when that proof fails is the file read as Latin-1, which
/// covers every accented letter Portuguese has and differs from Windows-1252 only
/// in the 0x80–0x9F block. Windows-1252 itself is not available under
/// <c>InvariantGlobalization</c> without registering a code-page provider, and the
/// difference is not worth the dependency.
/// </remarks>
public static class StatementText
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LittleEndianBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BigEndianBom = [0xFE, 0xFF];

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Utf8Bom))
        {
            return Encoding.UTF8.GetString(bytes[Utf8Bom.Length..]);
        }

        // Excel's "Unicode text" export, which some people reach for.
        if (bytes.StartsWith(Utf16LittleEndianBom))
        {
            return Encoding.Unicode.GetString(bytes[Utf16LittleEndianBom.Length..]);
        }

        if (bytes.StartsWith(Utf16BigEndianBom))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[Utf16BigEndianBom.Length..]);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
