namespace Finance.Api.Application.MarketData;

/// <summary>A day's stored close and adjusted close, as the rebasing check compares them.</summary>
public readonly record struct StoredPrice(decimal Close, decimal? AdjustedClose);

/// <summary>
/// The rebasing check (025, decisions 12 and 13). A provider that revises history (Yahoo
/// rebases its adjusted close at every dividend and split) is read again over the last days
/// stored; <see cref="Revised"/> says whether that read disagrees with what is stored, and
/// <see cref="EarliestChange"/> which stored close a write changes first, so the snapshots
/// can be rebuilt from there. Pure: the caller loads both sides.
/// </summary>
public static class PriceRevision
{
    /// <summary>The scale of <c>Prices</c> and <c>Benchmarks</c>: a fetched value is compared as it would be stored.</summary>
    private const int ColumnScale = 8;

    /// <summary>Float noise in a provider's JSON stays far inside a millionth; a dividend's rebase does not.</summary>
    private const decimal RelativeTolerance = 0.000001m;

    /// <summary>Two units of the column's last decimal, for values so small that a millionth of them is nothing.</summary>
    private const decimal AbsoluteTolerance = 0.00000002m;

    public static bool Differs(decimal stored, decimal fetched) =>
        Math.Abs(stored - Math.Round(fetched, ColumnScale, MidpointRounding.AwayFromZero))
            > Math.Max(AbsoluteTolerance, Math.Abs(stored) * RelativeTolerance);

    /// <summary>A missing value against a present one differs; two missing do not.</summary>
    public static bool Differs(decimal? stored, decimal? fetched) => (stored, fetched) switch
    {
        (null, null) => false,
        ({ } before, { } now) => Differs(before, now),
        _ => true,
    };

    /// <summary>Whether a day both stored and fetched now reads differently, in its close or its adjusted close.</summary>
    public static bool Revised(IReadOnlyDictionary<DateOnly, StoredPrice> stored, IEnumerable<DailyClose> fetched) =>
        fetched.Any(close => stored.TryGetValue(close.Date, out var price)
            && (Differs(price.Close, close.Close) || Differs(price.AdjustedClose, close.AdjustedClose)));

    /// <summary>
    /// The earliest day, on or before the latest stored one, whose stored close writing
    /// <paramref name="fetched"/> changes: a different close, a day filled in, older history,
    /// and, when <paramref name="replacing"/>, a stored day the read no longer has. Days after
    /// the latest stored one are new, not changed. Null when nothing changes.
    /// </summary>
    public static DateOnly? EarliestChange(
        IReadOnlyDictionary<DateOnly, decimal> storedCloses, IEnumerable<DailyClose> fetched, bool replacing)
    {
        if (storedCloses.Count == 0)
        {
            return null;
        }

        var latest = storedCloses.Keys.Max();
        var read = new HashSet<DateOnly>();
        DateOnly? earliest = null;
        foreach (var close in fetched)
        {
            read.Add(close.Date);
            if (close.Date <= latest
                && (!storedCloses.TryGetValue(close.Date, out var stored) || Differs(stored, close.Close))
                && (earliest is null || close.Date < earliest))
            {
                earliest = close.Date;
            }
        }

        if (replacing)
        {
            foreach (var day in storedCloses.Keys.Where(day => !read.Contains(day) && (earliest is null || day < earliest)))
            {
                earliest = day;
            }
        }

        return earliest;
    }
}
