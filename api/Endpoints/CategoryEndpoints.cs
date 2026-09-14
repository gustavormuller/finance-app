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

            if (!await TrySaveAsync(database, cancellationToken))
            {
                return Problems.Conflict($"A category called '{category.Name}' already exists here.");
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

            if (!await TrySaveAsync(database, cancellationToken))
            {
                return Problems.Conflict($"A category called '{category.Name}' already exists here.");
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
                    $"'{category.Name}' still has {children} child categor(y/ies). Delete them first.");
            }

            var referencing = await database.Transactions
                .CountAsync(transaction => transaction.CategoryId == id, cancellationToken);

            if (referencing > 0)
            {
                return Problems.Conflict(
                    $"'{category.Name}' still has {referencing} transaction(s). "
                    + "Recategorise or delete them first.");
            }

            database.Categories.Remove(category);
            await database.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        });

        return routes;
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
            return Problems.Validation("name", "Name is required.");
        }

        if (request.ParentId is not { } parentId)
        {
            return null;
        }

        if (parentId == editing)
        {
            return Problems.Validation("parentId", "A category cannot be its own parent.");
        }

        // Through the query filter: another user's category is not a parent, it is a
        // field that does not resolve.
        var parent = await database.Categories
            .SingleOrDefaultAsync(entity => entity.Id == parentId, cancellationToken);

        if (parent is null)
        {
            return Problems.Validation("parentId", "No such category.");
        }

        if (parent.ParentId is not null)
        {
            return Problems.Validation(
                "parentId",
                "Categories are two levels deep. That category is already a child.");
        }

        if (parent.Kind != request.Kind)
        {
            return Problems.Validation(
                "kind",
                $"A child of '{parent.Name}' must be {parent.Kind}, not {request.Kind}.");
        }

        if (editing is { } id
            && await database.Categories.AnyAsync(entity => entity.ParentId == id, cancellationToken))
        {
            return Problems.Validation(
                "parentId",
                "That category has children of its own, so it cannot become a child.");
        }

        return null;
    }

    private static async Task<bool> TrySaveAsync(AppDbContext database, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException exception) when (exception.IsDuplicate())
        {
            return false;
        }
    }

    private static CategoryResponse Describe(Category category) =>
        new(category.Id, category.Name, category.Kind, category.ParentId, category.CreatedAt);
}
