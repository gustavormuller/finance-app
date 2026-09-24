using System.Text.RegularExpressions;
using Dapper;
using Finance.Api.Application.Dashboard;
using Finance.Api.Domain.Transactions;
using Npgsql;
using static Finance.Api.Tests.Integration.DashboardFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 005 spec integration test 6, and the Dapper mapping types. The dashboard's SQL runs
/// outside EF Core, so neither the query filter nor the model's column types protect
/// it; these tests do.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed partial class DashboardSqlTests(PostgresFixture postgres)
{
    private const string UserPredicate = "\"UserId\" = @userId";

    /// <summary>
    /// Spec integration test 6, grep-style over the source: a missing clause fails
    /// the build, not production. Every SQL statement in the folder lives in a raw
    /// string literal or a <c>.sql</c> file; a SQL keyword in any other literal is a
    /// failure too, so a statement cannot hide from the check by being spelled
    /// differently.
    /// </summary>
    [Fact]
    public void Every_dashboard_query_carries_the_user_predicate()
    {
        var folder = Path.Combine(RepositoryRoot(), "api", "Application", "Dashboard");
        var statements = new List<(string File, string Sql)>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            if (file.EndsWith(".sql", StringComparison.Ordinal))
            {
                statements.Add((name, source));

                continue;
            }

            if (!file.EndsWith(".cs", StringComparison.Ordinal))
            {
                continue;
            }

            statements.AddRange(RawLiteral().Matches(source).Select(match => (name, match.Value)));

            var withoutRaw = RawLiteral().Replace(source, "");

            Assert.DoesNotMatch(SqlInOrdinaryLiteral(), withoutRaw);
        }

        var sql = statements.Where(statement => SqlKeyword().IsMatch(statement.Sql)).ToList();

        // Balances, the monthly series and the category breakdown, at the least.
        Assert.True(sql.Count >= 3, $"Expected at least three SQL statements, found {sql.Count}.");

        Assert.All(sql, statement => Assert.True(
            statement.Sql.Contains(UserPredicate, StringComparison.Ordinal),
            $"{statement.File}: a statement without {UserPredicate}:\n{statement.Sql}"));
    }

    /// <summary>
    /// Every money column comes back from PostgreSQL as <see cref="decimal"/>, read
    /// untyped so Dapper has no target type to convert a <c>double</c> into.
    /// </summary>
    [Fact]
    public async Task Every_money_column_maps_to_decimal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-types", cancellationToken);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.AddAccountAsync(user.Id, "Itau", openingBalance: 10m);
            var food = await context.AddCategoryAsync(user.Id, "Mercado");

            await context.AddTransactionAsync(account, food, -3.33m, ThisMonth);
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);

        var balances = await QueryAsync(connection, DashboardQueries.BalancesSql, new { userId = user.Id });
        var monthly = await QueryAsync(connection, DashboardQueries.MonthlySql, new
        {
            userId = user.Id,
            fromYear = ThisMonth.Year,
            fromMonth = ThisMonth.Month,
            count = 1,
            currency = Account.DefaultCurrency,
        });
        var byCategory = await QueryAsync(connection, DashboardQueries.ByCategorySql, new
        {
            userId = user.Id,
            year = ThisMonth.Year,
            month = ThisMonth.Month,
            kind = (int)CategoryKind.Expense,
            currency = Account.DefaultCurrency,
        });

        Assert.IsType<decimal>(Assert.Single(balances)["Balance"]);
        Assert.IsType<decimal>(Assert.Single(monthly)["Income"]);
        Assert.IsType<decimal>(monthly[0]["Expense"]);
        Assert.IsType<decimal>(Assert.Single(byCategory)["Amount"]);

        Assert.Equal(6.67m, balances[0]["Balance"]);
        Assert.Equal(-3.33m, byCategory[0]["Amount"]);
    }

    /// <summary>
    /// And the typed side: no <c>double</c> or <c>float</c> anywhere in the shapes
    /// the dashboard hands out.
    /// </summary>
    [Fact]
    public void No_dashboard_shape_carries_a_binary_floating_point_number()
    {
        var shapes = typeof(DashboardQueries).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(DashboardQueries).Namespace);

        var properties = shapes.SelectMany(type => type.GetProperties()).ToList();

        Assert.NotEmpty(properties);
        Assert.DoesNotContain(properties, property =>
            property.PropertyType == typeof(double) || property.PropertyType == typeof(float));
        Assert.Equal(typeof(decimal), typeof(AccountBalance).GetProperty(nameof(AccountBalance.Balance))!.PropertyType);
    }

    private static async Task<List<IDictionary<string, object>>> QueryAsync(
        NpgsqlConnection connection,
        string sql,
        object parameters) =>
        [.. (await connection.QueryAsync(sql, parameters)).Cast<IDictionary<string, object>>()];

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FinanceApp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("FinanceApp.slnx not found above the test binaries.");
    }

    [GeneratedRegex("\"\"\".*?\"\"\"", RegexOptions.Singleline)]
    private static partial Regex RawLiteral();

    [GeneratedRegex(@"\b(SELECT|INSERT|UPDATE|DELETE)\b")]
    private static partial Regex SqlKeyword();

    [GeneratedRegex("\"[^\"\\n]*\\b(SELECT|INSERT|UPDATE|DELETE|FROM|WHERE)\\b[^\"\\n]*\"")]
    private static partial Regex SqlInOrdinaryLiteral();
}
