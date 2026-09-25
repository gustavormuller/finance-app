# 018 — Performance, measured

## Goal

Find out, with numbers taken on the owner's own data, which requests and pages are slow, and
fix those and only those. Every change comes with its before and after, and none changes what
the app answers or shows.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | What is measured on | `financas_perf`, a `pg_dump` copy of `financas` (the owner: 1 419 transactions over 21 months, 13 assets, 550 movements, 22 662 daily rows over 5 years). Then the same database with synthetic volume, **"scaled"**: the owner's transactions copied five times further back (8 514 rows, 10 years) and 20 more users, each a copy of the owner (179 k transactions and 476 k daily rows in all). `financas` is only ever read, by `pg_dump`. |
| 2 | How it is timed | The API from a scratch build, `Development`, SQL logging off, on :5093. A Node script signs in with `dev-login` and times each route on a fresh connection, reporting the median. The machine is shared with other agents, so builds are timed **interleaved** (base, after, base, after…) over several rounds, and each route gets **40 warm-up calls** first: with 3, the returns maths still ran unoptimised tier-0 JIT code for most of the measured calls, and when the tiered compiler caught up depended on the machine's load more than on the build (see change 2). Production is one long-lived process, so steady state is what counts. |
| 3 | What counts as slow | A route whose median is well above the ~5 ms a plain filtered read costs here, and a cause `EXPLAIN (ANALYZE, BUFFERS)` or a timer can name. Anything at that floor is left alone. |
| 4 | Rewriting a query | Only behind a test that pins its result first, and in the same layer and tool it was in: EF LINQ stays LINQ (the query filters keep applying), Dapper SQL stays SQL naming `"UserId"` in every statement. |
| 5 | Indexes | Only where a plan shows the index used and the time gone. No index is added "in case". |
| 6 | The snapshot rebuild's insert | One `INSERT … SELECT FROM unnest(arrays)` through `Database.ExecuteSqlAsync`, instead of `SaveChanges` over thousands of tracked rows. Same rows, same transaction (EF enlists raw SQL in it), same advisory lock. A failed insert now surfaces as the `PostgresException` itself rather than wrapped in a `DbUpdateException`; nothing catches either on these paths. Binary `COPY` was the alternative; the database side is dominated by the foreign-key triggers either way. **(review)** |
| 7 | Web bundle | Each page is its own chunk, loaded on navigation (TanStack Router's `lazyRouteComponent`); the sign-in page and the protected layout stay in the entry. Recharts then lands in a chunk of its own through the bundler's own splitting, since only the lazy chart pages reach it. An explicit `charts` group was measured and dropped (see change 5). |
| 8 | React Query defaults | `staleTime` of 30 s, so a page mounted again within 30 s, or a tab focused again, does not refetch what it just read. Correctness after a write is kept by construction: **every** mutation, when it settles, marks every query stale (without refetching). The explicit invalidations stay and still refetch what the page shows. **(review)** |
| 9 | Response compression | Caddy already has `encode gzip`. See "Measured, not worth doing". |
| 10 | Test-suite speed | Vitest's pool/isolation changed only if every test stays green and isolated; measured below. |

## Out of scope

- Any change to what an endpoint answers, its shape, status codes or messages, and any UI copy
- New infrastructure (cache servers, read replicas, a CDN), per ADR-001 and ADR-003
- Caching computed returns between requests (see "Proposals")
- A new route that answers several assets' returns at once (an API change; see "Proposals")

## Method

- Scripts, kept outside the repository: `bench.mjs` (the timings), `requests.cjs` (headless
  Playwright: API requests and script bytes per page load, and on client-side navigation),
  `synthetic.sql` (the scaled data, refusing any database but `financas_perf`).
- SQL came from EF Core's command log (`Microsoft.EntityFrameworkCore.Database.Command` at
  `Information`) and from the Dapper statements in `DashboardQueries`.
