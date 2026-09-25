using Finance.Api.Application.Investments;
using Finance.Api.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using static Finance.Api.Tests.Integration.InvestmentsApi;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// <see cref="SnapshotRebuild"/>, the spec's <c>RebuildSnapshots</c>, against PostgreSQL.
/// Spec 007 integration test 22, and tests 19 and 20 at the service; their HTTP halves are
/// in <c>InvestmentMovementEndpointTests</c>.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SnapshotRebuildTests(PostgresFixture postgres)
{
    private static readonly DateOnly Start = Today.AddDays(-10);

    [Fact]
    public async Task A_rebuild_writes_one_row_a_day_from_the_first_movement_through_today()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("rebuild", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct, "BRL", (Start, 10m), (Start.AddDays(3), 12m));
        var asset = await api.HoldAsync(user.Id, petr4, ct, Buy(Start.AddDays(1), 100m, 10m, 5m));

        await using var context = api.Context(user.Id);
        var written = await Rebuild(context).RebuildAsync(asset.Id, Start, ct);

        var rows = await context.PortfolioDaily.OrderBy(row => row.Date).ToListAsync(ct);
        Assert.Equal(10, written);
        Assert.Equal(Start.AddDays(1), rows[0].Date);
        Assert.Equal(Today, rows[^1].Date);
        Assert.Equal(10, rows.Count);
        Assert.Equal((10m, Start), (rows[0].Price, rows[0].PriceDate));
        Assert.Equal((12m, Start.AddDays(3), 1200m, 1005m), (rows[^1].Price, rows[^1].PriceDate, rows[^1].ValueBrl, rows[^1].CostBasisBrl));
    }

    [Fact]
    public async Task A_rebuild_replaces_the_rows_from_its_date_and_leaves_the_earlier_ones_alone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("rebuild", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct, "BRL", (Start, 10m));
        var asset = await api.HoldAsync(user.Id, petr4, ct, Buy(Start, 100m, 10m));
        await using (var context = api.Context(user.Id))
        {
            await Rebuild(context).RebuildAsync(asset.Id, Start, ct);
            await context.PortfolioDaily.ExecuteUpdateAsync(set => set.SetProperty(row => row.ValueBrl, 1m), ct);
        }

        await using (var context = api.Context(user.Id))
        {
            await Rebuild(context).RebuildAsync(asset.Id, Start.AddDays(5), ct);

            var values = await context.PortfolioDaily.OrderBy(row => row.Date).Select(row => row.ValueBrl).ToListAsync(ct);
            Assert.Equal([1m, 1m, 1m, 1m, 1m, 1000m, 1000m, 1000m, 1000m, 1000m, 1000m], values);
        }
    }

    /// <summary>Spec test 22: the delete and the insert commit together or not at all.</summary>
    [Fact]
    public async Task A_failing_insert_leaves_the_previous_rows_intact()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("rebuild", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct, "BRL", (Start, 10m), (Start.AddDays(5), 9_999_999_999m));
        var asset = await api.HoldAsync(user.Id, petr4, ct, Buy(Start, 1m, 10m));
        await using (var context = api.Context(user.Id))
        {
            await Rebuild(context).RebuildAsync(asset.Id, Start, ct);
        }

        List<PortfolioDaily> before;
        await using (var context = api.Context(user.Id))
        {
            before = await context.PortfolioDaily.AsNoTracking().OrderBy(row => row.Date).ToListAsync(ct);

            // Now worth about 1e20 BRL from day 5, past numeric(18,2): that day's insert fails.
            await context.Movements.ExecuteUpdateAsync(set => set.SetProperty(movement => movement.Quantity, 9_999_999_999m), ct);
            await Assert.ThrowsAsync<DbUpdateException>(() => Rebuild(context).RebuildAsync(asset.Id, Start, ct));
        }

        await using (var context = api.Context(user.Id))
        {
            var after = await context.PortfolioDaily.AsNoTracking().OrderBy(row => row.Date).ToListAsync(ct);
            Assert.Equal(11, after.Count);
            Assert.Equal(
                before.Select(row => (row.Date, row.Quantity, row.ValueBrl)),
                after.Select(row => (row.Date, row.Quantity, row.ValueBrl)));
        }
    }

    [Fact]
    public async Task Rebuilding_an_asset_with_no_movements_left_removes_its_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("rebuild", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct, "BRL", (Start, 10m));
        var asset = await api.HoldAsync(user.Id, petr4, ct, Buy(Start, 1m, 10m));
        await using var context = api.Context(user.Id);
        await Rebuild(context).RebuildAsync(asset.Id, Start, ct);

        await context.Movements.ExecuteDeleteAsync(ct);
        var written = await Rebuild(context).RebuildAsync(asset.Id, Start, ct);

        Assert.Equal(0, written);
        Assert.Empty(await context.PortfolioDaily.ToListAsync(ct));
    }

    private static SnapshotRebuild Rebuild(Finance.Api.Infrastructure.AppDbContext context) => new(context, TimeProvider.System);
}
