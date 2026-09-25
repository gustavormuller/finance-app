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
| 2 | How it is timed | The API from a scratch build, `Development`, SQL logging off, on :5093. A Node script signs in with `dev-login` and times each route 20 times after 3 warm-ups, each on a fresh connection, and reports the median. The machine is shared with other work, so a before and its after are always taken back to back, never across hours. |
| 3 | What counts as slow | A route whose median is well above the ~5 ms a plain filtered read costs here, and a cause `EXPLAIN (ANALYZE, BUFFERS)` or a timer can name. Anything at that floor is left alone. |
| 4 | Rewriting a query | Only behind a test that pins its result first, and in the same layer and tool it was in: EF LINQ stays LINQ (the query filters keep applying), Dapper SQL stays SQL naming `"UserId"` in every statement. |
| 5 | Indexes | Only where a plan shows the index used and the time gone. No index is added "in case". |
| 6 | The snapshot rebuild's insert | One `INSERT … SELECT FROM unnest(arrays)` through `Database.ExecuteSqlAsync`, instead of `SaveChanges` over thousands of tracked rows. Same rows, same transaction (EF enlists raw SQL in it), same advisory lock. A failed insert now surfaces as the `PostgresException` itself rather than wrapped in a `DbUpdateException`; nothing catches either on these paths. Binary `COPY` was the alternative; the database side is dominated by the foreign-key triggers either way. **(review)** |
| 7 | Web bundle | Each page is its own chunk, loaded on navigation (TanStack Router's `lazyRouteComponent`), and Recharts with its dependencies is a separate `charts` chunk. |
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

Median of 20, milliseconds, one run each; "real" is the copy of `financas`, "scaled" adds the
synthetic volume (decision 1).

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

## Changes

Each before and after taken back to back on the scaled database, two rounds, median of 20
(ms); response bodies compared with `diff` and identical.

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
| `GET /api/investments/summary` | 78.0 | **5.8** |
| `GET /api/investments/assets` | 92.6 | **20.2** |

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

The machine was busier during this pair (another agent's build), so both columns are slower
than the baseline table; the median of four interleaved rounds:

| Route | before | last row | + projection |
|---|---:|---:|---:|
| `GET /api/returns/portfolio?period=inception` | 260 | 187 | **157** |
| `GET /api/returns/portfolio?period=12m` | 104 | 59 | **62** |
| `GET /api/returns/assets/{id}` | 37–54 | 35–51 | 43–63 (noise) |

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
| `POST /api/investments/rebuild` (13 assets, 22 662 rows) | 2 524 / 2 767 | **1 408 / 1 963** |
| `POST …/movements`, dated at the first buy (rebuilds ~1 800 rows) | 467 / 313 | **129 / 274** |
| `DELETE /api/investments/movements/{id}`, same | 472 / 332 | **133 / 257** |

(Two rounds, busy machine.)

## Measured, not worth doing

Filled in as measured.

## Proposals

Filled in as measured.

## Data model changes

None planned; an index lands here only under decision 5.

## API surface

None. Every response is byte-for-byte what it was on the same data: the timing script saves
every body, and a before and an after are compared with `diff`.

## Test plan

### Integration — `api.tests`

1. The summary and the positions take each asset's latest daily row **per user**: two assets
   whose rows end on different days, and another user's later rows for the same instrument,
   which count for nobody but that user (pins the query before it is rewritten)
2. The net-worth series opens at the first daily row when it predates every transaction
   (014's tests 3 and 5 already pin it; kept green)
3. The snapshot rebuild writes exactly the rows `SnapshotBuilder` builds, in the caller's
   transaction, and a failure rolls back the delete with it (007's rebuild tests kept green,
   plus one for the rollback)
4. 008's returns tests kept green unchanged

### Unit — `web`

5. A mutation, when it settles, marks every cached query stale, and the queries on screen still
   refetch through their explicit invalidations
6. Every existing page test passes with the pages loaded lazily

### E2E

7. Every existing test passes, on an isolated database (`financas_e2e_perf`)

## Verification

```
bash scripts/verify.sh
```

E2E on :5093/:5193 against `financas_e2e_perf` (never `scripts/verify-e2e.sh`, which stops
the shared PostgreSQL). The before/after tables above were taken with the scripts in "Method".