- In-process time (materialising rows, the returns maths, `SaveChanges`) was split with a
  throwaway `Stopwatch` build that was never committed.

## Baseline

The first look, used to choose what to examine: median of 20 after 3 warm-ups, milliseconds,
one run each; "real" is the copy of `financas`, "scaled" adds the synthetic volume (decision 1).
The before/after figures below are the steady-state ones of decision 2.

| Route | real | scaled |
|---|---:|---:|
| `GET /api/dashboard/summary` | 7.7 | 10.4 |
| `GET /api/dashboard/monthly?months=12` | 5.2 | 5.1 |
| `GET /api/dashboard/by-category` | 4.5 | 4.5 |
| `GET /api/dashboard/net-worth` (24) | 13.5 | 17.4 |
| `GET /api/dashboard/net-worth?months=120` | 18.7 | 22.4 |
| `GET /api/transactions` page 1 / page 20 | 7.3 / 7.3 | 12.9 / 12.9 |
| `GET /api/transactions` date range / account / category | 6.0 / 5.4 / 4.9 | 6.0 / 10.5 / 4.8 |
| `GET /api/categories` / `usage` | 3.3 / 3.7 | 3.4 / 3.5 |
| `GET /api/investments/summary` | **71.6** | **65.3** |
| `GET /api/investments/assets` (positions) | **81.1** | **71.5** |
| `GET /api/returns/portfolio` inception / 12m / ytd | **125.0** / 41.5 / 36.8 | **84.3** / 33.4 / 28.3 |
| `GET /api/returns/assets/{id}` (13 assets) | 27–37 | 21–30 |
| `GET /api/investments/assets/{id}/daily` | 10.8 | 7.4 |
| `POST /api/investments/rebuild` (13 assets, 22 662 rows) | **2 458** | **2 498** |

Web: one script of 1 050 kB (314 kB gzip) for every page, the sign-in page included.

## Summary

Steady state on the scaled database: 40 warm-up calls, then the median of 15, per round; three
interleaved rounds, median of the rounds (the rounds' minimum was within 2 ms of it for every
read route). Response bodies of all 35 routes saved by both builds are identical.

| Route | before (ms) | after (ms) |
|---|---:|---:|
| `GET /api/investments/summary` | 62.3 | **4.0** |
| `GET /api/investments/assets` (positions) | 69.1 | **11.6** |
| `GET /api/returns/portfolio?period=inception` | 79.8 | **64.9** |
| `GET /api/returns/portfolio?period=12m` / `ytd` | 32.1 / 29.0 | **26.1 / 23.8** |
| `GET /api/returns/assets/{id}` (13 assets) | 20.7–30.9 | 19.4–28.9 |
| `GET /api/dashboard/net-worth` (24) / `?months=120` | 16.9 / 21.5 | **13.2 / 17.8** |
| `POST …/movements` dated at the first buy (rebuilds ~1 800 rows) | 338 | **126** |
| `DELETE /api/investments/movements/{id}`, same | 326 | **119** |
| `POST /api/investments/rebuild` (13 assets, 22 662 rows) | 2 432 | **1 518** |
| every other route (dashboard summary, monthly, by-category, transactions, categories, accounts, daily, movements) | 2.6–12.5 | unchanged |

The three write rows: three interleaved rounds after a `VACUUM ANALYZE`, median.

Web: the signed-out `/login` loads 404 kB of JavaScript instead of 1 026 kB (131 kB gzip instead
of 303); coming back to a page within 30 s makes no request instead of 6–8.

## Changes

### 1. The latest daily row per asset (summary and positions)

**Cause.** Both read "each asset's latest row" as `WHERE "Date" = (SELECT max("Date") … WHERE
"AssetId" = p."AssetId")`. PostgreSQL ran it as a sequential scan of `PortfolioDaily` with the
subquery once **per row**: 22 662 probes and 69 282 buffers to return 13 rows (64 ms in
`EXPLAIN ANALYZE`). The obvious LINQ rewrite, `OrderByDescending(Date).Take(1)` per asset, is
turned by EF Core into `ROW_NUMBER() OVER (PARTITION BY "AssetId" …)` over every row the user
has: 22 ms, and still linear in history.

