namespace Finance.Api.Domain.Import;

/// <summary>
/// ADR-012 rung 3, the pure half: what is sent to the model, how many tokens it may answer
/// with, and what of its answer is kept (009, "CategorisationCascade — rung 3").
/// </summary>
public static class AiCategorisation
{
    /// <summary>
    /// The model's answer, read as untrusted text: a <c>{ rowId: categoryId }</c> object,
    /// kept only where the row was sent, the category is one of the user's, and its kind
    /// agrees with the row's sign (003, rule 3). Anything else is dropped, never thrown.
    /// </summary>
    /// <param name="rows">The rows sent, by id, with their signed amounts.</param>
    /// <param name="categories">The user's categories.</param>
    public static IReadOnlyDictionary<Guid, Guid> Parse(
        string? text,
        IReadOnlyDictionary<Guid, decimal> rows,
        IReadOnlyCollection<CategoryChoice> categories) =>
        throw new NotImplementedException();
}
