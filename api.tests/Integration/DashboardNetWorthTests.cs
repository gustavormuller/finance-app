using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Finance.Api.Application.Dashboard;
using Finance.Api.Domain.Transactions;
using Npgsql;
using static Finance.Api.Tests.Integration.DashboardFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 014 spec integration tests 1 to 8: <c>GET /api/dashboard/net-worth</c>, the month-end
/// series of accounts plus investments. Portfolio rows are written straight to the
/// database, as 008's tests write them, so each value is one the test chose.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DashboardNetWorthTests(PostgresFixture postgres)
{
    private const string MonthsMessage = "O número de meses deve ser um número inteiro entre 1 e 120.";

    /// <summary>
    /// Spec integration tests 1 and 2: a transaction on a month's last day is in that
    /// month's point, one on the next month's first day is not, and the series opens at
    /// the first month with a transaction rather than 24 months back.
    /// </summary>
    [Fact]
    public async Task Each_point_is_the_balance_at_its_months_end()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("net-worth-month-end", cancellationToken);
        var first = ThisMonth.AddMonths(-3);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.AddAccountAsync(user.Id, "Itaú", openingBalance: 1000m);
            var food = await context.AddCategoryAsync(user.Id, "Mercado");
            var refund = await context.AddCategoryAsync(user.Id, "Reembolso", CategoryKind.Income);

            await context.AddTransactionAsync(account, food, -200m, first.AddMonths(1).AddDays(-1));
            await context.AddTransactionAsync(account, refund, 50m, first.AddMonths(1));
            await context.AddTransactionAsync(account, food, -30.45m, ThisMonth);
        }

        var series = await user.Client.GetFromJsonAsync<List<NetWorthItem>>(
            "/api/dashboard/net-worth", cancellationToken);

        Assert.Equal(
            [
                new NetWorthItem(first.Key(), 800m, 0m, 800m),
                new NetWorthItem(first.AddMonths(1).Key(), 850m, 0m, 850m),
                new NetWorthItem(first.AddMonths(2).Key(), 850m, 0m, 850m),
                new NetWorthItem(ThisMonth.Key(), 819.55m, 0m, 819.55m),
            ],
            series);
    }

    /// <summary>
    /// Spec integration test 3: each asset at its latest row on or before the month's
    /// end, one that stops early at its last value, and the last point agreeing with
    /// the two figures the rest of the app already shows.
    /// </summary>
    [Fact]
    public async Task Investments_are_each_assets_latest_row_on_or_before_the_months_end()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var api = await InvestmentsApi.StartAsync(postgres, cancellationToken);
        var user = await api.SignInAsync("net-worth-invested", cancellationToken);
        var (older, previous) = (ThisMonth.AddMonths(-2), ThisMonth.AddMonths(-1));

        var steady = await api.HoldAsync(user.Id, await api.CatalogueAsync("PETR4", cancellationToken), cancellationToken);
        var stopped = await api.HoldAsync(user.Id, await api.CatalogueAsync("VALE3", cancellationToken), cancellationToken);

        await api.DailyAsync(
            steady,
            cancellationToken,
            (older.AddDays(4), 10m, 10m, 1m, 100m),
            (older.AddDays(19), 11m, 10m, 1m, 110m),
            // The day after the older month's end: in the next point, not this one.
            (previous, 12m, 10m, 1m, 120m),
            (ThisMonth, 13m, 10m, 1m, 130.25m));

        // Rows that stop two months back still count at their last value.
        await api.DailyAsync(stopped, cancellationToken, (older.AddDays(9), 4m, 10m, 1m, 40m));

        await using (var context = api.Context(user.Id))
        {
            await context.AddAccountAsync(user.Id, "Itaú", openingBalance: 500m);
        }

        var series = await user.Client.GetFromJsonAsync<List<NetWorthItem>>(
            "/api/dashboard/net-worth", cancellationToken);

        Assert.Equal(
            [
                new NetWorthItem(older.Key(), 500m, 150m, 650m),
                new NetWorthItem(previous.Key(), 500m, 160m, 660m),
                new NetWorthItem(ThisMonth.Key(), 500m, 170.25m, 670.25m),
            ],
            series);

        var summary = await user.Client.GetFromJsonAsync<Summary>("/api/dashboard/summary", cancellationToken);
        var invested = await user.Client.GetFromJsonAsync<JsonElement>("/api/investments/summary", cancellationToken);

        Assert.Equal(summary!.Total, series![^1].Accounts);
        Assert.Equal(invested.GetProperty("totalBrl").GetDecimal(), series[^1].Investments);
    }

    /// <summary>
    /// Spec 018 test 2: with no transactions, the series opens at the month of the caller's
    /// earliest daily row across assets, whichever asset has it; another user's earlier row
    /// does not move it.
    /// </summary>
    [Fact]
    public async Task The_series_opens_at_the_callers_earliest_daily_row_across_assets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var api = await InvestmentsApi.StartAsync(postgres, cancellationToken);
        var user = await api.SignInAsync("net-worth-first-row", cancellationToken);
        var other = await api.SignInAsync("net-worth-first-row-other", cancellationToken);
        var petr4 = await api.CatalogueAsync("PETR4", cancellationToken);

        var later = await api.HoldAsync(user.Id, petr4, cancellationToken);
        var earlier = await api.HoldAsync(user.Id, await api.CatalogueAsync("VALE3", cancellationToken), cancellationToken);
        var theirs = await api.HoldAsync(other.Id, petr4, cancellationToken);
        await api.DailyAsync(later, cancellationToken, (ThisMonth.AddMonths(-1), 1m, 10m, 1m, 10m));
        await api.DailyAsync(earlier, cancellationToken, (ThisMonth.AddMonths(-2).AddDays(3), 1m, 20m, 1m, 20m));
        await api.DailyAsync(theirs, cancellationToken, (ThisMonth.AddMonths(-6), 1m, 99m, 1m, 99m));

        var series = await user.Client.GetFromJsonAsync<List<NetWorthItem>>(
            "/api/dashboard/net-worth", cancellationToken);

        Assert.Equal(
            [
                new NetWorthItem(ThisMonth.AddMonths(-2).Key(), 0m, 20m, 20m),
                new NetWorthItem(ThisMonth.AddMonths(-1).Key(), 0m, 30m, 30m),
                new NetWorthItem(ThisMonth.Key(), 0m, 30m, 30m),
            ],
            series);
    }

    /// <summary>Spec integration test 4.</summary>
    [Fact]
    public async Task A_foreign_currency_account_is_left_out()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("net-worth-currency", cancellationToken);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var reais = await context.AddAccountAsync(user.Id, "Itaú", openingBalance: 100m);
            var dollars = await context.AddAccountAsync(user.Id, "Wise", openingBalance: 40m, currency: "USD");
            var food = await context.AddCategoryAsync(user.Id, "Mercado");

            // Older than anything in reais: it must not open the series either.
            await context.AddTransactionAsync(dollars, food, -10m, ThisMonth.AddMonths(-5));
            await context.AddTransactionAsync(reais, food, -25m, ThisMonth.AddMonths(-1));
        }

        var series = await user.Client.GetFromJsonAsync<List<NetWorthItem>>(
            "/api/dashboard/net-worth", cancellationToken);

        Assert.Equal(
            [
                new NetWorthItem(ThisMonth.AddMonths(-1).Key(), 75m, 0m, 75m),
                new NetWorthItem(ThisMonth.Key(), 75m, 0m, 75m),
            ],
            series);
    }

    /// <summary>
    /// Spec integration test 5. Dapper bypasses the query filter, so this is the test
    /// that notices a table read without its <c>UserId</c> predicate.
    /// </summary>
    [Fact]
    public async Task Another_users_data_never_reaches_the_series()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var api = await InvestmentsApi.StartAsync(postgres, cancellationToken);
        var alice = await api.SignInAsync("net-worth-alice", cancellationToken);
        var bob = await api.SignInAsync("net-worth-bob", cancellationToken);

        var held = await api.HoldAsync(alice.Id, await api.CatalogueAsync("ITSA4", cancellationToken), cancellationToken);
        await api.DailyAsync(held, cancellationToken, (ThisMonth.AddMonths(-3), 1m, 900m, 1m, 900m));

        await using (var context = api.Context(alice.Id))
        {
            var account = await context.AddAccountAsync(alice.Id, "Nubank", openingBalance: 5000m);
            var food = await context.AddCategoryAsync(alice.Id, "Mercado");

            await context.AddTransactionAsync(account, food, -120.35m, ThisMonth.AddMonths(-6));
        }

        Assert.Empty((await bob.Client.GetFromJsonAsync<List<NetWorthItem>>(
            "/api/dashboard/net-worth", cancellationToken))!);

        await using (var context = api.Context(bob.Id))
        {
            await context.AddAccountAsync(bob.Id, "Inter", openingBalance: 250m);
        }

        // Only an opening balance: undated, so the current month alone (decision 4).
        Assert.Equal(
            [new NetWorthItem(ThisMonth.Key(), 250m, 0m, 250m)],
            await bob.Client.GetFromJsonAsync<List<NetWorthItem>>("/api/dashboard/net-worth", cancellationToken));
    }

    /// <summary>Spec integration test 6.</summary>
    [Theory]
    [InlineData(null, 24)]
    [InlineData("1", 1)]
    [InlineData("24", 24)]
    [InlineData("120", 31)]
    public async Task The_window_ends_this_month_and_keeps_older_history_in_its_balance(string? months, int expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("net-worth-window", cancellationToken);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.AddAccountAsync(user.Id, "Itaú");
            var food = await context.AddCategoryAsync(user.Id, "Mercado");
            var salary = await context.AddCategoryAsync(user.Id, "Bolsa", CategoryKind.Income);

            await context.AddTransactionAsync(account, salary, 1000m, ThisMonth.AddMonths(-30));
            await context.AddTransactionAsync(account, food, -1m, ThisMonth.AddMonths(-10));
        }

        var url = months is null ? "/api/dashboard/net-worth" : $"/api/dashboard/net-worth?months={months}";
        var series = await user.Client.GetFromJsonAsync<List<NetWorthItem>>(url, cancellationToken);

        Assert.Equal(expected, series!.Count);
        Assert.Equal(
            Enumerable.Range(0, expected).Select(offset => ThisMonth.AddMonths(offset + 1 - expected).Key()),
            series.Select(point => point.Month));
        Assert.Equal(new NetWorthItem(ThisMonth.Key(), 999m, 0m, 999m), series[^1]);

        // Before the window or not, the first point carries the older salary.
        Assert.Equal(expected > 10 ? 1000m : 999m, series[0].Accounts);
    }

    /// <summary>Spec integration test 7.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("121")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("12.5")]
    public async Task Months_outside_one_to_120_is_a_400_in_portuguese(string months)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("net-worth-invalid", cancellationToken);

        using var response = await user.Client.GetAsync($"/api/dashboard/net-worth?months={months}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(MonthsMessage, document.RootElement.GetProperty("errors").GetProperty("months")[0].GetString());
    }

    [Fact]
    public async Task The_series_requires_a_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/api/dashboard/net-worth", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Spec integration test 8: every money column comes back from PostgreSQL as
    /// <see cref="decimal"/>, read untyped so Dapper has no target type to convert into.
    /// </summary>
    [Fact]
    public async Task Every_money_column_maps_to_decimal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("net-worth-types", cancellationToken);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.AddAccountAsync(user.Id, "Itau", openingBalance: 10m);
            var food = await context.AddCategoryAsync(user.Id, "Mercado");

            await context.AddTransactionAsync(account, food, -3.33m, ThisMonth);
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);

        var rows = (await connection.QueryAsync(DashboardQueries.NetWorthSql, new
            {
                userId = user.Id,
                currency = Account.DefaultCurrency,
                fromYear = ThisMonth.Year,
                fromMonth = ThisMonth.Month,
                toYear = ThisMonth.Year,
                toMonth = ThisMonth.Month,
            }))
            .Cast<IDictionary<string, object>>()
            .ToList();

        var row = Assert.Single(rows);
        Assert.IsType<decimal>(row["Accounts"]);
        Assert.IsType<decimal>(row["Investments"]);
        Assert.IsType<decimal>(row["Total"]);
        Assert.Equal(6.67m, row["Total"]);
    }
}
