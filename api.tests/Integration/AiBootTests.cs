namespace Finance.Api.Tests.Integration;

/// <summary>
/// At the host: a configured model with no price fails the boot, with the key named, before
/// any call could be made at a cost of zero.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AiBootTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_model_without_a_price_fails_the_boot()
    {
        await using var factory = new IdentityApiFactory(
            postgres.ConnectionString,
            settings: new Dictionary<string, string?> { ["Ai:Analysis:Model"] = "unpriced-model" });

        var error = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Ai:Pricing:unpriced-model", Flatten(error));
    }

    internal static string Flatten(Exception error) =>
        error.InnerException is null ? error.Message : error.Message + " | " + Flatten(error.InnerException);
}
