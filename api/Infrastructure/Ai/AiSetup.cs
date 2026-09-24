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

        // No resilience handler: every attempt spends tokens and the gateway records one
        // usage row per call, so a failure is returned, never retried. No client timeout
        // either: the gateway times each call out by purpose. Keys never reach a log.
        services.AddHttpClient<AnthropicAiProvider>()
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .RedactLoggedHeaders(["x-api-key"]);
        services.AddHttpClient<OpenAiProvider>()
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .RedactLoggedHeaders(["Authorization"]);

        services.AddTransient<IAiProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;
            if (options.FakeProvider)
            {
                return new FakeAiProvider();
            }

            return options.Provider.ToLowerInvariant() switch
            {
                "anthropic" => provider.GetRequiredService<AnthropicAiProvider>(),
                "openai" => provider.GetRequiredService<OpenAiProvider>(),
                _ => throw new InvalidOperationException($"Ai:Provider '{options.Provider}' has no adapter."),
            };
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
