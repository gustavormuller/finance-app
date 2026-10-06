using System.Text.Encodings.Web;
using System.Text.Json;
using Finance.Api.Application.Ai;
using Finance.Api.Application.Investments;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 009 unit tests 11 and 12: the analysis input's shape, pinned whole so a prompt revision
/// cannot drift it silently, and three months of aggregates with their month-over-month
/// changes. The document holds aggregates and merchant names only.
/// </summary>
public sealed class AnalysisInputBuilderTests
{
    private static readonly Guid Housing = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Food = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Transport = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid Leisure = Guid.Parse("00000000-0000-0000-0000-00000000000d");
    private static readonly Guid Salary = Guid.Parse("00000000-0000-0000-0000-00000000000e");
    private static readonly Guid Freelance = Guid.Parse("00000000-0000-0000-0000-00000000000f");

    /// <summary>Spec tests 11 and 12: the whole document, deltas included.</summary>
    [Fact]
    public void The_document_has_a_stable_shape_three_months_and_correct_month_over_month_changes()
    {
        var json = AnalysisInputBuilder.Build(Aggregates());

        Assert.Equal(Expected, Indented(json));
    }

    [Fact]
    public void Only_the_top_twenty_merchants_by_spend_are_kept()
    {
        var merchants = Enumerable.Range(1, 25).Select(index => new AnalysisMerchant($"LOJA {(char)('A' + index)}", index * 10.00m, 1)).ToList();

        using var document = JsonDocument.Parse(AnalysisInputBuilder.Build(Aggregates() with { Merchants = merchants }));

        var kept = document.RootElement.GetProperty("topMerchants").EnumerateArray().Select(merchant => merchant.GetProperty("spent").GetDecimal()).ToList();
        Assert.Equal(AnalysisInputBuilder.TopMerchants, kept.Count);
        Assert.Equal(250.00m, kept[0]);
        Assert.Equal(60.00m, kept[^1]);
    }

    [Fact]
    public void A_month_with_no_rows_is_zero_and_a_change_from_zero_has_no_percent()
    {
        var empty = new AnalysisAggregates(
            "2026-08",
            [new("2026-06", 0m, 0m), new("2026-07", 0m, 0m), new("2026-08", 100.00m, 0m)],
            [],
            [],
            [],
            0m,
            new PortfolioSummary(0m, 0m, 0m));

        using var document = JsonDocument.Parse(AnalysisInputBuilder.Build(empty));

        var income = document.RootElement.GetProperty("monthOverMonth").GetProperty("income");
        Assert.Equal("100.00", income.GetProperty("change").GetRawText());
        Assert.Equal(JsonValueKind.Null, income.GetProperty("changePercent").ValueKind);
        Assert.Equal("0.00", document.RootElement.GetProperty("months")[0].GetProperty("expense").GetRawText());
    }

    private static AnalysisAggregates Aggregates() => new(
        "2026-08",
        [new("2026-06", 5000.00m, -3000.00m), new("2026-07", 5300.00m, -3200.00m), new("2026-08", 5900.00m, -2800.00m)],
        [
            new("2026-06", Food, "Alimentação", CategoryKind.Expense, -800.00m),
            new("2026-07", Food, "Alimentação", CategoryKind.Expense, -1000.00m),
            new("2026-08", Food, "Alimentação", CategoryKind.Expense, -1200.00m),
            new("2026-06", Housing, "Moradia", CategoryKind.Expense, -1500.00m),
            new("2026-07", Housing, "Moradia", CategoryKind.Expense, -1500.00m),
            new("2026-08", Housing, "Moradia", CategoryKind.Expense, -1500.00m),
            new("2026-06", Leisure, "Lazer", CategoryKind.Expense, -700.00m),
            new("2026-07", Leisure, "Lazer", CategoryKind.Expense, -700.00m),
            new("2026-08", Transport, "Transporte", CategoryKind.Expense, -100.00m),
            new("2026-06", Salary, "Salário", CategoryKind.Income, 5000.00m),
            new("2026-07", Salary, "Salário", CategoryKind.Income, 5000.00m),
            new("2026-08", Salary, "Salário", CategoryKind.Income, 5500.00m),
            new("2026-07", Freelance, "Freela", CategoryKind.Income, 300.00m),
            new("2026-08", Freelance, "Freela", CategoryKind.Income, 400.00m),
        ],
        [new("PAG IFOOD", 350.40m, 6), new("SUPERMERCADO DIA", 820.00m, 3)],
        [
            new(Guid.NewGuid(), "Nubank", AccountType.Checking, "BRL", 1234.56m, false),
            new(Guid.NewGuid(), "Wise", AccountType.Checking, "USD", 100.00m, true),
        ],
        1234.56m,
        new PortfolioSummary(10000.00m, 9000.00m, 1000.00m));

