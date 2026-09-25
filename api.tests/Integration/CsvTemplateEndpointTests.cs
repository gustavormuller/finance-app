using System.Net;
using System.Net.Http.Json;

namespace Finance.Api.Tests.Integration;

/// <summary>The three template routes, and the shape a template has to have to be saved.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class CsvTemplateEndpointTests(PostgresFixture postgres)
{
    /// <summary>The Nubank account export: comma, dd/MM/yyyy, dot decimals, signed.</summary>
    public static object NubankTemplate(string name) => new
    {
        name,
        delimiter = ",",
        hasHeader = true,
        culture = "en-US",
        dateFormat = "dd/MM/yyyy",
        signMode = "Signed",
        dateColumn = "Data",
        amountColumn = "Valor",
        descriptionColumns = "Descrição",
    };

    [Fact]
    public async Task A_template_is_created_listed_and_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("template-crud", cancellationToken);

        using var created = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/csv-templates", NubankTemplate("Nubank conta")),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var template = (await created.Content.ReadFromJsonAsync<ImportFixtures.TemplateItem>(cancellationToken))!;
        Assert.Equal($"/api/csv-templates/{template.Id}", created.Headers.Location?.ToString());
        Assert.Equal("Nubank conta", template.Name);
        Assert.Equal(",", template.Delimiter);
        Assert.Equal("Signed", template.SignMode);
        Assert.Equal("Descrição", template.DescriptionColumns);
        Assert.Null(template.DebitColumn);

        var listed = await user.Client.GetFromJsonAsync<List<ImportFixtures.TemplateItem>>("/api/csv-templates", cancellationToken);
        Assert.Equal(template.Id, Assert.Single(listed!).Id);

        using var deleted = await user.Client.SendAsync(TransactionsFixtures.Delete($"/api/csv-templates/{template.Id}"), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Empty((await user.Client.GetFromJsonAsync<List<ImportFixtures.TemplateItem>>("/api/csv-templates", cancellationToken))!);

        using var again = await user.Client.SendAsync(TransactionsFixtures.Delete($"/api/csv-templates/{template.Id}"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Two_templates_with_one_name_is_a_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("template-dup", cancellationToken);

        using var first = await user.Client.SendAsync(TransactionsFixtures.Post("/api/csv-templates", NubankTemplate("Nubank")), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await user.Client.SendAsync(TransactionsFixtures.Post("/api/csv-templates", NubankTemplate("Nubank")), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task An_invalid_template_is_a_400_naming_every_bad_field()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("template-invalid", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/csv-templates", new
            {
                name = "",
                delimiter = ";;",
                hasHeader = true,
                culture = "pt-br",
                dateFormat = "dd/MM",
                signMode = "DebitCredit",
                dateColumn = "",
                amountColumn = "Valor",
                descriptionColumns = "",
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var fields = await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken);

        Assert.Equal(
            ["creditColumn", "culture", "dateColumn", "dateFormat", "debitColumn", "delimiter", "descriptionColumns", "name"],
            fields.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_debit_credit_template_needs_both_columns_and_no_amount_column()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("template-dc", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/csv-templates", new
            {
                name = "Planilha",
                delimiter = ";",
                hasHeader = true,
                culture = "pt-BR",
                dateFormat = "dd/MM/yyyy",
                signMode = "DebitCredit",
                dateColumn = "Data",
                debitColumn = "Débito (R$)",
                creditColumn = "Crédito (R$)",
                descriptionColumns = "Lançamento",
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var template = (await response.Content.ReadFromJsonAsync<ImportFixtures.TemplateItem>(cancellationToken))!;
        Assert.Null(template.AmountColumn);
        Assert.Equal("Débito (R$)", template.DebitColumn);
    }
}
