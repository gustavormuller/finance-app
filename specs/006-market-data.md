# 006 — Market data

## Goal

A shared, non-user-owned catalogue of priced assets and benchmark series, filled by a nightly job from four external providers and readable by 007 and 008. No investment UI yet — this feature is plumbing.

## Decisions made in this spec

| # | Question | Decision |
|---|---|---|
| 1 | Job infrastructure (ADR-003, deferred from 004) | **Hosted `BackgroundService` with Cronos for the schedule.** No Hangfire: one nightly job does not justify ten tables and a dashboard. Re-hosting under Hangfire later is trivial because the job is a method. |
| 2 | Ports | Two, not one. `IPriceProvider` (ticker → daily closes) and `IBenchmarkProvider` (code → daily values). Each provider implements the one that fits. |
| 3 | Ownership | `MarketAsset`, `Price`, `Benchmark`, `SyncRun` are **not** `IUserOwned`. Two users holding PETR4 share one series. Tested explicitly. |
| 4 | Resilience | `Microsoft.Extensions.Http.Resilience` — first-party, built on Polly. Retry with backoff and a circuit breaker **per provider**. One provider failing must not fail the sync. |
| 5 | Gaps | Store only what the provider returns. Weekends and holidays are absent rows. Carry-forward is a read concern in 007. |
| 6 | Series codes and model names | Configuration, not code. Defaults are listed but must be verified against the SGS portal before the first sync. |
| 7 | Manual trigger | `POST /api/market-data/sync` for any authenticated user, rate-limited to one run per 10 minutes globally. It is idempotent. |
| 8 | Backfill | On first activation of an asset: 5 years. Typically one request per provider. |
| 9 | Provider tests | Never hit the network. `HttpClient` with a fake handler returning JSON captured once from each real API into `api.tests/Fixtures/MarketData/`. |

### Corrections to existing docs

- **ADR-003** — amend to record the decision in row 1. 004 left it open; 006 closes it.
- **ARCHITECTURE.md `prices` table** — it says `asset_key`. It is `MarketAssetId`, an FK to the catalogue. Amend.

## Out of scope

- User-owned assets, positions, movements — 007
- Any return calculation — 008
- Fixed income (CDB, Tesouro, LCI) — no market price exists; needs an accrual model, deferred past 007
- Intraday prices
- Corporate actions (splits, dividends) as data — 007 records them as user movements
- Currency conversion of cash accounts

## Data model changes

Migration: `AddMarketData`. Review the SQL.

### `MarketAsset` — not user-owned

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | |
| `Ticker` | `varchar(20)` | display, e.g. `PETR4`, `BTC`, `AAPL` |
| `Name` | `varchar(200)` | |
| `Class` | `int` | `StockBr`, `Fii`, `EtfBr`, `Bdr`, `StockUs`, `Crypto` |
| `Currency` | `char(3)` | `BRL` or `USD` |
| `Provider` | `int` | `Brapi`, `CoinGecko`, `TwelveData` |
| `ProviderSymbol` | `varchar(50)` | what the provider calls it, e.g. `bitcoin` for CoinGecko |
| `IsActive` | `bool` | inactive assets are skipped by sync |
| `LastSyncedAt` | `timestamptz NULL` | |
| `CreatedAt` | `timestamptz` | |

Unique `(Provider, ProviderSymbol)`.

### `Price` — not user-owned

| Column | Type |
|---|---|
| `MarketAssetId` | `uuid` FK |
| `Date` | `date` |
| `Close` | `numeric(18,8)` |

PK `(MarketAssetId, Date)`. Upsert on conflict — a re-sync of the same day overwrites.

### `Benchmark` — not user-owned

| Column | Type | Notes |
|---|---|---|
| `Code` | `varchar(20)` | `CDI`, `SELIC`, `IPCA`, `USDBRL`, `IVVB11` |
| `Date` | `date` | |
| `Value` | `numeric(18,8)` | in the series' own unit |

PK `(Code, Date)`. Upsert.

### `SyncRun` — not user-owned

| Column | Type |
|---|---|
| `Id` | `uuid` |
| `StartedAt` | `timestamptz` |
| `FinishedAt` | `timestamptz NULL` |
| `Trigger` | `int` — `Scheduled`, `Manual` |
| `Status` | `int` — `Running`, `Succeeded`, `PartialFailure`, `Failed` |
| `Summary` | `jsonb` — per provider: rows written, error |

## Configuration

```
MarketData:Schedule            "0 3 * * *"        cron, server local time
MarketData:BackfillYears       5
MarketData:Bcb:BaseUrl         https://api.bcb.gov.br/dados/serie/bcdata.sgs.
MarketData:Bcb:Series          { CDI: 12, SELIC: 11, IPCA: 433, USDBRL: 1 }
MarketData:Brapi:Token
MarketData:CoinGecko:DemoKey
MarketData:TwelveData:Key
```

**Verify the SGS codes against the portal before the first sync.** They are believed correct and stable, but they are configuration precisely so that a wrong one is a config edit, not a code change. Also record each series' **unit** (percent per day, percent per month, index level) in a comment beside the code — 008's accumulation logic depends on it.

`IVVB11` is not a BCB series; it is fetched via brapi as a price and stored as a benchmark too, so 008 has an S&P 500 proxy in BRL with FX already embedded.

## Domain and ports

### `Application/MarketData/IPriceProvider.cs`

