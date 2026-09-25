using Finance.Api.Application;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Endpoints;

public static class CategoryEndpoints
{
    private sealed record CategoryRequest(string Name, CategoryKind Kind, Guid? ParentId);

    private sealed record CategoryResponse(
        Guid Id,
        string Name,
        CategoryKind Kind,
        Guid? ParentId,
        DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder routes)
    {
        var categories = routes.MapGroup("/api/categories").RequireAuthorization();

        // Flat, with the hierarchy carried by ParentId. Two levels do not need a tree
        // in the payload, and the screen groups them itself.
        categories.MapGet("/", async (AppDbContext database, CancellationToken cancellationToken) =>
            Results.Ok(await database.Categories
                .OrderBy(category => category.Name)
                .Select(category => new CategoryResponse(
                    category.Id,
                    category.Name,
                    category.Kind,
                    category.ParentId,
                    category.CreatedAt))
                .ToListAsync(cancellationToken)));

        categories.MapPost("/", async (
            CategoryRequest request,
            AppDbContext database,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            if (await ValidateAsync(request, editing: null, database, cancellationToken) is { } invalid)
            {
                return invalid;
            }

            var category = new Category
            {
                UserId = currentUser.Id!.Value,
                Name = request.Name.Trim(),
                Kind = request.Kind,
                ParentId = request.ParentId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            database.Categories.Add(category);

            if (!await database.TrySaveAsync(cancellationToken))
            {
                return Problems.Conflict($"Já existe uma categoria chamada '{category.Name}' neste nível.");
            }

            return Results.Created($"/api/categories/{category.Id}", Describe(category));
        });

        categories.MapPut("/{id:guid}", async (
            Guid id,
            CategoryRequest request,
            AppDbContext database,
            CancellationToken cancellationToken) =>
        {
            var category = await database.Categories
                .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

            if (category is null)
            {
                return Results.NotFound();
            }

            if (await ValidateAsync(request, id, database, cancellationToken) is { } invalid)
            {
                return invalid;
            }

            category.Name = request.Name.Trim();
            category.Kind = request.Kind;
            category.ParentId = request.ParentId;

            if (!await database.TrySaveAsync(cancellationToken))
            {
                return Problems.Conflict($"Já existe uma categoria chamada '{category.Name}' neste nível.");
            }

            return Results.Ok(Describe(category));
        });

        categories.MapDelete("/{id:guid}", async (
            Guid id,
            AppDbContext database,
            CancellationToken cancellationToken) =>
        {
            var category = await database.Categories
                .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

            if (category is null)
            {
                return Results.NotFound();
            }

            var children = await database.Categories
                .CountAsync(entity => entity.ParentId == id, cancellationToken);

            if (children > 0)
            {
                return Problems.Conflict(
                    $"'{category.Name}' ainda tem {children} subcategoria(s). Exclua-as antes.");
            }

            var referencing = await database.Transactions
                .CountAsync(transaction => transaction.CategoryId == id, cancellationToken);

            if (referencing > 0)
            {
                return Problems.Conflict(
                    $"'{category.Name}' ainda tem {referencing} lançamento(s). "
                    + "Recategorize-os ou exclua-os antes.");
            }

            database.Categories.Remove(category);
            await database.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        });

        // How much each category was used in a range, by default the 12 months
        // ending today. Only categories with a transaction in the range appear; the
        // screen rolls subcategories up into their main category itself.
        categories.MapGet("/usage", async (
            AppDbContext database,
            CancellationToken cancellationToken,
            string? from = null,
            string? to = null) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var (start, end, problem) = ParseRange(from, to, today);

            if (problem is not null)
            {
                return problem;
            }

            return Results.Ok(await database.Transactions
                .Where(transaction => transaction.Date >= start && transaction.Date <= end)
                .GroupBy(transaction => transaction.CategoryId)
                .Select(group => new UsageResponse(group.Key, group.Count(), group.Sum(transaction => transaction.Money.Amount)))
                .ToListAsync(cancellationToken));
        });

        return routes;
    }

    private sealed record UsageResponse(Guid CategoryId, int Count, decimal Total);

    private static (DateOnly From, DateOnly To, IResult? Problem) ParseRange(string? from, string? to, DateOnly today)
    {
        static DateOnly? Parse(string? text) =>
            DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
                ? date
                : null;

        var end = to is null ? today : Parse(to);
        if (end is null)
        {
            return (default, default, Problems.Validation("to", "Data inválida."));
        }

        var start = from is null ? end.Value.AddMonths(-12).AddDays(1) : Parse(from);
        if (start is null)
        {
            return (default, default, Problems.Validation("from", "Data inválida."));
        }

        return start > end
            ? (default, default, Problems.Validation("from", "A data inicial deve ser anterior ou igual à data final."))
            : (start.Value, end.Value, null);
    }

    /// <summary>
    /// The whole of spec rule 6 and integration tests 10 and 11: exactly two levels,
    /// and a child agreeing with its parent about what kind of money it is.
    /// </summary>
    /// <param name="editing">
    /// The category being edited, or null when creating. An edit has two extra ways to
    /// break the tree that a create does not: adopting itself, and acquiring a parent
    /// while already having children.
    /// </param>
    private static async Task<IResult?> ValidateAsync(
        CategoryRequest request,
        Guid? editing,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Problems.Validation("name", "O nome é obrigatório.");
        }

        if (request.ParentId is not { } parentId)
        {
            return null;
        }

        if (parentId == editing)
        {
            return Problems.Validation("parentId", "Uma categoria não pode ser mãe de si mesma.");
        }

        // Through the query filter: another user's category is not a parent, it is a
        // field that does not resolve.
        var parent = await database.Categories
            .SingleOrDefaultAsync(entity => entity.Id == parentId, cancellationToken);

        if (parent is null)
        {
            return Problems.Validation("parentId", "Categoria não encontrada.");
        }

        if (parent.ParentId is not null)
        {
            return Problems.Validation(
                "parentId",
                "As categorias têm no máximo dois níveis, e essa já é uma subcategoria.");
        }

        if (parent.Kind != request.Kind)
        {
            return Problems.Validation(
                "kind",
                $"Uma subcategoria de '{parent.Name}' precisa ser do mesmo tipo que ela.");
        }

        if (editing is { } id
            && await database.Categories.AnyAsync(entity => entity.ParentId == id, cancellationToken))
        {
            return Problems.Validation(
                "parentId",
                "Essa categoria tem subcategorias, então não pode virar subcategoria.");
        }

        return null;
    }

    private static CategoryResponse Describe(Category category) =>
        new(category.Id, category.Name, category.Kind, category.ParentId, category.CreatedAt);
}