    private const string Expected = """
        {
          "month": "2026-08",
          "currency": "BRL",
          "months": [
            {
              "month": "2026-06",
              "income": 5000.00,
              "expense": 3000.00,
              "net": 2000.00
            },
            {
              "month": "2026-07",
              "income": 5300.00,
              "expense": 3200.00,
              "net": 2100.00
            },
            {
              "month": "2026-08",
              "income": 5900.00,
              "expense": 2800.00,
              "net": 3100.00
            }
          ],
          "monthOverMonth": {
            "income": {
              "previous": 5300.00,
              "current": 5900.00,
              "change": 600.00,
              "changePercent": 11.3
            },
            "expense": {
              "previous": 3200.00,
              "current": 2800.00,
              "change": -400.00,
              "changePercent": -12.5
            },
            "net": {
              "previous": 2100.00,
              "current": 3100.00,
              "change": 1000.00,
              "changePercent": 47.6
            }
          },
          "categories": [
            {
              "name": "Moradia",
              "kind": "Expense",
              "amounts": {
                "2026-06": 1500.00,
                "2026-07": 1500.00,
                "2026-08": 1500.00
              },
              "change": 0.00,
              "changePercent": 0.0
            },
            {
              "name": "Alimentação",
              "kind": "Expense",
              "amounts": {
                "2026-06": 800.00,
                "2026-07": 1000.00,
                "2026-08": 1200.00
              },
              "change": 200.00,
              "changePercent": 20.0
            },
            {
              "name": "Transporte",
              "kind": "Expense",
              "amounts": {
                "2026-06": 0.00,
                "2026-07": 0.00,
                "2026-08": 100.00
              },
              "change": 100.00,
              "changePercent": null
            },
            {
              "name": "Lazer",
              "kind": "Expense",
              "amounts": {
                "2026-06": 700.00,
                "2026-07": 700.00,
                "2026-08": 0.00
              },
              "change": -700.00,
              "changePercent": -100.0
            },
            {
              "name": "Salário",
              "kind": "Income",
              "amounts": {
                "2026-06": 5000.00,
                "2026-07": 5000.00,
                "2026-08": 5500.00
              },
              "change": 500.00,
              "changePercent": 10.0
            },
            {
              "name": "Freela",
              "kind": "Income",
              "amounts": {
                "2026-06": 0.00,
                "2026-07": 300.00,
                "2026-08": 400.00
              },
              "change": 100.00,
              "changePercent": 33.3
            }
          ],
          "topMerchants": [
            {
              "name": "SUPERMERCADO DIA",
              "spent": 820.00,
              "transactions": 3
            },
            {
              "name": "PAG IFOOD",
              "spent": 350.40,
              "transactions": 6
            }
          ],
          "accounts": [
            {
              "name": "Nubank",
              "type": "Checking",
              "currency": "BRL",
              "balance": 1234.56
            },
            {
              "name": "Wise",
              "type": "Checking",
              "currency": "USD",
              "balance": 100.00
            }
          ],
          "balanceTotalBrl": 1234.56,
          "investments": {
            "valueBrl": 10000.00,
            "costBrl": 9000.00,
            "unrealisedBrl": 1000.00
          }
        }
        """;

    /// <summary>The document re-indented with its numbers as written, so the comparison sees scale too.</summary>
    private static string Indented(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            document.WriteTo(writer);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()).ReplaceLineEndings("\n");
    }
}
