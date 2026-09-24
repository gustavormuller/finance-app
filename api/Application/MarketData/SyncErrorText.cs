namespace Finance.Api.Application.MarketData;

/// <summary>
/// The pt-BR text a person reads about a sync failure, chosen by the exception's type.
/// Not implemented yet.
/// </summary>
public static class SyncErrorText
{
    public static string For(Exception failure) => throw new NotImplementedException(failure.GetType().Name);
}
