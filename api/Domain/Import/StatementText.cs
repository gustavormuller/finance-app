namespace Finance.Api.Domain.Import;

/// <summary>
/// Bytes of an uploaded statement to text. Brazilian banks export in whichever
/// encoding their mainframe had — Windows-1252 is as common as UTF-8 — and a file
/// that is decoded wrong turns every accented description into mojibake that the
/// normalizer then cannot match against history.
/// </summary>
public static class StatementText
{
    public static string Decode(ReadOnlySpan<byte> bytes) => throw new NotImplementedException();
}
