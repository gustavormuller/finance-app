# 016 — Investments: returns first, in R$ and US$

## Goal

The investments page answers the question the module exists for before anything else: how is
the portfolio doing, and against what. It opens with the portfolio's returns (design "P1"), then
what contributed and what is held, and the positions below; and the whole investments area can
be read in reais or in dollars.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | What `/investments` opens with | A **returns hero**: "Rentabilidade da carteira · desde {base day}", the period's TWR as the large figure in the positive or negative token, the annualised rate beside it, the XIRR line, a period selector **No ano / 12 meses / Desde o início** (default Desde o início), three tiles against **CDI**, **IPCA + 6%** and **S&P 500**, and 008's comparison chart. Every figure is `GET /api/returns/portfolio`'s. |
| 2 | Which benchmarks the hero shows | CDI, IPCA + 6% and S&P 500 (IVVB11), as in the P1 design: the tiles and the chart lines. SELIC and Dólar stay in the full report. A benchmark that could not anchor reads "Sem dados". |
| 3 | The annualised rate beside the total | Only past a year, 008's rule (`showsAnnualised`); a year or less reads "em N dias" instead. |
| 4 | `/investments/returns` | **Stays the detailed report** (custom range, SELIC and Dólar, the timing effect, the benchmark table, per asset), linked as "Rentabilidade" in the investments header. The hero is its summary, not a replacement. **(review)** |
| 5 | "O que mais contribuiu" | Open positions with a valuation, ranked by the **size** of their unrealised result (`unrealisedBrl`, gains and losses alike), top 8, listed from the largest gain to the largest loss; the bar is the result against the largest one. Realised gains and dividends are not in it, as they are not in the positions table's "Resultado". A link "Todas as N posições" goes to the positions. **(review)** |
| 6 | "Patrimônio investido" | The summary's total and its result over cost, and the **allocation by asset class** as a stacked bar and a list. The allocation is **computed by the API** (a new `allocation` field on the summary, shares by largest remainder as 005's breakdown), because the web client does no totalling (`web/src/api/finance.ts`). **(review)** |
| 7 | Where the positions go | Lower on the same page, under a "Posições" heading with the anchor `#posicoes` (a "Posições" link in the header jumps there), followed by "Adicionar ativo". Nothing about them changes. |
| 8 | Currencies | **R$ and US$ only.** USDBRL (BCB SGS 1) is the only exchange rate 006 stores. EUR would need a new BCB series (SGS 21619, euro, venda) in `MarketData:Bcb:Series` as a `Level`, and a `Returns:Benchmarks` entry for its index; out of scope here. |
| 9 | Where the rate comes from | `GET /api/investments/summary` gains `usdBrl: { rate, date } \| null`, the latest stored USDBRL row. Every page with the toggle already reads the summary for its totals, so the rate travels with the figures it converts, in one request. It is shared market data, the same for every user. A separate `GET /api/market-data/fx/latest` was the alternative. **(review)** |
| 10 | The toggle | A `R$ \| US$` segmented control in the header of `/investments`, `/investments/{id}`, `/investments/returns` and `/investments/{id}/returns`. Per device in `localStorage`, key `currency`, values `BRL` \| `USD`; unreadable storage is R$, and a choice that cannot be stored still applies until the page is left (as `lib/theme.ts`). Not a server setting. |
| 11 | What converts, and how | Every BRL money figure (position value, result, realised, dividends, totals, allocation) divided by the latest rate, **in the browser, at display time**: the architecture's "convert on read". Exact decimal arithmetic (`bigint`), rounded once, half to even, to the cent, as `lib/decimal.ts` does for the movement total. Unit prices and average cost stay in the asset's own currency (a PETR4 price stays in R$, an AAPL price in US$). Percentages do not change (value and cost divide by the same rate). The header says "em dólar · US$ 1 = R$ 5,3012 em 23/09". |
| 12 | What a result in US$ means | The BRL result at today's rate. It is **not** what a dollar-based investor made, which needs each purchase's cost at that day's rate; the API does not expose that for BRL assets. **(review)** |
| 13 | Returns in US$ | Derived from the returns series, which carries USDBRL as a base-100 index: portfolio in US$ `= portfolio / USDBRL × 100` at each point, the same for each benchmark, so TWR in US$ `= last / 100 − 1`, i.e. `(1 + r_brl) / (1 + r_usd) − 1`. Exact for the totals (`bigint` over the six-place index, ten places out, as the API rounds rates); floats for the chart points and the annualisation `(1 + t)^(365/days) − 1`, which are display only. The Dólar benchmark is dropped in US$: it is the unit. **(review)** |
| 14 | XIRR in US$ | **Not derived.** It needs every flow converted at its own day's rate, and the series is sampled weekly with no flows. XIRR, and the timing effect built on it, are shown in reais and labelled "em reais". **(review)** |
| 15 | No rate | USDBRL never synced: US$ shows the figures in reais with the note "Sem cotação do dólar sincronizada; valores em reais." USDBRL could not anchor on the period's base day (the returns send no USDBRL index): the returns stay in reais with "Rentabilidade em reais: sem cotação do dólar no início do período." |
| 16 | The asset's value history | Stays in reais, labelled "em reais" when US$ is chosen. Converting it needs a rate per day, which the daily rows do not carry for a BRL asset. |
| 17 | Layout | Both themes through the existing tokens (`Card`, `PageHeader`, `lib/chart.ts`); no hard-coded colour. At 390 px nothing scrolls sideways: grid children carry `min-w-0`, rows wrap to two lines. |

