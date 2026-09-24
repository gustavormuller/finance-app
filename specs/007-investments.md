# 007 — Investments

## Goal

Record what the user holds and what they did with it — buys, sells, dividends — and derive a daily valued position from movements and 006's prices. The daily snapshot is what makes 008 possible.

## Decisions made in this spec

| # | Question | Decision |
|---|---|---|
| 1 | Scope of asset types | **Priced assets only** — anything with a `MarketAsset`. Fixed income (CDB, Tesouro, LCI) is deferred: it has no market price and needs an accrual model (% CDI, IPCA+, prefixado) that is its own feature. |
| 2 | Movement kinds | `Buy`, `Sell`, `Dividend`, `Jcp`, `Split`. Amortization deferred with fixed income and FIIs' capital returns. |
| 3 | Split modelling | A `Split` is a zero-cost quantity movement: 1:2 on 100 shares is `Quantity = +100, UnitPrice = 0`. No ratio logic, matches how brokers report it. |
| 4 | Cost basis | **Average cost (preço médio)** — the Brazilian tax standard. Not FIFO. |
| 5 | Fees | In cost basis on buy; deducted from proceeds on sell. |
| 6 | Dividends and cash flow | Recorded on the asset. **Not** auto-created as `Transactions`. Linking investment income to a cash account is a later refinement — it needs a "which account receives it" model. |
| 7 | Snapshot granularity | **Every calendar day** from first movement to today, per asset. Weekends carry forward the last close. A dense series keeps 008's maths simple. |
| 8 | FX | `Price` in native currency, `FxRate` from `USDBRL` benchmark, `ValueBrl` computed. 008 decomposes from these. |
| 9 | Rebuild trigger | Editing a movement rebuilds that asset from the movement's date, **synchronously in the request**. ~1 500 rows per asset-year is trivial. Nightly full rebuild after 006's sync. |
| 10 | Price and quantity types | Raw `decimal` with explicit EF precision, **not** `Money`. `Money` pins scale to 2 and is for ledger amounts. Prices and quantities are measurements. Totals become `Money` at the boundary. |

## Out of scope

- Fixed income of any kind
- Return calculation — 008
- Amortization, capital returns, bonus shares as distinct kinds
- Linking dividends to cash accounts
- Broker statement import (B3 CEI/CSV) — a later import feature, not 004's
- Tax lots, IR calculation, DARF

## Data model changes

Migration: `AddInvestments`. Review the SQL.

### `Asset : IUserOwned`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | |
| `UserId` | `uuid` | |
| `MarketAssetId` | `uuid` | FK → `MarketAssets`, `RESTRICT` |
| `Nickname` | `varchar(100) NULL` | |
| `CreatedAt` | `timestamptz` | |

Unique `(UserId, MarketAssetId)` — one position per user per ticker.

### `Movement : IUserOwned`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | |
| `UserId` | `uuid` | |
| `AssetId` | `uuid` | FK, `RESTRICT` |
| `Date` | `date` | |
| `Kind` | `int` | `Buy`, `Sell`, `Dividend`, `Jcp`, `Split` |
| `Quantity` | `numeric(18,8)` | `> 0` for Buy/Sell/Split; `0` for Dividend/Jcp |
| `UnitPrice` | `numeric(18,8)` | native currency; `0` for Split |
| `Amount` | `numeric(18,2)` | Dividend/Jcp cash received, native currency; `0` otherwise |
| `Fees` | `numeric(18,2)` | `>= 0` |
| `Currency` | `char(3)` | must equal the `MarketAsset` currency |
| `Notes` | `varchar(300) NULL` | |
| `CreatedAt` | `timestamptz` | |

Index `(UserId, AssetId, Date)`.

### `PortfolioDaily : IUserOwned` — derived, truncatable

| Column | Type | Notes |
|---|---|---|
| `UserId` | `uuid` | |
| `AssetId` | `uuid` | |
| `Date` | `date` | |
| `Quantity` | `numeric(18,8)` | |
| `AverageCost` | `numeric(18,8)` | native, per unit |
| `Price` | `numeric(18,8)` | native, carried forward on non-trading days |
| `PriceDate` | `date` | the actual date the carried price is from |
| `FxRate` | `numeric(18,8)` | `1` for BRL |
| `ValueBrl` | `numeric(18,2)` | `Quantity × Price × FxRate` |
| `CostBasisBrl` | `numeric(18,2)` | `Quantity × AverageCost × FxRate at purchase` — see below |

