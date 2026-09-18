namespace Finance.Api.Domain.Import;

/// <summary>Why <see cref="SignResolver"/> could not produce an amount.</summary>
public enum SignProblem
{
    None = 0,

    /// <summary>No column had a value to resolve.</summary>
    MissingAmount = 1,

    /// <summary>Debit and credit both populated: the row does not say which way the money went.</summary>
    DebitAndCredit = 2,
}

public readonly record struct SignResolution(decimal? Amount, SignProblem Problem);

/// <summary>Spec decision 4: turns what the template's columns hold into one signed amount.</summary>
public static class SignResolver
{
    public static SignResolution Resolve(SignMode mode, decimal? amount, decimal? debit, decimal? credit) =>
        throw new NotImplementedException();
}
