using System.Data.Common;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Endpoints;

public static class HealthEndpoints
{
    private sealed record HealthResponse(string Status, string Database);

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/health", async (
            AppDbContext database,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                // A real round trip. Reading the connection string back out of
                // configuration would report "ok" from a machine with no database.
                await database.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

                return Results.Ok(new HealthResponse("ok", "ok"));
            }
            catch (DbException exception)
            {
                loggerFactory
                    .CreateLogger("Finance.Api.Endpoints.Health")
                    .LogWarning(exception, "Readiness check failed: the database is unreachable.");

                // 503 rather than 200-with-a-flag, so callers and tests can decide on
                // the status code alone.
                return Results.Json(
                    new HealthResponse("degraded", "unreachable"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        return routes;
    }
}
