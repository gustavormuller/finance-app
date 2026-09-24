using Finance.Api.Domain.MarketData;

namespace Finance.Api.Application.MarketData;

/// <summary>
/// Picks the <see cref="IPriceProvider"/> for a <see cref="ProviderKind"/>. Adding a
/// provider is one class and one registration (006).
/// </summary>
public interface IPriceProviderRegistry
{
    /// <exception cref="InvalidOperationException">No provider is registered for <paramref name="kind"/>.</exception>
    IPriceProvider For(ProviderKind kind);
}

/// <summary>Built from every registered <see cref="IPriceProvider"/>; two for one kind is a startup error.</summary>
public sealed class PriceProviderRegistry(IEnumerable<IPriceProvider> providers) : IPriceProviderRegistry
{
    private readonly IReadOnlyList<IPriceProvider> providers = [.. providers];

    public IPriceProvider For(ProviderKind kind) => throw new NotImplementedException();
}
