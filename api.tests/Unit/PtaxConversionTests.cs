using Finance.Api.Domain.Compare;
using Finance.Api.Domain.Returns;

namespace Finance.Api.Tests.Unit;

/// <summary>Spec 026 unit test 3: a series in reais or in dollars at the day's PTAX.</summary>
public sealed class PtaxConversionTests
{
    // 2026-09-04 is a Friday; 2026-09-07 is a Monday and a Brazilian holiday, with no PTAX.
    private static readonly DateOnly Fri = new(2026, 9, 4);
    private static readonly DateOnly Mon = Fri.AddDays(3);
    private static readonly DateOnly Tue = Fri.AddDays(4);
    private static readonly DateOnly Wed = Fri.AddDays(5);

    /// <summary>Dollars to reais at the day's PTAX, carried over Monday's holiday.</summary>
    [Fact]
    public void A_usd_series_times_ptax_is_in_reais()
    {
        DailyPoint[] ptax = [new(Fri, 5.0m), new(Tue, 5.2m), new(Wed, 5.5m)];

        var brl = PtaxConversion.UsdToBrl([new(Mon, 10m), new(Tue, 11m), new(Wed, 12m)], ptax);

        // Monday has no PTAX: Friday's 5.0 applies. Friday itself is before the series begins.
        Assert.Equal([new(Mon, 50.0m), new(Tue, 57.2m), new(Wed, 66.0m)], brl);
    }

    [Fact]
    public void A_brl_series_over_ptax_is_in_dollars_and_moves_with_ptax_alone()
    {
        DailyPoint[] ptax = [new(Fri, 5.0m), new(Tue, 5.2m), new(Wed, 5.5m)];

        var usd = PtaxConversion.BrlToUsd([new(Fri, 50m), new(Tue, 52m)], ptax);

        Assert.Equal([Fri, Tue, Wed], usd.Select(point => point.Date));
        Assert.Equal([10m, 10m, 52m / 5.5m], usd.Select(point => point.Value));
    }

    [Fact]
    public void A_converted_series_begins_where_both_have_begun()
    {
        var brl = PtaxConversion.UsdToBrl([new(Fri, 10m), new(Mon, 11m), new(Tue, 12m)], [new(Tue, 5m)]);

        Assert.Equal([new DailyPoint(Tue, 60m)], brl);
    }
}
