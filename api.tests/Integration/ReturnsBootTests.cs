namespace Finance.Api.Tests.Integration;

/// <summary>
/// 008 spec unit test 21, at the host: a benchmark type that does not match the unit 006
/// records for its series fails the boot, with the key named, before any request is served.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ReturnsBootTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_mismatched_benchmark_type_fails_the_boot()
    {
        await using var factory = new IdentityApiFactory(
            postgres.ConnectionString,
            settings: new Dictionary<string, string?> { ["Returns:Benchmarks:CDI:Type"] = "Level" });

        var error = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Returns:Benchmarks:CDI", Flatten(error));
    }

    private static string Flatten(Exception error) =>
        error.InnerException is null ? error.Message : error.Message + " | " + Flatten(error.InnerException);
}
