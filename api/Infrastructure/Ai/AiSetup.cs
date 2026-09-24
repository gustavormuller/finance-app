using Finance.Api.Application.Ai;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.Ai;

/// <summary>009's <c>Ai</c> settings, the provider port and the gateway every call goes through.</summary>
public static class AiSetup
{
    public static IServiceCollection AddAi(this IServiceCollection services)
    {
        services.AddOptions<AiOptions>().BindConfiguration(AiOptions.Section);
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<BudgetGuard>();
        services.AddScoped<AiPricing>();
        services.AddScoped<AiGateway>();

        // A delegate, so resolving it is what fails, not the boot: no real adapter until CP3.
        services.AddTransient<IAiProvider>(provider => throw new InvalidOperationException(
            $"Ai:Provider '{provider.GetRequiredService<IOptions<AiOptions>>().Value.Provider}' has no adapter yet."));

        return services;
    }
}
