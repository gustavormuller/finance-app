using Finance.Api.Domain.Import;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec unit tests 1-5. The key that dedupe and the history lookup share, so a
/// change here silently changes both.
/// </summary>
public sealed class DescriptionNormalizerTests
{
    /// <summary>Spec unit test 1, the example the spec is built around.</summary>
    [Fact]
    public void Same_merchant_on_different_days_is_one_key()
    {
        Assert.Equal("PAG IFOOD", DescriptionNormalizer.Normalize("PAG*IFOOD 12/03"));
        Assert.Equal("PAG IFOOD", DescriptionNormalizer.Normalize("PAG*IFOOD  15/04"));
    }

    /// <summary>
    /// The shapes real Brazilian statements take: Pix with a document number, card
    /// purchases with instalment counters, subscriptions with a trailing id, and the
    /// separators each bank favours.
    /// </summary>
    [Theory]
    [InlineData("Pix enviado - 12.345.678/0001-90 - MERCADO SAO JOSE", "PIX ENVIADO MERCADO SAO JOSE")]
    [InlineData("Compra no débito - UBER *TRIP 04/09", "COMPRA NO DEBITO UBER TRIP")]
    [InlineData("NETFLIX.COM 3/12", "NETFLIX COM")]
    [InlineData("Transferência recebida pelo Pix - JOÃO DA SILVA - 123.456.789-00", "TRANSFERENCIA RECEBIDA PELO PIX JOAO DA SILVA")]
    [InlineData("PAGTO FATURA #4521", "PAGTO FATURA")]
    [InlineData("AMAZON BR 2/6 (parcela)", "AMAZON BR (PARCELA)")]
    [InlineData("SPOTIFY: assinatura", "SPOTIFY ASSINATURA")]
    [InlineData("TED 341 0001 12345-6 FULANO", "TED FULANO")]
    public void Strips_ids_dates_and_separators_from_real_descriptions(string raw, string expected) =>
        Assert.Equal(expected, DescriptionNormalizer.Normalize(raw));

    /// <summary>Spec unit test 2.</summary>
    [Theory]
    [InlineData("Alimentação", "ALIMENTACAO")]
    [InlineData("pão de açúcar", "PAO DE ACUCAR")]
    [InlineData("Ação Ética Übér Ñandu Çedilha", "ACAO ETICA UBER NANDU CEDILHA")]
    [InlineData("crème brûlée", "CREME BRULEE")]
    public void Accents_are_stripped(string raw, string expected) =>
        Assert.Equal(expected, DescriptionNormalizer.Normalize(raw));

    /// <summary>Spec unit test 3.</summary>
    [Theory]
    [InlineData("ifood", "IFOOD")]
    [InlineData("iFood", "IFOOD")]
    [InlineData("IFOOD", "IFOOD")]
    public void Case_does_not_matter(string raw, string expected) =>
        Assert.Equal(expected, DescriptionNormalizer.Normalize(raw));

    /// <summary>Spec unit test 4.</summary>
    [Fact]
    public void Long_input_is_truncated_to_the_column_width()
    {
        var raw = string.Join(' ', Enumerable.Repeat("PALAVRA", 100));

        var normalized = DescriptionNormalizer.Normalize(raw);

        Assert.True(normalized.Length <= DescriptionNormalizer.MaxLength);
        Assert.True(normalized.Length >= DescriptionNormalizer.MaxLength - 8, "truncation happens at the limit, not far below it");
        Assert.False(normalized.EndsWith(' '), "truncation must not leave a trailing space");
    }

    /// <summary>Spec unit test 5.</summary>
    [Theory]
    [InlineData("PAG*IFOOD 12/03")]
    [InlineData("  Pix   enviado  ")]
    [InlineData("Ação 123 #45 ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12/03/2026")]
    public void Normalizing_twice_changes_nothing(string raw)
    {
        var once = DescriptionNormalizer.Normalize(raw);

        Assert.Equal(once, DescriptionNormalizer.Normalize(once));
    }

    [Fact]
    public void Normalizing_a_truncated_result_again_changes_nothing()
    {
        var once = DescriptionNormalizer.Normalize(string.Join(' ', Enumerable.Repeat("PALAVRA", 100)));

        Assert.Equal(once, DescriptionNormalizer.Normalize(once));
    }

    /// <summary>
    /// A description that was nothing but digits and punctuation is empty afterwards,
    /// which the row validation reports as "Descrição vazia" rather than importing a
    /// row nobody can recognise.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12/03/2026")]
    [InlineData("*** --- ###")]
    [InlineData("1.234,56")]
    public void Nothing_recognisable_normalizes_to_empty(string? raw) =>
        Assert.Equal("", DescriptionNormalizer.Normalize(raw));

    [Fact]
    public void Whitespace_runs_including_tabs_and_newlines_collapse_to_one_space() =>
        Assert.Equal("A B C", DescriptionNormalizer.Normalize("A \t B\r\n  C"));
}
