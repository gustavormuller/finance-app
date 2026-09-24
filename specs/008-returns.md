# 008 — Returns

## Goal

Answer the question the whole investments module exists for: how did my money actually do, and how does that compare to what I could have had. TWR for the fair benchmark comparison, XIRR for the real return including timing, and the difference between them made visible.

This is the technical core of the project (ADR-017). It gets value objects, pure functions, and the most exhaustive unit tests in the codebase.

## Decisions made in this spec

| # | Question | Decision |
|---|---|---|
| 1 | TWR method | **Daily linking** over `PortfolioDaily`: `r_d = (V_d + D_d − F_d) / V_{d−1} − 1`, `TWR = Π(1 + r_d) − 1`. Sub-period logic is a special case of this and harder to get right. |
| 2 | Dividends in TWR | **Return, not external flow.** Added to the day's end value. This matches "total return" and makes the comparison with CDI (which is total return) fair. |
| 3 | External flows | `Buy` = inflow `qty × price + fees`. `Sell` = outflow `qty × price − fees`. Nothing else. |
| 4 | XIRR flows | `−(buys + fees)` on buy dates, `+sells − fees` on sell dates, `+dividends` on their dates, `+current value` today. Day count ACT/365. Annualised. |
| 5 | XIRR solver | Newton–Raphson from 10%, tolerance `1e-8`, max 100 iterations; on divergence or non-convergence, **bisection** on `[−0.99, 10]`. All-same-sign flows → `null`, not an exception. |
| 6 | Benchmark accumulation | Each benchmark declares its **type** in config: `DailyRate` (compound `1 + v/100` on each row), `MonthlyRate` (compound monthly), `Level` (ratio to start). The type must match the unit recorded in 006. |
| 7 | Base 100 | All series normalised to 100 at the period start. Portfolio series is the TWR index, not `ValueBrl` — value moves with deposits, the index does not. |
| 8 | FX decomposition | `(1 + r_total) = (1 + r_native)(1 + r_fx)`. Report all three. Identity asserted in tests to `1e-10`. |
| 9 | Periods | Since inception (default), YTD, 12m, custom. A period starting before the first movement is clamped. |
| 10 | Precision | `decimal` throughout. Newton on `decimal` converges fine at this scale. |

## Out of scope

- Per-lot or FIFO returns
- Risk metrics — volatility, drawdown, Sharpe
- Tax-adjusted returns
- Benchmarks beyond the five from 006
- Projections or forecasts
- Alerts on performance

## Configuration

```
Returns:Benchmarks:
  CDI:     { Type: DailyRate,   Label: "CDI" }
  SELIC:   { Type: DailyRate,   Label: "SELIC" }
  IPCA6:   { Type: MonthlyRate, Label: "IPCA + 6%", Source: IPCA, Spread: 6 }
  USDBRL:  { Type: Level,       Label: "Dólar" }
  IVVB11:  { Type: Level,       Label: "S&P 500 (IVVB11)" }
```

`IPCA6` is derived: monthly IPCA compounded with a 6% a.a. spread, itself compounded monthly as `(1.06)^(1/12) − 1`. It is the purchasing-power reference from ARCHITECTURE.md.

## Domain — `Domain/Returns/`

All pure. Inputs are plain records; no EF, no clock, no configuration objects.

### Value objects

```csharp
public readonly record struct Rate(decimal Value);          // 0.1234 = 12.34%
public readonly record struct CashFlow(DateOnly Date, Money Amount);
public readonly record struct DailyPoint(DateOnly Date, decimal Value);
```

### `TimeWeightedReturn`

Input: `PortfolioDaily` rows for the period (one asset or the whole portfolio summed by day), plus movements in the period.

For each day `d` after the first:
- `V_d` = `ValueBrl` on `d`
- `D_d` = dividends + JCP received on `d`, in BRL
- `F_d` = net external flow on `d`, in BRL (buys positive, sells negative)
- `r_d = (V_d + D_d − F_d) / V_{d−1} − 1`

Skip days where `V_{d−1} = 0`. Chain: `Π(1 + r_d) − 1`.

Also returns the **index series** — `100 × Π(1 + r_i)` up to each day — for the chart.

Annualise when the period exceeds one year: `(1 + twr)^(365 / days) − 1`. Report both.

### `MoneyWeightedReturn`

Input: cash flows per decision 4. Output: annualised rate or `null`.

NPV: `Σ CF_i / (1 + r)^(t_i / 365)` where `t_i` is days from the first flow.

Newton: `r_{n+1} = r_n − NPV(r_n) / NPV′(r_n)`. Bail to bisection if `|r|` leaves `[−0.99, 10]` or the derivative is near zero. Bisection: standard, 200 iterations, `1e-8`.

### `BenchmarkAccumulator`

Input: benchmark points, type, period. Output: index series base 100.

- `DailyRate`: `index_d = index_{d−1} × (1 + v_d / 100)` on rows present; days without a row hold the previous index
- `MonthlyRate`: same, monthly, with the spread applied if configured
- `Level`: `index_d = 100 × v_d / v_start`, carried forward on gaps

### `FxDecomposition`

For a non-BRL asset over a period:
- `r_native` = TWR computed on `Price` (native) and native flows
- `r_total` = TWR computed on `ValueBrl` and BRL flows
- `r_fx = (1 + r_total) / (1 + r_native) − 1`

The identity holds by construction; the test checks the implementation did not break it.

### Portfolio aggregation

Portfolio-level `PortfolioDaily` is the per-day sum of `ValueBrl` across all the user's assets. Days where only some assets have rows are still summed — an asset before its first movement contributes zero. Movements and dividends are summed the same way.

## API surface