PK `(UserId, AssetId, Date)`.

**ADR-011 applies:** this table can be truncated and rebuilt at any time with no loss. It is never the source of truth.

`CostBasisBrl` uses the FX rate **at each purchase**, accumulated — not today's rate. That is what makes unrealised gain in BRL correct for a USD asset bought when the dollar was cheaper.

## Domain — `Domain/Investments/`

All pure. No EF, no clock.

### `PositionCalculator`

Input: movements for one asset, ordered by date. Output: position after each movement — quantity, average cost, realised gain to date.

Average cost on buy:
```
newAvg = (qty × avg + buyQty × buyPrice + fees) / (qty + buyQty)
```
On sell: average cost unchanged; realised gain `= (sellPrice × sellQty − fees) − avg × sellQty`.
On split: quantity increases, `newAvg = (qty × avg) / (qty + splitQty)`.
Dividend and JCP: no change to quantity or average cost; recorded as income.

### Validation rules

| Rule | Message |
|---|---|
| Sell quantity exceeds position on that date | `Quantidade vendida maior que a posição` |
| Buy/Sell/Split with `Quantity <= 0` | `Quantidade deve ser positiva` |
| Dividend/Jcp with `Amount <= 0` | `Valor deve ser positivo` |
| Currency differs from the market asset | `Moeda diferente do ativo` |
| Date before 1990-01-01 or after today | `Data fora do intervalo` |

"Position on that date" means the position computed from movements **up to and including that date, ordered by date then `CreatedAt`**. Inserting an old sell that would make a later position negative is rejected too.

### `SnapshotBuilder`

Input: movements, prices (sparse), FX rates (sparse), date range. Output: one row per calendar day.

For each day `d` from first movement to `to`:
- position = `PositionCalculator` up to `d`
- price = latest close with `Date <= d`; `PriceDate` records it
- fx = latest `USDBRL` with `Date <= d`, or `1` for BRL
- if no price exists yet for the asset at all → skip the day (an asset bought before its price history begins)

Cost basis in BRL accumulates per buy at that buy's FX rate.

Pure over its inputs; the caller loads the inputs.

## Application

### `RebuildSnapshots(userId, assetId, fromDate)`

Delete `PortfolioDaily` rows for the asset from `fromDate`; run `SnapshotBuilder` from `fromDate`; insert. One DB transaction. Called synchronously by every movement write, with `fromDate = min(oldDate, newDate)`.

### Nightly job

After 006's sync completes, rebuild **every** user's assets from `yesterday`. Registered in the same `BackgroundService` host, sequenced after the market sync. Recorded in `SyncRun.Summary` as a section.

## API surface

```
GET    /api/investments/assets               200 positions, see shape below
POST   /api/investments/assets               201 { marketAssetId, nickname? }
       409 already held
DELETE /api/investments/assets/{id}          204 | 409 if it has movements

GET    /api/investments/assets/{id}/movements       200
POST   /api/investments/assets/{id}/movements       201
PUT    /api/investments/movements/{id}              200
DELETE /api/investments/movements/{id}              204

GET    /api/investments/assets/{id}/daily?from&to   200 PortfolioDaily rows
GET    /api/investments/summary                     200 { totalBrl, totalCostBrl, unrealisedBrl }
POST   /api/investments/rebuild                     202 rebuild all of the user's assets
```

Position shape:
```json
{
  "assetId": "...", "ticker": "PETR4", "name": "...", "class": "StockBr",
  "currency": "BRL", "quantity": 100, "averageCost": 32.1234,
  "price": 35.10, "priceDate": "2026-09-22",
  "valueBrl": 3510.00, "costBasisBrl": 3212.34,
  "unrealisedBrl": 297.66, "unrealisedPct": 0.0927,
  "realisedBrl": 0.00, "dividendsBrl": 120.00
}
```