**Change.** The per-asset `Take(1)` projects a column of the outer query, which makes EF Core
emit `JOIN LATERAL (… ORDER BY "Date" DESC LIMIT 1)`: one backwards probe of the primary key
`(UserId, AssetId, Date)` per asset, 52 buffers, 0.2 ms. Still LINQ, still under the query
filter. Pinned first by `Each_asset_is_valued_at_its_own_latest_row_and_only_for_its_owner`.

| Route | before | after |
|---|---:|---:|
| `GET /api/investments/summary` | 62.3 | **4.0** |
| `GET /api/investments/assets` | 69.1 | **11.6** |

### 2. The portfolio's returns: the last day and the daily rows

**Cause.** A timer per phase of `MeasureAsync` (inception, scaled data) put 58% of the request
in loading the daily rows, 12% in the TWR, 10% in the benchmarks and 6% in finding the last
row. Two of those are the query, not the maths:

- The last row was `max("Date")` over `"AssetId" = ANY(ids)`: an index-only scan of every entry
  of every asset, 29 278 buffers, 35 ms in `EXPLAIN ANALYZE`. Per asset, a backwards probe of
  the primary key and the largest of those: 64 buffers, 0.25 ms.
- The rows were loaded as whole entities, ten columns of which six are `numeric`. The database
  side is 17 ms for 22 662 rows; the rest was moving and decoding columns nobody reads. The
  returns read the date and value (TWR, XIRR) and quantity and price (the FX split); the query
  now projects those five.

The maths itself (TWR, XIRR, benchmark indices) is the returns module's and is not touched
(ADR-017). Pinned first by `The_period_ends_on_the_callers_latest_row_across_assets`.

Each step on its own, three builds interleaved over four rounds, 40 warm-up calls (median of
the rounds):

| Route | before | last row | + projection |
|---|---:|---:|---:|
| `GET /api/returns/portfolio?period=inception` | 91.7 | 87.5 | **78.6** |
| `GET /api/returns/portfolio?period=12m` | 33.6 | 31.3 | **28.3** |
| `GET /api/returns/assets/{id}` (BTC, PETR4) | 29.6, 22.7 | 29.7, 21.5 | 29.2, 21.0 |

A smaller gain than the phase timer suggested, because at steady state the rows cost less than
in the profiled build. A first pair of runs, with 3 warm-ups on a loaded machine, had shown
260 → 157 ms; repeated, the same build ranged from 106 to 385 ms between rounds, which is what
decision 2 now guards against. The per-asset returns do not move: one asset is about 1 800 rows
and one probe either way.

### 3. The snapshot rebuild's insert

**Cause.** A throwaway timer around the rebuild of each asset (about 1 800 rows for five years):
building the rows 1–6 ms, `AddRange` 20–55 ms, `SaveChanges` 240–400 ms. The rebuild runs on
every movement write, from the movement's date, so a movement dated years back paid it too.

