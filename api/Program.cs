using System.Text.Json.Serialization;
using Finance.Api.Application;
using Finance.Api.Application.Ai;
using Finance.Api.Application.Dashboard;
using Finance.Api.Application.Investments;
using Finance.Api.Application.Returns;
using Finance.Api.Endpoints;
using Finance.Api.Infrastructure;
using Finance.Api.Infrastructure.Ai;
using Finance.Api.Infrastructure.MarketData;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The query filters read the caller's id from the request, so the context needs an
// ICurrentUser in every scope, including the ones that serve no request at all.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ActingUser>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// The connection string is read from the built service provider rather than from
// builder.Configuration, because configuration sources added by the host — including
// the ones WebApplicationFactory injects in the integration tests — are only merged
// in during Build().
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
    options.UseNpgsql(serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Default")));

// Enums cross the wire as their names. "Checking" survives a renumbering of the
// enum and reads the same in a 400 message, an OpenAPI schema and the generated TS
// client; the columns stay int.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddFinanceAuthentication();
builder.Services.AddAuthorization();

// 004's use cases. Scoped, like the context they take (ADR-016).
builder.Services.AddScoped<ImportStaging>();
builder.Services.AddScoped<ImportCommands>();

// 005's dashboard reads: Dapper on the context's connection (ARCHITECTURE.md section 6).
builder.Services.AddScoped<DashboardQueries>();

// 006's market-data provider adapters, the MarketData settings, the sync and its
// nightly job (MarketData:ScheduledSync switches the job off).
builder.Services.AddMarketDataProviders();
builder.Services.AddMarketDataSync();

// 007's snapshot rebuild, run by every movement write, POST /rebuild and after the sync.
builder.Services.AddScoped<SnapshotRebuild>();
builder.Services.AddScoped<PositionQueries>();
builder.Services.AddScoped<MovementCommands>();

// 008's returns: the benchmarks they compare with, and the reads behind /api/returns.
builder.Services.AddOptions<ReturnsOptions>().BindConfiguration(ReturnsOptions.Section);
builder.Services.AddScoped<ReturnsQueries>();

// 009's AI settings, the provider port, the budget and the gateway every call goes through.
builder.Services.AddAi();

var app = builder.Build();

// Everything the process cannot run without, checked once, at boot, with a message
// that says what the setting is for. In development these come from user secrets
// (UserSecretsId finance-app-api), in production from the environment. Each of them
// otherwise fails later and worse: inside Npgsql, inside the Google handler on every
// single request, at the first sign-in, or — for the keys — silently, as sessions
// quietly dying on every restart.
Require(
    "ConnectionStrings:Default",
    "the PostgreSQL connection string");
Require(
    "Google:ClientId",
    "the OAuth client id from the Google Cloud Console; Google is the only way in");
Require(
    "Google:ClientSecret",
    "the OAuth client secret matching Google:ClientId");
Require(
    ConfigurationKeys.AppOrigin,
    "the exact scheme, host and port the browser loads the application from; every "
    + "mutating request to /api must carry it as its Origin header");
Require(
    ConfigurationKeys.DataProtectionKeysPath,
    "the directory holding the Data Protection key ring; without one, every restart "
    + "invalidates every session");

// The E2E run's network-free market-data providers never reach another environment.
MarketDataSetup.RefuseFakeProvidersOutsideDevelopment(app.Configuration, app.Environment);

// Likewise its canned AI provider.
AiSetup.RefuseFakeProviderOutsideDevelopment(app.Configuration, app.Environment);

// 009's models must each have a price, or a call would cost nothing against the budget.
AiOptions.RefuseInvalid(app.Configuration);

// 008's benchmark types must match the units 006 records for their series (spec test 21).
ReturnsOptions.RefuseMismatchedBenchmarks(app.Configuration);

var appOrigin = app.Configuration[ConfigurationKeys.AppOrigin]!;

void Require(string key, string purpose)
{
    if (!string.IsNullOrWhiteSpace(app.Configuration[key]))
    {
        return;
    }

    throw new InvalidOperationException(
        $"{key} is not configured. It is {purpose}. In development, set it with "
        + $"`dotnet user-secrets set \"{key}\" \"...\" --project api`.");
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

app.UseRouting();

// Before authentication, so a cross-origin request is refused without its cookie ever
// being looked at.
app.UseApiOriginCheck(appOrigin);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness: the process is up. No database work — scripts/verify-e2e.sh polls this to
// know the API is ready, and e2e/smoke.spec.ts asserts its shape.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Readiness: the process can serve traffic, which means the database answers.
app.MapHealthEndpoints();

app.MapAuthEndpoints(app.Environment);

app.MapAccountEndpoints();
app.MapCategoryEndpoints();
app.MapTransactionEndpoints();
app.MapImportEndpoints();
app.MapCsvTemplateEndpoints();
app.MapDashboardEndpoints();
app.MapMarketDataEndpoints();
app.MapInvestmentEndpoints();
app.MapReturnsEndpoints();

app.Run();

// Exposed so api.tests can drive the real pipeline with WebApplicationFactory<Program>.
public partial class Program;
