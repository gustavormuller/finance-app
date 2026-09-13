var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Liveness probe. Intentionally the only endpoint in the bootstrap scaffold:
// scripts/verify-e2e.sh polls it to know the API is ready.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Exposed so api.tests can drive the real pipeline with WebApplicationFactory<Program>.
public partial class Program;
