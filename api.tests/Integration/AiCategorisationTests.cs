using System.Text.Json;
using Finance.Api.Application;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 009 checkpoint 4a: which rung chose a staged row's category (<see cref="CategorySource"/>),
/// and rung 3 on the application path (<see cref="CategorisationCascade"/>) with a scripted
/// provider: the application halves of spec tests 18-20. CP4b covers them over HTTP.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AiCategorisationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Staging_records_history_for_a_remembered_merchant_default_for_the_rest_and_none_for_an_invalid_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("ai-source", ct);
        var accountId = await user.CreateAccountAsync("Nubank", ct);
        var first = await user.UploadOfxAsync(
            accountId, ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260901", "-42.90", "S-1", "PAG*IFOOD 01/09")]), ct);
        await user.CommitAsync(first.BatchId, ct);

        var second = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx(
            [
                new ImportFixtures.OfxRow("20260915", "-58.00", "S-2", "PAG*IFOOD 15/09"),
                new ImportFixtures.OfxRow("20260915", "-19.90", "S-3", "PADARIA REAL"),
                new ImportFixtures.OfxRow("20260915", "3000.00", "S-4", "TED RECEBIDA"),
                new ImportFixtures.OfxRow("20260915", "0.00", "S-5", "ESTORNO"),
            ]),
            ct);

        Assert.Equal(
            [CategorySource.History, CategorySource.Default, CategorySource.Default, CategorySource.None],
            await SourcesAsync(user.Id, second.BatchId, ct));
    }

    private async Task<List<CategorySource>> SourcesAsync(Guid userId, Guid batchId, CancellationToken ct)
    {
        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, userId);
        return await context.StagedTransactions
            .Where(row => row.ImportBatchId == batchId)
            .OrderBy(row => row.RowNumber)
            .Select(row => row.CategorySource)
            .ToListAsync(ct);
    }

    /// <summary>Spec tests 18 and 19, below the endpoint.</summary>
    [Fact]
    public async Task Only_default_rows_are_sent_and_the_answer_replaces_their_category()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(AnswerByDescription(new()
        {
            ["PADARIA REAL"] = "Alimentação > Padaria",
            ["TED RECEBIDA"] = "Salário",
            ["UBER TRIP"] = "Outros",
        }));
        await using var factory = Factory(provider);
        var (user, batchId, categories) = await StagedAsync(factory, "ai-rung3", ct);

        var result = await SuggestAsync(factory, user, batchId, ct);

        Assert.Equal(new SuggestResult(2, 2, ImportCommandProblem.None), result);
        var request = Assert.Single(provider.Requests);
        Assert.Equal(AiCategorisation.System, request.System);
        Assert.Equal(AiCategorisation.MaxTokensFor(4), request.MaxTokens);
        using var sent = JsonDocument.Parse(request.User);
        Assert.Equal(
            ["PADARIA REAL", "TED RECEBIDA", "UBER TRIP", "POSTO SHELL"],
            sent.RootElement.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("description").GetString()));
        Assert.Contains("Alimentação > Padaria", request.User);
        Assert.Equal(
            [
                (categories["Alimentação"], CategorySource.History),
                (categories["Alimentação > Padaria"], CategorySource.Ai),
                (categories["Salário"], CategorySource.Ai),
                (categories["Outros"], CategorySource.Default),
                (categories["Outros"], CategorySource.Default),
            ],
            await RowsAsync(user, batchId, ct));
    }

    /// <summary>Spec test 20, below the endpoint.</summary>
    [Fact]
    public async Task A_garbage_answer_changes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => new AiCompletion("## Resumo\n\nNada a ver.", 100, 10));
        await using var factory = Factory(provider);
        var (user, batchId, _) = await StagedAsync(factory, "ai-garbage", ct);
        var before = await RowsAsync(user, batchId, ct);

        var result = await SuggestAsync(factory, user, batchId, ct);

        Assert.Equal(new SuggestResult(0, 4, ImportCommandProblem.None), result);
        Assert.Equal(before, await RowsAsync(user, batchId, ct));
    }

    [Fact]
    public async Task Rows_go_in_batches_and_a_failed_batch_keeps_what_earlier_ones_suggested()
    {
        var ct = TestContext.Current.CancellationToken;
        var calls = 0;
        var provider = new ScriptedAiProvider(request => ++calls == 1
            ? AnswerByDescription(new() { ["LOJA"] = "Lazer" })(request)
            : throw new AiProviderException("provider error", inputTokens: 0));
        await using var factory = Factory(provider);
        var user = await factory.SignInNewUserAsync("ai-batches", ct);
        await EnableAiAsync(user.Id, ct);
        var accountId = await user.CreateAccountAsync("Nubank", ct);
        var batch = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx(Enumerable.Range(1, AiCategorisation.BatchSize + 5)
                .Select(day => new ImportFixtures.OfxRow("20260915", $"-{day}.00", $"L-{day}", "LOJA"))),
            ct);

        await Assert.ThrowsAsync<AiProviderException>(() => SuggestAsync(factory, user, batch.BatchId, ct));

        Assert.Equal(
            [AiCategorisation.MaxTokensFor(AiCategorisation.BatchSize), AiCategorisation.MaxTokensFor(5)],
            provider.Requests.Select(request => request.MaxTokens));
        var sources = (await RowsAsync(user, batch.BatchId, ct)).Select(row => row.Source).ToList();
        Assert.Equal(AiCategorisation.BatchSize, sources.Count(source => source == CategorySource.Ai));
        Assert.Equal(5, sources.Count(source => source == CategorySource.Default));

        // Asking again sends only what is still on its default.
        provider.Requests.Clear();
        await Assert.ThrowsAsync<AiProviderException>(() => SuggestAsync(factory, user, batch.BatchId, ct));
        Assert.Equal(AiCategorisation.MaxTokensFor(5), Assert.Single(provider.Requests).MaxTokens);
    }

    [Fact]
    public async Task A_user_with_ai_off_is_refused_even_with_nothing_to_send()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => throw new InvalidOperationException("not called"));
        await using var factory = Factory(provider);
        var user = await factory.SignInNewUserAsync("ai-off", ct);
        var accountId = await user.CreateAccountAsync("Nubank", ct);
        var batch = await user.UploadOfxAsync(
            accountId, ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260915", "0.00", "Z-1", "ESTORNO")]), ct);

        await Assert.ThrowsAsync<AiDisabledException>(() => SuggestAsync(factory, user, batch.BatchId, ct));
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Another_users_batch_is_not_found_and_a_committed_one_is_the_wrong_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new ScriptedAiProvider(_ => throw new InvalidOperationException("not called"));
        await using var factory = Factory(provider);
        var (owner, batchId, _) = await StagedAsync(factory, "ai-owner", ct);
        var other = await factory.SignInNewUserAsync("ai-other", ct);
        await EnableAiAsync(other.Id, ct);

        Assert.Equal(ImportCommandProblem.NotFound, (await SuggestAsync(factory, other, batchId, ct)).Problem);
        await owner.CommitAsync(batchId, ct);
        Assert.Equal(ImportCommandProblem.WrongStatus, (await SuggestAsync(factory, owner, batchId, ct)).Problem);
        Assert.Empty(provider.Requests);
    }

    private IdentityApiFactory Factory(IAiProvider provider) =>
        new(postgres.ConnectionString, services: services => services.AddSingleton(provider));

    /// <summary>
    /// A user with AI on, a child category under Alimentação, one committed iFood row for
    /// history, then a batch of one remembered row, four sign-default rows and an invalid one.
    /// </summary>
    private async Task<(SignedInUser User, Guid BatchId, Dictionary<string, Guid> Categories)> StagedAsync(
        IdentityApiFactory factory, string prefix, CancellationToken ct)
    {
        var user = await factory.SignInNewUserAsync(prefix, ct);
        await EnableAiAsync(user.Id, ct);
        var categories = (await user.CategoriesAsync(ct)).ToDictionary(pair => pair.Key, pair => pair.Value.Id);
        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var bakery = new Category
            {
                Id = Guid.NewGuid(), UserId = user.Id, Name = "Padaria", Kind = CategoryKind.Expense,
                ParentId = categories["Alimentação"], CreatedAt = DateTimeOffset.UtcNow,
            };
            context.Categories.Add(bakery);
            await context.SaveChangesAsync(ct);
            categories["Alimentação > Padaria"] = bakery.Id;
        }

        var accountId = await user.CreateAccountAsync("Nubank", ct);
        var first = await user.UploadOfxAsync(
            accountId, ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260901", "-42.90", "S-1", "PAG*IFOOD 01/09")]), ct);
        using (var patch = await user.Client.SendAsync(
            ImportFixtures.Patch($"/api/imports/{first.BatchId}/rows/{Assert.Single((await user.GetBatchAsync(first.BatchId, ct)).Rows.Items).Id}",
                new { categoryId = categories["Alimentação"] }),
            ct))
        {
            patch.EnsureSuccessStatusCode();
        }

        await user.CommitAsync(first.BatchId, ct);
        var second = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx(
            [
                new ImportFixtures.OfxRow("20260915", "-58.00", "S-2", "PAG*IFOOD 15/09"),
                new ImportFixtures.OfxRow("20260915", "-19.90", "S-3", "PADARIA REAL"),
                new ImportFixtures.OfxRow("20260915", "3000.00", "S-4", "TED RECEBIDA"),
                new ImportFixtures.OfxRow("20260915", "-31.00", "S-5", "UBER *TRIP"),
                new ImportFixtures.OfxRow("20260915", "-150.00", "S-6", "POSTO SHELL"),
                new ImportFixtures.OfxRow("20260915", "0.00", "S-7", "ESTORNO"),
            ]),
            ct);
        return (user, second.BatchId, categories);
    }

    private async Task EnableAiAsync(Guid userId, CancellationToken ct)
    {
        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, null);
        await context.Users.Where(appUser => appUser.Id == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(appUser => appUser.AiEnabled, true), ct);
    }

    private static async Task<SuggestResult> SuggestAsync(IdentityApiFactory factory, SignedInUser user, Guid batchId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ActingUser>().ActAs(user.Id);
        return await scope.ServiceProvider.GetRequiredService<CategorisationCascade>().SuggestAsync(batchId, ct);
    }

    /// <summary>The valid rows, in file order: their category and the rung that chose it.</summary>
    private async Task<List<(Guid? Category, CategorySource Source)>> RowsAsync(SignedInUser user, Guid batchId, CancellationToken ct)
    {
        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id);
        return (await context.StagedTransactions
            .Where(row => row.ImportBatchId == batchId && row.Status != StagedRowStatus.Invalid)
            .OrderBy(row => row.RowNumber)
            .Select(row => new { row.CategoryId, row.CategorySource })
            .ToListAsync(ct))
            .Select(row => (row.CategoryId, row.CategorySource))
            .ToList();
    }

    /// <summary>Answers each sent row whose description is listed with the category of that name, as a model would.</summary>
    private static Func<AiRequest, AiCompletion> AnswerByDescription(Dictionary<string, string> byDescription) => request =>
    {
        using var sent = JsonDocument.Parse(request.User);
        var ids = sent.RootElement.GetProperty("categories").EnumerateArray()
            .ToDictionary(category => category.GetProperty("name").GetString()!, category => category.GetProperty("id").GetString()!);
        var answer = sent.RootElement.GetProperty("rows").EnumerateArray()
            .Where(row => byDescription.ContainsKey(row.GetProperty("description").GetString()!))
            .ToDictionary(row => row.GetProperty("rowId").GetString()!, row => ids[byDescription[row.GetProperty("description").GetString()!]]);
        return new AiCompletion("```json\n" + JsonSerializer.Serialize(answer) + "\n```", 500, 50);
    };

    private sealed class ScriptedAiProvider(Func<AiRequest, AiCompletion> answer) : IAiProvider
    {
        public List<AiRequest> Requests { get; } = [];

        public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }
}
