namespace Finance.Api.Domain.Returns;

/// <summary>
/// 008's headline number: <c>timingEffect = xirr - twr.annualised</c>. XIRR is the return
/// of the money, timing included; TWR is the return of a passive investor in the same
/// assets. The difference is what the choice of when to deposit added or took away.
/// </summary>
public static class TimingEffect
{
    /// <summary>
    /// The difference, or <c>null</c> when either side is: XIRR with no root in range, or a
    /// TWR with no period to annualise over or past the <c>decimal</c> range.
    /// </summary>
    public static Rate? Of(Rate? xirr, Rate? twrAnnualised) =>
        xirr is { } money && twrAnnualised is { } time ? new Rate(money.Value - time.Value) : null;
}
