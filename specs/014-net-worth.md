# 014 — Net worth over time, compact in the dashboard hero

## Goal

Show what the person owns — money in accounts plus what is invested — as one figure, and how
it moved over the last month, the year and twelve months. The dashboard hero becomes that
figure with three change chips and a sparkline (design "N4, compacto"), backed by a month-end
series from a new read-only endpoint.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | What "accounts" means on a month | The accounts the summary's `total` adds up (005): base-currency (BRL) accounts only. Their opening balances plus every transaction dated **on or before the month's last day**. A foreign-currency account is left out, as it is from the total, and its transactions do not start the series either. |
| 2 | What "investments" means on a month | For **each asset**, `PortfolioDaily.ValueBrl` of its latest row on or before the month's last day; the sum over assets. Per asset rather than one global date, so an asset whose rows stop a few days earlier still counts at its last value — the same reading `GET /api/investments/summary` makes of today. A position sold down to zero adds its zero row. **(review)** |
| 3 | The current month | Month-to-date: its last day is still ahead, so the point holds everything recorded so far, including a transaction already dated later this month. A transaction dated after the current month (the date rule allows a year ahead) is in no point of the series. |
| 4 | Where the series starts | At the first month with **data**: a transaction in an included account, or a `PortfolioDaily` row. Before that there is nothing to show: the opening balance has no date (005 decision 3), so the series does not draw it backwards as a flat line. A person with included accounts but no transaction and no row gets one point, the current month, with the opening balances. No included account and no row: an empty list. The window (`months` ending with the current month) cuts the start further; history before the window still counts in the first point's balance. **(review)** |
| 5 | `months` | Default 24, 1 to 120. Anything else — not an integer, below 1, above 120 — is a 400 naming `months`, not clamped: the endpoint is new, so it can refuse from the start. |
| 6 | "Current month" | The UTC month, as every dashboard route defaults to (005). The web asks for one month more and drops a month that has not begun locally, as the monthly chart already does. |
| 7 | Where the query lives | A fourth Dapper statement in `Application/Dashboard/DashboardQueries.cs`, with `"UserId" = @userId` on every table it reads, so 005's grep test (integration test 6) covers it. The per-asset lookup walks `PortfolioDaily`'s primary key `(UserId, AssetId, Date)` backwards, one probe per month and asset. **No migration.** |
| 8 | The hero's figures | Today's, and each one the same number another screen shows: **Em contas** is the summary's `total` (the account list below adds up to it), **investido** is the series' last `investments` (equal to `GET /api/investments/summary`), and the large figure is their sum. Each chip is that figure minus the total at a reference month's end. The sparkline's last point differs from the large figure only in decision 3's case, a transaction dated after this month. **(review)** |
| 9 | Chips | **1 mês**: against the end of the previous month. **No ano**: against the end of last December. **12 meses**: against the end of the same month a year ago. Signed as `Amount` signs (`+`, U+2212 `−`); the positive tone when ≥ 0, the negative tone otherwise. A chip is **hidden** when its reference month is not in the series — the history is too short to say. In January, 1 mês and No ano compare with the same month-end and both show. |
| 10 | Sparkline | The series' `total`, as a Recharts area in `--primary` fading to the ground, with the first and last month (`jan/25`, `set/26`) under it and a dot on the last point; a tooltip on hover. `aria-hidden`, with an `sr-only` sentence naming the range and both ends. Drawn only with two points or more. |
| 11 | Accounts list | Stays in the hero, compact: a grid under the KPI row (one column on a phone, two from `sm`, three from `lg`), same rows, same `account-balance-<id>` test ids, same card-debt colour. |
| 12 | What leaves | The 012 "Investido … sobre o custo" chip (`hero-invested`, decision 7 of 012). The invested total moves into the "Em contas · investido" line, linked to `/investments`, and keeps the `hero-invested` test id; the unrealised gain stays on `/investments`. **(review)** |
| 13 | Loading, errors, empty | The page waits for the series as it waits for the summary and the monthly chart, and a failed series is the page's error message. The empty state now also needs an empty series: someone with investments and no account sees the dashboard. |

## Out of scope

