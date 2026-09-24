using Finance.Api.Application;
using Finance.Api.Application.Ai;
using Finance.Api.Application.Dashboard;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 009 spec test 11's database half (what reaches the document is aggregates and merchant
/// names, never a raw description) and test 13's application half (one user's input holds
/// nothing of another's). Loaded in a scope acting for the user, as the job loads it.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AnalysisInputQueriesTests(PostgresFixture postgres)
{
    private const string Month = "2026-05";

    [Fact]
    public async Task Three_months_of_totals_and_the_months_brl_merchants_by_normalized_name_as_the_user()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var a = await SeedAsync(factory, "input-a", "Nubank", ct);
        await SeedAsync(factory, "input-b", "Conta do B", ct, merchant: "LOJA DO B 77");

        var input = await AggregatesAsync(factory, a, ct);

        Assert.Equal(
            [new MonthTotals("2026-03", 0m, -100.00m), new MonthTotals("2026-04", 0m, -200.00m), new MonthTotals("2026-05", 5000.00m, -310.00m)],
            input.Months);
        Assert.Equal(
            [("2026-03", "Alimentação", -100.00m), ("2026-04", "Alimentação", -200.00m), ("2026-05", "Alimentação", -230.00m),
             ("2026-05", "Lazer", -80.00m), ("2026-05", "Salário", 5000.00m)],
            input.Categories.Select(category => (category.Month, category.Name, category.Amount)).OrderBy(entry => entry.Month).ThenBy(entry => entry.Name));
        Assert.Equal(
            [
                new AnalysisMerchant(DescriptionNormalizer.Normalize("PAG*IFOOD 12/05"), 200.00m, 2),
                new AnalysisMerchant(DescriptionNormalizer.Normalize("CINEMARK 1234"), 80.00m, 1),
                new AnalysisMerchant(DescriptionNormalizer.Normalize("Padaria do Zé 99"), 30.00m, 1),
            ],
            input.Merchants.OrderByDescending(merchant => merchant.Spent));
        Assert.Equal(["Nubank", "Wise"], input.Accounts.Select(account => account.Name));
        Assert.Equal(1000.00m - 100.00m - 200.00m - 310.00m + 5000.00m - 300.00m, input.BalanceTotalBrl);
        Assert.Equal(0m, input.Investments.TotalBrl);
    }

    [Fact]
    public async Task The_document_holds_no_raw_description_no_income_payer_and_nothing_of_another_user()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var a = await SeedAsync(factory, "input-doc-a", "Nubank", ct);
        var b = await SeedAsync(factory, "input-doc-b", "Conta do B", ct, merchant: "LOJA DO B 77");

        var json = AnalysisInputBuilder.Build(await AggregatesAsync(factory, a, ct));

        foreach (var leak in new[] { "PAG*IFOOD", "12/05", "1234", "Padaria do Zé", "PIX", "MARIA", "123.456", "AMAZON", "LOJA DO B", "Conta do B" })
        {
            Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("CINEMARK", json, StringComparison.Ordinal);
        Assert.Contains("LOJA DO B", AnalysisInputBuilder.Build(await AggregatesAsync(factory, b, ct)), StringComparison.Ordinal);
    }

    private static async Task<AnalysisAggregates> AggregatesAsync(IdentityApiFactory factory, Guid user, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ActingUser>().ActAs(user);
        return await scope.ServiceProvider.GetRequiredService<AnalysisInputQueries>().AggregatesAsync(Month, ct);
    }

    /// <summary>
    /// A user with a BRL account (opening 1000), a USD one, three months of food, one
    /// month's leisure and salary, rows before and after, and descriptions that must not leak.
    /// </summary>
    private async Task<Guid> SeedAsync(IdentityApiFactory factory, string prefix, string account, CancellationToken ct, string? merchant = null)
    {
        var user = (await factory.SignInNewUserAsync(prefix, ct)).Id;
        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user);
        var categories = await context.Categories.ToDictionaryAsync(category => category.Name, ct);
        var brl = await context.AddAccountAsync(user, account, openingBalance: 1000.00m);
        var usd = await context.AddAccountAsync(user, "Wise", currency: "USD");

        Add(context, brl, categories["Alimentação"], -100.00m, new DateOnly(2026, 3, 3), "PAG*IFOOD 03/03");
        Add(context, brl, categories["Alimentação"], -200.00m, new DateOnly(2026, 4, 3), "PAG*IFOOD 03/04");
        Add(context, brl, categories["Alimentação"], -150.00m, new DateOnly(2026, 5, 12), merchant ?? "PAG*IFOOD 12/05");
        Add(context, brl, categories["Alimentação"], -50.00m, new DateOnly(2026, 5, 20), "PAG*IFOOD 20/05");
        Add(context, brl, categories["Alimentação"], -30.00m, new DateOnly(2026, 5, 21), "Padaria do Zé 99", normalized: false);
        Add(context, brl, categories["Lazer"], -80.00m, new DateOnly(2026, 5, 22), "CINEMARK 1234");
        Add(context, brl, categories["Salário"], 5000.00m, new DateOnly(2026, 5, 5), "PIX RECEBIDO MARIA 123.456.789-00");
        Add(context, brl, categories["Alimentação"], -300.00m, new DateOnly(2026, 6, 1), "PAG*IFOOD 01/06");
        Add(context, usd, categories["Alimentação"], -50.00m, new DateOnly(2026, 5, 10), "AMAZON US");
        await context.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>An imported row carries its normalized description; a hand-entered one has none.</summary>
    private static void Add(AppDbContext context, Account account, Category category, decimal amount, DateOnly date, string description, bool normalized = true)
    {
        var row = TransactionsFixtures.ATransaction(account.UserId, account.Id, category.Id, amount, date);
        row.Money = new Money(amount, account.Currency);
        row.Description = description;
        row.NormalizedDescription = normalized ? DescriptionNormalizer.Normalize(description) : null;
        context.Transactions.Add(row);
    }
}
