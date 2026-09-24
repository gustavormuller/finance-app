namespace Finance.Api.Domain.Import;

/// <summary>
/// Which rung of ADR-012's cascade put a staged row's category there, or that the user
/// did. 009 sends only <see cref="Default"/> rows to the AI (spec decision 4, test 18) and
/// marks <see cref="Ai"/> rows in the preview. Stored as <c>int</c>, values written down.
/// </summary>
public enum CategorySource
{
    /// <summary>Nothing resolved a category, or the row was staged before 009 recorded this.</summary>
    None = 0,

    /// <summary>Rung 2: the category last used for the same normalized description.</summary>
    History = 1,

    /// <summary>The sign default, "Outros" or "Outras receitas". The rows rung 3 may change.</summary>
    Default = 2,

    /// <summary>Rung 3: suggested by the AI. Still only a suggestion until the commit.</summary>
    Ai = 3,

    /// <summary>Chosen by the user in the preview.</summary>
    User = 4,
}
