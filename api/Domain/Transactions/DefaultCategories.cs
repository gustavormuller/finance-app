namespace Finance.Api.Domain.Transactions;

/// <summary>
/// The categories every new account starts with.
/// </summary>
/// <remarks>
/// Without them the transactions screen opens with an empty category dropdown and no
/// way to file anything, so they are written in the same transaction that creates the
/// user rather than offered as a later "set up your categories" step.
/// <para>
/// All top level. A starter set with a hierarchy would be guessing at how someone
/// wants to break down their spending; eight flat ones are enough to file a
/// statement, and subdividing is one click away.
/// </para>
/// </remarks>
public static class DefaultCategories
{
    public static readonly IReadOnlyList<(string Name, CategoryKind Kind)> All =
    [
        ("Salary", CategoryKind.Income),
        ("Other income", CategoryKind.Income),
        ("Housing", CategoryKind.Expense),
        ("Food", CategoryKind.Expense),
        ("Transport", CategoryKind.Expense),
        ("Health", CategoryKind.Expense),
        ("Leisure", CategoryKind.Expense),
        ("Other", CategoryKind.Expense),
    ];

    public static IEnumerable<Category> For(Guid userId, DateTimeOffset createdAt) =>
        All.Select(category => new Category
        {
            UserId = userId,
            Name = category.Name,
            Kind = category.Kind,
            ParentId = null,
            CreatedAt = createdAt,
        });
}
