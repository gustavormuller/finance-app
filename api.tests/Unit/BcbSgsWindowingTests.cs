using System.Net;
using System.Text;
using System.Web;
using Finance.Api.Application.MarketData;
using Finance.Api.Infrastructure.MarketData;
using static Finance.Api.Tests.Unit.MarketDataProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec 025 test 10: SGS refuses a window over 10 years and can time out on long ones, so a
/// history back to 1994 is asked in windows of at most 5 years and read back as one series.
/// </summary>
public sealed class BcbSgsWindowingTests
{
    [Fact]
    public async Task A_history_back_to_1994_is_asked_in_consecutive_windows_of_at_most_five_years()
    {
        var handler = new WindowHandler();

        var values = await Provider(handler).GetSeriesAsync("CDI", new(1994, 7, 1), new(2026, 10, 5), CancellationToken.None);

        Assert.Equal(
            [("01/07/1994", "30/06/1999"), ("01/07/1999", "30/06/2004"), ("01/07/2004", "30/06/2009"),
             ("01/07/2009", "30/06/2014"), ("01/07/2014", "30/06/2019"), ("01/07/2019", "30/06/2024"),
             ("01/07/2024", "05/10/2026")],
            handler.Windows);
        Assert.Equal(
            [new(1994, 7, 1), new(1999, 7, 1), new(2004, 7, 1), new(2009, 7, 1), new(2014, 7, 1), new(2019, 7, 1),
             new DateOnly(2024, 7, 1)],
            values.Select(value => value.Date));
    }

    [Fact]
    public async Task Five_years_or_less_is_one_request()
    {
        var handler = new WindowHandler();

        await Provider(handler).GetSeriesAsync("CDI", new(2021, 10, 6), new(2026, 10, 5), CancellationToken.None);

        Assert.Equal([("06/10/2021", "05/10/2026")], handler.Windows);
    }

    private static BcbSgsProvider Provider(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options()));

    /// <summary>Answers each request with one value, dated the window's first day, and records the window.</summary>
    private sealed class WindowHandler : HttpMessageHandler
    {
        public List<(string From, string To)> Windows { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var window = (query["dataInicial"]!, query["dataFinal"]!);
            Windows.Add(window);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""[{"data":"{{window.Item1}}","valor":"1"}]""", Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
