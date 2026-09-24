namespace Finance.Api.Domain.Returns;

/// <summary>
/// How a benchmark's stored values accumulate into an index (008, decision 6). Declared
/// per benchmark in <c>Returns:Benchmarks</c>, and checked at boot against the unit 006
/// records for the series it reads.
/// </summary>
public enum BenchmarkType
{
    /// <summary>A percentage for one day, compounded on each row: CDI, SELIC.</summary>
    DailyRate = 0,

    /// <summary>A percentage for one month, compounded on each row: IPCA, with an optional annual spread.</summary>
    MonthlyRate = 1,

    /// <summary>A level, whose return is a ratio of two values: USDBRL, IVVB11.</summary>
    Level = 2,
}
