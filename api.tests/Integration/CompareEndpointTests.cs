using System.Net;
using System.Net.Http.Json;
using static Finance.Api.Tests.Integration.InvestmentsApi;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// <c>GET /api/compare</c>'s door: spec 026 integration tests 7 and 8, the session and every
/// refusal, in the words the page shows.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CompareEndpointTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    /// <summary>Spec integration test 7.</summary>
    [Fact]
    public async Task Comparing_needs_a_session()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(Today));

        using var response = await api.Factory.CreateApiClient().GetAsync("/api/compare?series=benchmark:CDI,benchmark:IPCA", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Spec integration test 8: every refusal names its field, in the words the page shows.</summary>
    [Fact]
    public async Task Every_refusal_is_a_400_naming_its_field_in_portuguese()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(Today));
        var user = await api.SignInAsync("compare", ct);
        var gold = await api.CatalogueAsync("GOLD", ct, "EUR");
        var missing = Guid.NewGuid();
        const string Two = "series=benchmark:CDI,benchmark:IPCA";
        const string Count = "Escolha de 2 a 6 séries para comparar.";

        (string Query, string Field, string Message)[] cases =
        [
            ("", "series", Count),
            ("series=benchmark:CDI", "series", Count),
            ("series=" + string.Join(',', Enumerable.Repeat("benchmark:CDI", 7)), "series", Count),
            ("series=benchmark:CDI,foo", "series", "Série não reconhecida: 'foo'. Use asset:{id} ou benchmark:{código}."),
            ("series=benchmark:CDI,asset:123", "series", "Série não reconhecida: 'asset:123'. Use asset:{id} ou benchmark:{código}."),
            ("series=benchmark:CDI,benchmark:cdi", "series", "A série 'benchmark:CDI' foi escolhida mais de uma vez."),
            ($"series=benchmark:CDI,asset:{missing}", "series", $"Ativo não encontrado no catálogo: {missing}."),
            ("series=benchmark:CDI,benchmark:XYZ", "series", "Referência desconhecida: XYZ."),
            (Two + "&period=2w", "period",
                "O período deve ser 1 mês (1m), 6 meses (6m), no ano (ytd), 1 ano (1y), 5 anos (5y), 10 anos (10y), "
                + "máximo (max) ou personalizado (custom)."),
            (Two + "&period=1y&from=2026-01-01", "period", "As datas de início e fim valem apenas para o período personalizado (custom)."),
            (Two + "&from=2026-13-01", "from", "A data inicial deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31."),
            (Two + "&to=31/12/2026", "to", "A data final deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31."),
            (Two + "&from=2026-06-07&to=2026-06-07", "from", "A data inicial deve ser anterior à data final."),
            (Two + "&currency=EUR", "currency", "A moeda deve ser a original (original), reais (BRL) ou dólares (USD)."),
            ($"series=benchmark:CDI,asset:{gold.Id}&currency=BRL", "currency",
                "Não é possível converter GOLD de EUR para BRL: só há cotação entre reais e dólares."),
        ];

        foreach (var (query, field, message) in cases)
        {
            using var response = await user.Client.GetAsync("/api/compare?" + query, ct);
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(ct);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{query}: {response.StatusCode}");
            Assert.True(problem!.Errors.TryGetValue(field, out var messages), $"{query}: no error on {field}");
            Assert.Equal([message], messages);
        }

        // In its own currency nothing converts, so a euro series compares.
        using var original = await user.Client.GetAsync($"/api/compare?series=benchmark:CDI,asset:{gold.Id}", ct);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
    }

    private sealed record ProblemBody(Dictionary<string, string[]> Errors);
}