## Out of scope

- Currencies other than R$ and US$ (decision 8)
- XIRR in dollars (decision 14); a dollar-based cost basis (decision 12)
- The asset's value history in dollars (decision 16)
- Storing the currency choice on the server
- New benchmarks, periods beyond 008's, or a "Nova movimentação" shortcut on the portfolio page

## Data model changes

None. The summary reads `PortfolioDaily`, `Assets`, `MarketAssets` and `Benchmarks` as today.

## API surface

```
GET /api/investments/summary
    200 {
      totalBrl, totalCostBrl, unrealisedBrl,                   unchanged
      allocation: [{ class: "StockBr", valueBrl: 3510.00, share: 0.2419 }],
                   the latest row of each asset, by class, highest value first; a class
                   worth zero is left out; shares to four places, summing to exactly 1
      usdBrl: { rate: 5.3012, date: "2026-09-23" } | null
                   the latest USDBRL benchmark row; null when none was ever synced
    }
```

Everything is `decimal`. `allocation` is user-owned (filtered); `usdBrl` is shared market data.

## UI behaviour

**`/investments`** — header: "Investimentos", the `R$ | US$` toggle, "Posições" (to `#posicoes`) and
"Rentabilidade" (the report). Then, when something is held:

1. The returns hero (decision 1), `data-testid="returns-hero"`, figures `hero-twr`, `hero-annualised`,
   `hero-xirr`, tiles `hero-benchmark-{code}`.
2. Two cards side by side on a wide screen, stacked on a phone: "O que mais contribuiu"
   (`contributors`, rows `contributor-{assetId}`) and "Patrimônio investido" (`holdings`).
3. "Posições" (`#posicoes`): the positions table exactly as 007 left it, then "Adicionar ativo".

With nothing held, only the empty state and "Adicionar ativo", as today.

**In US$** — the header's subtitle is "em dólar · US$ 1 = R$ 5,3012 em 23/09"; money figures convert
(decision 11); the hero reads "Rentabilidade da carteira em dólar"; the XIRR line ends "(em reais)".

**`/investments/returns` and `/investments/{id}/returns`** — the toggle in the header. In US$ the
headline's TWR, the chart, the benchmark table and the per-asset TWR are the converted ones
(decision 13), without the Dólar row; XIRR and the timing effect say "em reais".

**`/investments/{id}`** — the toggle in the header; the summary's money figures convert; the value
history is labelled "em reais" (decision 16).

## Test plan

### Integration — `api.tests/Integration`

1. `summary.usdBrl` is the latest USDBRL row, rate and date, exactly as stored (four places); `null` when none exists; the same for a user who holds nothing
2. `summary.allocation` groups the latest row of each asset by class, highest first, shares summing to exactly 1; a class worth zero is left out; another user's allocation is empty

### Unit — `web`

3. `brlToUsd`: R$ 1.000,00 at 0,05 is US$ 20.000,00; R$ 226.692,85 at 5,30 is US$ 42.772,24; a half cent rounds to even; a loss stays negative; exact where `a / b` in float64 is not
4. `returnsInUsd`: portfolio 184,23 over USDBRL 101 is +82,41%; each benchmark converted the same way; the Dólar benchmark dropped; the annualised rate past a year only; `null` when the series has no USDBRL
5. The currency choice: R$ by default; `USD` stored is read back; unreadable or unwritable storage is R$ and the toggle still switches
6. A US$ figure prints `US$`
7. The hero: the TWR in the sign's token, the annualised rate past a year only, the XIRR line, the three tiles with their difference in p.p. and its tone, "Desde o início" pressed by default and a period change refetching
8. "O que mais contribuiu": at most 8, ranked by the size of the result, gains before losses, each linked to its asset, and "Todas as N posições"
9. "Patrimônio investido": the total, the result over cost, and each class with its share and value
10. The positions table and "Adicionar ativo" are still on the page (007's tests keep passing unchanged)
11. `/investments` in US$: the rate and its date in the header, positions, total and allocation converted, the hero's TWR derived and its XIRR "em reais"; no rate, the note and reais
12. The returns report in US$: the headline and the benchmark table converted, no Dólar row, XIRR and timing "em reais"
13. The asset page in US$: its money figures converted, prices in the asset's currency, the history "em reais"

### E2E — `web/e2e`

14. After 008's buy 30 days back, `/investments` shows the hero's TWR (+2,99%) and XIRR (+43,05% a.a.) before the report shows the same
15. PETR4 held: US$ shows the position and the total at the fake rate (R$ 1.000,00 at 0,05 is US$ 20.000,00) with the rate in the header; a reload keeps US$; R$ goes back

Every existing E2E assertion keeps passing.

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual, against the real portfolio:

1. Open `/investments`: the hero's TWR and XIRR match `/investments/returns` for the same period
2. The tiles' differences match the report's "Carteira menos referência" column
3. Switch to US$: the total equals the R$ total divided by the rate shown in the header, to the cent
4. In US$, the TWR matches `(1 + TWR em reais) / (1 + Dólar no período) − 1` from the report in R$
5. Reload: still US$. Both themes, and 390 px wide: nothing scrolls sideways

## Definition of done

- `verify.sh` green; E2E green on an isolated database
- Tests 1–15 exist and pass
- No `double` or `float` in the API code this spec touches
- Manual steps 1–5 behave as described
