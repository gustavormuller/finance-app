namespace Finance.Api.Endpoints;

internal static class RequestText
{
    /// <summary>An optional text field as it is used and stored: trimmed, and null when blank.</summary>
    public static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
