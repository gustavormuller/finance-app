using Finance.Api.Application;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Endpoints;

public static class CsvTemplateEndpoints
{
    private const int NameLength = 100;

    private sealed record TemplateRequest(
        string Name,
        string Delimiter,
        bool HasHeader,
        string Culture,
        string DateFormat,
        SignMode SignMode,
        string DateColumn,
        string? AmountColumn,
        string? DebitColumn,
        string? CreditColumn,
        string DescriptionColumns);

    private sealed record TemplateResponse(
        Guid Id,
        string Name,
        string Delimiter,
        bool HasHeader,
        string Culture,
        string DateFormat,
        SignMode SignMode,
        string DateColumn,
        string? AmountColumn,
        string? DebitColumn,
        string? CreditColumn,
        string DescriptionColumns,
        DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapCsvTemplateEndpoints(this IEndpointRouteBuilder routes)
    {
        var templates = routes.MapGroup("/api/csv-templates").RequireAuthorization();

        templates.MapGet("/", async (AppDbContext database, CancellationToken cancellationToken) =>
            Results.Ok((await database.CsvTemplates
                    .OrderBy(template => template.Name)
                    .ToListAsync(cancellationToken))
                .Select(Describe)
                .ToList()));

        templates.MapPost("/", async (
            TemplateRequest request,
            AppDbContext database,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            if (Validate(request) is { } invalid)
            {
                return invalid;
            }

            var template = new CsvTemplate
            {
                UserId = currentUser.Id!.Value,
                Name = request.Name.Trim(),
                Delimiter = request.Delimiter[0],
                HasHeader = request.HasHeader,
                Culture = request.Culture,
                DateFormat = request.DateFormat.Trim(),
                SignMode = request.SignMode,
                DateColumn = request.DateColumn.Trim(),
                AmountColumn = Optional(request.AmountColumn),
                DebitColumn = Optional(request.DebitColumn),
                CreditColumn = Optional(request.CreditColumn),
                DescriptionColumns = request.DescriptionColumns.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
            };

            database.CsvTemplates.Add(template);

            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.IsDuplicate())
            {
                return Problems.Conflict($"Já existe um modelo chamado '{template.Name}'.");
            }

            return Results.Created($"/api/csv-templates/{template.Id}", Describe(template));
        });

        templates.MapDelete("/{id:guid}", async (Guid id, AppDbContext database, CancellationToken cancellationToken) =>
        {
            var template = await database.CsvTemplates.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

            if (template is null)
            {
                return Results.NotFound();
            }

            database.CsvTemplates.Remove(template);
            await database.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        });

        return routes;
    }

    /// <summary>
    /// The shape of the mapping, without a file to resolve columns against: that
    /// check happens on every upload, against the real headers of that file.
    /// </summary>
    private static IResult? Validate(TemplateRequest request)
    {
        var mapping = new CsvMapping(
            request.Delimiter is { Length: 1 } ? request.Delimiter[0] : ';',
            request.HasHeader,
            request.Culture,
            request.DateFormat,
            request.SignMode,
            request.DateColumn,
            request.AmountColumn,
            request.DebitColumn,
            request.CreditColumn,
            request.DescriptionColumns);

        var violations = new List<RuleViolation?>();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > NameLength)
        {
            violations.Add(new RuleViolation("name", $"O nome é obrigatório e tem no máximo {NameLength} caracteres."));
        }

        if (request.Delimiter is not { Length: 1 })
        {
            violations.Add(new RuleViolation("delimiter", "O delimitador é um único caractere."));
        }

        violations.AddRange(mapping.Validate(table: null).Select(violation => (RuleViolation?)violation));

        return Problems.Validation([.. violations]);
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TemplateResponse Describe(CsvTemplate template) => new(
        template.Id,
        template.Name,
        template.Delimiter.ToString(),
        template.HasHeader,
        template.Culture,
        template.DateFormat,
        template.SignMode,
        template.DateColumn,
        template.AmountColumn,
        template.DebitColumn,
        template.CreditColumn,
        template.DescriptionColumns,
        template.CreatedAt);
}