```
GET /api/returns/portfolio?period=inception|ytd|12m&from&to
    200 {
      period: { from, to, days },
      twr: { total, annualised },
      xirr: 0.1234 | null,
      timingEffect: xirr − twr.annualised,
      benchmarks: { CDI: { total, annualised }, IPCA6: ..., USDBRL: ..., IVVB11: ... },
      series: [{ date, portfolio, CDI, IPCA6, USDBRL, IVVB11 }]     base 100, weekly sampled
    }

GET /api/returns/assets/{id}?period=...
    200 {
      period, twr, xirr, timingEffect,
      fx: { native, fx, total } | null,       null for BRL assets
      benchmarks, series
    }
```

`series` is sampled to at most 260 points (weekly over 5 years) to keep payloads small; the chart does not need every day.

`timingEffect` is the headline number: how much the user's aporte timing helped or hurt versus a passive investor in the same assets.

## UI behaviour

Route `/investments/returns`, linked from the positions page.

**Period selector** — inception, YTD, 12m, custom range.

**Headline** — three numbers side by side: TWR, XIRR, and the difference labelled *"Efeito do timing"* with one sentence explaining it: *"Quanto suas decisões de quando aportar ajudaram ou atrapalharam."* Positive green, negative the same red as expenses.

**Comparison chart** — Recharts line, base 100: portfolio index in the primary colour, benchmarks in muted colours, toggleable. Hover shows every series' value on that date.

**Benchmarks table** — each benchmark's total and annualised return for the period beside the portfolio's, with the difference.

**Per asset** — table with TWR, XIRR, and for USD assets the three-way decomposition. Click through to the asset's own returns page.

## Test plan

This is where the exhaustive tests live. Every scenario below is hand-computable and the expected value is written into the test, not derived by running the code.

### Unit — `api.tests/Unit`, no database

**TWR — properties:**
1. No flows, value 100 → 110 → `0.10`
2. Flow-timing independence: scenario A deposits 1000 on day 1; scenario B deposits 500 on day 1 and 500 on day 5; identical asset prices → **identical TWR**. This is the property that justifies TWR's existence.
3. Deposit 1000 on day 1, +100% on day 2, deposit 10 000 on day 3, −50% on day 4 → TWR `0.00` (`2 × 0.5 − 1`), even though the investor lost money
4. Dividend of 5 on day 3 with value 100 → 100 → day-3 return `0.05`
5. Sell on day 3 → treated as outflow, return unaffected by the withdrawal
6. First day with `V_{d−1} = 0` is skipped, not divided by zero
7. Annualisation: `0.21` over 730 days → `0.10` annualised
8. Index series starts at exactly `100.00000000`

**XIRR:**
9. −1000 on day 0, +1100 on day 365 → `0.10` to `1e-8`
10. −1000 on day 0, +1210 on day 730 → `0.10` (annualisation correct)
11. −1000 day 0, −1000 day 182, +2300 day 365 → hand-computed value, to `1e-6`
12. All flows negative → `null`
13. A flow set that makes Newton diverge (large late inflow) → bisection converges to the same value as a spreadsheet's XIRR, to `1e-6`
14. Single flow → `null`

**XIRR vs TWR — the timing effect:**
15. Scenario 3 above: XIRR is negative, TWR is zero, `timingEffect` is negative — the investor's timing hurt
16. Mirror scenario (big deposit before the gain) → `timingEffect` positive

**Benchmarks:**
17. `DailyRate` with three rows of `0.05` → `100 × 1.0005³`
18. `DailyRate` gap day holds the previous index
19. `MonthlyRate` with spread: IPCA 0.5% + 6% a.a. → monthly factor `1.005 × 1.06^(1/12)`
20. `Level`: `v_start = 5.0`, `v_end = 5.5` → index `110`
21. Type mismatch (a `Level` series fed to `DailyRate`) is a config error surfaced at startup, not a wrong number

**FX:**
22. Native `+10%`, FX `+10%` → total `+21%`; identity holds to `1e-10`
23. Native `+10%`, FX `−10%` → total `−1%`
24. BRL asset → `fx` is `null`

**Aggregation:**
25. Two assets, one starting later → portfolio series is the sum with zero before the second starts
26. Portfolio TWR with two assets equals the value-weighted chaining, not the average of the two TWRs

### Integration — `api.tests/Integration`

27. Isolation: B's returns are empty when only A holds anything
28. `period=ytd` from and to are correct on a mid-year call
29. `from` before first movement → clamped to first movement
30. `series` sampled to ≤ 260 points for a 5-year range
31. Benchmark rows missing for the period → that benchmark `null`, others unaffected
32. End to end: seed 007 rows + 006 benchmarks with known values → endpoint returns the hand-computed TWR, XIRR, and benchmark returns

### Unit — `web`

33. Timing effect renders with the explanatory sentence and sign colour
34. Benchmark toggles hide and show series
35. Period change refetches

### E2E

36. Buy → value change via fake prices → returns page shows non-zero TWR and the chart

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual, against your real portfolio:

1. Open the returns page, since inception
2. Compare XIRR against your broker's "rentabilidade" if it shows one — many show MWR; they should be close but not identical (fees, dividend timing)
3. Put the same cash flows into a spreadsheet's `XIRR()` — must match to 4 decimals
4. Confirm CDI accumulated over the period against a public CDI calculator for the same dates
5. Look at the timing effect. Decide whether the sign matches your memory of when you bought

Step 3 is the external authority for XIRR. Step 4 is the one for the benchmark accumulation.

## Definition of done

- Both verify scripts green
- Tests 2, 3, 9, 13, 22 pass — these are the load-bearing ones
- Manual steps 1–5 behave as described; XIRR matches a spreadsheet to 4 decimals
- No `double` anywhere in `Domain/Returns/`
- Benchmark types in config match the units recorded in 006
