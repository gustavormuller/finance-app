using Finance.Api.Endpoints;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The connection string is read from the built service provider rather than from
// builder.Configuration, because configuration sources added by the host — including
// the ones WebApplicationFactory injects in the integration tests — are only merged
// in during Build().
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
    options.UseNpgsql(serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Default")));

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Default")))
{
    // In development this comes from user secrets (UserSecretsId finance-app-api),
    // in production from the environment. Failing at boot with a readable message
    // beats failing later inside Npgsql.
    throw new InvalidOperationException(
        "ConnectionStrings:Default is not configured. In development, set it with "
        + "`dotnet user-secrets set \"ConnectionStrings:Default\" \"...\" --project api`.");
}

// The deploy model is `git pull && docker compose up -d --build` with no separate
// migration step, so the application migrates itself on the way up. The
// unreachable-database test switches this off: the process has to boot without a
// database in order to be able to report that it has none.
if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// Liveness: the process is up. No database work — scripts/verify-e2e.sh polls this to
// know the API is ready, and e2e/smoke.spec.ts asserts its shape.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Readiness: the process can serve traffic, which means the database answers.
app.MapHealthEndpoints();

app.Run();

// Exposed so api.tests can drive the real pipeline with WebApplicationFactory<Program>.
public partial class Program;