**Change.** Decision 6: one statement with the rows as arrays. On the database side the insert
of 1 814 rows is 33 ms, of which 23 ms are the two foreign-key triggers (`UserId`, `AssetId`),
and the delete before it 1.2 ms. Pinned first by
`The_stored_rows_are_exactly_the_builders_in_every_column` (a USD asset: all ten columns equal
to `SnapshotBuilder.Build`'s). 007's `A_failing_insert_leaves_the_previous_rows_intact` keeps
its rollback assertion and now names the exception it gets (`PostgresException`, 22003).

| Operation | before | after |
|---|---:|---:|
| `POST /api/investments/rebuild` (13 assets, 22 662 rows) | 2 432 | **1 518** |
| `POST …/movements`, dated at the first buy (rebuilds ~1 800 rows) | 338 | **126** |
| `DELETE /api/investments/movements/{id}`, same | 326 | **119** |

What remains of the full rebuild is per asset: loading its closes and rates, and the insert's
foreign-key checks. The nightly rebuild after the sync starts at yesterday, one or two rows per
asset, and was never slow.

### 4. Where the net-worth series opens

**Cause.** 014's `bounds` takes the month of the user's first `PortfolioDaily` row with
`min("Date")`, which the primary key `(UserId, AssetId, Date)` cannot answer without reading all
of the user's rows, and PostgreSQL evaluates `bounds` twice (it is inlined in two places): two
bitmap scans of 22 662 rows, 6–10 ms each, on every dashboard load whatever the window.

**Change.** The first row per asset is one forward probe (the same `LATERAL` shape the series
already uses for each month's last row), and the earliest of those: `EXPLAIN ANALYZE` 25 → 17 ms.
Still Dapper, still `"UserId" = @userId` in each part. Pinned first by
`The_series_opens_at_the_callers_earliest_daily_row_across_assets`.

| Route | before | after |
|---|---:|---:|
| `GET /api/dashboard/net-worth` (24) | 16.9 | **13.2** |
| `GET /api/dashboard/net-worth?months=120` | 21.5 | **17.8** |

What is left is the all-time sum of the user's transactions per month (8 514 rows at ten years),
which the balances need too; see "Measured, not worth doing".

### 5. The web bundle, split by page

**Cause.** `vite build` produced one script of 1 050 kB (314 kB gzip) and warned about it: every
page, the sign-in page included, downloaded every other page, React Hook Form and zod (only the
transactions form uses them) and Recharts (only the dashboard and the investment pages draw
charts).

**Change.** Decision 7: lazy routes. The entry is 366 kB (120 kB gzip); Recharts is a 340 kB
chunk (99 kB gzip) loaded only by the pages that chart; the transactions page carries its form
libraries (122 kB). The unit tests that render the route tree import every page first
(`src/test-routes.ts`), as the static route tree used to, so a first lazy import does not
transform a page's whole graph inside a test's timeout; without it five tests timed out.

Tried and dropped: an explicit `codeSplitting` group `charts` for Recharts and its dependencies.
Rolldown pulls a group's own dependencies into it (React, `clsx`), so the entry imported the
charts chunk and the sign-in page loaded 776 kB instead of 404 kB. Turning that off needs
`strictExecutionOrder`, a runtime cost for a naming nicety.

Scripts per cold page load (headless Chromium, HTTP cache off, sizes from the build output):

| Page | before | after | gzip before → after |
|---|---:|---:|---:|
| `/login` (signed out) | 1 026 kB | **404 kB** | 303 → **131 kB** |
| `/` (dashboard, charts) | 1 026 kB | 791 kB | 303 → 246 kB |
| `/transactions` | 1 026 kB | 530 kB | 303 → 171 kB |
| `/categories` | 1 026 kB | 422 kB | 303 → 139 kB |
| `/settings` | 1 026 kB | 411 kB | 303 → 135 kB |
| `/investments`, returns, asset pages | 1 026 kB | 769–781 kB | 303 → 241–245 kB |

A client-side navigation to a page not yet visited fetches its chunk: 123 kB for the
transactions page, 14–39 kB for the others; Recharts is fetched once.

### 6. React Query: 30 seconds of freshness, and every write ends it

**Cause.** With the library's default `staleTime` of 0, a query is refetched by every observer
that mounts after the first: on a cold load of the dashboard `/api/auth/me` went out three times
(the guard, the layout, the AI card), `/api/accounts` twice on `/accounts`; every return to a
page refetched all of it, and so did every focus of the tab.

**Change.** Decision 8, in `src/lib/queryClient.ts`, used by `App.tsx`. The reason a longer
`staleTime` is safe is the other half: the transaction form, for one, invalidates only
`transactions`, and until now the dashboard's balances were fresh again only because every
mount refetched. A `MutationCache` `onSettled` now marks every cached query stale (without
fetching), so what is read next is fetched; each mutation's own invalidations still refetch
what is on screen. Unit tests in `queryClient.test.ts`; a new E2E test adds an expense after
the dashboard was read and comes back through the sidebar, cache intact, to the new balance.

API requests per page, headless Chromium, the owner's data:

| Load | before | after |
|---|---:|---:|
| cold `/` | 10 (`me` ×3) | **8** |
| cold `/accounts` | 8 (`me` ×2, `accounts` ×2) | **6** |
| cold `/transactions`, `/categories`, `/investments`, `/settings` | 6, 5, 6, 4 | **5, 4, 5, 3** |
| cold `/investments/returns` | 18 | **17** |
| back to `/` through the sidebar, within 30 s | 6–7 | **0** |
| the tab focused again, within 30 s | 8 | **0** |

`refetchOnWindowFocus` stays on: past 30 s, coming back to the tab still picks up what another
tab or the nightly sync changed.

### 7. The web unit suite on `vmThreads`

**Measured** (34 files, 248 tests, same tree, busy machine, several runs each): the default
`forks` pool 29–60 s, `threads` 32 s, `vmThreads` **12–26 s**. `vmThreads` keeps what isolation
means here: each test file gets a fresh VM context, so its own modules and globals. Checked
with a throwaway pair of files in one worker, each writing to a shared module and to
`globalThis` and expecting to see only its own write: green on `vmThreads`, red with
`isolate: false`, which the Vitest hint also offered and which is therefore not used.

Found on the way: under load the first test of `AccountsPage.test.tsx` and
`AccountImport.test.tsx` timed out, because `accounts-fixtures.tsx` renders the route tree
without importing the pages first (change 5). It now imports `src/test-routes.ts` too; five
further runs were green. One run under the same load also failed
`TransactionForm > submits a positive amount for an income category`, which renders no route
and did not fail again.

## Measured, not worth doing

- **An index for the transactions list**, `(UserId, Date DESC, CreatedAt DESC)`. Tried in a
  rolled-back transaction on the scaled data: the planner kept its bitmap AND of
  `IX_Transactions_AccountId` and `IX_Transactions_UserId_Date` and a top-N sort of the user's
  8 514 rows (8 ms); only the date-range filter used the new index, and that query was already
  under 1 ms. No migration.
- **The balances** (`/api/dashboard/summary`, 9.7 ms): an all-time sum per account over the
  user's transactions, 4.7 ms in the database at ten years. A covering index would save a few
  milliseconds for a migration; not now (see "Proposals").
- **Tracking queries.** Every read route projects or uses `AsNoTracking`; the tracked queries
  left are single-row lookups on write paths, which need tracking.
- **The per-asset returns** (19–29 ms): about ten round trips each, at 1.5–2 ms per round trip
  on this Windows and Docker set-up (far less on the Linux host), and the benchmark series built
  again per call (about 10 ms). The fix is structural (see "Proposals").
- **The returns maths** (TWR, XIRR, benchmark indices: about a third of the inception route).
  The returns module is where the modelling rigour lives (ADR-017); nothing here justifies
  touching it.
- **Binary `COPY` for the rebuild's insert** instead of `unnest`: the database side is the
  foreign-key triggers either way (23 of 33 ms for 1 814 rows).
- **zstd in Caddy.** `encode gzip` is already on, for the static files and the proxied `/api`
  JSON. The browser talks to Cloudflare, which picks the encoding it serves; zstd between Caddy
  and the tunnel would change nothing a person waits for.
- **An explicit `charts` chunk** and **`isolate: false`** in Vitest: changes 5 and 7.

## Proposals

Not done, each for the reason given.

1. **One route for every asset's returns**, e.g. `GET /api/returns/assets?period=…`. The returns
   report makes 1 + 13 calls (17 requests on a cold `/investments/returns`), and each reloads the
   movements, the USDBRL series and the benchmarks. One pass would read them once. It is a new
   API shape, which this spec rules out; it needs its own spec.
2. **`Cache-Control: public, max-age=31536000, immutable` on `/assets/*` in Caddy.** The chunk
   names carry a content hash, so they never need revalidating; today each page load
   revalidates its 10–20 chunks. Not measured here: it needs the production stack behind
   Cloudflare, and it is a deploy change for the owner to take.
3. **Benchmark indices cached in-process**, per code, base day and end, dropped after each sync.
   It would take the ~10 ms off each per-asset call. It trades freshness rules for speed, so it
   is a decision for a person, not a measurement.
4. **A covering index for the all-time balances**, `(UserId, AccountId) INCLUDE ("Amount")`, if
   the transaction count grows well past ten years of the owner's volume.

## Data model changes

None. No migration: the only index candidate did not change the plan (decision 5, and
"Measured, not worth doing"). `docs/migrations/` is unchanged.

## API surface

None. Every response is byte-for-byte what it was on the same data: the timing script saves
every body, and a before and an after are compared with `diff`.

## Test plan

Each test that pins a query was written first and passed against the query it pins, before the
query was rewritten.

Found while verifying: the integration suite's PostgreSQL container ran out of connections.
Every test that makes a database of its own (`InvestmentsApi`, `MarketDataApi`) left its Npgsql
pool open, and Npgsql keeps idle connections for five minutes, longer than the suite runs. A poll
of `pg_stat_activity` during the run peaked at exactly 100, the container's limit, and
`TransactionPersistenceTests.Date_round_trips_as_the_same_calendar_day_under_any_server_timezone`,
which opens two new connection strings, failed on `53300 too many clients` three runs out of
three; this spec's four new per-database tests were enough to tip it. Both hosts now clear their
database's pool when disposed (`PostgresFixture.ReleaseConnections`): peak 30, all green.

### Integration — `api.tests`

1. `InvestmentSummaryTests.Each_asset_is_valued_at_its_own_latest_row_and_only_for_its_owner`:
   the summary and the positions take each asset's own latest row, the rows ending on different
   days, and another user's later row for the same instrument counts for that user only
2. `DashboardNetWorthTests.The_series_opens_at_the_callers_earliest_daily_row_across_assets`:
   with no transactions, the series opens at the caller's earliest row, whichever asset has it,
   and not at another user's earlier one
3. `SnapshotRebuildTests.The_stored_rows_are_exactly_the_builders_in_every_column`, a USD asset;
   007's `A_failing_insert_leaves_the_previous_rows_intact` keeps its rollback assertion and
   expects the `PostgresException` (22003) the insert now throws
4. `ReturnsEndpointTests.The_period_ends_on_the_callers_latest_row_across_assets`; 008's returns
   tests unchanged and green

### Unit — `web`

5. `src/lib/queryClient.test.ts`: a read within 30 s is not fetched again; a mutation that
   succeeds or fails marks every cached query stale without fetching; the next read fetches;
   a query on screen that the mutation invalidates itself is refetched
6. Every existing page test passes with the pages lazy; the tests that render the route tree
   import `src/test-routes.ts` first

### E2E

7. `dashboard.spec.ts`: an expense added after the dashboard was read shows on it after a
   client-side navigation (it would fail with the 30 s `staleTime` alone)
8. Every existing test passes, on an isolated database (`financas_e2e_perf`)

## Verification

```
bash scripts/verify.sh
```

E2E on :5093/:5193 against `financas_e2e_perf` (never `scripts/verify-e2e.sh`, which stops
the shared PostgreSQL). The before/after tables above were taken with the scripts in "Method".

Result on this branch: `verify.sh` green, .NET 1 010 of 1 010 and web 248 of 248 (34 files);
E2E 30 of 30. With the mutation-wide invalidation taken out, the new E2E test fails, the
dashboard still reading `+1.000,00`, as it should.
