# 026 — Compare: any instruments side by side

## Goal

"Quero poder comparar ação do Itaú com dólar, por exemplo. Bitcoin USD com WEGE3." A page,
`/compare`, draws two to six series (any catalogue asset, held or not, and the stored
benchmark series) rebased to 100 on a common start, over long periods, each in its own
currency or converted to R$ or US$ at the daily PTAX.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | Where | Route `/compare`, page title "Comparar". A nav item **Comparar** right after **Investimentos**, with the `ChartLine` icon. |
| 2 | What can be compared | 2 to 6 series. Any row of the shared catalogue (`MarketAssets`), whatever its class, held or not, active or not: `asset:{marketAssetId}`. Any benchmark series 006 stores (`MarketData:Bcb:Series` and `MarketData:PriceBenchmarks`): `benchmark:{code}`, today CDI, SELIC, IPCA, USDBRL and IVVB11. IPCA + 6% is a returns-page reference (008's `IPCA6`), not a stored series, so it is not offered. **(review)** |
| 3 | How a series becomes a line | A **price** row contributes the value of `ComparisonPrice.ValueOf` (decision 4). A **level** benchmark (`Level`: USDBRL, IVVB11) contributes its value. A **rate** benchmark (`PercentPerDay`: CDI, SELIC; `PercentPerMonth`: IPCA) is accumulated into an index by 008's `BenchmarkAccumulator`, with its convention: a rate compounds on its own date, so IPCA's month compounds on the 1st, and a day without a row holds the index. A rate series can start on the day before its first row. A non-positive price or level is not an observation. |
| 4 | Price source | One function, `Application/Compare/ComparisonPrice.ValueOf(Price)`, decides what a price row contributes. Today it is `price.Close`. Once 025 adds `Price.AdjustedClose`, the lead flips that one line to `price.AdjustedClose ?? price.Close`: the total return, dividends reinvested. Nothing else changes. The coalesce is per row, so a series whose rows mix adjusted and unadjusted closes would jump where they meet. |
| 5 | Periods | **1M, 6M, YTD, 1A, 5A, 10A, Máx**, and a custom from/to (`period=1m\|6m\|ytd\|1y\|5y\|10y\|max\|custom`). A preset's start is counted back from today (UTC, as 008): one month, six months, one, five or ten years by the calendar (the 31st goes to a shorter month's last day); YTD starts on 31 December of the previous year, so the year's first close counts. **Máx** has no start of its own. Custom: `from` optional (missing, it is Máx's), `to` optional (missing, today), `from` before `to`. Dates alone mean custom; dates beside a preset are refused, as in 008. The default, with no period and no dates, is **5A**. **(review)** |
| 6 | The common period | A series *has data in the window* when its own data covers part of it: `max(first, from) < min(last, to)`. The others are left out of the chart and named (decision 9). Of the rest, the **start** is the latest of the window's start and every series' first day, so every series has a value there; the **end** is the latest observation of any of them, not after `to`. Máx is therefore where the latest-starting series begins. When that moves the start later than the period's own (always under Máx), the page names the series that begins there: "Começa em 17/09/2014, primeiro dia com dados de BTC." A preset whose start merely coincides with a first day (5A over a 5-year backfill) says nothing. |
| 7 | Calendars | B3, NYSE, crypto every day, BCB business days: the points are the **union** of the dates on which any drawn series has an observation, plus the start and the end, and PTAX's dates when a series is converted. Each series is carried forward from its last observation. No interpolation. A series whose data ends before the end is drawn flat from there, and its row reads "dados até 01/08/2026" when that is more than 7 days before the end (IPCA is a month or two behind; a weekend is not worth a note). **(review)** |
| 8 | Rebasing and figures | Every series is 100 on the start: `100 × v(d) / v(start)`. Its change over the period is `v(end) / v(start) − 1`, its annualised rate `(1 + change)^(365 / days) − 1` through 008's `TimeWeightedReturn.Annualise` (`null` past `decimal`'s range), with `days` = end − start. Everything in `decimal`; on the wire, rates to 10 places and index points to 6, half to even, as 008. |
| 9 | Empty and error states | Fewer than 2 series chosen: the page asks for more and sends nothing (the API answers 400). A series without data in the window: left out of the chart, its row reads "Sem dados no período" and a note names it. No series with data: "Nenhuma das séries tem dados no período escolhido." **No common period**, when a series' data ends on or before the start (or the start is not before the end): nothing is drawn, the page says "As séries escolhidas não têm um período em comum." and lists where each series' data begins and ends. |
| 10 | Currency | **Moeda original** (default): each series in its own currency, which is exactly "Bitcoin em dólar × WEGE3". **Converter para R$** multiplies a USD series by the day's PTAX; **Converter para US$** divides a BRL series by it. PTAX is the `USDBRL` benchmark (BCB SGS 1, reais per dollar), carried forward over days without a quote. Every benchmark is a BRL series (CDI, SELIC and IPCA are Brazilian rates, USDBRL is reais per dollar, IVVB11 trades in reais), so in US$ the dollar itself is a flat line. A converted series starts no earlier than PTAX. A series in any other currency cannot be converted: a 400 on `currency`. **(review)** |
| 11 | Annualised | Shown only for a period longer than a year, as on the returns page (008's `showsAnnualised`): a month's change at a year's rate misleads. The API always sends it. **(review)** |
| 12 | Long ranges | Every point up to 3 years (1 096 days). Up to 10 years (3 653 days), one point a week: the last date of each ISO week. Beyond, one a month: the last date of each month. The start and the end are always kept, and the changes are computed before sampling. 5A is about 260 points, Máx since 2000 about 310. **(review)** |
| 13 | Log scale | A toggle **Linear / Log** for the Y axis, linear by default. BTC since 2014 reaches tens of thousands on base 100, which flattens WEGE3 against the floor on a linear axis. **(review)** |
| 14 | The URL | The selection and the settings live in the search params: `series` (the keys, comma-separated, in the order chosen), `period`, `from`, `to`, `currency` (`BRL` or `USD`; absent is original), `scale` (`log`; absent is linear). A change replaces the history entry instead of pushing one, so Back leaves the page. A bookmark or a reload rebuilds the same comparison. |
| 15 | Picking series | One search field, by ticker or name. Catalogue assets come from 006's `GET /api/market-data/assets?q=` as you type (250 ms after the last key); benchmarks are matched in the browser by code or pt-BR name, and listed first. A result is a button "Adicionar CDI"; a chosen series is a chip with "Remover CDI". At 6 the search is disabled. The page offers nothing to register or sync: that stays on `/market-data` and `/investments`. |
| 16 | Access | `GET /api/compare` requires a session, like every market-data read. It reads only shared market data (no `UserId`, no filter) and only the local Postgres (ARCHITECTURE.md principle 6). |
| 17 | E2E fakes | `FakeMarketDataProviders`' benchmark series gets a value, 0.05, on **every day** from `from` to `to` (it had one, on `to`), so the E2E has a CDI and a PTAX history to compare and convert with. The latest USDBRL is still 0.05, so 016's E2E is unchanged. |
| 18 | Layout | Both themes through the existing tokens. Up to six lines in `--chart-1` to `--chart-5` and `--muted-foreground`, the fourth to sixth dashed as well, so hue is never the only carrier. At 390 px nothing scrolls sideways. |

## Out of scope

- Adjusted closes and full history: 025. This spec only leaves the one-line switch (decision 4).
- Currencies other than R$ and US$, and a converted rate series' own currency (every benchmark is BRL).
- Risk figures: volatility, drawdown, correlation.
- IPCA + spread, or any reference not stored by 006.
- Saving comparisons on the server, or sharing beyond the URL.
- Registering a ticker or syncing from this page.

## Data model changes

None. No migration. Reads `MarketAssets`, `Prices` and `Benchmarks`.

## API surface

```
GET /api/compare?series=asset:{id},benchmark:{code}[,…]
                &period=1m|6m|ytd|1y|5y|10y|max|custom
                &from=YYYY-MM-DD&to=YYYY-MM-DD
                &currency=original|BRL|USD
    authenticated
    200 {
      currency: "original" | "BRL" | "USD",
      period: { from: "2021-10-06", to: "2026-10-05", days: 1825, startMoved: false } | null,
              from is the start, at 100; startMoved when that is a series' first day, later than
              the period's own start (always under Máx); null when nothing is drawn (decision 9)
      series: [{                                  in the order requested
        key: "asset:3f2c…" | "benchmark:CDI",     normalised: lower-case id, upper-case code
        kind: "asset" | "benchmark",
        ticker, name, class,                      the catalogue's; null for a benchmark
        code,                                     null for an asset
        currency: "BRL" | "USD",                  its own; drawn in the response's currency unless original
        firstDate, lastDate,                      where its data begins (in the drawn currency) and its
                                                  last observation; null when it has none
        hasData: true,                            its data covers part of the window (decision 6)
        change: 0.1234 | null,                    over the period; null when it is not drawn
        annualised: 0.0234 | null
      }],
      points: [{ date: "2021-10-06", values: [100, 100, null] }]
              one value per series, in the series' order; null for a series not drawn;
              sampled as decision 12
    }
    400  problem details naming the field, pt-BR:
         series    missing, fewer than 2 or more than 6, malformed, repeated, an unknown asset id,
                   an unknown benchmark code
         period    not one of the values; dates beside a preset
         from, to  not AAAA-MM-DD; from not before to
         currency  not one of the values; a series whose currency cannot be converted
    401  no session
```

Everything is `decimal` until it is serialised.

## UI behaviour

`/compare`, header "Comparar", "Ativos e índices lado a lado, base 100."

1. **Series** (`aria-labelledby` "Séries"): the chosen chips, each with its line's colour, the search field
   "Buscar ativo ou índice" and its results (decision 15).
2. **Controls**: the period group (`role="group"`, "Período": 1M, 6M, YTD, 1A, 5A, 10A, Máx, Personalizado, each
   `aria-pressed`); Personalizado opens De/Até and "Aplicar". The currency group ("Moeda": Moeda original,
   Converter para R$, Converter para US$). The scale group ("Escala": Linear, Log).
3. **States** (decision 9), in place of the chart: "Escolha pelo menos duas séries para comparar." with
   one or none chosen; "Carregando…"; a 400's messages as an alert; "Não foi possível carregar a
   comparação. Recarregue a página para tentar de novo." for anything else.
4. **Result**: the period line "06/10/2021 a 05/10/2026 · 1.825 dias", the start note when it applies
   (decision 6), the note naming series without data, the chart (`data-testid="compare-chart"`, a line per
   drawn series, hover lists every series on the date, a screen-reader table at each month's last point),
   and the table (`data-testid="compare-table"`): Série (colour, ticker or name, the catalogue name under
   it, and its currency: "R$", "US$", or "convertido para R$"), No período, Ao ano (decision 11; on a
   phone it goes under No período, so no column scrolls out of sight).

## Test plan

### Unit — `api.tests/Unit`, no database

1. Rebasing: two level series are 100 on the start, and each point is `100 × v / v(start)`; the change and the annualised rate are hand-computed.
2. Calendars: a weekday series and an every-day series give points on the union of their dates; the weekday one carries Friday over the weekend; the start takes each series' last observation on or before it; nothing is interpolated.
3. Conversion: a USD series times PTAX, a BRL series divided by it, PTAX carried over a holiday; the converted series begins where both have begun.
4. Rate series through 008's accumulator: CDI's daily rows and IPCA's monthly row become the accumulator's index on their own dates; with no row in the period the series is flat at 100.
5. Sampling: up to 1 096 days every point; past it the last date of each ISO week; past 3 653 days the last of each month; the start and the end always.
6. Periods: each preset's start from a fixed today (1M from 31 October is 30 September; YTD is 31 December); Máx starts at the latest first day; a custom window; the end is the latest observation, not after `to`; a series that does not cover the window is left out; no common period when a series ends on or before the start.

### Integration — `api.tests/Integration`

7. Without a session, 401.
8. Every refusal names its field with its pt-BR message: one series, seven, malformed, repeated, an unknown asset, an unknown benchmark; an unknown period; dates beside a preset; a malformed date; `from` not before `to`; an unknown currency.
9. Seeded closes and benchmarks, a custom period: an asset in BRL, one in USD, CDI, IPCA and USDBRL come back with hand-computed changes, annualised rates and points, in the order requested; converting to R$ and to US$ multiplies and divides by the PTAX of each day.
10. States: a series without data in the window is `hasData: false` with null values; series that do not overlap give `period: null`.
11. With a fixed clock, a preset and Máx resolve their start, and the start moves to the latest-starting series.

### E2E — `web/e2e/compare.spec.ts`, on the fake providers

12. With nothing chosen, and with CDI alone, the page asks for a second series and draws nothing.
13. Two catalogue assets registered on `/market-data` (one BRL on brapi, one USD on CoinGecko) and synced, and CDI, picked by search: a custom period of the last ten days shows each asset's change (9,90 to 10,00: +1,01%), three rows and three lines; 1A presses its button, refetches and writes `period=1y` to the URL.
14. "Converter para R$" marks the USD asset as converted, "Converter para US$" the BRL asset and CDI, and the changes hold (PTAX is constant in the fakes); the URL carries `currency`. The URL opened in a new page shows the same series, period and currency.

No web unit test: the page's behaviour is the E2E's. Every existing test keeps passing.

## End-to-end verification

```
bash scripts/verify.sh
```

E2E against an isolated database (never the owner's `financas`).

Manual, after 025 has loaded full history and the switch of decision 4 is flipped:

1. ITUB4 × Dólar (USDBRL), 10A: the dollar's line matches the PTAX's change over the same dates.
2. BTC (USD) × WEGE3, Máx, Moeda original: starts on BTC's first day, with the note; on the Log scale both lines are readable.
3. The same in R$: BTC's change matches BTC-BRL's for the same dates, within the PTAX-versus-market spread.
4. 1A for one asset against Google Finance's 1A: the same within a close's rounding.
5. Reload, bookmark, open the bookmark: the same comparison. Both themes, and 390 px wide.

## Definition of done

- `verify.sh` green; E2E green on an isolated database
- Tests 1–14 exist and pass
- No migration; no `double` or `float` in the API code this spec touches
- The (review) decisions confirmed or changed by a person