```csharp
public interface IPriceProvider
{
    ProviderKind Kind { get; }
    Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct);
}
public readonly record struct DailyClose(DateOnly Date, decimal Close);
```

### `Application/MarketData/IBenchmarkProvider.cs`

```csharp
public interface IBenchmarkProvider
{
    Task<IReadOnlyList<DailyValue>> GetSeriesAsync(
        string code, DateOnly from, DateOnly to, CancellationToken ct);
}
```

### Provider registry

`IPriceProviderRegistry.For(ProviderKind)` returns the right implementation. Adding a provider is one class and one registration.

### Sync — `Application/MarketData/MarketDataSync.cs`

For each active `MarketAsset`, grouped by provider:
1. `from` = day after the latest stored price, or `today − BackfillYears` if none
2. Fetch, upsert, update `LastSyncedAt`
3. Catch per asset — one bad ticker does not stop the provider's other assets
4. Catch per provider — one provider down does not stop the others

Then each benchmark code the same way.

Write the `SyncRun` row at start (`Running`) and update at end. The row is how you find out what happened at 3 a.m.

### Job host — `Infrastructure/Jobs/MarketDataSyncJob.cs`

`BackgroundService`. Cronos computes the next occurrence; `Task.Delay` until then; run; loop. On startup, if the latest `SyncRun` is older than 26 hours, run immediately — the box was down at 3 a.m.

## API surface

```
GET  /api/market-data/assets?q=PETR         200 catalogue search
POST /api/market-data/assets                201 register a ticker
     { ticker, class, provider, providerSymbol, currency }
     409 already exists
GET  /api/market-data/assets/{id}/prices?from&to   200
GET  /api/market-data/benchmarks/{code}?from&to    200
POST /api/market-data/sync                  202 { syncRunId }
     429 if one ran in the last 10 minutes
GET  /api/market-data/sync-runs             200 last 20
```

All authenticated. Registering an asset is how 007 will create catalogue entries — 006 exposes it so it can be tested and used manually.

## UI behaviour

Minimal. A `/market-data` route, not in the main nav, reachable from settings later:

- Table of sync runs: when, trigger, status, per-provider summary
- Button to trigger a sync
- Search and register a ticker

No charts. This screen exists so a human can see whether the plumbing works.

## Test plan

### Unit — `api.tests/Unit`

**Provider parsing, one set per provider, against captured fixtures:**
1. BCB: happy path parses dates and values
2. BCB: `dd/MM/yyyy` dates parsed with `InvariantCulture` and an explicit format
3. Brapi: historical range parsed
4. Brapi: response for an unknown ticker → empty, not an exception
5. CoinGecko: `market_chart` parsed, timestamps to `DateOnly` in UTC
6. TwelveData: `time_series` parsed
7. Each provider: `429` → `ProviderRateLimited` exception, not a crash
8. Each provider: malformed JSON → `ProviderResponseInvalid`, no exception escaping
9. All values reach `decimal` without `double` — assert on declared types

**Sync logic:**
10. `from` is the day after the latest stored price
11. No stored prices → `from` is `BackfillYears` ago
12. One asset failing → others in the same provider still written; `SyncRun` is `PartialFailure`
13. One provider failing → others still written; `SyncRun` is `PartialFailure`
14. All succeed → `Succeeded`; all fail → `Failed`

**Scheduling:**
15. Cronos next-occurrence for the configured expression is tomorrow 03:00 when run at 04:00 today
16. Startup with latest run older than 26h → immediate run

### Integration — `api.tests/Integration`

**Ownership:**
17. `MarketAsset`, `Price`, `Benchmark`, `SyncRun` have **no** query filter — a test that inserts as user A and reads as user B sees the rows. This is the inverse of every isolation test so far, and it is deliberate.

**Persistence:**
18. Upsert on `(MarketAssetId, Date)` overwrites
19. `(Provider, ProviderSymbol)` unique → `409` on duplicate register

**Endpoints:**
20. Register → sync (with fake providers) → prices readable
21. Manual sync twice within 10 minutes → second is `429`
22. `sync-runs` returns newest first

**Resilience — with a fake handler:**
23. Transient `503` then `200` → retried, succeeds
24. Five consecutive failures → circuit opens; sixth call fails fast without hitting the handler

### Unit — `web`

25. Sync run status renders with the per-provider breakdown

### E2E

26. Trigger a sync (fake providers in the test host) → run appears with `Succeeded`

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual, with real keys:

1. Verify the four SGS codes on the portal; note each unit
2. Register PETR4 (brapi), BTC (CoinGecko, `bitcoin`), AAPL (TwelveData)
3. Trigger a sync — all three providers `Succeeded` in the run summary
4. `SELECT COUNT(*) FROM "Prices"` — roughly 1250 rows per asset for 5 years of trading days, ~1800 for BTC
5. `SELECT * FROM "Benchmarks" WHERE "Code" = 'CDI' ORDER BY "Date" DESC LIMIT 3` — recent business days, values that look like a daily rate
6. Trigger again — near-zero new rows, `Succeeded`
7. Leave it overnight; confirm a `Scheduled` run exists next morning

Step 5 is where a wrong series code shows up: values in the wrong magnitude.

## Definition of done

- Both verify scripts green
- Manual steps 1–7 behave as described
- Test 17 passes — market data is shared
- ADR-003 amended; ARCHITECTURE.md `prices` corrected
- Series units recorded beside their codes in config
