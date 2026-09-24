using System.Net;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Finance.Api.Application.MarketData;

/// <summary>
/// The pt-BR text a person reads on the /market-data screen about a sync failure, chosen
/// by the exception's type. Exception messages are English diagnostics and go to the log
/// only (CLAUDE.md: anything on screen is pt-BR).
/// </summary>
/// <remarks>
/// The Polly types are what the per-provider resilience handler throws through the
/// adapters: a per-attempt timeout and an open circuit.
/// </remarks>
public static class SyncErrorText
{
    public static string For(Exception failure) => failure switch
    {
        ProviderRateLimitedException => "O provedor recusou por excesso de requisições; tente mais tarde.",
        ProviderResponseInvalidException => "O provedor respondeu em um formato inesperado.",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
            "O provedor recusou a chave de acesso.",
        HttpRequestException => "Falha de comunicação com o provedor.",
        TaskCanceledException or TimeoutRejectedException or TimeoutException => "O provedor não respondeu a tempo.",
        BrokenCircuitException => "Provedor suspenso temporariamente após falhas seguidas.",
        InvalidOperationException => "Provedor ou série sem configuração.",
        _ => "Erro inesperado ao sincronizar.",
    };
}
