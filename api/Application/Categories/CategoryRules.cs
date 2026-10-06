using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.Categories;

/// <summary>
/// The category rules that need a query to decide: where a category may sit in the tree
/// (spec rule 6 and integration tests 10 and 11: exactly two levels, and a child agreeing
/// with its parent about what kind of money it is), and what keeps one from being deleted.
/// </summary>
public sealed class CategoryRules(AppDbContext database)
{
    /// <summary>The placement's first broken rule, or null when the category may sit there.</summary>
    /// <param name="editing">
    /// The category being edited, or null when creating. An edit has two extra ways to
    /// break the tree that a create does not: adopting itself, and acquiring a parent
    /// while already having children.
    /// </param>
    public async Task<RuleViolation?> ValidatePlacementAsync(
        CategoryKind kind,
        Guid? parentId,
        Guid? editing,
        CancellationToken cancellationToken)
    {
        if (parentId is not { } id)
        {
            return null;
        }

        if (id == editing)
        {
            return new RuleViolation("parentId", "Uma categoria não pode ser mãe de si mesma.");
        }

        // Through the query filter: another user's category is not a parent, it is a
        // field that does not resolve.
        var parent = await database.Categories.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (parent is null)
        {
            return new RuleViolation("parentId", "Categoria não encontrada.");
        }

        if (parent.ParentId is not null)
        {
            return new RuleViolation("parentId", "As categorias têm no máximo dois níveis, e essa já é uma subcategoria.");
        }

        if (parent.Kind != kind)
        {
            return new RuleViolation("kind", $"Uma subcategoria de '{parent.Name}' precisa ser do mesmo tipo que ela.");
        }

        if (editing is { } edited && await database.Categories.AnyAsync(entity => entity.ParentId == edited, cancellationToken))
        {
            return new RuleViolation("parentId", "Essa categoria tem subcategorias, então não pode virar subcategoria.");
        }

        return null;
    }

    /// <summary>Why the category cannot be deleted yet, or null when nothing is behind it.</summary>
    public async Task<string?> DeleteRefusalAsync(Category category, CancellationToken cancellationToken)
    {
        var children = await database.Categories.CountAsync(entity => entity.ParentId == category.Id, cancellationToken);

        if (children > 0)
        {
            return $"'{category.Name}' ainda tem {children} subcategoria(s). Exclua-as antes.";
        }

        var referencing = await database.Transactions
            .CountAsync(transaction => transaction.CategoryId == category.Id, cancellationToken);

        return referencing > 0
            ? $"'{category.Name}' ainda tem {referencing} lançamento(s). Recategorize-os ou exclua-os antes."
            : null;
    }
}
