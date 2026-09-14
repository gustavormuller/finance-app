namespace Finance.Api.Domain.Transactions;

/// <summary>
/// What a transaction was for. Exactly two levels: a category is either a parent or a
/// child of one, never a grandchild.
/// </summary>
/// <remarks>
/// No navigation properties. The two questions the code actually asks — "does this
/// one have children?" and "what kind is the parent?" — are single queries, and
/// leaving the graph out keeps the entity anaemic and the query filter's reach
/// obvious (ADR-014).
/// </remarks>
public sealed class Category : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>
    /// Income or expense. A child's kind must equal its parent's, and the sign of
    /// every transaction filed under it must agree with it.
    /// </summary>
    public CategoryKind Kind { get; set; }

    /// <summary>Null for a top-level category.</summary>
    public Guid? ParentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
