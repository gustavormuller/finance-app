using System.Globalization;
using System.Reflection;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec unit tests 6-13. The two parsers where a statement most often goes wrong
/// silently: a Brazilian decimal read as an American one, and a day read as a month.
/// </summary>
/// <remarks>
/// Expected decimals are written as strings and parsed with the invariant culture,
/// because an attribute argument cannot be a <see cref="decimal"/> and a bare
/// literal would arrive as a <see cref="double"/> — the type these tests exist to
/// keep out.
/// </remarks>
public sealed class FieldParserTests
{
    private const string PtBr = "pt-BR";
    private const string EnUs = "en-US";

    /// <summary>Spec unit test 6, plus the shapes Brazilian exports actually use.</summary>
    [Theory]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("-1.234,56", "-1234.56")]
    [InlineData("1.234.567,89", "1234567.89")]
    [InlineData("42,90", "42.90")]
    [InlineData("-42,90", "-42.90")]
    [InlineData("+35,00", "35.00")]
    [InlineData("3000", "3000")]
    [InlineData("1.234", "1234")]
    [InlineData("R$ 1.234,56", "1234.56")]
    [InlineData("-R$ 50,00", "-50.00")]
    [InlineData("R$ -50,00", "-50.00")]
    [InlineData("R$1.234,56", "1234.56")]
    [InlineData("(50,00)", "-50.00")]
    [InlineData("50,00-", "-50.00")]
    [InlineData("  12,5  ", "12.5")]
    [InlineData("0,00", "0")]
    public void Brazilian_amounts_reach_decimal_exactly(string text, string expected)
    {
        Assert.True(AmountParser.TryParse(text, PtBr, out var amount));

        Assert.Equal(Decimal(expected), amount);
    }

    /// <summary>Spec unit test 7.</summary>
    [Theory]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("-1,234.56", "-1234.56")]
    [InlineData("1234.56", "1234.56")]
    [InlineData("$1,234.56", "1234.56")]
    [InlineData("-58.00", "-58.00")]
    [InlineData("1,234", "1234")]
    public void American_amounts_reach_decimal_exactly(string text, string expected)
    {
        Assert.True(AmountParser.TryParse(text, EnUs, out var amount));

        Assert.Equal(Decimal(expected), amount);
    }

    /// <summary>
    /// Spec unit test 8. The declared culture is a contract, not a hint: a Brazilian
    /// number under an American template is a failure, never 1.23456 or 123456.
    /// </summary>
    [Theory]
    [InlineData("1.234,56", EnUs)]
    [InlineData("1,234.56", PtBr)]
    [InlineData("12,34,56", PtBr)]
    [InlineData("1.234.56", EnUs)]
    [InlineData("abc", PtBr)]
    [InlineData("", PtBr)]
    [InlineData("   ", PtBr)]
    [InlineData(null, PtBr)]
    [InlineData("1e3", PtBr)]
    [InlineData("R$", PtBr)]
    [InlineData("--5,00", PtBr)]
    [InlineData("1 234,56", PtBr)]
    public void Wrong_culture_or_garbage_is_a_failure_not_a_wrong_number(string? text, string culture)
    {
        Assert.False(AmountParser.TryParse(text, culture, out var amount));
        Assert.Equal(0m, amount);
    }

    /// <summary>
    /// Spec unit test 9. The parser hands back exactly what was written; the rounding
    /// that turns it into zero — and the rejection — belong to <see cref="Money"/> and
    /// the row validation. Asserted here so nobody "helpfully" rounds in the parser
    /// and hides the zero.
    /// </summary>
    [Fact]
    public void A_sub_cent_amount_parses_exactly_and_rounds_to_zero()
    {
        Assert.True(AmountParser.TryParse("0,001", PtBr, out var amount));

        Assert.Equal(0.001m, amount);

        var rounded = new Money(amount, "BRL").Amount;

        Assert.Equal(0m, rounded);
        Assert.NotNull(TransactionRules.ValidateAmount(rounded));
    }

    /// <summary>
    /// Spec unit test 10. Every member of the import domain, checked by reflection:
    /// no parameter, return type, property or field is a floating-point type.
    /// Principle 4 asserted on the declared types, not on one happy path.
    /// </summary>
    [Fact]
    public void Nothing_in_the_import_domain_is_declared_as_a_floating_point_type()
    {
        var floating = new HashSet<Type> { typeof(double), typeof(float), typeof(Half) };

        bool IsFloating(Type type) =>
            floating.Contains(Nullable.GetUnderlyingType(type) ?? type)
            || (type.IsByRef && IsFloating(type.GetElementType()!))
            || (type.IsArray && IsFloating(type.GetElementType()!))
            || (type.IsGenericType && type.GetGenericArguments().Any(IsFloating));

        const BindingFlags everything = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var offenders = typeof(AmountParser).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(AmountParser).Namespace)
            .SelectMany(type => type.GetMembers(everything))
            .SelectMany(member => member switch
            {
                MethodInfo method => method.GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType),
                ConstructorInfo constructor => constructor.GetParameters()
                    .Select(parameter => parameter.ParameterType),
                PropertyInfo property => [property.PropertyType],
                FieldInfo field => [field.FieldType],
                _ => [],
            })
            .Where(IsFloating)
            .ToList();

        Assert.Empty(offenders);

        var amountParameter = typeof(AmountParser)
            .GetMethod(nameof(AmountParser.TryParse))!
            .GetParameters()[2]
            .ParameterType;

        Assert.Equal(typeof(decimal), amountParameter.GetElementType());
    }

    [Fact]
    public void Only_declared_cultures_are_accepted()
    {
        Assert.Contains(PtBr, AmountParser.SupportedCultures);
        Assert.Contains(EnUs, AmountParser.SupportedCultures);
        Assert.True(AmountParser.IsSupportedCulture(PtBr));
        Assert.False(AmountParser.IsSupportedCulture("pt-br"));
        Assert.False(AmountParser.IsSupportedCulture("xx-XX"));
        Assert.False(AmountParser.IsSupportedCulture(null));

        Assert.ThrowsAny<ArgumentException>(() => AmountParser.TryParse("1,00", "xx-XX", out _));
    }

    /// <summary>Spec unit test 11.</summary>
    [Fact]
    public void Day_month_year_reads_03_04_as_the_third_of_April()
    {
        Assert.True(DateParser.TryParseExact("03/04/2026", "dd/MM/yyyy", out var date));

        Assert.Equal(new DateOnly(2026, 4, 3), date);
    }

    /// <summary>Spec unit test 12. Same text, other format, other day.</summary>
    [Fact]
    public void Month_day_year_reads_03_04_as_the_fourth_of_March()
    {
        Assert.True(DateParser.TryParseExact("03/04/2026", "MM/dd/yyyy", out var date));

        Assert.Equal(new DateOnly(2026, 3, 4), date);
    }

    /// <summary>
    /// Spec unit test 13, the one that matters most. A 31st month is not a date, and
    /// the parser must say so rather than roll it over or quietly try the other order.
    /// </summary>
    [Theory]
    [InlineData("31/12/2026", "MM/dd/yyyy")]
    [InlineData("13/25/2026", "dd/MM/yyyy")]
    [InlineData("30/02/2026", "dd/MM/yyyy")]
    [InlineData("29/02/2026", "dd/MM/yyyy")]
    [InlineData("2026/13/01", "yyyy/MM/dd")]
    [InlineData("03-04-2026", "dd/MM/yyyy")]
    [InlineData("3/4/2026", "dd/MM/yyyy")]
    [InlineData("2026-04-03", "dd/MM/yyyy")]
    [InlineData("03/04/26", "dd/MM/yyyy")]
    [InlineData("", "dd/MM/yyyy")]
    [InlineData(null, "dd/MM/yyyy")]
    [InlineData("Data", "dd/MM/yyyy")]
    [InlineData("03/04/2026 10:22", "dd/MM/yyyy")]
    public void A_date_that_does_not_fit_the_declared_format_fails_loudly(string? text, string format)
    {
        Assert.False(DateParser.TryParseExact(text, format, out var date));
        Assert.Equal(default, date);
    }

    /// <summary>The formats Brazilian and international exports actually use.</summary>
    [Theory]
    [InlineData("2026-04-03", "yyyy-MM-dd", 2026, 4, 3)]
    [InlineData("3/4/2026", "d/M/yyyy", 2026, 4, 3)]
    [InlineData("03/04/26", "dd/MM/yy", 2026, 4, 3)]
    [InlineData("03/04/2026 10:22", "dd/MM/yyyy HH:mm", 2026, 4, 3)]
    [InlineData("03/04/2026 10:22:41", "dd/MM/yyyy HH:mm:ss", 2026, 4, 3)]
    [InlineData("20260403", "yyyyMMdd", 2026, 4, 3)]
    [InlineData("29/02/2028", "dd/MM/yyyy", 2028, 2, 29)]
    [InlineData(" 03/04/2026 ", "dd/MM/yyyy", 2026, 4, 3)]
    public void Common_formats_parse_to_the_declared_day(string text, string format, int year, int month, int day)
    {
        Assert.True(DateParser.TryParseExact(text, format, out var date));

        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("dd/MM/yyyy", true)]
    [InlineData("MM/dd/yyyy", true)]
    [InlineData("yyyy-MM-dd", true)]
    [InlineData("d/M/yyyy", true)]
    [InlineData("dd/MM/yyyy HH:mm", true)]
    [InlineData("dd/MM", false)]
    [InlineData("yyyy", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("HH:mm", false)]
    public void A_format_must_name_day_month_and_year(string? format, bool valid) =>
        Assert.Equal(valid, DateParser.IsValidFormat(format));

    private static decimal Decimal(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