`POST /assets` also accepts `{ ticker, class, provider, providerSymbol, currency }` to register a new `MarketAsset` via 006 in the same call when it does not exist yet.

## UI behaviour

Route `/investments`, in the nav.

**Positions table** — ticker, name, quantity, average cost, current price with its date, value, unrealised gain (amount and %), sorted by value. Total row. A `priceDate` older than 3 business days is flagged — the sync may be broken.

**Asset detail** `/investments/{id}` — movements table with add/edit/delete, and the position history as a simple line of `ValueBrl` over time (Recharts). Not the comparison chart — that is 008.

**Add movement form** — kind, date, quantity, unit price, fees, notes. Fields shown depend on kind: Dividend/Jcp show `amount` instead of quantity and price. Live preview of the total.

**Add asset** — search the 006 catalogue; if absent, register inline.

## Test plan

### Unit — `api.tests/Unit`, no database

**PositionCalculator:**
1. Buy 100 @ 10, fees 5 → avg `10.05`
2. Buy 100 @ 10 then 100 @ 20 → avg `15`
3. Buy then sell half → avg unchanged, realised gain correct with fees
4. Sell all then buy again → new average starts fresh
5. Split 1:2 → quantity doubles, avg halves, value unchanged
6. Dividend → quantity and avg unchanged, income recorded
7. Sell more than held → rule error
8. Old sell that makes a later position negative → rule error
9. All arithmetic in `decimal`; a scenario with 3 decimal quantities and 8 decimal prices reproduces a hand-computed result exactly

**SnapshotBuilder:**
10. 10 trading days of prices, 14 calendar days → 14 rows, weekends carry Friday's close and `PriceDate`
11. Buy on day 5 of a 14-day range → rows only from day 5
12. Asset with no price history → no rows, no exception
13. USD asset: `ValueBrl = qty × price × fx`, `CostBasisBrl` uses purchase-date FX
14. Two buys at different FX rates → `CostBasisBrl` is the sum, not `qty × avg × todayFx`
15. Sell reduces `CostBasisBrl` proportionally

### Integration — `api.tests/Integration`

**Isolation — mandatory:**
16. A's assets, movements, daily rows invisible to B on every endpoint
17. B posts a movement to A's asset → `404`, nothing written
18. B's `summary` is zero when only A holds anything

**Persistence and rebuild:**
19. Post a buy → `PortfolioDaily` rows exist from that date to today
20. Edit the buy's date earlier → rows rebuilt from the earlier date
21. Delete the only movement → rows gone
22. Rebuild is one transaction: a failing insert leaves the previous rows intact
23. `(UserId, MarketAssetId)` unique → `409`
24. Delete asset with movements → `409`

**Correctness:**
25. Position endpoint matches `PositionCalculator` for the same movements
26. `summary.totalBrl` equals the sum of the latest `ValueBrl` per asset
27. Nightly job (invoked directly) rebuilds from yesterday for two users

### Unit — `web`

28. Movement form shows `amount` for Dividend and hides quantity/price
29. Total preview updates with quantity, price and fees
30. Stale price flag renders when `priceDate` is older than 3 business days

### E2E

31. Add PETR4 (fake provider) → buy 100 → position shows with value
32. Add a dividend → `dividendsBrl` increases, quantity unchanged
33. Delete the buy → position disappears

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual, with your real broker statement:

1. Enter every buy and sell of one Brazilian stock you actually hold, with the real fees
2. Compare average cost against your broker's "preço médio" — they should agree to the cent
3. Compare current value against the broker
4. Enter one USD asset if you hold one; compare `costBasisBrl` against what you actually paid in reais at the time
5. Edit the oldest buy's price → position and daily rows update; the broker's number is the reference
6. In `psql`: `SELECT "Date", "Price", "PriceDate" FROM "PortfolioDaily" WHERE ... ORDER BY "Date" DESC LIMIT 7` → weekends show Friday's `PriceDate`

Step 2 is the one that validates the average-cost arithmetic against an external authority.

## Definition of done

- Both verify scripts green
- Manual steps 1–6 behave as described
- Average cost matches the broker to the cent
- Migration SQL reviewed
- `PortfolioDaily` can be truncated and rebuilt via `POST /rebuild` with identical results
