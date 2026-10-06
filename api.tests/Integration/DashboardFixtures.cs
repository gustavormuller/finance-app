using System.Globalization;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Shapes and seeding for 005's dashboard tests. Rows are written straight through
/// <see cref="AppDbContext"/>: the endpoints under test only read, and the rules
/// that guard the writes are 003's and already tested there.
/// </summary>
internal static class DashboardFixtures
{
    /// <summary>
    /// The response shapes, declared by the tests rather than shared with the
    /// endpoints, for the same reason as <see cref="TransactionsFixtures"/>'s.
    /// </summary>
    public sealed record BalanceItem(
        Guid AccountId,
        string Name,
        string Type,
        string Currency,
        decimal Balance,
        bool ExcludedFromTotal);

    public sealed record MonthItem(decimal Income, decimal Expense, decimal Net);

    public sealed record Summary(IReadOnlyList<BalanceItem> Balances, decimal Total, MonthItem Month);

    public sealed record MonthlyItem(string Month, decimal Income, decimal Expense);

    public sealed record CategoryItem(Guid CategoryId, string Name, decimal Amount, decimal Share);

    /// <summary>One month-end point of the net-worth series.</summary>
    public sealed record NetWorthItem(string Month, decimal Accounts, decimal Investments, decimal Total);

    /// <summary>
    /// The first day of the current calendar month, by the same UTC clock the API
    /// defaults its <c>month</c> parameter with.
    /// </summary>
    public static DateOnly ThisMonth
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            return new DateOnly(today.Year, today.Month, 1);
        }
    }

    public static string Key(this DateOnly month) =>
        month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static async Task<Account> AddAccountAsync(
        this AppDbContext context,
        Guid userId,
        string name,
        decimal openingBalance = 0m,
        string currency = Account.DefaultCurrency,
        AccountType type = AccountType.Checking)
    {
        var account = TransactionsFixtures.AnAccount(userId, name);
        account.OpeningBalance = openingBalance;
        account.Currency = currency;
        account.Type = type;

        context.Accounts.Add(account);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return account;
    }

    public static async Task<Category> AddCategoryAsync(
        this AppDbContext context,
        Guid userId,
        string name,
        CategoryKind kind = CategoryKind.Expense,
        Guid? parentId = null)
    {
        var category = TransactionsFixtures.ACategory(userId, name, kind, parentId);

        context.Categories.Add(category);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return category;
    }

    public static async Task AddTransactionAsync(
        this AppDbContext context,
        Account account,
        Category category,
        decimal amount,
        DateOnly date)
    {
        var transaction = TransactionsFixtures.ATransaction(
            account.UserId,
            account.Id,
            category.Id,
            amount,
            date);

        transaction.Money = new Money(amount, account.Currency);

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
