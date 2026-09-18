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

public readonly record struct SignResolution(decimal? Amount, SignProblem Problem)
{
    public static SignResolution Of(decimal amount) => new(amount, SignProblem.None);

    public static SignResolution Failed(SignProblem problem) => new(null, problem);
}

/// <summary>Spec decision 4: turns what the template's columns hold into one signed amount.</summary>
public static class SignResolver
{
    public static SignResolution Resolve(SignMode mode, decimal? amount, decimal? debit, decimal? credit) =>
        mode switch
        {
            SignMode.Signed => amount is { } signed
                ? SignResolution.Of(signed)
                : SignResolution.Failed(SignProblem.MissingAmount),

            SignMode.SignedInverted => amount is { } inverted
                ? SignResolution.Of(-inverted)
                : SignResolution.Failed(SignProblem.MissingAmount),

            SignMode.DebitCredit => ResolveDebitCredit(debit, credit),

            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown sign mode."),
        };

    /// <summary>
    /// A zero on one side is the same as an empty cell: sheets that print
    /// <c>0,00</c> in the unused column are common. The side that is populated
    /// decides the direction, whatever sign the bank printed the number with.
    /// </summary>
    private static SignResolution ResolveDebitCredit(decimal? debit, decimal? credit)
    {
        var hasDebit = debit is { } leaving && leaving != 0m;
        var hasCredit = credit is { } arriving && arriving != 0m;

        return (hasDebit, hasCredit) switch
        {
            (true, true) => SignResolution.Failed(SignProblem.DebitAndCredit),
            (true, false) => SignResolution.Of(-Math.Abs(debit!.Value)),
            (false, true) => SignResolution.Of(Math.Abs(credit!.Value)),
            (false, false) => SignResolution.Failed(SignProblem.MissingAmount),
        };
    }
}
