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
/// wants to break down their spending; a flat handful is enough to file a
/// statement, and subdividing is one click away.
/// </para>
/// </remarks>
public static class DefaultCategories
{
    /// <summary>Where an imported expense lands when nothing in history says otherwise (004).</summary>
    public const string OtherExpenseName = "Outros";

    /// <summary>Where an imported income lands when nothing in history says otherwise (004).</summary>
    public const string OtherIncomeName = "Outras receitas";

    /// <summary>
    /// Money moving between the user's own accounts (005): counted in balances, never
    /// as income or expense.
    /// </summary>
    public const string TransferName = "Transferência";

    public static readonly IReadOnlyList<(string Name, CategoryKind Kind)> All =
    [
        ("Salário", CategoryKind.Income),
        (OtherIncomeName, CategoryKind.Income),
        ("Moradia", CategoryKind.Expense),
        ("Alimentação", CategoryKind.Expense),
        ("Transporte", CategoryKind.Expense),
        ("Saúde", CategoryKind.Expense),
        ("Lazer", CategoryKind.Expense),
        (OtherExpenseName, CategoryKind.Expense),
        (TransferName, CategoryKind.Transfer),
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
