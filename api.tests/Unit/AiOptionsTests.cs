using Finance.Api.Application.Ai;
using Microsoft.Extensions.Configuration;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// The <c>Ai</c> settings bind from <c>appsettings.json</c>, commit no key, and are refused
/// when a call could not be priced: a price of zero, or no price at all, would let every
/// call past the budget (ADR-008).
/// </summary>
public sealed class AiOptionsTests
{
    [Fact]
    public void Appsettings_binds_the_provider_the_budget_the_models_and_their_usd_prices()
    {
        var options = Bind();

        Assert.Equal(("anthropic", 15.00m), (options.Provider, options.MonthlyBudgetBrl));
        Assert.NotEqual("", options.Categorisation.Model);
        Assert.NotEqual("", options.Analysis.Model);
        Assert.All([options.Categorisation.Model, options.Analysis.Model], model =>
        {
            var price = options.Pricing[model];
            Assert.True(price.InputPerMTokUsd > 0m && price.OutputPerMTokUsd > 0m, model);
        });
        Assert.True(options.UsdBrl > 0m);
        Assert.False(options.FakeProvider);
        Assert.Empty(AiOptions.Problems(options));
    }

    [Fact]
    public void Appsettings_commits_no_ai_key()
    {
        var options = Bind();

        Assert.Equal(("", ""), (options.Anthropic.ApiKey, options.OpenAi.ApiKey));
    }

    [Fact]
    public void A_configured_model_without_a_price_is_a_problem()
    {
        var options = Sound();
        options.Analysis.Model = "unpriced-model";

        var problem = Assert.Single(AiOptions.Problems(options));

        Assert.Contains("Ai:Pricing:unpriced-model", problem);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 0)]
    [InlineData(-1, 5)]
    public void A_price_that_is_not_positive_is_a_problem(int input, int output)
    {
        var options = Sound();
        options.Pricing["cheap"] = new AiModelPrice { InputPerMTokUsd = input, OutputPerMTokUsd = output };

        Assert.Contains("Ai:Pricing:cheap", Assert.Single(AiOptions.Problems(options)));
    }

    [Fact]
    public void An_unknown_provider_an_empty_model_a_negative_budget_and_no_fallback_rate_are_problems()
    {
        var options = Sound();
        options.Provider = "gemini";
        options.Categorisation.Model = "";
        options.MonthlyBudgetBrl = -1m;
        options.UsdBrl = 0m;

        var problems = string.Join(" | ", AiOptions.Problems(options));

        Assert.Contains("Ai:Provider", problems);
        Assert.Contains("Ai:Categorisation:Model", problems);
        Assert.Contains("Ai:MonthlyBudgetBrl", problems);
        Assert.Contains("Ai:UsdBrl", problems);
    }

    [Fact]
    public void The_provider_is_read_ignoring_case_and_openai_is_accepted()
    {
        var options = Sound();
        options.Provider = "OpenAI";

        Assert.Empty(AiOptions.Problems(options));
    }

    [Fact]
    public void A_zero_budget_is_sound_it_refuses_every_call()
    {
        var options = Sound();
        options.MonthlyBudgetBrl = 0m;

        Assert.Empty(AiOptions.Problems(options));
    }

    [Fact]
    public void Appsettings_binds_both_base_urls_and_a_30_second_categorisation_timeout()
    {
        var options = Bind();

        Assert.Equal(
            ("https://api.anthropic.com/", "https://api.openai.com/"),
            (options.Anthropic.BaseUrl, options.OpenAi.BaseUrl));
        Assert.Equal(30, options.Categorisation.TimeoutSeconds); // decision 4
        Assert.True(options.Analysis.TimeoutSeconds >= options.Categorisation.TimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_timeout_that_is_not_positive_is_a_problem(int seconds)
    {
        var options = Sound();
        options.Analysis.TimeoutSeconds = seconds;

        Assert.Contains("Ai:Analysis:TimeoutSeconds", Assert.Single(AiOptions.Problems(options)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("api.anthropic.com")]
    [InlineData("ftp://api.anthropic.com/")]
    [InlineData("https://api.anthropic.com/v1")]
    public void A_base_url_that_is_not_absolute_http_ending_in_a_slash_is_a_problem(string url)
    {
        var options = Sound();
        options.Anthropic.BaseUrl = url;

        Assert.Contains("Ai:Anthropic:BaseUrl", Assert.Single(AiOptions.Problems(options)));
    }

    internal static AiOptions Sound() => new()
    {
        Provider = "anthropic",
        MonthlyBudgetBrl = 15m,
        Categorisation = { Model = "cheap", TimeoutSeconds = 30 },
        Analysis = { Model = "better", TimeoutSeconds = 120 },
        Anthropic = { BaseUrl = "https://api.anthropic.com/" },
        OpenAi = { BaseUrl = "https://api.openai.com/" },
        Pricing =
        {
            ["cheap"] = new AiModelPrice { InputPerMTokUsd = 1m, OutputPerMTokUsd = 5m },
            ["better"] = new AiModelPrice { InputPerMTokUsd = 5m, OutputPerMTokUsd = 25m },
        },
        UsdBrl = 5.40m,
    };

    private static AiOptions Bind()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(TestPaths.RepositoryRoot(), "api", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();

        return configuration.GetSection(AiOptions.Section).Get<AiOptions>()
            ?? throw new InvalidOperationException("No Ai section in appsettings.json.");
    }
}
