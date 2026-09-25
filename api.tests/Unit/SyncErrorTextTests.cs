using System.Net;
using Finance.Api.Application.MarketData;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 006: what the /market-data screen reads about a failure is pt-BR, chosen by the
/// exception's type. The English message is for the log and never shown.
/// </summary>
public sealed class SyncErrorTextTests
{
    public static TheoryData<Exception, string> Failures => new()
    {
        { new ProviderRateLimitedException("Brapi", null), "O provedor recusou por excesso de requisições; tente mais tarde." },
        { new ProviderResponseInvalidException("Brapi", "bad"), "O provedor respondeu em um formato inesperado." },
        { new HttpRequestException("401", null, HttpStatusCode.Unauthorized), "O provedor recusou a chave de acesso." },
        { new HttpRequestException("403", null, HttpStatusCode.Forbidden), "O provedor recusou a chave de acesso." },
        { new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable), "Falha de comunicação com o provedor." },
        { new HttpRequestException("refused"), "Falha de comunicação com o provedor." },
        { new TaskCanceledException("timed out"), "O provedor não respondeu a tempo." },
        { new TimeoutRejectedException("timed out"), "O provedor não respondeu a tempo." },
        { new BrokenCircuitException("open"), "Provedor suspenso temporariamente após falhas seguidas." },
        { new InvalidOperationException("No price provider is registered for Brapi."), "Provedor ou série sem configuração." },
        { new DivideByZeroException(), "Erro inesperado ao sincronizar." },
        {
            new ProviderKeyMissingException("Brapi", "BBAS3", "MarketData:Brapi:Token"),
            "O brapi exige um token para BBAS3. Configure MarketData:Brapi:Token."
        },
        {
            new ProviderKeyMissingException("TwelveData", "AAPL", "MarketData:TwelveData:Key"),
            "O Twelve Data exige uma chave de API para AAPL. Configure MarketData:TwelveData:Key."
        },
        {
            new ProviderKeyMissingException("CoinGecko", "bitcoin", "MarketData:CoinGecko:DemoKey"),
            "O CoinGecko exige uma chave demo para bitcoin. Configure MarketData:CoinGecko:DemoKey."
        },
        {
            new ProviderKeyMissingException("Acme", "XYZ", "MarketData:Acme:Key"),
            "O provedor Acme exige uma chave de acesso para XYZ. Configure MarketData:Acme:Key."
        },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void Each_failure_type_has_its_own_pt_BR_text(Exception failure, string expected)
    {
        Assert.Equal(expected, SyncErrorText.For(failure));
        Assert.NotEqual(failure.Message, SyncErrorText.For(failure));
    }
}
