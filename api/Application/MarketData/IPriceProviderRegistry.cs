using Finance.Api.Domain.MarketData;

namespace Finance.Api.Application.MarketData;

/// <summary>
/// Picks the <see cref="IPriceProvider"/> for a <see cref="ProviderKind"/>. Adding a
/// provider is one class and one registration.
/// </summary>
public interface IPriceProviderRegistry
{
    /// <exception cref="InvalidOperationException">No provider is registered for <paramref name="kind"/>.</exception>
    IPriceProvider For(ProviderKind kind);
}

/// <summary>Built from every registered <see cref="IPriceProvider"/>; two for one kind is a startup error.</summary>
public sealed class PriceProviderRegistry : IPriceProviderRegistry
{
    private readonly Dictionary<ProviderKind, IPriceProvider> byKind = [];

    public PriceProviderRegistry(IEnumerable<IPriceProvider> providers)
    {
        foreach (var provider in providers)
        {
            if (!byKind.TryAdd(provider.Kind, provider))
            {
                throw new InvalidOperationException(
                    $"Two price providers are registered for {provider.Kind}: "
                    + $"{byKind[provider.Kind].GetType().Name} and {provider.GetType().Name}.");
            }
        }
    }

    public IPriceProvider For(ProviderKind kind) =>
        byKind.TryGetValue(kind, out var provider)
            ? provider
            : throw new InvalidOperationException($"No price provider is registered for {kind}.");
}
