using System.Net.Http.Json;
using Finance.Api.Domain.Transactions;
using static Finance.Api.Tests.Integration.DashboardFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>005 spec integration tests 13 to 18: the monthly series and the category breakdown.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class DashboardSeriesTests(PostgresFixture postgres)
{
    /// <summary>Spec integration test 13.</summary>
    [Fact]
    public async Task The_series_is_zero_filled_and_oldest_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-series", cancellationToken);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.AddAccountAsync(user.Id, "Itaú");
            var food = await context.AddCategoryAsync(user.Id, "Mercado");
            var salary = await context.AddCategoryAsync(user.Id, "Bolsa", CategoryKind.Income);

            await context.AddTransactionAsync(account, salary, 3000m, ThisMonth);
            await context.AddTransactionAsync(account, food, -45.50m, ThisMonth.AddDays(3));
            await context.AddTransactionAsync(account, food, -10m, ThisMonth.AddMonths(-4));
            await context.AddTransactionAsync(account, salary, 1200.01m, ThisMonth.AddMonths(-11));

            // One month before the window: must not appear, and must not shift it.
            await context.AddTransactionAsync(account, salary, 999m, ThisMonth.AddMonths(-12));
        }

        var monthly = await user.Client.GetFromJsonAsync<List<MonthlyItem>>(
            "/api/dashboard/monthly?months=12", cancellationToken);
        Assert.NotNull(monthly);

        Assert.Equal(
            Enumerable.Range(0, 12).Select(offset => ThisMonth.AddMonths(offset - 11).Key()),
            monthly.Select(month => month.Month));

        Assert.Equal(new MonthlyItem(ThisMonth.AddMonths(-11).Key(), 1200.01m, 0m), monthly[0]);
        Assert.Equal(new MonthlyItem(ThisMonth.AddMonths(-4).Key(), 0m, -10m), monthly[7]);
        Assert.Equal(new MonthlyItem(ThisMonth.Key(), 3000m, -45.50m), monthly[11]);
        Assert.Equal(9, monthly.Count(month => month is { Income: 0m, Expense: 0m }));
    }

    /// <summary>Spec integration test 14.</summary>
    [Fact]
    public async Task The_last_day_of_a_month_belongs_to_that_month()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-month-edge", cancellationToken);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.AddAccountAsync(user.Id, "Itaú");
            var food = await context.AddCategoryAsync(user.Id, "Mercado");

            await context.AddTransactionAsync(account, food, -31m, ThisMonth.AddDays(-1));
            await context.AddTransactionAsync(account, food, -1m, ThisMonth);
        }

        var monthly = await user.Client.GetFromJsonAsync<List<MonthlyItem>>(
            "/api/dashboard/monthly?months=2", cancellationToken);

        Assert.Equal(
            [
                new MonthlyItem(ThisMonth.AddMonths(-1).Key(), 0m, -31m),
                new MonthlyItem(ThisMonth.Key(), 0m, -1m),
            ],
            monthly);

        var previous = await user.Client.GetFromJsonAsync<Summary>(
            $"/api/dashboard/summary?month={ThisMonth.AddMonths(-1).Key()}", cancellationToken);

        Assert.Equal(new MonthItem(0m, -31m, -31m), previous!.Month);
    }

    /// <summary>
    /// Spec integration test 15, and below the floor: fewer than one month is clamped
    /// up to one, the way an oversized transactions page is clamped down.
    /// </summary>
    [Theory]
    [InlineData("100", 36)]
    [InlineData("36", 36)]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData(null, 12)]
    public async Task The_number_of_months_is_clamped(string? months, int expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-clamp", cancellationToken);

        var url = months is null ? "/api/dashboard/monthly" : $"/api/dashboard/monthly?months={months}";
        var monthly = await user.Client.GetFromJsonAsync<List<MonthlyItem>>(url, cancellationToken);

        Assert.Equal(expected, monthly!.Count);
        Assert.Equal(ThisMonth.Key(), monthly[^1].Month);
        Assert.Equal(ThisMonth.AddMonths(1 - expected).Key(), monthly[0].Month);
    }

    /// <summary>Spec integration test 16, and the month parameter picking the month.</summary>
    [Fact]
    public async Task A_child_category_rolls_up_into_its_parent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-rollup", cancellationToken);
        var month = ThisMonth.AddMonths(-1);

        Guid homeId;

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.AddAccountAsync(user.Id, "Itaú");
            var home = await context.AddCategoryAsync(user.Id, "Casa");
            var rent = await context.AddCategoryAsync(user.Id, "Aluguel", parentId: home.Id);

            await context.AddTransactionAsync(account, rent, -2000m, month);
            await context.AddTransactionAsync(account, home, -150.25m, month.AddDays(10));

            // Another month: not part of the breakdown asked for.
            await context.AddTransactionAsync(account, rent, -2000m, ThisMonth);

            homeId = home.Id;
        }

        var rows = await user.Client.GetFromJsonAsync<List<CategoryItem>>(
            $"/api/dashboard/by-category?month={month.Key()}&kind=Expense", cancellationToken);

        Assert.Equal([new CategoryItem(homeId, "Casa", -2150.25m, 1.0000m)], rows);
    }

    /// <summary>Spec integration tests 17 and 18.</summary>
    [Fact]
    public async Task Shares_sum_to_one_and_rows_are_ordered_by_size()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-shares", cancellationToken);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.AddAccountAsync(user.Id, "Itaú");

            // Three equal thirds and a few odd amounts: every share is a repeating
            // decimal, which is where rounding to four places drifts off 1.
            (string Name, decimal Amount)[] spending =
            [
                ("Pequeno", -10m), ("Terço A", -333.33m), ("Grande", -1000m),
                ("Terço B", -333.33m), ("Médio", -77.77m), ("Terço C", -333.33m),
            ];

            foreach (var (name, amount) in spending)
            {
                var category = await context.AddCategoryAsync(user.Id, name);
                await context.AddTransactionAsync(account, category, amount, ThisMonth);
            }
        }

        var rows = await user.Client.GetFromJsonAsync<List<CategoryItem>>(
            "/api/dashboard/by-category", cancellationToken);

        Assert.Equal(6, rows!.Count);
        Assert.InRange(rows.Sum(row => row.Share), 0.9999m, 1.0001m);
        Assert.All(rows, row => Assert.Equal(row.Share, decimal.Round(row.Share, 4)));

        var sizes = rows.Select(row => Math.Abs(row.Amount)).ToList();
        Assert.Equal(sizes.OrderDescending(), sizes);
        Assert.Equal("Grande", rows[0].Name);
        Assert.Equal("Pequeno", rows[^1].Name);
    }
}