- Converting foreign-currency accounts into BRL (still 005's "not this feature's problem")
- A dated opening balance, or reconstructing balances before the first transaction
- Liabilities other than what accounts already hold (a card in debt is already negative)
- A full-size net-worth chart page, period selectors, or a breakdown by asset class
- Materialising the series: it is computed on read (005 decision 4)

## Data model changes

None. The endpoint reads `Accounts`, `Transactions` and `PortfolioDaily` as they are.

## API surface

```
GET /api/dashboard/net-worth?months=24
    200 [{ month: "2026-09", accounts: 90482.00, investments: 226693.00, total: 317175.00 }]
        month-end points, oldest first, from the later of the window's start and the
        first month with data (decision 4) to the current month; [] with no data
        total = accounts + investments
    400 months not an integer in 1..120
```

Message (rendered verbatim), under `errors.months`:
`O número de meses deve ser um número inteiro entre 1 e 120.`

## UI behaviour

Route `/`, the hero card (`components/dashboard/Balances.tsx`), after mockup N4:

- **Left:** `Patrimônio · contas + investimentos` (the section heading); the large figure in the
  display face, `R$` and the cents muted, as 012's hero draws it (`net-worth-total`); the chips
  `1 mês +7.194,00`, `No ano +62.524,00`, `12 meses +71.371,00` (`net-worth-change-month`,
  `-year`, `-twelve`); then `Em contas R$ +90.482,00 · investido R$ +226.693,00`, the first
  figure being `total-balance`'s `Amount`. The invested half is left out when nothing is
  invested.
- **Right:** the sparkline (`net-worth-chart`) with its two month labels.
- **Below both:** the accounts list (decision 11).

At 390 px the columns stack, the chips wrap, and nothing scrolls sideways (grid children take
`min-w-0`). Both themes, from the theme tokens only.

## Test plan

### Integration — `api.tests/Integration`

1. Month-end values: an opening balance and transactions on a month's last day and the next
   month's first day land in the right points; each point is opening + everything up to its end
2. The series starts at the first month with data, not at the window's start
3. Investments per asset from the latest row on or before each month end: a row after the month
   end is ignored, an asset whose rows stop earlier counts at its last value; `total` is the sum;
   the last point equals the summary's `total` and `GET /api/investments/summary`'s `totalBrl`
4. A foreign-currency account (opening balance and transactions) is left out, and its older
   transaction does not start the series
5. Isolation: B sees none of A's accounts, transactions or portfolio rows — `[]` with nothing,
   one current-month point with only an account of B's own
6. The window: 24 by default ending with the current month, history before it still in the first
   point's balance; `months=1` is the current month alone; `months=120` reaches back to the first
   data month
7. `months` of `0`, `121`, `-1`, `abc`, `12.5` → 400 with the message above
8. Every money column of the statement maps to `decimal` (005's grep test 6 covers its
   `UserId` predicate unchanged)

### Unit — `web`

9. The large figure is Em contas + investido; the chips come from the stubbed series
   (1 mês, No ano and 12 meses, signed, the negative tone on a fall)
10. `total-balance` holds the accounts total as an `Amount` (`+1.000,00`), and investido shows the
    last point's investments, linked to `/investments`; with nothing invested it is not shown
11. A short series hides the chips it cannot compute, and a single point draws no sparkline
12. `lib/netWorth`: the reference months, including January, where 1 mês and No ano coincide
13. The dashboard stays in the empty state with no accounts, a year of zeros and an empty series,
    and leaves it when only the series has points

Existing tests keep passing; 012's test 6 (`hero-invested` with the unrealised gain) is
replaced by test 10.

### E2E — `web/e2e`

14. An account with 1.000,00, an expense of 100,00 last month and 42,90 today → the hero shows
    `+857,10` as net worth and as Em contas, the 1 mês chip `−42,90`, no 12 meses chip, and the
    sparkline

All existing E2E tests keep passing unchanged.

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual:

1. With real accounts and positions, the large figure equals the account total plus the total on
   `/investments`
2. Last month's point (hover the sparkline) matches the bank balances at that month's end plus
   the portfolio's value that day, where opening balances are right (005 manual step 1)
3. Both themes at 1440 px and at 390 px: nothing clipped, nothing scrolling sideways

## Definition of done

- Both verify scripts green
- Tests 1–14 exist and pass
- No migration; no `double` or `float` in the new API code
- Screenshots of the hero in both themes, at 1440 px and 390 px
