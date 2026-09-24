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

        // A delegate, so that without the switch resolving it is what fails, not the boot:
        // the real adapters arrive in CP3.
        services.AddTransient<IAiProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;
            return options.FakeProvider
                ? new FakeAiProvider()
                : throw new InvalidOperationException($"Ai:Provider '{options.Provider}' has no adapter yet.");
        });

        return services;
    }

    /// <summary>Fails the boot when <c>Ai:FakeProvider</c> is on outside Development.</summary>
    /// <remarks>
    /// Its usage rows are priced like real ones, but they say <c>fake</c>, and no real user
    /// should ever get its canned text for their money.
    /// </remarks>
    public static void RefuseFakeProviderOutsideDevelopment(IConfiguration configuration, IHostEnvironment environment)
    {
        if (configuration.GetValue<bool>($"{AiOptions.Section}:{nameof(AiOptions.FakeProvider)}") && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Ai:FakeProvider is on in the " + environment.EnvironmentName + " environment. "
                + "It exists for the E2E run and is refused outside Development.");
        }
    }
}
