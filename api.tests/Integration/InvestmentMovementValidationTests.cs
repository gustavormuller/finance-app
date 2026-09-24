using System.Net;
using System.Net.Http.Json;
using static Finance.Api.Tests.Integration.InvestmentsApi;
using static Finance.Api.Tests.Integration.TransactionsFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>The spec's validation table over HTTP, the oversell rule on every write, and what is stored.</summary>
public sealed partial class InvestmentMovementEndpointTests
{
    public static TheoryData<string, object, string, string> Refused => new()
    {
        { "buy of zero", new { date = Today, kind = "Buy", quantity = 0m, unitPrice = 10m }, "quantity", "Quantidade deve ser positiva" },
        { "dividend of zero", new { date = Today, kind = "Dividend", amount = 0m }, "amount", "Valor deve ser positivo" },
        { "other currency", new { date = Today, kind = "Buy", quantity = 1m, unitPrice = 10m, currency = "USD" }, "currency", "Moeda diferente do ativo" },
        { "tomorrow", new { date = Today.AddDays(1), kind = "Buy", quantity = 1m, unitPrice = 10m }, "date", "Data fora do intervalo" },
        { "before 1990", new { date = new DateOnly(1989, 12, 31), kind = "Buy", quantity = 1m, unitPrice = 10m }, "date", "Data fora do intervalo" },
        { "negative fees", new { date = Today, kind = "Buy", quantity = 1m, unitPrice = 10m, fees = -1m }, "fees", "Taxas não podem ser negativas" },
        { "oversell", new { date = Today, kind = "Sell", quantity = 101m, unitPrice = 10m }, "quantity", "Quantidade vendida maior que a posição" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_movement_breaking_a_rule_is_a_400_naming_the_field_and_nothing_is_written(
        string _, object body, string field, string message)
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, user, assetId) = await HeldAsync(ct);
        await using var __ = api;
        await PostAsync(user, assetId, ABuy(Today.AddDays(-5)), ct);

        using var response = await user.Client.SendAsync(Post($"/api/investments/assets/{assetId}/movements", body), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], (await response.Content.ReadFromJsonAsync<Problem>(ct))!.Errors[field]);
        Assert.Single((await user.Client.GetFromJsonAsync<List<MovementItem>>($"/api/investments/assets/{assetId}/movements", ct))!);
    }

    /// <summary>Spec test 8 over HTTP: an edit or a delete that leaves a later sell uncovered.</summary>
    [Fact]
    public async Task Edits_and_deletes_that_uncover_a_later_sell_are_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, user, assetId) = await HeldAsync(ct);
        await using var _ = api;
        var buyId = await PostAsync(user, assetId, ABuy(Today.AddDays(-5)), ct);
        await PostAsync(user, assetId, new { date = Today.AddDays(-2), kind = "Sell", quantity = 80m, unitPrice = 12m }, ct);

        using var later = await user.Client.SendAsync(Put($"/api/investments/movements/{buyId}", ABuy(Today.AddDays(-1))), ct);
        using var smaller = await user.Client.SendAsync(Put($"/api/investments/movements/{buyId}", ABuy(Today.AddDays(-5), quantity: 50m)), ct);
        using var deleted = await user.Client.SendAsync(Delete($"/api/investments/movements/{buyId}"), ct);

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (later.StatusCode, smaller.StatusCode));
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal("Quantidade vendida maior que a posição", await InvestmentAssetEndpointTests.DetailAsync(deleted, ct));
        var movements = await user.Client.GetFromJsonAsync<List<MovementItem>>($"/api/investments/assets/{assetId}/movements", ct);
        Assert.Equal([(Today.AddDays(-5), 100m), (Today.AddDays(-2), 80m)], movements!.Select(movement => (movement.Date, movement.Quantity)));
        Assert.Equal(20m, (await DailyAsync(user, assetId, ct))[^1].Quantity);
    }

    [Fact]
    public async Task Fields_irrelevant_to_the_kind_are_stored_as_zero_and_the_currency_defaults_to_the_assets()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, user, assetId) = await HeldAsync(ct);
        await using var _ = api;
        await PostAsync(user, assetId, ABuy(Today.AddDays(-5)), ct);

        using var dividend = await user.Client.SendAsync(Post($"/api/investments/assets/{assetId}/movements",
            new { date = Today, kind = "Dividend", quantity = 5m, unitPrice = 3m, amount = 12.5m, notes = "  proventos  " }), ct);
        using var split = await user.Client.SendAsync(Post($"/api/investments/assets/{assetId}/movements",
            new { date = Today, kind = "Split", quantity = 100m, unitPrice = 3m, amount = 7m }), ct);

        var paid = (await dividend.Content.ReadFromJsonAsync<MovementItem>(ct))!;
        var doubled = (await split.Content.ReadFromJsonAsync<MovementItem>(ct))!;
        Assert.Equal((0m, 0m, 12.5m, "BRL", "proventos"), (paid.Quantity, paid.UnitPrice, paid.Amount, paid.Currency, paid.Notes));
        Assert.Equal((100m, 0m, 0m), (doubled.Quantity, doubled.UnitPrice, doubled.Amount));
        Assert.Equal((200m, 5.025m), ((await DailyAsync(user, assetId, ct))[^1].Quantity, (await DailyAsync(user, assetId, ct))[^1].AverageCost));
    }

    [Fact]
    public async Task Daily_rows_are_read_by_an_inclusive_range()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, user, assetId) = await HeldAsync(ct);
        await using var _ = api;
        await PostAsync(user, assetId, ABuy(Today.AddDays(-5)), ct);

        var rows = await user.Client.GetFromJsonAsync<List<DailyItem>>(
            $"/api/investments/assets/{assetId}/daily?from={Today.AddDays(-4):yyyy-MM-dd}&to={Today.AddDays(-2):yyyy-MM-dd}", ct);

        Assert.Equal(Days(Today.AddDays(-4), Today.AddDays(-2)), rows!.Select(row => row.Date));
    }

    private sealed record Problem(Dictionary<string, string[]> Errors);
}
