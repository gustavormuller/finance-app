using Finance.Api.Application;
using Finance.Api.Application.Categories;
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
            CategoryRules rules,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            if (await ValidateAsync(request, editing: null, rules, cancellationToken) is { } invalid)
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
            CategoryRules rules,
            CancellationToken cancellationToken) =>
        {
            var category = await database.Categories
                .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

            if (category is null)
            {
                return Results.NotFound();
            }

            if (await ValidateAsync(request, id, rules, cancellationToken) is { } invalid)
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
            CategoryRules rules,
            CancellationToken cancellationToken) =>
        {
            var category = await database.Categories
                .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

            if (category is null)
            {
                return Results.NotFound();
            }

            if (await rules.DeleteRefusalAsync(category, cancellationToken) is { } refusal)
            {
                return Problems.Conflict(refusal);
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

    /// <summary>The name first, then the tree rules <see cref="CategoryRules"/> owns.</summary>
    /// <param name="editing">The category being edited, or null when creating.</param>
    private static async Task<IResult?> ValidateAsync(
        CategoryRequest request,
        Guid? editing,
        CategoryRules rules,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Problems.Validation("name", "O nome é obrigatório.");
        }

        return Problems.Validation(
            await rules.ValidatePlacementAsync(request.Kind, request.ParentId, editing, cancellationToken));
    }

    private static CategoryResponse Describe(Category category) =>
        new(category.Id, category.Name, category.Kind, category.ParentId, category.CreatedAt);
}
