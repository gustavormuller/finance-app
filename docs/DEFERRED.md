# Deferred — questions, deviations, skipped items

Every entry: spec · checkpoint · what · why deferred · what was done instead.

## Run setup (orchestrator)

- **run · setup · `docs/CLAUDE_CODE_WORKFLOW.md` does not exist.** The run prompt says to read
  it. Nothing to read; proceeded with CLAUDE.md, ARCHITECTURE.md and the specs.
- **run · setup · `docs/handoffs/003.md` does not exist**, and neither did `docs/handoffs/`. The
  prompt says handoffs follow 003.md's shape. Shape taken from the prompt instead: status, what
  exists now, conventions, decisions the next spec must make, known debts, lessons.
- **run · setup · specs define no checkpoints.** The orchestrator split each spec into
  checkpoints along its own sections (domain + data model → application/API → web → E2E),
  keeping each near the ~200-line diff guideline. The split is listed per spec in STATUS.md.
- **run · setup · cloud Linux environment, not Windows.** .NET SDK 10.0.401 (pinned by
  global.json) installed from packages.microsoft.com's Debian 12 feed into /opt/dotnet,
  because builds.dotnet.microsoft.com is blocked by the egress proxy. Playwright expected
  Chromium build 1243; the preinstalled build 1194 was symlinked in its place. E2E runs against
  database `financas_e2e` via user-secrets (Google keys are dummies; E2E uses dev-login).
  None of this is committed — it is environment, not repo.
- **run · setup · orchestrator read the specs just-in-time.** ARCHITECTURE.md ADRs, 004 and
  005 were read up front; 006–010 are read when each spec starts, to keep the orchestrator's
  context small. Each subagent reads its own spec in full.

## 005 · checkpoint 1

- **005 · CP1 · unit test 3 ("Rule 3 with Transfer rejects zero") — ambiguity.** 003's
  design reports zero once, from rule 1 (`ValidateAmount`), and keeps rule 3 (`ValidateSign`)
  silent on zero so the `amount` field is not reported twice. Simplest reading kept: the test
  asserts `ValidateAmount(0)` fails and `ValidateSign(0, Transfer)` is null. A zero Transfer is
  still refused by every endpoint, which call both rules.
- **005 · CP1 · unit test 4 — no new test.** 003's unit test 7
  (`Sign_must_agree_with_the_category_kind`, all four Income/Expense combinations) already is
  test 4; its doc comment now says so. Unchanged and still green.
- **005 · CP1 · `openingBalance` on PUT — ambiguity.** Amendment 3 says "default 0". Chosen:
  optional (`decimal?`) on both routes; omitted on create → 0, omitted on update → the stored
  value is kept (same pattern as `currency`). Any sign accepted (a card starts negative).
  Rounded to 2 places `ToEven` like `Money`, so the response matches what `numeric(18,2)`
  stores. No upper-bound check: a value beyond `numeric(18,2)` would be a 500, as it already
  is for transaction amounts.
- **005 · CP1 · `docs/migrations/` did not exist.** Created it for `005-AddDashboard.sql`.
- **005 · CP1 · flaky test observed.** One full run failed
  `TransactionPersistenceTests` (two `Amount_round_trips_byte_identical` cases and the timezone
  test) before any implementation touched them; they passed in isolation and on every rerun.
  Not investigated further.

## 005 · checkpoint 2

- **005 · CP2 · Dapper 2.1.89** (latest stable on nuget.org at the time), restored through the
  proxy with no extra setup. No migration in this checkpoint.
- **005 · CP2 · where the code lives.** `Application/Dashboard/DashboardQueries.cs` (a scoped
  class taking `AppDbContext`, ADR-016; SQL as `internal const` raw string literals so the tests
  can run them) and `Shares.cs`; routes in `Endpoints/DashboardEndpoints.cs`. The BRL-only total,
  `excludedFromTotal`, `net` and the share rounding live in `Application/`, not the endpoint.
- **005 · CP2 · income and expense sum BRL rows only.** The spec restricts `total` to BRL but is
  silent on `month`, `monthly` and `by-category`. Summing two currencies is the bug `Money`
  exists to stop (ARCHITECTURE.md §4), so every income/expense aggregation filters
  `"Currency" = 'BRL'` (as a parameter). Foreign-currency activity is invisible there until
  currency conversion exists.
- **005 · CP2 · signs as stored.** `expense` and every Expense `amount` in `by-category` are
  negative; `net = income + expense`. `share` is positive (amount / sum of amounts, same sign).
- **005 · CP2 · `share` uses largest-remainder rounding** to 4 places, so shares sum to exactly
  1.0000 (spec test 17 allows 0.0001 of drift; three equal thirds would otherwise sum to
  0.9999). Every share stays within 0.0001 of the exact value. All shares are 0 if the sum is 0.
- **005 · CP2 · `months` below 1 is clamped up to 1**, symmetric with the clamp to 36 and with
  the transactions page size ("an oversized page is a caller being greedy, not wrong"). A
  non-integer `months` is a 400 on field `months`.
- **005 · CP2 · parameters bound as strings.** `month`, `months` and `kind` are parsed in the
  endpoint so every malformed value is a 400 with a pt-BR message naming the field, rather than
  the framework's English binding failure. `month` must match `YYYY-MM` exactly (`2026-9` is
  refused). `kind` accepts `Income`/`Expense` by name (case-insensitive) and defaults to
  `Expense`; `Transfer` and `2` get a message explaining transfers are neither; any other value
  (including other numbers) gets the generic message. `by-category` reports `month` and `kind`
  errors together.
- **005 · CP2 · "current month" is the UTC calendar month**, the same clock
  `TransactionRules.ValidateDate` uses. For a few hours at the turn of a month it differs from
  Brasília's; the web sends `month` explicitly whenever it matters.
- **005 · CP2 · SQL defence in depth.** Besides the mandated `"UserId" = @userId` on the driving
  table, joined `Transactions`/`Categories` are also constrained to the user. Month arithmetic
  casts `date` to `timestamp` explicitly, so PostgreSQL never picks the `timestamptz` overload
  of `date_trunc`/`generate_series` and the session timezone cannot move a row between months.
- **005 · CP2 · test 6** scans every `.cs`/`.sql` file under `api/Application/Dashboard/`: each
  raw string literal containing SELECT/INSERT/UPDATE/DELETE must contain `"UserId" = @userId`,
  at least three such statements must exist, and a SQL keyword in an ordinary (non-raw) string
  literal fails the test, so SQL cannot dodge the check by being written another way. The
  decimal-mapping test runs each SQL constant untyped through Dapper and asserts every money
  column arrives as `decimal`; a third test asserts no type in the namespace has a
  `double`/`float` property.
- **005 · CP2 · extra tests beyond the plan:** 401 without a session, the 400 cases for every
  parameter, `months` of 36/0/-5/omitted, and `by-category` honouring `month`.
- **005 · CP2 · root cause of CP1's "flaky" `TransactionPersistenceTests`.** Not flaky: the
  suite exhausted Linux's default `fs.inotify.max_user_instances` (128). Every test host
  watched appsettings and user-secrets files, and once the limit was hit later hosts failed in
  `WebApplication.CreateBuilder` with an `IOException`. The dashboard tests pushed the run over
  the edge deterministically (15-16 failures). Fixed in `IdentityApiFactory` with
  `UseSetting("hostBuilder:reloadConfigOnChange", "false")`; the full suite is green again.
- **005 · CP2 · observed, not fixed (out of scope):** `api/Program.cs`,
  `Domain/IUserOwned.cs`, `Domain/Transactions/Category.cs`, `Domain/Transactions/Money.cs`,
  `Infrastructure/AuthenticationSetup.cs` and `Infrastructure/GoogleUserInfo.cs` contain
  non-ASCII (em dashes) with no UTF-8 BOM, against the CLAUDE.md rule. All occurrences are in
  comments, so nothing compiles wrong; adding the BOMs is a one-line chore for a later
  checkpoint.

## 005 · checkpoint 3

- **005 · CP3 · where the old landing's contents went.** `HomePage.tsx` is deleted. The
  signed-in name (`data-testid="current-user"`) and the Sair button move to the end of the
  navigation row in `ProtectedLayout.tsx`, so they are on every protected page. The existing
  E2E suite (`auth.spec.ts` asserts both on `/`) passes unchanged. On a phone the nav row
  scrolls horizontally as before, so Sair sits at the end of that scroll.
- **005 · CP3 · month selector.** Previous/next buttons; forward stops at the local current
  month. The selected month drives both "Resumo do mês" and the category breakdown (spec §UI
  says "this month" for both; following the selector is more useful and costs nothing). Kept
  in component state, not the URL: nothing links to a dashboard month yet.
- **005 · CP3 · the monthly series has no `month` parameter.** The API ends it at the UTC
  month. The web asks for 13 months and keeps the 12 up to the local month
  (`lib/months.ts` `lastMonths`), which covers Brasília's evening at a month's turn. A
  browser east of UTC would still see the series end one month early; not handled.
- **005 · CP3 · category breakdown is HTML, not Recharts.** A ranked list with name, share,
  amount and a bar is readable as markup to a screen reader and trivially testable; Recharts
  is used where the spec names it (the 12-month grouped bars). Bar length is the row's
  `share` relative to the largest share, so no money arithmetic happens client-side.
- **005 · CP3 · money on the client.** Values stay JS numbers, as elsewhere in the web
  (`api/finance.ts` comment). The only operations are `Math.abs` for bar heights and
  formatting; `share` (a ratio) is multiplied for the percentage and bar width.
- **005 · CP3 · chart colours.** Blue/orange (`--chart-income`/`--chart-expense` in
  `index.css`, light and dark), not green/red: green/red failed the colour-vision check
  (ΔE 5 deutan). The monthly chart also renders a visually hidden table of its values.
- **005 · CP3 · credit card style.** `text-destructive` on the `Amount` when the account is
  a `CreditCard` **and** its balance is negative; a card in credit keeps `Amount`'s normal
  style. Non-BRL rows say "USD, fora do total".
- **005 · CP3 · empty state** follows test 23 literally: `balances` empty and every month
  zero. An account with no transactions shows the dashboard (its opening balance is news).
- **005 · CP3 · Transfer in the transaction form.** A Saída/Entrada radio pair appears only
  for a Transfer category, default Saída; editing opens on the stored sign. The typed sign is
  still ignored. The category dropdown always has three optgroups, Transferência last.
- **005 · CP3 · Transfer on the categories page** is listed under its own "Transferências"
  heading; before, a seeded Transferência would have been invisible there.
- **005 · CP3 · `openingBalance` input.** Typed as shown: pt-BR (`-1.234,56`), or a plain
  `1234.56` when there is no comma; U+2212 accepted; empty is 0; anything else is refused in
  the browser ("Informe um número."). Always sent, on update too (the form shows the stored
  value). The accounts table gained a "Saldo inicial" column (hidden on phones).
- **005 · CP3 · field errors on the account form.** A 400's `errors[field]` now render under
  the field, fixing handoff 004's English-title debt for this page only. The transactions and
  categories pages still show `ApiError.message`; left for a later chore.
- **005 · CP3 · freshness.** Transaction, account and import writes do not invalidate
  `['dashboard', …]`; the dashboard refetches on mount (no stale time), which is enough since
  every write happens on another page. The recent list shares the `['transactions']` prefix.
- **005 · CP3 · diff size.** `build: add recharts` is 369 lines, almost all
  `package-lock.json`. The sections 1–2 commit is 263 lines, of which 90 are the deleted
  `HomePage.tsx`.

## 006 · checkpoint 1

- **006 · CP1 · five checkpoints, not four.** The spec has a web screen and an E2E test
  (25, 26) besides the API; putting them in the endpoint checkpoint would pass ~200 lines
  twice over. Plan in STATUS.md: data model → providers → sync/resilience/scheduling →
  endpoints → web + E2E + handoff.
- **006 · CP1 · `MarketAsset` and `SyncRun` have no `UserId` — CLAUDE.md names only
  `prices` and `benchmarks` as exceptions.** Spec decision 3 makes all four types shared
  and says to test it, and the run prompt repeats it. `MarketAsset` is the catalogue the
  `prices` rows hang off; `SyncRun` is the state of a shared job (ADR-003's "jobs table with
  status"). Followed the spec. **For the human:** CLAUDE.md's exception list could name
  `market_assets` and `sync_runs` too; not edited here.
- **006 · CP1 · ADR-015 names one port, `IMarketDataProvider`; the spec declares two**
  (`IPriceProvider`, `IBenchmarkProvider`). The spec authorises amending ADR-003 and the
  `prices` table only. The substance of ADR-015 holds (market ports in the core,
  adapters in infrastructure), so this was not treated as a contradiction to stop on, and
  ADR-015 and the §7 example are left as written. Note for CP2: by ADR-015's own criterion
  ("more than one real or foreseen implementation"), `IBenchmarkProvider` has one, BCB
  (IVVB11 comes through brapi's price provider). **For the human:** confirm, or amend ADR-015
  to name the two ports.
- **006 · CP1 · ARCHITECTURE.md edits beyond the two mandated.** The stack table's jobs row
  and §2's "the job infrastructure below arrives with 009" restated ADR-003's old "decided
  in 009"; both now point at 006. The `prices` block also lists `market_assets` and
  `sync_runs`, and drops `prices.currency` (it lives on the catalogue row, spec data model).
- **006 · CP1 · the upsert lives in CP1**, in `Application/MarketData/MarketDataStore.cs`, so
  test 18 has something to test before the sync exists. One `INSERT … SELECT … FROM
  unnest(dates, values) … ON CONFLICT DO UPDATE` per batch through `ExecuteSqlAsync`
  (parameters, not Dapper, not change tracking): five years of closes in one round trip.
  Returns rows inserted or overwritten, which the sync can report as "rows written". A day
  repeated in one batch keeps its last value (PostgreSQL refuses to update a row twice in
  one statement; CoinGecko's final point is "now"). Not yet registered in DI; CP3 does that.
- **006 · CP1 · `numeric(18,8)` limits.** More than eight decimals are rounded by
  PostgreSQL (half away from zero); a value of 10^10 or more fails the statement. The
  sync's per-asset catch (CP3) is where such a failure would land.
- **006 · CP1 · `DailyClose` and `DailyValue` in their own files**, not in
  `IPriceProvider.cs` as the spec sketches, because the store needs them before the ports
  exist. `DailyValue` is not defined in the spec; it mirrors `DailyClose`.
- **006 · CP1 · choices the spec leaves open.** `Prices → MarketAssets` is `ON DELETE
  RESTRICT` (nothing deletes an asset yet; an accidental delete should not take five years
  of closes). `SyncRun.Summary` is a `string` mapped to `jsonb`, starting as `{}`; its shape
  is the sync's (CP3), and `Domain/` stays serializer-free. No database defaults on
  `IsActive` or `Summary`. `SyncRuns` has an index on `StartedAt DESC` (the last-20 list and
  the 26-hour startup check); `MarketAssets.Ticker` has none (a small catalogue).
  `ProviderKind` has no BCB member: BCB serves benchmarks only. The class enum is
  `MarketAssetClass`, not `AssetClass`, to leave that name to 007.
- **006 · CP1 · tests.** Written before the mapping, so they use `Set<T>()`: declaring DbSets
  first would have left the model with pending changes and failed every migrating test.
  Test 17 reads as a second random user id and as no user (market data has no FK to users,
  so no sign-in is needed), and also checks the model: no `IUserOwned`, no `UserId`, no
  declared query filter, with `Account` as the control. The declared-type half of test 9
  scans `Domain`, `Application` and `Infrastructure.MarketData`, so a CP2 provider DTO with
  a `double` fails when written. No provider fixtures touched in CP1.

## 006 · checkpoint 2

- **006 · CP2 · ADR-015 amended (human decision).** Two ports, `IPriceProvider` and
  `IBenchmarkProvider`, replace `IMarketDataProvider` in ADR-015 and in the §7 example.
  `IBenchmarkProvider` has one implementation (BCB SGS) and rests on a foreseen second
  source (USDBRL or IVVB11 from brapi or Twelve Data). Its own docs commit, 92c9508.
- **006 · CP2 · no fixture is captured; all five are hand-written.** On 2026-09-24 the
  environment's egress policy refused `api.bcb.gov.br`, `www3.bcb.gov.br`, `brapi.dev`,
  `api.coingecko.com` and `api.twelvedata.com` (proxy CONNECT 403), and there were no keys.
  The files in `api.tests/Fixtures/MarketData/` follow each provider's documented response
  shape. Their values are illustrative, not market data, and the folder's README says so
  file by file. **For the human:** capture one real response per provider under the same
  file names, then update the README. If a real shape differs, the tests that break
  point at the adapter to fix. Decision 9 ("captured once") is not met until then.
- **006 · CP2 · SGS codes unverified.** `www3.bcb.gov.br/sgspub` was unreachable, so the
  codes are the spec's: CDI 12 and SELIC 11 in percent per day, IPCA 433 in percent per
  month, USDBRL 1 (sell rate, BRL per USD) as a level. **Pending human verification**
  (manual step 1) before the first real sync. The units sit beside the codes in
  `appsettings.json` as a `BenchmarkUnit` enum, not as a comment:
  `{ "Code": 12, "Unit": "PercentPerDay" }`. That gives 008 a typed value, and a typo
  fails binding. `Level` covers index points, exchange rates and prices; for these the
  return is a ratio.
  **Verified live on 2026-09-25 (019):** all four codes answer, with values in the expected
  units: CDI and SELIC 0.050788 (% a day), IPCA −0.32 (% a month, 2026-08), USDBRL 5.1795.
  No change to `appsettings.json` was needed.
- **006 · CP2 · provider behaviour from documentation, not observation.** Each of these is
  unverified against the live API:
  - BCB: a `404` is read as "no values in the range", for example a weekend or a month IPCA
    has not published. Otherwise every weekend sync would fail. BCB caps daily-series
    queries at a 10-year window, which the 5-year backfill stays inside.
  - brapi: an unknown ticker is a `404` with `{"error":true,...}`, read as an empty series
    (test 4). brapi takes a named range, not dates, so the adapter asks for the smallest
    one that reaches `from` and trims the result. Timestamps become days at a fixed UTC-3,
    which avoids needing a tz database in the container; Brazil has had no DST since 2019.
    A null close is skipped. **Plan limits:** brapi's free plan may not serve 5-year
    ranges or tickers beyond its demo set, so the backfill needs a paid token.
  - CoinGecko: `market_chart?days=N&interval=daily`. Points are at 00:00 UTC, and each
    one is dated with its own UTC day, as test 5 asks. That point is in effect the close
    of the previous day; 008 should know. The trailing point is "now", so a day keeps its
    first point. The public and demo plans serve 365 days, so `MaxHistoryDays: 365` caps
    the request. A 5-year crypto backfill is therefore one year, and manual step 4's
    "~1800 rows for BTC" needs a paid plan and a raised cap. `VsCurrency` is configuration
    (`usd`), because the port passes no currency. **For CP4:** registering a CoinGecko
    asset should require its `Currency` to match.
  - Twelve Data: error bodies may come with HTTP 200 or 400. A 429 is a rate limit. A 404,
    or a 400 "No data is available", is an empty series. Any other code throws an
    `HttpRequestException` with that code, because a bad key is not a malformed body.
    `end_date` is sent as `to + 1` and the result trimmed, so it works whether the API
    reads the bound as inclusive or exclusive. `outputsize=5000` is sent because the
    default is 30.
- **006 · CP2 · keys travel in headers, never in URLs.** The brapi token is a bearer
  header. The CoinGecko demo key is `x-cg-demo-api-key`. Twelve Data uses
  `Authorization: apikey`. `HttpClient` logging records URLs, so a key in a query string
  would end up in logs. No key is committed, and a test asserts that `appsettings.json`
  keeps them empty. A missing key is not a boot error, since brapi and CoinGecko answer
  some calls without one. It shows up as a failed provider in the sync run.
- **006 · CP2 · exception names.** The spec's `ProviderRateLimited` and
  `ProviderResponseInvalid` are `ProviderRateLimitedException` (with `Retry-After`) and
  `ProviderResponseInvalidException`, under an abstract `MarketDataProviderException`
  carrying the provider name. Their messages are English diagnostics for the logs. **For
  CP3/CP5:** error text that reaches the `/market-data` screen through `SyncRun.Summary`
  must be pt-BR. Map it from the exception type; do not show the message.
- **006 · CP2 · what counts as "malformed" (test 8).** JSON that does not parse, a missing
  property, a value of the wrong kind, and a date or number that does not parse all become
  `ProviderResponseInvalidException`. Transport failures such as a 5xx or a timeout stay
  `HttpRequestException` or `TaskCanceledException`, left for CP3's resilience handler to
  retry. The adapters also return only days inside `[from, to]`, oldest first.
- **006 · CP2 · test 9, parsing half.** Every number goes from JSON text to `decimal`
  through `JsonElement.GetDecimal` or `decimal.Parse(..., InvariantCulture)`, never
  through `double`. The CP1 declared-type scan covers `Infrastructure.MarketData` and
  passes. A CoinGecko test also feeds a 26-significant-digit price and asserts it arrives
  exact, which a double could not carry.
- **006 · CP2 · test 2 without named cultures.** The test host runs .NET in
  globalization-invariant mode, so `CultureInfo.GetCultureInfo("pt-BR")` throws. The test
  instead clones the invariant culture into hostile ones: month-first dates and a decimal
  comma.
- **006 · CP2 · wiring.** `AddMarketDataProviders()` in `Infrastructure/MarketData/`
  binds `MarketData`, registers `TimeProvider.System` with `TryAdd`, gives each adapter a
  typed client, and registers the three price providers as `IPriceProvider`, from which
  the registry is built. The registry is transient, like the typed clients. Two providers
  for one kind are refused when it is built. BCB is registered as `IBenchmarkProvider`.
  It is wired in `Program.cs` but nothing calls it yet. The adapters build absolute URIs
  from `MarketData:*:BaseUrl` rather than setting `BaseAddress`, because BCB's base is a
  prefix the code is appended to. The spec lists only the BCB base URL; the other three
  are configuration too.
- **006 · CP2 · diff sizes.** Commit 9948bf0 is ~240 lines without fixture JSON: the
  harness, the README and BCB's tests together. The other commits are under ~200 lines.
- **006 · CP2 · handoff to CP3.**
  - The `IHttpClientBuilder` from `AddPriceProvider<T>` (and BCB's `AddHttpClient`) is
    where to attach `AddResilienceHandler` per provider (tests 23, 24). The package is
    `Microsoft.Extensions.Http.Resilience`, not yet referenced. Do not retry
    `ProviderRateLimitedException` blindly; it carries `RetryAfter`.
  - The sync should pass `to` = yesterday, or else trim today. Otherwise a manual daytime
    sync stores an intraday price as today's close, and because the next `from` is the
    day after the latest stored price, that price is never corrected.
  - IVVB11 as a benchmark comes from brapi's `IPriceProvider` (spec, Configuration), not
    through `IBenchmarkProvider`. The sync must copy it into `Benchmarks` under code
    `IVVB11`, with unit `Level`.
  - Benchmark codes to sync are the keys of `MarketData:Bcb:Series`. An unconfigured code
    throws `InvalidOperationException` before any request.

## 006 · checkpoint 3

- **006 · CP3 · sync tests 10–14 are integration tests, not unit tests.** The spec files
  them under `Unit`. But the sync reads and writes through `AppDbContext` (ADR-016, no
  repository to fake), and the upsert is PostgreSQL SQL (`unnest`, `ON CONFLICT`). So they
  run against the Testcontainers database with fake providers
  (`api.tests/Integration/MarketDataSync*.cs`). The sync reads every active asset in a
  shared catalogue, so each test migrates a database of its own. Tests 15, 16, 23 and 24
  stay unit tests.
- **006 · CP3 · the date range.** `to` is yesterday **in UTC**. A UTC day ends after both
  B3 (about 21:00 UTC) and the US markets (20:00–21:00 UTC) close, and CoinGecko dates its
  points by UTC day, so every stored close is final, whatever time a manual sync runs.
  `from` is the day after the latest stored row. When there is none, it is `today (UTC) −
  BackfillYears`, as the spec says (not `to − BackfillYears`). A series already stored
  through yesterday makes no request and counts as synced.
- **006 · CP3 · `LastSyncedAt`** is set only after the asset is current, whether that took
  a fetch and upsert or no request at all. A failed asset keeps its old value. It is
  written with `ExecuteUpdate`, not change tracking, so a failure in one asset never leaves
  tracked state behind for the next.
- **006 · CP3 · summary shape.** `SyncRun.Summary` is a JSON object keyed by provider name:
  `Brapi`, `CoinGecko`, `TwelveData` (the `ProviderKind` name) and `Bcb` for the benchmark
  port. Each value is `{ rowsWritten, itemsSynced, itemsFailed, error, failures: [{ item,
  error }] }`. `item` is the ticker or the benchmark code, and `error` is the first
  failure's text. IVVB11 counts under `Brapi`. A provider with nothing to sync is absent.
  `SyncSummaryJson` reads and writes it (camelCase), so CP4's endpoint and CP5's screen
  share one shape.
- **006 · CP3 · status.** `Succeeded` when no item failed, which includes a run with
  nothing to sync. `Failed` when none succeeded. `PartialFailure` otherwise. An item is
  one asset or one benchmark series, so "one provider failing" (test 13) is every item of
  that provider failing.
- **006 · CP3 · failures reported in pt-BR.** `SyncErrorText.For(exception)` maps the
  exception type to fixed pt-BR text: rate limit, unreadable response, key refused
  (401/403), communication failure (other `HttpRequestException`), timeout
  (`TaskCanceledException`, Polly `TimeoutRejectedException`), circuit open
  (`BrokenCircuitException`), not configured (`InvalidOperationException`: no provider
  registered, unknown SGS code), and a generic fallback. The exception itself goes to the
  log with its English message. Referencing Polly's exception types from `Application/`
  is a small leak of the resilience library into the core. It is accepted because those
  types are what the ports surface. `InvalidOperationException` is broad: an EF Core
  failure of that type would read as "sem configuração".
- **006 · CP3 · a `429` stops its provider for the rest of the run.** The provider's
  remaining assets are recorded as failed with the rate-limit text and are not requested.
  The resilience handler neither retries a 429 nor counts it toward the circuit: it is
  the provider asking for less, not a fault. `RetryAfter` is not otherwise used, because
  the next attempt is the next run.
- **006 · CP3 · a run that stops mid-way** (shutdown cancellation, or a database failure
  outside any item) is marked `Failed` with whatever summary it had, on a best-effort
  basis, and the exception propagates. A row can still stay `Running` if the process dies
  outright, as `SyncRun`'s doc comment already says.
- **006 · CP3 · IVVB11 is configuration.** `MarketData:PriceBenchmarks:IVVB11 = { Provider:
  Brapi, Symbol: IVVB11, Unit: Level }`. The sync reads it through brapi's `IPriceProvider`
  and upserts it into `Benchmarks` under `IVVB11`. The BCB codes synced are the keys of
  `MarketData:Bcb:Series`, in ordinal order, after the assets and price benchmarks.
- **006 · CP3 · resilience.** Every provider's typed client gets `AddResilienceHandler`,
  including BCB's. From the outside in: a total timeout (3 min), then retry (3 retries,
  exponential from 2 s with jitter, for 5xx/408/network/timeout, and it honours
  `Retry-After`), then the circuit breaker, then a per-attempt timeout (30 s). All values
  are under `MarketData:Resilience`. `HttpClient.Timeout` is switched off, because at 100
  s it would cut the retries short. Polly v8 has no "N consecutive failures" breaker, so
  "five consecutive failures" (test 24) is `FailureRatio = 1.0` with `MinimumThroughput =
  5` over a 5-minute window: every attempt in the window failed, and there were at least
  five. The retry sits outside the breaker, so each attempt counts: a failing call makes
  four attempts, and the circuit opens during the second failing call. The pipeline is
  keyed by the typed client's name, so each provider has its own circuit. It lives in a
  singleton registry, so it outlives each transient client. It is in-process state and a
  restart closes it.
- **006 · CP3 · the job.** `Infrastructure/Jobs/MarketDataSyncJob` computes the next
  occurrence with Cronos in `TimeProvider.LocalTimeZone` and waits on the injected
  `TimeProvider`, one day at most per `Task.Delay`, which refuses anything past about 49
  days. The 26-hour startup check uses the latest `SyncRun.StartedAt` of any trigger, so a
  recent manual run counts. A cron that does not parse fails the boot, in `StartAsync`.
  A failed run is logged and the loop goes on. If the latest run cannot be read at
  startup, the job waits for the schedule. **For the human:** "server local time" is the
  container's zone, and a stock .NET image is UTC. There, `0 3 * * *` runs at 00:00 in
  São Paulo. Set `TZ=America/Sao_Paulo` in the production container (none exists yet;
  only `deploy/docker-compose.dev.yml`), or write the cron in UTC.
- **006 · CP3 · keeping tests off the network.** `MarketData:ScheduledSync` defaults to
  `false` in the options class and is `true` in `appsettings.json`. Both
  `WebApplicationFactory` subclasses (`IdentityApiFactory`, `HealthApiFactory`) and
  `verify-e2e.sh` (`MarketData__ScheduledSync=false`) switch it off. Otherwise every
  host would run a sync at boot, because there is no run in the last 26 hours. A test
  asserts both factories keep it off. **A new test host must set it too.**
- **006 · CP3 · no schema change**, so no migration. `MarketDataStore` is now registered
  (scoped), with `MarketDataSync` beside it (`AddMarketDataSync()`).
- **006 · CP3 · not prevented: overlapping runs.** Nothing stops a scheduled run and a
  manual one (CP4) from running at the same time. Both would upsert the same rows, which
  is idempotent, so the cost is wasted calls and two `SyncRun` rows. CP4 owns the gate.
- **006 · CP3 · diff sizes.** `41d2c5f` (sync tests 10/11, 214 lines of test) and
  `1dd20e7` (the sync, 207 insertions) are slightly over ~200. The others are under.
- **006 · CP3 · handoff to CP4** (endpoints, manual trigger, tests 19–22).
  - `MarketDataSync.RunAsync(SyncTrigger.Manual, ct)` writes the `Running` row, runs to
    the end, and returns the finished `SyncRun`. `POST /api/market-data/sync` answers `202
    { syncRunId }`, so the id is needed before the run ends. Either split `RunAsync` into
    "create the row" and "run it", or start it in the background with **a new scope and
    not the request's `CancellationToken`**. The request's context is disposed when the
    response ends.
  - The 10-minute limit is global and survives restarts only if it reads the database:
    the latest `SyncRun.StartedAt` of any trigger (index `StartedAt DESC` exists). ASP.NET's
    rate limiter is per-process and in-memory. Two simultaneous requests can both pass a
    database check, so pair it with a process-wide gate (a singleton `SemaphoreSlim`, or a
    PostgreSQL advisory lock), which also stops a manual run overlapping the nightly one.
    Decide whether a scheduled run within 10 minutes also yields `429`; the spec says "if
    one ran".
  - Tests 20–22 in the integration suite: swap `IPriceProviderRegistry` and
    `IBenchmarkProvider` in the factory's `ConfigureServices` for the fakes in
    `api.tests/Integration/MarketDataSyncFakes.cs`. The rate limit is global and the test
    database is shared, so these tests need a database of their own, as
    `MarketDataSyncTests` does. Otherwise one test's run makes another's POST a `429`.
  - `sync-runs` can return `Summary` parsed with `SyncSummaryJson.Read`. Its text is
    already pt-BR, so problem-details messages are the only new text needed.
  - From CP2: a CoinGecko asset's `Currency` should match `MarketData:CoinGecko:VsCurrency`.
  - For CP5's E2E (test 26): the E2E API is a real process. "Fake providers in the test
    host" needs either a Development-only switch that registers fakes, or the providers'
    `BaseUrl`s pointed at a local stub. Keep `ScheduledSync` off there.

## 006 · checkpoint 4

- **006 · CP4 · no ADR conflict, no migration.** The endpoints read and write the CP1
  tables as they are. `Endpoints/MarketDataEndpoints.cs` takes `AppDbContext` directly,
  like the account routes (ADR-016).
- **006 · CP4 · `202 {syncRunId}`: the sync is split, the run is in the background.**
  `MarketDataSync.StartAsync` writes the `Running` row; `ExecuteAsync(syncRunId)` loads it
  in another scope and performs the run; `RunAsync` is both, and the job still calls it.
  `Infrastructure/Jobs/ManualMarketDataSync` (singleton) writes the row inside the request,
  then starts the run with `Task.Run` under `ExecutionContext.SuppressFlow()`, in a new
  scope, on `IHostApplicationLifetime.ApplicationStopping` rather than the request's token.
  A shutdown mid-run marks it `Failed`, as CP3's cancellation path does. The run is not
  awaited at shutdown; a process killed outright leaves a `Running` row, as before.
- **006 · CP4 · the 10-minute limit.** Counted from the latest `SyncRun.StartedAt` of
  **any** trigger, in the database, so it survives a restart. The spec says "429 if one ran
  in the last 10 minutes" and decision 7 "one run per 10 minutes globally", so a nightly
  run inside the window refuses a manual one too. The answer is pt-BR problem details
  (title "Muitas solicitações") with a `Retry-After` in seconds: what is left of the window,
  or one minute when the gate is held by a run that started longer ago. A refused request
  writes no row. ASP.NET's rate limiter was not used: it is in memory and per process.
- **006 · CP4 · the gate.** `MarketDataSyncGate`, a singleton `SemaphoreSlim(1, 1)`. The
  manual trigger takes it without waiting before the database check, and a busy gate is a
  `429`, so two simultaneous POSTs cannot both pass. It is held until the background run
  ends. The nightly job waits for it (`WaitAsync`) and then runs: a scheduled run is never
  skipped because a manual one was in progress, and the rerun is idempotent and cheap.
  In-process is enough for one API process (ADR-004); a second instance would need a
  PostgreSQL advisory lock. The gate is deliberately not `IDisposable`, so a background run
  can release it after the container is disposed.
- **006 · CP4 · registering an asset.** The spec's body has no `name`, but the column is
  required: `name` is optional and defaults to the ticker. The ticker is trimmed and
  upper-cased; `providerSymbol` is trimmed but keeps its case (CoinGecko ids are
  lower-case), so `petr4` and `PETR4` at brapi would be two rows. `currency` must be `BRL`
  or `USD` (upper-cased), and a CoinGecko asset's must equal `MarketData:CoinGecko:VsCurrency`
  (CP2's note), a 400 on `currency`. `class` and `provider` are checked with
  `Enum.IsDefined`. Class and provider are not cross-checked (a `Crypto` at brapi is
  accepted). New assets are active. A duplicate `(Provider, ProviderSymbol)` is a 409 caught
  from the unique index, not pre-checked (`Problems.IsDuplicate`). The register route is
  open to any signed-in user, as the spec says; the catalogue is shared, so one user's typo
  is everyone's row, and nothing deletes or deactivates an asset yet.
- **006 · CP4 · reads.** `GET assets?q=` matches ticker or name, case-insensitive
  (`ILIKE`, with `%`, `_` and `\` escaped), ordered by ticker, at most 50; without `q`,
  the first 50. `prices` and `benchmarks/{code}` take optional inclusive `from`/`to`,
  oldest first. An unknown asset id is a 404; an unknown benchmark code is an empty list,
  like a configured one not yet synced. The code is upper-cased. A malformed date is the
  framework's binding 400, as on the transactions route. `sync-runs` is the last 20 by
  `StartedAt` descending, with `summary` parsed by `SyncSummaryJson.Read` into an object.
  Enums cross the wire as names (`Manual`, `PartialFailure`).
- **006 · CP4 · tests.** `MarketDataApi` boots `IdentityApiFactory` on a database of its
  own (the host migrates it) with the CP3 fakes as `IPriceProviderRegistry` and
  `IBenchmarkProvider`; the factory gained an optional `services` hook for that. Tests 20
  and 21 poll `sync-runs` until the run leaves `Running`. Beyond 19–22: a recent scheduled
  run also yields 429 and an 11-minute-old one does not, a held gate yields 429 and writes
  no row, validation, the CoinGecko currency rule, ranged reads, 401 without a session.
  .NET tests 521 → 535. Not tested: the job actually waiting on the gate (its loop is
  tested through delegates, and `RunOnceAsync` is the only code that takes it).
- **006 · CP4 · BOMs.** `api/Program.cs` and `IdentityApiFactory.cs` were touched and
  carry em dashes, so they now have a UTF-8 BOM (005 · CP2's observed debt, in part).
- **006 · CP4 · diff sizes.** The first test commit (`7582841`) is 225 lines: the host
  helper and the seven catalogue tests. The others are under ~200.
- **006 · CP4 · handoff to CP5** (web `/market-data`, tests 25 and 26).
  - Routes and shapes: `GET /api/market-data/sync-runs` → `[{ id, startedAt, finishedAt,
    trigger, status, summary: { [provider]: { rowsWritten, itemsSynced, itemsFailed,
    error, failures: [{ item, error }] } } }]`; `POST /api/market-data/sync` → `202 {
    syncRunId }` or `429` problem details whose `detail` is pt-BR and fit to show as is;
    `GET /api/market-data/assets?q=` and `POST /api/market-data/assets { ticker, name?,
    class, provider, providerSymbol, currency }` → `201` asset, `400` with `errors[field]`,
    `409` with `detail`. Enum names (`StockBr`, `CoinGecko`, `Scheduled`, `Running`, …)
    need pt-BR labels in `web/src/lib/labels.ts`. `error` texts are already pt-BR.
  - The run is asynchronous: after a 202 the screen should poll `sync-runs` (or refetch on
    an interval while the newest is `Running`).
  - Test 26 needs fake providers in the real E2E API process with `ScheduledSync` off
    (`verify-e2e.sh` already sets `MarketData__ScheduledSync=false`). Options: a
    Development-only switch (for example `MarketData:FakeProviders=true`) that registers
    the fakes as `IPriceProviderRegistry`/`IBenchmarkProvider`, or the providers'
    `BaseUrl`s pointed at a local stub. The fakes today live in `api.tests`, so the switch
    needs its own copy under `api/`, guarded so production cannot enable it.
  - The E2E database is shared across the E2E run and the 10-minute limit is global, so
    only one E2E test can trigger a sync per run, unless the suite truncates `SyncRuns`
    first or the switch also shortens the window.

## 006 · checkpoint 5

- **006 · CP5 · no ADR conflict, no migration.** The screen and the E2E read the CP4 routes
  as they are. No schema change.
- **006 · CP5 · fake providers in the E2E API: `MarketData:FakeProviders`.** A boolean on
  `MarketDataOptions`, default `false`, absent from every `appsettings*.json`, set only by
  `verify-e2e.sh` (`MarketData__FakeProviders=true`). When on, `IPriceProviderRegistry` and
  `IBenchmarkProvider` resolve to `Infrastructure/MarketData/FakeMarketDataProviders`: every
  series answers one value dated `to` (close `10`, benchmark `0.05`), so a run writes a row
  per item and always ends `Succeeded`. The choice is made when the service is resolved, not
  when it is registered, because the test hosts' configuration is merged only at `Build()`
  (Program.cs says as much). The real adapters stay registered either way.
- **006 · CP5 · the switch cannot reach production.** `Program.cs` calls
  `MarketDataSetup.RefuseFakeProvidersOutsideDevelopment` next to the `Require` checks,
  before the migration: the switch on in any environment but Development fails the boot
  with a message naming it. Tested for `Production` and `Staging`. The fakes are in the
  production assembly (the E2E API is a real process, and `api.tests` is not loaded there),
  as CP4's handoff asked.
- **006 · CP5 · the 10-minute limit in E2E: the switch shortens the window to zero.** Of the
  three options CP4 left, this is the least invasive that is also correct:
  - *Clearing `SyncRuns` in setup* needs the script or Playwright to reach into the
    database. `verify-e2e.sh` does not know the connection string (it lives in the user
    secrets), and on a developer machine that database may be the dev one, whose run
    history would be deleted on every E2E run.
  - *Only one test triggers a sync* is true (test 26 is the only one), but it is not enough:
    the E2E database is kept between runs (`docker compose down` keeps the volume), so a
    second `verify-e2e.sh` within ten minutes would get a 429. So would a Playwright retry
    in CI.
  - *The switch shortens the window* is one line in `ManualMarketDataSync` (`Window` is
    `MinimumInterval`, or zero under the switch), and it cannot be on in production. The gate
    still refuses a run while another is going. Tests 21 and the other 429 tests run without
    the switch and still hold the 10 minutes. Two back-to-back `verify-e2e.sh` runs passed.
  Only one E2E test triggers a sync anyway, so no two tests race for the gate under
  `fullyParallel`.
- **006 · CP5 · `IdentityApiFactory` gained a `settings` dictionary**, added over its default
  configuration, for the switch's tests (`MarketDataFakeProvidersTests`).
- **006 · CP5 · the screen.** `/market-data` (`routes/MarketDataPage.tsx`), under the
  protected layout and not in the navigation (spec: "reachable from settings later").
  Two sections in `components/market-data/`:
  - `SyncRuns` lists the last 20 runs: start (browser's local time, pt-BR short date and
    time), trigger, status, and one line per summary key with rows written, items synced,
    items failed and each failure as `item: error`. The `error` texts are the API's pt-BR,
    shown as they come. "Sincronizar agora" posts, then refetches; while the newest run is
    `Running` the list is polled every 2 s, and the button is disabled. A refusal (429) is
    the problem's `detail`, shown verbatim in an alert.
  - `AssetCatalogue` is the register form (ticker, optional name, class, provider, symbol at
    the provider, BRL/USD) and a search that runs on submit, not as you type. Without a query
    it shows the API's first 50. A 400 is shown under the field it names; a 409 is the
    API's `detail`.
  - Labels in `lib/labels.ts`: class, provider (brand spelling: brapi, CoinGecko, Twelve
    Data), trigger, status, and `syncProviderLabel` for summary keys (`Bcb` → "Banco Central
    (SGS)"; an unknown key is shown as sent).
- **006 · CP5 · polling a stuck run.** Only the newest run is watched, but if the process died
  mid-run (a `Running` row that never ends, CP3), the open screen polls every 2 s and keeps
  the button disabled. The API would refuse a manual run for 10 minutes after that row's
  start anyway, then accept one. Not handled further.
- **006 · CP5 · tests.** Web test 25 is `routes/MarketDataPage.test.tsx` (rendered through
  the real route tree), with the empty state, the trigger and polling, and a verbatim 429.
  `components/market-data/AssetCatalogue.test.tsx` covers search, registration, a 400 under
  its field and a 409. E2E test 26 is `e2e/market-data.spec.ts`: a registered ticker, then a
  manual sync whose run (found by the `syncRunId` of the 202) turns `Concluída` with brapi
  and BCB in its breakdown and no failure. A second E2E test registers, searches and gets the
  409 on a repeat. It asserts the API's sentence exactly, so rewording that 409 breaks it.
  Counts: .NET 535 → 539, web 67 → 74, E2E 16 → 18. The vitest files were committed failing
  before the screen; the E2E spec was written after the screen and failed once on an
  ambiguous label ("Provedor" also matches "Símbolo no provedor") before passing.
- **006 · CP5 · diff sizes.** Every commit is under ~200 lines; the largest is
  `2efcef2` (the catalogue, 194).

## 006 · handoff

Full handoff: `docs/handoffs/006.md`. **006 is complete in code; these steps need a human.**

- **Provider keys.** None is committed. Set them as user secrets in development and as
  environment variables in production: `MarketData:Brapi:Token`,
  `MarketData:CoinGecko:DemoKey`, `MarketData:TwelveData:Key`. BCB needs none. Plans
  matter: brapi's free plan may not serve 5-year ranges, and CoinGecko's demo plan serves
  365 days (`MaxHistoryDays: 365`), so manual step 4's "~1800 rows for BTC" needs a paid
  plan and a raised cap (CP2).
- **SGS codes (manual step 1).** Check CDI 12, SELIC 11, IPCA 433 and USDBRL 1 on
  `https://www3.bcb.gov.br/sgspub/` and their units (percent per day, percent per day,
  percent per month, level) against `MarketData:Bcb:Series` in `api/appsettings.json`.
  Step 5 of the spec shows a wrong code as values of the wrong magnitude.
- **Fixtures.** All five files in `api.tests/Fixtures/MarketData/` are hand-written from
  documentation; the egress proxy blocked every provider. Capture one real response per
  provider under the same file names, update that folder's README, and fix any adapter
  whose tests then break. Decision 9 ("captured once") is not met until then. The
  behaviours listed under 006 · CP2 (BCB 404 as empty, brapi ranges, CoinGecko daily
  points, Twelve Data error bodies) are also unverified.
- **Container time zone for the cron.** `0 3 * * *` is read in the container's zone, and a
  stock .NET image is UTC, so it would run at 00:00 in São Paulo. Set
  `TZ=America/Sao_Paulo` in the production container (010 writes the compose file), or write
  the cron in UTC.
- **Manual steps 2–7** of the spec, with real keys, including the overnight `Scheduled` run.
- **CLAUDE.md.** Its exception list names `prices` and `benchmarks`; `market_assets` and
  `sync_runs` are shared too (006 · CP1). A one-line edit for the human.
- **Starting spec 007.** Read `specs/007-investments.md`, `docs/handoffs/006.md` (forward
  notes) and the 006 entries above. Plan the checkpoints in STATUS.md as 005 and 006 did.
  From the baseline .NET 539, web 74, E2E 18. 007's `Asset` hangs off `MarketAssets` (FK,
  `RESTRICT`); its `POST /api/investments/assets` registering a missing catalogue entry
  should reuse the CP4 validation in `Endpoints/MarketDataEndpoints.cs` rather than copy
  it; its nightly rebuild goes after the sync in `MarketDataSyncJob` and takes the same
  `MarketDataSyncGate`. Its E2E runs on `MarketData:FakeProviders`, where every close is
  `10` and dated yesterday (UTC).

## 007 · checkpoint 1

- **007 · CP1 · no ADR conflict found; nothing to stop on.** Checked against the ADRs and
  what 006 built:
  - `Asset`, `Movement` and `PortfolioDaily` all implement `IUserOwned`. Each gets its
    query filter from the loop in `AppDbContext`, and a test asserts the filter is declared.
  - The only shared data the spec touches is `MarketAssets`, `Prices` and `Benchmarks`,
    read through FKs. `PortfolioDaily` copies a price and an FX rate per user. That copy is
    derived, user-owned data (ADR-011), not shared data.
  - Every quantity, price, FX rate and amount is `decimal`. A reflection test covers
    `Domain.Investments` and `Application.Investments`.
  - `Domain/Investments/` is POCOs with no EF or `HttpClient`. No port is introduced, so
    ADR-015 is not touched. No Repository and no MediatR.
  - Spec decision 10 (raw decimals for prices and quantities, not `Money`) is compatible
    with §4 and ADR-014. `Money` is a ledger amount pinned to two places; a unit price
    with eight places is not one.
  - The synchronous rebuild (decision 9) is compatible with ADR-011 and principle 6. It
    reads only local Postgres.
- **007 · CP1 · for the human: ARCHITECTURE.md's Investments data-model block is stale.** It
  lists `assets` with `ticker, name, class (… fixed_income …), currency, benchmark_hint`,
  movement kind `amortization`, and a five-column `portfolio_daily`. The spec replaces the
  asset columns with `MarketAssetId` → `MarketAssets` (what 006's `prices` correction
  already implies). It defers fixed income and amortization, and adds `Movement.Amount`,
  `Movement.Currency` and the extra `PortfolioDaily` columns. That block is a data model,
  not an ADR, so I did not treat this as a conflict, and I did not edit the file. It needs a
  one-block edit. `benchmark_hint` is dropped with no replacement; 008 may want it back.
- **007 · CP1 · commit order.** The four entity declarations were committed first, unmapped
  (`cf2b820`), so the failing tests would compile. That is 006 · CP1's order. The two test
  commits failed 13 of 14 cases against the unmapped model; the mapping commit turned them
  green. The decimal-type scan passed from the start. It is a guard, not a driver.
- **007 · CP1 · `Amount` and `Fees` are plain `decimal`, not a `Money` complex property.**
  Both share the movement's one `Currency` column, and two EF complex properties cannot map
  to one column. They become `Money` where they are summed (CP2/CP3), as decision 10 says
  for totals.
- **007 · CP1 · foreign keys.** `Assets → AspNetUsers`, `Movements → AspNetUsers` and
  `PortfolioDaily → AspNetUsers` are CASCADE, like 003. `Assets → MarketAssets` is RESTRICT
  (spec), so a catalogue row someone holds cannot go. `Movements → Assets` is RESTRICT
  (spec, test 24). `PortfolioDaily → Assets` is **CASCADE**. The spec is silent here.
  Derived rows never hold their asset in place (ADR-011), and the endpoint refuses deleting
  an asset with movements anyway. Deleting a user takes everything with it: I checked this
  in a scratch PostgreSQL 16, because RESTRICT sits between two tables that both cascade
  from the user, as `Transactions → Accounts` already does.
- **007 · CP1 · no CHECK constraints.** The spec's rules (quantity > 0, fees ≥ 0, currency
  equal to the asset's) are domain rules, as 003's are. The codebase has no CHECK
  constraints, and CP2 writes the rules.
- **007 · CP1 · no composite FK for `Movement.UserId = Asset.UserId`.** A movement row could
  in principle carry B's `UserId` and A's `AssetId`. The endpoint resolves the asset
  through the filter first, so B gets a 404 (test 17), which is 003's shape for
  transactions and accounts. A composite FK `(UserId, AssetId) → Assets(UserId, Id)`
  would close it in the database, at the cost of an alternate key. That is optional
  hardening for the human.
- **007 · CP1 · names and indexes.** The table is `PortfolioDaily` (singular, set explicitly),
  which matches the spec's manual step 6 in `psql`. The DbSet is `PortfolioDaily`. Indexes:
  - Unique `(UserId, MarketAssetId)` on `Assets`.
  - `(UserId, AssetId, Date)` on `Movements` (spec).
  - EF's FK indexes on `Assets.MarketAssetId`, `Movements.AssetId` and
    `PortfolioDaily.AssetId`.
  `PortfolioDaily`'s PK `(UserId, AssetId, Date)` serves the per-asset range reads and the
  delete-from-date of the rebuild.
- **007 · CP1 · column precision is tested at the edges.** `numeric(18,8)` is used for
  quantity, unit price, average cost, price and FX, and `numeric(18,2)` for amount, fees,
  `ValueBrl` and `CostBasisBrl`. Each value is written at the edge of its column and must
  come back exact.
- **007 · CP1 · migration SQL** (`dotnet ef migrations script AddMarketData AddInvestments`,
  also in `docs/migrations/007-AddInvestments.sql`):

  ```sql
  CREATE TABLE "Assets" (
      "Id" uuid NOT NULL,
      "UserId" uuid NOT NULL,
      "MarketAssetId" uuid NOT NULL,
      "Nickname" character varying(100),
      "CreatedAt" timestamp with time zone NOT NULL,
      CONSTRAINT "PK_Assets" PRIMARY KEY ("Id"),
      CONSTRAINT "FK_Assets_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
      CONSTRAINT "FK_Assets_MarketAssets_MarketAssetId" FOREIGN KEY ("MarketAssetId") REFERENCES "MarketAssets" ("Id") ON DELETE RESTRICT
  );
  CREATE TABLE "Movements" (
      "Id" uuid NOT NULL, "UserId" uuid NOT NULL, "AssetId" uuid NOT NULL,
      "Date" date NOT NULL, "Kind" integer NOT NULL,
      "Quantity" numeric(18,8) NOT NULL, "UnitPrice" numeric(18,8) NOT NULL,
      "Amount" numeric(18,2) NOT NULL, "Fees" numeric(18,2) NOT NULL,
      "Currency" char(3) NOT NULL, "Notes" character varying(300),
      "CreatedAt" timestamp with time zone NOT NULL,
      CONSTRAINT "PK_Movements" PRIMARY KEY ("Id"),
      CONSTRAINT "FK_Movements_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
      CONSTRAINT "FK_Movements_Assets_AssetId" FOREIGN KEY ("AssetId") REFERENCES "Assets" ("Id") ON DELETE RESTRICT
  );
  CREATE TABLE "PortfolioDaily" (
      "UserId" uuid NOT NULL, "AssetId" uuid NOT NULL, "Date" date NOT NULL,
      "Quantity" numeric(18,8) NOT NULL, "AverageCost" numeric(18,8) NOT NULL,
      "Price" numeric(18,8) NOT NULL, "PriceDate" date NOT NULL,
      "FxRate" numeric(18,8) NOT NULL,
      "ValueBrl" numeric(18,2) NOT NULL, "CostBasisBrl" numeric(18,2) NOT NULL,
      CONSTRAINT "PK_PortfolioDaily" PRIMARY KEY ("UserId", "AssetId", "Date"),
      CONSTRAINT "FK_PortfolioDaily_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
      CONSTRAINT "FK_PortfolioDaily_Assets_AssetId" FOREIGN KEY ("AssetId") REFERENCES "Assets" ("Id") ON DELETE CASCADE
  );
  CREATE INDEX "IX_Assets_MarketAssetId" ON "Assets" ("MarketAssetId");
  CREATE UNIQUE INDEX "IX_Assets_UserId_MarketAssetId" ON "Assets" ("UserId", "MarketAssetId");
  CREATE INDEX "IX_Movements_AssetId" ON "Movements" ("AssetId");
  CREATE INDEX "IX_Movements_UserId_AssetId_Date" ON "Movements" ("UserId", "AssetId", "Date");
  CREATE INDEX "IX_PortfolioDaily_AssetId" ON "PortfolioDaily" ("AssetId");
  ```
- **007 · CP1 · counts.** .NET 539 → 549 (nine persistence cases, one type scan), web 74,
  E2E 18.
- **007 · CP1 · handoff to CP2** (domain, tests 1–15) **and CP3** (rebuild and endpoints).
  - Keep the domain free of any clock. The "after today" rule takes `today` as an argument,
    as `TransactionRules.ValidateDate` does, and `SnapshotBuilder` takes `to`.
  - **Rounding.** `AverageCost` is stored at 8 places, and `ValueBrl`/`CostBasisBrl` at 2.
    Decide whether `PositionCalculator` carries full `decimal` precision and rounds only
    when writing a row. Test 9 ("reproduces a hand-computed result exactly") and broker
    agreement "to the cent" both depend on this. If it rounds, round `ToEven` to match
    `Money`, and say so.
  - The validation texts in the spec's table are pt-BR and are rendered verbatim. A C# file
    that holds them needs a UTF-8 BOM (`Quantidade vendida maior que a posição`,
    `Moeda diferente do ativo`).
  - **Nightly rebuild across users (test 27).** A job has no `ICurrentUser` (`Id` is null),
    so every filtered query returns nothing. Either open one scope per user with an
    `ICurrentUser` set to that user, which keeps the filter on, or use
    `IgnoreQueryFilters()` with explicit `UserId` predicates. I prefer the first.
    Production code has no `IgnoreQueryFilters` today.
  - **The rebuild's section in `SyncRun.Summary` is shared data.** Every signed-in user sees
    it on `/market-data`. Write counts only, with no ticker, asset id or user id in
    `failures`, so no one learns what someone else holds. The summary is typed
    provider → `ProviderSyncSummary`. A key such as `Snapshots` in that shape needs a
    pt-BR label in `syncProviderLabel` (`web/src/lib/labels.ts`).
  - FX is benchmark `USDBRL` (level, BRL per USD). Under `MarketData:FakeProviders` every
    benchmark is `0.05` and every close is `10`, dated yesterday (UTC). A USD asset in E2E
    would be valued at 0.05 BRL per USD, so keep the E2E on a BRL asset (PETR4, as the spec
    says).
  - Test 19 ("rows from that date to today") needs a price on or before the buy date.
    `SnapshotBuilder` skips days before the first price (decision/test 12).
  - `POST /assets` registering a missing catalogue entry should reuse
    `MarketDataEndpoints`' validation. The nightly rebuild goes after the sync in
    `MarketDataSyncJob.RunOnceAsync`, under `MarketDataSyncGate`.
  - CP3 carries twelve integration tests and the whole API surface. It will likely need
    two sub-checkpoints to stay near ~200 lines a commit: the rebuild, `POST /rebuild` and
    the nightly job, then the routes.

## 007 · checkpoint 2

- **007 · CP2 · no ADR conflict found.** `PositionCalculator`, `MovementRules` and
  `SnapshotBuilder` are static pure functions in `Domain/Investments/`, with no EF,
  no `HttpClient` and no clock (ADR-014, ADR-017). No port, no Repository. They read
  `Price` and `Benchmark` as plain POCOs. Every value is `decimal`, locals included,
  and the CP1 reflection scan still covers the namespace.
- **007 · CP2 · rounding (for the human: manual step 2 decides it).** The spec says
  nothing about where to round. I chose **full precision in the calculator, rounded
  once at the column**:
  - `PositionCalculator` applies the spec's formulas exactly as written. It carries the
    average as state at `decimal`'s 28 significant digits, so a sell leaves the average
    untouched to the last digit. Nothing is rounded there.
  - `SnapshotBuilder` rounds when it writes a row. `AverageCost` goes to 8 places and
    `ValueBrl`/`CostBasisBrl` go to 2 (through `Money`). Both use
    `MidpointRounding.ToEven`, as `Money` does. As far as I know this is also ABNT NBR
    5891's rule for a half, but I have not checked the standard.
  - Why: a broker's preço médio is total cost over quantity, rounded for display.
    Rounding the average at every step (to 2 or even 8 places) drifts from it over many
    buys. The calculator's full-precision average, rounded to 2, is the closest match
    to "agrees to the cent".
  - Risk: some brokers round intermediate values, or truncate the displayed preço
    médio. That can only be settled against a real statement, which is the spec's
    manual step 2. If it disagrees by a cent, the fix is at the column boundary and not
    in the formulas.
  - CP3 should read the position endpoint's `averageCost` from the calculator
    unrounded, or round it the same way. It must not recompute it from the rounded
    `PortfolioDaily.AverageCost`.
- **007 · CP2 · spec gaps I closed, each for the human to confirm:**
  - **Fees ≥ 0 has no message in the spec's table.** The data model requires it, so
    I added `Taxas não podem ser negativas`. That is invented copy and needs a pt-BR
    review.
  - **Fees on a Dividend/Jcp are subtracted from income** (`Income += Amount − Fees`).
    This mirrors sell proceeds (decision 5).
  - **Fees on a Split are ignored,** because the spec's split formula has none.
    Rejecting them would need another invented message.
  - **Fields irrelevant to the kind are ignored, not rejected.** The calculator ignores
    `UnitPrice` on a Split, `Quantity`/`UnitPrice` on income, and `Amount` on
    Buy/Sell/Split. CP3 should zero them on write, so the stored row matches the data
    model's "0 for …" notes. The form hides those fields anyway.
  - **Buy/Sell `UnitPrice` has no rule.** Zero and negative prices pass, as the spec
    lists no rule for them.
  - **A USD buy dated before the first known FX rate is costed at the earliest later
    rate.** Days with no rate yet are skipped, like days with no close. This only
    happens on a catalogue whose USDBRL history has not been backfilled. A guard test
    pins it (`Buy_before_the_first_rate_costs_at_the_earliest_later_rate`).
  - **A split on a zero position is allowed.** It gives quantity > 0 with average 0.
    The spec has no rule against it.
- **007 · CP2 · shapes CP3 builds on.**
  - `PositionCalculator.Calculate(movements, fxOn?)` returns a `PositionStep` per
    movement. Each step holds `Quantity`, `AverageCost`, `CostBasisBrl`,
    `RealisedGain` and `Income`. `RealisedGain` and `Income` are **native currency**.
  - `InOrder` is the single ordering, by date then `CreatedAt`. `Apply` throws on an
    oversell, and that is a bug, not a user error.
  - `MovementRules.Validate(movement, assetCurrency, today)` returns the field
    violations. `ValidatePositions(history)` takes the whole history *after* the write.
    For a PUT, replace the old row in the list. For a DELETE, remove it: deleting a
    buy can uncover a later sell. Violations are 003's `RuleViolation`, ready for
    problem details.
  - `SnapshotBuilder.Build(userId, assetId, currency, movements, prices, fxRates,
    from, to)` needs the **whole** movement history. It also needs the latest close
    and FX rate **on or before** `from` (and before each USD buy), not only the rows
    in `[from, to]`. The rows start at `max(from, first movement)`.
- **007 · CP2 · counts.** .NET 549 → 580, web 74, E2E 18. The 31 new tests are 9
  calculator (tests 1–6, 9, plus ordering), 12 rules (tests 7, 8, plus the table), and
  10 builder (tests 10–15, plus four for skips, from-date, the FX edge and rounding).
  Each test commit stubbed its signatures with `NotImplementedException` and failed
  every case before its feat commit, except the FX guard, which was added after.
- **007 · CP2 · handoff to CP3** (rebuild, routes, `POST /rebuild`, summary, nightly
  job; tests 16–27).
  - **`realisedBrl` and `dividendsBrl` in the position shape are open for USD assets.**
    The calculator yields them in native currency. Converting at each event's FX rate
    (like the cost basis) is the consistent choice, but the spec does not say, so
    decide it and record the decision. For BRL assets, native is BRL.
  - `RebuildSnapshots` loads what `Build` needs as described above. It deletes the
    rows `>= fromDate`, inserts `Build(..., fromDate, today)`, and does both in one
    transaction (test 22). `today` comes from the caller's `TimeProvider`, never from
    the domain.
  - Carried over from CP1, unchanged:
    - **Nightly rebuild across users:** open one scope per user with `ICurrentUser`
      set, which keeps the filter on. This is preferred over `IgnoreQueryFilters()`
      with explicit `UserId` predicates. Production code has no `IgnoreQueryFilters`
      today.
    - **The rebuild's section in `SyncRun.Summary` holds counts only.** No ticker,
      asset id or user id, because every user sees it. The new key needs a pt-BR label
      in `syncProviderLabel`.
    - **E2E stays on PETR4 (BRL).** Fake providers price every benchmark, USDBRL
      included, at `0.05`. Test 19 needs a close on or before the buy date, and the
      fake close is dated yesterday.
    - Two sub-checkpoints are likely: first the rebuild, `POST /rebuild` and the
      nightly job, then the routes.

## 007 · checkpoint 3

Done in two parts. 3a is the rebuild and the asset and movement routes (tests 16, 17,
19–24). 3b is positions against the calculator, the summary, `POST /rebuild` and the
rebuild after the sync (tests 18, 25–27).

- **007 · CP3 · no ADR conflict, no migration.** The code reads and writes the CP1 tables
  as they are.
  - `Application/Investments/` has `SnapshotRebuild`, `PositionQueries` and
    `MovementCommands`, which use `AppDbContext` directly (ADR-016).
  - The endpoints stay thin (`Endpoints/InvestmentEndpoints.cs`).
  - The after-sync step is in `Infrastructure/Jobs/`, beside the two sync triggers.
  - The domain is unchanged from CP2. No port, no Repository, no MediatR.
- **007 · CP3 · the rebuild (`SnapshotRebuild`, the spec's `RebuildSnapshots`).**
  - It deletes the asset's rows with `Date >= from`, calls `SnapshotBuilder.Build(…, from,
    today)` and inserts the result.
  - It loads the whole movement history, the closes from the latest one on or before
    `from`, and the USDBRL rates from the latest one on or before the first movement.
  - "Today" is the **UTC** date, which the sync's "yesterday" also counts from. São Paulo
    is behind UTC, so this never refuses a movement dated today in local time. From 21:00
    local time it accepts one dated tomorrow.
  - **One transaction (test 22).** The rebuild opens its own transaction, or joins the
    caller's when there is one. A movement write therefore saves and rebuilds in **one**
    transaction. That is stronger than the spec asks: a failing rebuild never leaves a
    movement without its rows.
  - A transaction-scoped advisory lock on the asset (`pg_advisory_xact_lock`) serialises
    two rebuilds of it. Without the lock, both would delete, then both insert, and one
    would fail on the primary key.
  - Rows are detached after the save, whether it succeeds or fails, so one context can
    rebuild many assets. Test 22 makes the insert fail through a real `numeric(18,2)`
    overflow.
- **007 · CP3 · movement writes (`MovementCommands`).**
  - Each write is checked with `MovementRules.Validate`, then with `ValidatePositions` over
    the history as it stands after the write (a PUT replaces the old row, a DELETE removes
    it). It then saves and rebuilds from `min(oldDate, newDate)`.
  - Fields irrelevant to the kind are **stored as zero**, as the CP2 handoff asked:
    quantity and unit price on Dividend and Jcp, unit price on Split, and amount on
    Buy, Sell and Split. Fees are kept on every kind; the calculator ignores them on a
    Split.
  - `currency` may be omitted, and then defaults to the asset's. When sent, it is
    trimmed and upper-cased before the rule compares it.
  - An unknown `kind` sent as a number is a 400 on `kind`. An unknown name fails JSON
    binding, which is the framework's 400.
  - **For the human:** a DELETE that would uncover a later sell is a **409** whose
    `detail` is the rule's text (`Quantidade vendida maior que a posição`). The spec
    names no status for it, and a DELETE has no field to blame. A POST or PUT that
    breaks the rule is a 400 on `quantity`.
  - **Not validated: excess precision.** PostgreSQL silently rounds a quantity or unit
    price past 8 places, and an amount or fee past 2. The rebuild reads what was stored,
    so rows and positions stay consistent with each other, but the stored value may not
    be what was typed. A value past the column's range is a 500. Refusing either needs
    new pt-BR copy.
- **007 · CP3 · positions (`GET /assets`, `PositionQueries`).**
  - Quantity, `averageCost`, `realisedBrl` and `dividendsBrl` come from
    `PositionCalculator` over the movements, at full precision. `averageCost` is then
    rounded to 8 places, half to even, the column's rounding. It is never read back from
    `PortfolioDaily` (CP2 handoff).
  - `price`, `priceDate`, `valueBrl` and `costBasisBrl` come from the asset's latest
    daily row. `unrealisedBrl` is `valueBrl − costBasisBrl`, and `unrealisedPct` is that
    over `costBasisBrl`, to 4 places (the spec's example shows 0.0927). All six are
    `null` while the asset has no row: no movement yet, or no close yet.
  - `dividendsBrl` is net of the fees on the income (CP2's rule).
  - Every held asset is listed, including ones with no movement and positions sold to
    zero. The order is `valueBrl` descending, then ticker.
  - **For the human: `realisedBrl` and `dividendsBrl` of a USD asset.** The spec does not
    say how to convert them. Each event is converted at the USDBRL rate of **its own
    date**: the latest on or before it, or the earliest after it, which is how CP2 costs
    a buy. The alternative, today's rate, would move a gain that is already realised.
    Both are `null` for a USD asset while no USDBRL rate exists at all. Pinned by
    `A_usd_position_converts_each_event_at_its_own_dates_rate`.
- **007 · CP3 · `POST /assets`.**
  - The body takes `{ marketAssetId, nickname? }`, or the 006 registration
    `{ ticker, name?, class, provider, providerSymbol, currency }`. The registration goes
    through 006's own `Validate` and `NewAsset`, which are now `internal` on
    `MarketDataEndpoints` (not copied).
  - An entry already in the catalogue under the same `(provider, providerSymbol)` is
    reused, and the request's other fields do not overwrite it.
  - Answers:
    - Holding it already is a 409 (`Você já possui este ativo na carteira.`).
    - A catalogue race on a new symbol is 006's 409.
    - An unknown `marketAssetId` is a 400 on `marketAssetId`.
    - A nickname over 100 characters is a 400 on `nickname`.
    - Success is a 201 with the position shape (all zeros and nulls).
- **007 · CP3 · `DELETE /assets/{id}`** answers 404 for someone else's asset or a missing
  one, 409 when it has movements, and 204 otherwise. The daily rows go with it
  (CASCADE). The movement check is a query and the FK is not caught, so a movement
  written concurrently would make it a 500. That is rare, and noted here only.
- **007 · CP3 · summary and `POST /rebuild`.**
  - The summary sums the latest daily row of each asset. An asset with no row adds
    nothing.
  - `POST /rebuild` rebuilds each of the caller's assets from 1990-01-01, the rule's
    lower bound, so everything is rebuilt, one transaction per asset. It answers **202
    `{ assetsRebuilt, rowsWritten }` once done**: 202 because the spec says so, but the
    work is already finished and there is nothing to poll.
  - The definition of done's "truncate, rebuild, identical" is a test. It also checks
    that another user's truncated rows are not rebuilt.
- **007 · CP3 · the rebuild after the sync (`SnapshotRebuildAfterSync`, test 27).**
  - **No `IgnoreQueryFilters()`.** A new scoped `ActingUser` is set once, right after a
    job opens a scope for one user. `HttpCurrentUser` reads it before the request's
    claims. Each user gets a scope of their own, so every query stays filtered.
  - Users are listed from `AspNetUsers`, which has no filter. Production code still has
    no `IgnoreQueryFilters`.
  - **For the human: it runs after the manual sync too, not only the nightly one.** Both
    triggers call it after the sync and inside `MarketDataSyncGate`, so it never overlaps
    itself. Without it, closes from a manual sync would not reach anyone's rows until the
    next night. The job resolves it from its scope, so `MarketDataSyncJob`'s constructor
    and unit tests are unchanged.
  - **For the human: it starts from yesterday, or earlier where rows are missing.** The
    spec says "from yesterday". Two gaps would stay open forever with that alone, so it
    starts earlier in two cases (`SnapshotRebuild.NightlyFromAsync`):
    - The asset's rows stop before yesterday because the job missed nights. It starts the
      day after the last row.
    - The rows start after the first day that has a movement, a close and, for USD, a
      rate. This happens when a new asset's history was backfilled after its movements
      were written. It starts at that first day.
  - **The summary section holds counts only.** The key is `Snapshots` (web label
    "Posições da carteira"):
    - `rowsWritten` is the rows written, `itemsSynced` the assets rebuilt and
      `itemsFailed` the assets that failed.
    - `failures` is always empty.
    - `error` is a fixed sentence (`Não foi possível recalcular as posições de um ou mais
      ativos.`).
    - The asset and user ids go to the log only.
    - The section is left out when nobody holds anything.
  - The run's status is recomputed with the section counted. One failed asset therefore
    makes the run `PartialFailure` on the screen every user sees. That reveals a count,
    nothing else.
  - A manual run turns `Succeeded` before the section is written, because the sync
    finishes first. So `/market-data` can stop polling a few hundred milliseconds before
    the section appears. It appears on the next refetch.
  - An exception outside any one asset (the database down, say) reaches the job's
    handler, and is logged as "the scheduled market-data sync failed".
- **007 · CP3 · invented pt-BR copy, for review.**
  - `Você já possui este ativo na carteira.`
  - `Ativo não encontrado no catálogo.`
  - `O apelido deve ter até 100 caracteres.`
  - `O ativo tem movimentações. Exclua-as antes de remover o ativo.`
  - `O tipo de movimentação não é válido.`
  - `As observações devem ter até 300 caracteres.`
  - `Não foi possível recalcular as posições de um ou mais ativos.`
  - `Posições da carteira`
- **007 · CP3 · tests.**
  - `InvestmentsApi` is the test host. Each test gets a database of its own, and the
    fakes from 006 · CP3 replace brapi and BCB.
  - The rebuild tests construct `SnapshotRebuild` over a context filtered as the user.
  - Every test commit failed before its feat commit, with two exceptions:
    - The validation guards (`670da77`) were written after the routes and passed on
      their first run.
    - Both cases of test 25 (`e680c0a`) passed against 3a's position read, which already
      existed.
  - Counts: .NET 580 → 611, web 74 → 76 (the `Snapshots` label), E2E 18.
- **007 · CP3 · diff sizes.** `d5db351` is 238 lines: the new test host plus the four
  rebuild tests. Two feat commits went over ~200 and were split before handoff
  (`3bf51ec`/`748a143` and `a8c1e3f`/`12ced45`). Every other commit is under ~200.
- **007 · CP3 · handoff to CP4** (web `/investments`, asset detail, movement form, add
  asset; web tests 28–30).
  - Routes and shapes, all under `/api/investments`, all with problem details in pt-BR:
    - `GET assets` returns `[position]`. A position is the spec's shape plus `nickname`
      and `class`.
    - `POST assets` answers `201 position`, or 400 `errors[marketAssetId|nickname|ticker|
      currency|…]`, or 409 `detail`.
    - `DELETE assets/{id}` answers 204, or 404, or 409 `detail`.
    - `GET assets/{id}/movements` returns `[{ id, assetId, date, kind, quantity,
      unitPrice, amount, fees, currency, notes, createdAt }]`, oldest first (replay
      order).
    - `POST assets/{id}/movements` answers 201. `PUT movements/{id}` answers 200.
    - A rule broken on POST or PUT is a 400 with `errors[quantity|amount|fees|currency|
      date|kind|notes]`, whose texts are the spec's table.
    - `DELETE movements/{id}` answers 204, or 409 `detail` when it would uncover a later
      sell.
    - `GET assets/{id}/daily?from&to` returns `[{ date, quantity, averageCost, price,
      priceDate, fxRate, valueBrl, costBasisBrl }]`, oldest first. This is the Recharts
      series.
    - `GET summary` returns `{ totalBrl, totalCostBrl, unrealisedBrl }`.
    - `POST rebuild` answers `202 { assetsRebuilt, rowsWritten }`.
  - Enums cross as names. `MovementKind` (`Buy`, `Sell`, `Dividend`, `Jcp`, `Split`)
    needs pt-BR labels in `web/src/lib/labels.ts`. `class` reuses 006's labels.
  - The movement form may omit `currency`. For Dividend and Jcp it sends `amount`; the
    API zeroes quantity and unit price anyway.
  - A position's valuation fields are `null` until the asset has a daily row. Positions
    with quantity 0 are listed: the screen decides whether to hide them. Spec E2E 33
    ("delete the buy → position disappears") needs that decision. Filtering
    `quantity > 0` in the UI is the simplest, but it must still leave a way to reach an
    asset that has no movement yet.
  - Test 30's stale flag reads `priceDate`. The API does not compute staleness.
  - Add asset: search `GET /api/market-data/assets?q=`, then `POST /api/investments/assets
    { marketAssetId }`. For inline registration, post the 006 body to the same route.
  - For CP5's E2E: under `MarketData:FakeProviders` every close is 10, dated yesterday
    (UTC), and a manual sync now also rebuilds. The order that yields a valued position
    at once is: register or add PETR4, run a manual sync, then post a buy dated today or
    yesterday. A buy posted before any close gets rows after the next sync or a `POST
    /rebuild`.

## 007 · checkpoint 4

- **007 · CP4 · no ADR conflict, no API change, no migration.** The screens read the CP3
  routes as they are. The client is hand-written in `web/src/api/finance.ts` (ADR-002's
  trade-off). No figure is totalled in the browser. The one calculation in the browser is
  the form's preview, which is exact (see below).
- **007 · CP4 · navigation.** The spec says "Route `/investments`, in the nav", so it is
  linked as "Investimentos", between Importar and Contas. The detail page is
  `/investments/$assetId`. Both are under the protected layout.
- **007 · CP4 · for the human: positions at zero are hidden by default.** The spec does not
  say whether to hide them. E2E 33 ("delete the buy → position disappears") holds only if
  they are hidden. This covers both kinds of zero: an asset with no movement yet, and a
  position sold down to zero.
  - A checkbox, "Mostrar ativos sem posição (N)", shows them. It appears only when N > 0.
  - Adding an asset opens its detail page straight away, so a new asset with nothing
    recorded is always reachable.
  - "Nenhum ativo na carteira ainda." (nothing held) is a different message from
    "Nenhuma posição em aberto." (everything closed).
  - Hiding by `quantity === 0` is the only test available: the position shape does not say
    whether an asset has movements.
- **007 · CP4 · for the human: stale price.** The spec says "older than 3 business days".
  I read that as more than 3 weekdays in `(priceDate, today]`, where today is the
  browser's local date (`lib/money.ts`, `isStalePrice`).
  - Holidays are not known, so they count as business days. After a long holiday (for
    example Carnival Monday and Tuesday), a Friday close is flagged on Thursday although
    it is only 2 trading days old.
  - The flag is "Cotação desatualizada", with a `title` that explains it. It is shown
    only where there is a price.
- **007 · CP4 · the total row is `GET /summary`**, not a sum of the rows. It has no
  percentage, because that would be arithmetic on money in the browser and the spec does
  not ask for one.
- **007 · CP4 · money formats (`lib/money.ts`).**
  - Every BRL figure is `Intl` pt-BR currency, e.g. `R$ 3.510,00`.
  - Gains and losses carry a sign: `+R$ 297,66`, `-R$ 12,00`, `+9,27%`.
  - Price and average cost are shown in the asset's own currency (USD as `US$`), with at
    least 2 and at most 8 decimal places. Value, result and income are shown in BRL.
  - Quantities have up to 8 decimal places.
- **007 · CP4 · the live total is exact.** CLAUDE.md says money is never `float`. The
  browser has no `decimal`, so `lib/decimal.ts` reads the typed figures into a `bigint` at
  8 places. It multiplies at 16 places and rounds once to the cent, half to even, as
  `Money` does.
  - Buy: `q × p + fees`, labelled "Custo total".
  - Sell: `q × p − fees` (decision 5), "Valor líquido da venda".
  - Dividend and Jcp: `amount − fees` (CP2's rule), "Valor líquido recebido".
  - Split: no total.
  - The input accepts a comma or a dot as the decimal separator. When a comma is present,
    dots are read as thousands separators.
- **007 · CP4 · for the human: precision on the wire.** The form sends JSON numbers, which
  are float64. They are exact up to 15 significant digits; the columns hold 18. A value
  such as `1234567890,12345678` would be rounded in the browser before the API sees it.
  Fixing it means sending strings, which needs `AllowReadingFromString` on the API's JSON
  options. I did not do that here. The same limit already applies to 003's amounts.
- **007 · CP4 · fields shown by kind.** Dividend and Jcp show "Valor recebido" instead of
  quantity and price (spec). **Split shows only the quantity.** Its price is 0 by decision
  3, and the calculator ignores its fees (CP2). The spec names only the Dividend/Jcp rule,
  so this one is mine. Hidden fields are sent as 0, which the API stores anyway. `currency`
  is omitted, so the API uses the asset's.
- **007 · CP4 · the form does no validation of its own.** The API's messages are the spec's
  table:
  - A 400 is shown under the field it names.
  - A message for a field the form does not render (`currency`) goes in an alert inside
    the form.
  - An unreadable number is sent as 0, so the user sees the API's "Quantidade deve ser
    positiva" rather than "not a number". That is the one mismatch, and it is acceptable.
  - A 409 on a movement delete (it would uncover a later sell) is shown as sent, in an
    alert. Deleting has no confirmation step, the same as the transactions list.
- **007 · CP4 · add asset.**
  - The search runs on submit. Results are listed only after a search, not the first 50
    as `/market-data` lists them.
  - "Adicionar" posts `{ marketAssetId }`. "Cadastrar novo ativo" opens 006's
    registration fields and posts that body to `POST /api/investments/assets`.
  - A 400 on a registration is shown under its field. A 409 ("já possui", or 006's
    duplicate symbol) is shown verbatim.
  - There is no "already held" marker, because the position shape carries no
    `marketAssetId`. The 409 says so instead.
  - There is no nickname input. The API accepts one, and the screens show it in place of
    the name when it is set.
  - 006's registration fields were extracted into
    `components/market-data/RegistrationFields.tsx` and `registration.ts` (refactor
    `41febf5`, with the 006 tests unchanged and green), plus a shared
    `components/FormField.tsx`. Nothing was copied.
- **007 · CP4 · asset detail.**
  - The asset comes from the positions list: the API has no `GET /assets/{id}`, and the
    list has every figure the page shows. An id that is not the user's shows "Ativo não
    encontrado.".
  - The summary shows quantity, average cost, price and date, value, result, realised
    result and dividends ("Proventos").
  - "Remover ativo" is always offered. The API's 409 (the asset has movements) is shown
    verbatim.
  - The chart is one series of `ValueBrl` from `GET daily` with no range (every row, about
    1,500 a year). It is 2 px, with no legend, a crosshair tooltip, and a visually hidden
    table of month-end values. It reuses the dashboard's blue `--chart-income` token; a
    neutral `--chart-series` token would read better, but that is cosmetic.
  - All 007 queries sit under `['investments']`, and one invalidation after each write
    refreshes the positions, summary, movements and series.
- **007 · CP4 · invented pt-BR copy, for review.**
  - Section and field names: "Investimentos", "Adicionar ativo", "Buscar no catálogo",
    "Cadastrar novo ativo", "Cadastrar e adicionar", "Nova movimentação", "Registrar
    movimentação", "Salvar movimentação", "Valor recebido", "Taxas", "Preço unitário",
    "Remover ativo", "Proventos", "Resultado realizado", "Valor ao longo do tempo".
  - Messages and flags: "Cotação desatualizada", "Sem cotação", "Mostrar ativos sem
    posição (N)", "Nenhum ativo na carteira ainda.", "Nenhuma posição em aberto.",
    "Nenhuma movimentação registrada.", "Sem histórico de valor ainda.", "Nenhum ativo
    corresponde a esta busca. Cadastre-o abaixo.", "Ativo não encontrado.".
  - Kind labels: Compra, Venda, Dividendo, JCP, Desdobramento.
- **007 · CP4 · tests.**
  - Web test 28 and 29 are `components/investments/MovementForm.test.tsx`.
  - Web test 30 is `routes/InvestmentsPage.test.tsx` ("flags a price older than three
    business days"), plus the rule itself in `lib/money.test.ts`. The date is frozen with
    `vi.useFakeTimers({ toFake: ['Date'] })`.
  - The page tests run through the real route tree: the nav link, zero positions hidden,
    add asset and navigation, and `AssetPage.test.tsx` (add, edit, delete, 400 and 409,
    chart, removal).
  - Every test commit failed before its feat commit.
  - Two test files had a literal no-break space in a regex, which lint rejected. It was
    fixed in the next feat commit.
  - Counts: .NET 611, web 76 → 115, E2E 18.
- **007 · CP4 · diff sizes.**
  - `268f3cc` (the detail tests) is 207 lines.
  - Two feat commits went over and were split before handoff (`f1b219b`/`241a401` and
    `354073c`/`9836b9d`), and so was the detail page (`46c0833`/`500f2a4`).
  - Every other commit is under 200.
- **007 · CP4 · the E2E path was tried once.** A throwaway Playwright spec (deleted, not
  committed) ran 31–33's path against the real API under `verify-e2e.sh`, and passed
  (19/19 that run):
  1. Register PETR4 inline.
  2. Manual sync.
  3. Buy 100 @ 9,50 with fees 4,90: the preview reads R$ 954,90, and the position is
     R$ 1.000,00 at the fake close of 10.
  4. A dividend of 12.
  5. Delete the dividend, then the buy.
  6. `/investments` shows "Nenhuma posição em aberto." and no row.
- **007 · CP4 · handoff to CP5** (E2E 31–33, then the 007 handoff).
  - **The path that works** (see the probe above):
    - `devLogin`, then `/investments`, then "Cadastrar novo ativo". In the form named
      "Cadastrar e adicionar ativo", fill Ticker `PETR4`, `getByLabel('Provedor', { exact:
      true })` Brapi, and "Símbolo no provedor" `PETR4`. Class and currency default to
      StockBr and BRL. Click "Cadastrar e adicionar"; the heading `PETR4` appears.
    - PETR4 is in the shared catalogue after the first run. The registration reuses the
      Brapi/PETR4 entry and does not answer 409, so reruns are safe. Each test brings its
      own user.
    - **Sync before the buy.** The fake close (10) is dated yesterday (UTC). A buy posted
      before any close has no rows until the next sync. I posted the sync from the page
      (`fetch('/api/market-data/sync', …)`) and polled `/api/market-data/sync-runs` until
      the run left `Running`.
  - **Sync gate: race with test 26.** Under `fullyParallel`, a sync started by the
    investments spec can overlap `market-data.spec.ts`'s test 26. The gate refuses a
    second run while one is going (429), and test 26 asserts a 202. Serialise them (for
    example one `test.describe.configure({ mode: 'serial' })` file, or retry on 429 in the
    new spec only), or seed a close another way. I did not hit the race in two runs, but
    it is real.
  - **Locators:**
    - `position-<assetId>` for a row on the list.
    - `asset-summary` for the detail figures ("Quantidade", "Proventos").
    - `movement-<id>` for a movement row, with "Editar" and "Excluir".
    - `movement-total` for the preview.
    - The movement form is `getByRole('form', { name: 'Movimentação' })`, with fields
      "Tipo", "Data", "Quantidade", "Preço unitário", "Valor recebido", "Taxas" and
      "Observações (opcional)".
  - **32:** Proventos goes from R$ 0,00 to the amount, and Quantidade stays 100.
  - **33:** I deleted the dividend first. Deleting only the buy, with a dividend left,
    should also hide the row: quantity 0, and the rules allow income on a zero position.
    That is untested, so check it.
  - Then write `docs/handoffs/007.md` in the shape of `006.md`, and add the `## 007 ·
    handoff` entry and the STATUS rows. Carry the "for the human" items from CP1–CP4:
    - ARCHITECTURE's stale Investments block.
    - Rounding against a broker (manual step 2).
    - USD realised and dividends at each event's FX.
    - The 409 on a delete that uncovers a sell.
    - The rebuild after manual syncs and from before yesterday.
    - Zero positions hidden.
    - The stale-price reading.
    - The Split fields.
    - float64 on the wire.
    - All invented pt-BR copy.

## 007 · checkpoint 5

- **007 · CP5 · no ADR conflict, no application change, no migration.** E2E 31–33 drive the
  CP4 screens against the CP3 routes as they are. The only non-test change is
  `web/playwright.config.ts`.
- **007 · CP5 · the sync race with test 26: a Playwright project dependency.** Every
  investments test triggers a manual sync, because a buy with no close has no daily row.
  The gate refuses a second run while one is going (429), and test 26 asserts a 202.
  - **Chosen:** `investments.spec.ts` runs in its own project, `investments`, which
    depends on a `market-data` project holding `market-data.spec.ts`. Everything else
    stays in `chromium`, still `fullyParallel`, and runs alongside both. The investments
    tests start only once test 26 has finished, so they can never overlap it. Test 26 is
    unchanged.
  - **Also needed:** the three investments tests run in order (`test.describe.configure({
    mode: 'default' })`), and each waits out a 429 (every 500 ms, up to 30 s) before its
    sync. The rebuild after a sync runs inside the gate *after* the run reads
    `Succeeded`, so the next sync can meet a 429 for a moment. A 429 is safe to wait out
    here, because nothing in this project asserts a 202.
  - **Rejected:** *Retrying on 429 in the new spec alone.* That does not protect test 26:
    if the investments sync starts first, test 26 gets the 429.
  - **Rejected:** *One serial file holding both specs' sync tests.* It would move test 26
    out of 006's file, and a failure in serial mode skips the rest.
  - **Rejected:** *Seeding a close without a sync.* The E2E scripts have no database access
    (006 · CP5).
  - Cost: if the `market-data` project fails, the investments tests report "did not run"
    rather than pass or fail. Running `investments.spec.ts` on its own also runs
    `market-data.spec.ts` first.
  - No 429 was seen in either full run. The wait is a guard, not a workaround for an
    observed failure.
- **007 · CP5 · the tests.** Each test brings its own user, adds PETR4 through "Cadastrar
  novo ativo", syncs, and buys 100 @ 9,50 with 4,90 in fees, dated today (the form's
  default). The preview reads R$ 954,90.
  - PETR4 is registered on the first run and reused after that (Brapi/PETR4 already in the
    catalogue, no 409). BRL, so the fakes' 0.05 USDBRL never enters.
  - **31:** on the detail page, Quantidade `100`, Valor `R$ 1.000,00` (the fake close of
    10) and Resultado `+R$ 45,10`. On the list, the row and the total row both show R$
    1.000,00.
  - **32:** Proventos goes from `R$ 0,00` to `R$ 12,34`. Quantidade stays `100`, and Valor
    stays `R$ 1.000,00`.
  - **33:** a dividend of 5 is recorded, and only the buy is deleted. This answers CP4's
    open question: the rules allow income on a zero position, so the delete is accepted.
    Quantidade reads `0`. The list says "Nenhuma posição em aberto." with no row. Ticking
    "Mostrar ativos sem posição (1)" shows the PETR4 row again, so it is hidden and not
    removed.
- **007 · CP5 · test-first, honestly.** The screens and routes existed since CP4, and CP4's
  throwaway probe had already walked this path. All three tests passed on their first run.
  They guard the path; they did not drive it.
- **007 · CP5 · two back-to-back `verify-e2e.sh` runs passed**, 21/21 each, against the kept
  database. `market-data.spec.ts`'s header comment was updated to name the investments
  spec's sync (`e98f1c6`).
- **007 · CP5 · counts.** .NET 611, web 115, E2E 18 → 21.
- **007 · CP5 · diff sizes.** `afec18a` is 183 lines (the spec and the config), and
  `e98f1c6` is 5. The handoff (`74b569b`) is 216 lines, one document, left whole.

## 007 · handoff

Full handoff: `docs/handoffs/007.md`. **007 is complete in code; these steps need a human.**

- **006's pending steps first.** Manual step 3 needs real closes, so it needs the brapi
  token. Step 4 needs Twelve Data's key and the real USDBRL series (006 · handoff).
- **Manual steps 1–6** with a real broker statement. **Step 2 settles rounding.** The
  calculator carries full precision and rounds once at the column (average to 8 places,
  BRL to 2, half to even; 007 · CP2). If the broker disagrees by a cent, change the
  column boundary, not the formulas.
- **Migration sign-off.** `docs/migrations/007-AddInvestments.sql` (shown in 007 · CP1).
- **Spec-silent decisions to confirm or overturn:**
  - USD `realisedBrl` and `dividendsBrl` at each event's own FX rate (CP3).
  - The nightly rebuild also starts before yesterday where rows are missing, and it also
    runs after a manual sync (CP3).
  - The 409 on a DELETE that uncovers a later sell (CP3).
  - `POST /rebuild` answers 202 after it has already finished (CP3).
  - Silent rounding of excess decimals, and a 500 past a column's range (CP3).
  - float64 JSON numbers past 15 significant digits (CP4).
  - Zero positions hidden by default (CP4).
  - Stale price counting weekdays, with holidays unknown (CP4).
  - Split fields: quantity only (CP2, CP4).
  - Fees on income subtracted; irrelevant fields stored as zero; no unit-price rule; a
    split on a zero position allowed (CP2).
- **Invented pt-BR copy**, listed in 007 · CP2, CP3 and CP4, for a native review.
- **ARCHITECTURE.md's Investments block** is stale against the spec (007 · CP1). It needs a
  one-block edit. `benchmark_hint` was dropped with no replacement, and 008 may want it
  back.
- **CLAUDE.md's shared-table list** should also name `market_assets` and `sync_runs` (006).
  007 adds no shared table.
- **Starting spec 008.** Read `specs/008-returns.md`, `docs/handoffs/007.md` ("Starting spec
  008") and the 007 entries above. The baseline is .NET 611, web 115, E2E 21.
  - Flows come from `Movements`, not `PortfolioDaily`. Convert USD flows with 007's
    rule: the latest rate on or before the date, else the earliest after it.
  - Decide gross or net dividends (`dividendsBrl` is net of fees).
  - Days before an asset's first close have no row.
  - Derive the benchmark types from 006's `BenchmarkUnit`.
  - A syncing E2E spec must follow CP5's project rule.

## 008 · checkpoint 1

- **008 · CP1 · no ADR conflict; nothing to stop on.** Checked against the ADRs and what 006
  and 007 built:
  - 008 adds no table and no migration. It reads `PortfolioDaily` and `Movements` (both
    `IUserOwned`, filtered) and `Benchmarks` (shared, unfiltered), as ADR-007 and CLAUDE.md
    say.
  - `Domain/Returns/` is static pure functions and value objects. No EF, no `HttpClient`, no
    clock, no configuration type (ADR-014, ADR-017). No port, Repository or MediatR.
  - Every value is `decimal`, locals included. Two tests pin this: a reflection scan, and a
    source scan of `Domain/Returns/` and `Application/Returns/` for `double`, `float`,
    `Half`, a `d`/`f` literal suffix, and `Math.Pow`/`Exp`/`Log`/`Sqrt` outside comments.
  - The spec's daily linking is ARCHITECTURE's "sub-periods at every cash movement" with a
    sub-period per day, not a departure from it. Base 100 "at the period start" is the
    architecture's "date of the first contribution" at inception.
  - `benchmark_hint` (dropped in 007) is not needed. 008 compares every asset with the same
    configured set.
- **008 · CP1 · for the human, before CP3: spec test 15 cannot pass as written.** Scenario 3
  runs over four days. Its XIRR root is `1 + r ≈ 5.9e-87`, found with Python's `decimal` at
  50 digits. That is outside decision 5's bisection bracket `[-0.99, 10]` and below the
  smallest `decimal` (`1e-28`). So the solver returns `null`, not "negative", and test 15's
  `timingEffect` is `null` too. Test 16's mirror has the opposite problem: its root is
  `1 + r ≈ 2.7e79`, far above 10. The options:
  - **(a) Proposed default:** keep the scenarios' shape and returns, but space the four
    dates over a year, for example at days 0, 91, 182 and 365. TWR stays exactly `0`, since
    it is chained per step. XIRR is then finite, and the signs the spec asks for are
    testable. This changes the fixture, not the method.
  - **(b)** Keep the four days and assert `null`. This keeps the letter of the dates but
    drops the point of the test.
  - **(c)** Widen the bracket. Not possible: `decimal` cannot represent the root.
  CP3 will take (a) unless told otherwise.
- **008 · CP1 · `DecimalMath` (`Exp`, `Ln`, `Pow`).** `decimal` has no `Pow`, and decision 10
  rules out `double`, so the module has its own:
  - `Exp` splits off the whole part (`e^n` by squaring, then a Taylor series on the
    fraction). `Ln` halves or doubles into `[0.75, 1.5]`, then uses the atanh series.
  - A whole exponent is repeated multiplication, which is exact where the type can hold the
    result. `1.0005^3` is exactly `1.001500750125`.
  - Measured against a 40-digit reference, results agree to 1e-24 relative, or 1e-27
    absolute for values that small. Every expected value in the test was computed outside
    this code base (Python `decimal`) and written in by hand.
  - `Exp` above 66.5 throws `OverflowException`. Below −66.5 it returns 0.
  - **For CP3:** the XIRR discount `(1 + r)^(-t/365)` at `r = -0.99` over 15 or more years is
    `exp(> 66)`. So `NPV(-0.99)` overflows on a long history, and the bisection must guard
    it. One way is to discount to the last flow's date instead of the first: the
    exponents are then negative and underflow harmlessly to 0. Test that case.
- **008 · CP1 · value objects.** `Rate`, `CashFlow(Date, Money)` and `DailyPoint(Date,
  decimal)` are as the spec sketches them, one file each. `DailyPoint` is a measurement, so
  it holds a raw `decimal`, not `Money`. That follows 007's decision 10 and the 007 handoff.
- **008 · CP1 · `BenchmarkAccumulator`: what the spec leaves open** (spec silent; each choice
  is pinned by a test):
  - **The base day.** `start` holds exactly 100. A rate row compounds on its own date when
    that date is in `(start, end]`, so a row dated `start` does not compound. This is the
    spec's recurrence `index_d = index_{d-1} × …` taken literally. CP4 passes `start =
    from - 1`, so the first day of the period counts (see below).
  - **The output is one point per calendar day,** so it lines up with the portfolio index
    built from `PortfolioDaily`. Sampling (≤ 260 points) is CP4's job.
  - **`MonthlyRate` compounds a month's whole rate on the date of its row.** IPCA is dated
    the first of its month. So a period from 14 March to 2 April includes none of March and
    all of April. There is no pro-rata. Over months or years, each end is off by at most
    one month's rate. The spread `(1 + s)^(1/12)` is applied on the same rows only, so the
    months IPCA has not yet published hold flat, spread included. Manual step 4 checks CDI
    only; **for the human:** if IPCA + 6% has to match a calculator to the day, this needs
    pro-rata by days.
  - **`Level` anchors on the last value on or before `start`,** and carries forward on
    gaps.
  - **`null`** (spec test 31 needs it) means a level with no positive value on or before
    `start`, or a rate with no row in `(start, end]`. A rate series that stops partway
    through the period holds flat after its last row. It is not `null`.
  - A spread on anything but a `MonthlyRate` is an `ArgumentException`, and so is an end
    before the start.
- **008 · CP1 · `Returns:Benchmarks` and test 21.** The spec's five entries are in
  `appsettings.json`:
  - CDI, SELIC, IPCA6 (`Source: IPCA`, `Spread: 6`), USDBRL and IVVB11, with the spec's
    labels.
  - `ReturnsOptions.Problems` checks each type against the `BenchmarkUnit` that 006 records
    for its source. The source is the benchmark's own key unless `Source` names another.
    Units are read from `MarketData:Bcb:Series` or `MarketData:PriceBenchmarks`.
  - The mapping is `DailyRate` ↔ `PercentPerDay`, `MonthlyRate` ↔ `PercentPerMonth` and
    `Level` ↔ `Level`.
  - Each of these is a problem:
    - an unknown source;
    - a type that does not match its unit;
    - a spread on anything but a `MonthlyRate`;
    - an empty section.
  - `Program.cs` calls `RefuseMismatchedBenchmarks` at boot, next to 006's fake-provider
    check and before the migration. One `InvalidOperationException` lists every problem in
    English, like the other boot checks, and names the key (`Returns:Benchmarks:CDI`).
  - The unit is not copied by hand: the domain has its own `BenchmarkType` because
    `Domain/` cannot reference `Application/`'s `BenchmarkUnit`. The two are tied by the
    check, as the 007 handoff asked ("derive or assert").
  - Test 21 has a unit half (`ReturnsOptionsTests`) and a host half (`ReturnsBootTests`: `CDI`
    set to `Level` fails the boot). A test also asserts that the committed appsettings pass.
  - `Spread` is percent a year (`6`), as the spec writes it. The accumulator takes it as a
    `Rate` (`0.06`), so CP4 divides by 100.
- **008 · CP1 · decisions recorded now for later checkpoints** (spec silent; CP2–CP4 build on
  them unless the human says otherwise):
  - **Period base day (CP4).** The TWR rows and the benchmark indices start at `from - 1`,
    the base day, so the first day's return counts. XIRR's opening flow is `-V(from - 1)`,
    dated `from - 1`. At inception that value is 0, so decision 4 applies literally. For YTD,
    12m and custom periods it is the standard opening balance.
  - **A flow dated before its asset's first daily row** (a buy before the first close) moves
    to that first row's date. Otherwise the portfolio would record the cash going out one
    day and the value arriving on another, with no flow to match. A two-asset test in CP2
    pins it.
  - **Dividends and JCP are net of their fees,** in both `D_d` and XIRR. That is the cash
    received, and it matches 007's `dividendsBrl`.
  - **USD flows** convert at 007's rule: the latest USDBRL on or before the date, else the
    earliest after it. This is the same rule the rows use, so `r_fx` decomposes cleanly.
  - **SELIC is returned too.** The spec's configuration lists it, but its response example
    does not. Every configured benchmark is returned.
  - **`annualised` for a period of a year or less: open.** The spec says to annualise
    past a year and to report both, and `timingEffect = xirr − twr.annualised`. XIRR is
    always annualised. The default CP2/CP3 will take: always compute the annualised TWR,
    so `timingEffect` compares like with like. Whether the screen shows it for YTD/12m is
    CP5's choice. **For the human** to confirm.
  - **Labels.** The spec puts pt-BR labels in the configuration, but CLAUDE.md maps
    identifiers in `web/src/lib/labels.ts`. CP4/CP5 will serve the config's `Label` (already
    pt-BR, one source) unless the human prefers `labels.ts`.
- **008 · CP1 · counts.** .NET 611 → 667 (+56: 24 decimal maths, 14 accumulator, 16 config
  and boot, 2 type scans), web 115, E2E 21.
- **008 · CP1 · test-first.** Each test commit failed before its feat commit: 24/24, 14/14
  and 16/16. The two type scans passed from the start. They are guards, not drivers.
- **008 · CP1 · diff sizes.** Every commit is under ~200 lines. The test-21 commit was 262
  lines with the type scans, and was split before handoff (`336a30c`, 80, and `9705372`,
  182).
- **008 · CP1 · handoff to CP2** (`TimeWeightedReturn`, `FxDecomposition`, portfolio
  aggregation; tests 1–8 and 22–26).
  - Everything goes in `Domain/Returns/`, pure. The inputs are plain records. The
    type scans already cover the namespace.
  - Use `DecimalMath.Pow` for annualising (`Pow(1.21m, 365m/730m)` is `1.1` to 1e-27). Test
    7 needs a tolerance, not `Assert.Equal`. `365m/730m` is `0.5` exactly, but most ratios
    are not.
  - Inputs by day: `V_d` (`ValueBrl`), `D_d` (net income) and `F_d` (buys positive, sells
    negative). Skip a day when `V_{d-1} = 0` (test 6). The index series starts at exactly
    `100` (test 8); return `DailyPoint`s so it can be sampled with the benchmarks.
  - Aggregation (25, 26) sums by day across assets, with 0 before an asset starts. Move a
    flow dated before its asset's first row to that row (decision above). A test should
    show the portfolio does not jump on that day.
  - FX (22–24): `r_native` is TWR on `Quantity × Price` and native flows. `r_total` is TWR
    on `ValueBrl` and BRL flows. `r_fx = (1 + r_total)/(1 + r_native) − 1`, with the
    identity checked to 1e-10. BRL gives `null`.
  - Money flows are `CashFlow`, so `Money` rounds them to 2 places. Values are raw
    `decimal`.

## 008 · checkpoint 2

- **008 · CP2 · no ADR conflict; nothing to stop on.** Everything is in `Domain/Returns/`:
  static pure functions and records, with no EF, no `HttpClient`, no clock and no endpoint
  (ADR-014, ADR-017). There is no migration. `Domain/Returns/` reads `Domain/Investments/`'s
  `PortfolioDaily` and `Movement` as plain inputs. That is within `Domain/`, so it is not a
  layer crossing. CP1's type scans pass over the new files, and every value is `decimal`.
- **008 · CP2 · what was built.**
  - `ReturnDay(Date, Value, Income, Flow)`: one day's `V_d`, `D_d` and `F_d` in one
    currency.
  - `TimeWeightedReturn.Compute(days)` returns `TwrResult(Total, Annualised, Days, Index)`.
  - `ReturnSeries.InBrl` and `InNative` build one asset's days from its rows and movements.
  - `PortfolioAggregation.Sum` sums the assets' days by date.
  - `FxDecomposition.Split` returns `FxSplit(Native, Fx, Total)`.
  - The TWR takes `ReturnDay`s, not `PortfolioDaily`, because the FX split needs the same
    chain over native values and native flows.
- **008 · CP2 · TWR** (spec silent on each of these; each is pinned by a test):
  - **Base day.** The first day is the base day. Its own return is never computed, so a
    flow or income on it does not count. This matches CP1's "period starts from `from - 1`".
  - `1 + r_d` is one quotient, `(V + D - F) / V_prev`. It is not `r_d` computed and then
    `1` added back, so no rounding enters through the `- 1`. Test 2's two scenarios come
    out exactly equal, not just within a tolerance.
  - **Day handling.** A day whose previous value is 0 holds the index (test 6). Days are
    sorted, need not be consecutive, and each links to the day before it. Two days on one
    date are an `ArgumentException`: sum them first.
  - **Results.** No days gives `null`. One day gives a total of 0, an index of `[100]` and a
    `null` annualised rate. `Days` counts calendar days from the base day to the last day.
- **008 · CP2 · annualised TWR for a year or less: computed.** The spec says "annualise when
  the period exceeds one year … report both" and says nothing about shorter periods.
  - Following CP1's recorded default, `Annualised` is always `(1 + total)^(365/days) - 1`,
    computed with `DecimalMath.Pow`. That keeps `timingEffect = xirr - twr.annualised`
    like for like, because XIRR is always annualised.
  - **Guards.**
    - An `OverflowException` from `Exp` is caught and gives `null` (100% in one day is
      `2^365`).
    - A negative growth gives `null`. It cannot come from real rows, only from a flow that
      has no value to match it.
    - A total loss gives `-1`.
  - **For the human (CP5):** annualising a short period magnifies it. For example, 1% over
    10 days is about 44% a year. Whether the screen shows the annualised figure for YTD, 12m
    or short custom ranges is CP5's choice. The API carries both figures either way.
- **008 · CP2 · flows and values** (these follow the CP1 defaults):
  - **Flows.**
    - A buy is `qty x price + fees` in.
    - A sell is `qty x price - fees` out.
    - A dividend or JCP is `amount - fees` as income.
    - A split is nothing.
    - Each amount is rounded to cents as `Money`, since it is cash. BRL values are
      `ValueBrl`, already at 2 places. Native values are `Quantity x Price`, unrounded.
  - **FX.** A USD flow converts at 007's rule, at the rate for its **own** date, even when
    the flow is moved (below). That is the BRL the investor actually paid, the same as
    `CostBasisBrl`. A USD flow with no rate at all is an `InvalidOperationException`.
  - **Moved and dropped movements.**
    - A movement dated on or before the asset's first row lands on the first row.
    - For an asset on its own, that row is the base day, so the flow does not count.
    - In a portfolio that started earlier, the flow offsets the value the asset brings in.
      A test shows the portfolio does not drop on the buy day.
    - A movement after the last row is left out.
- **008 · CP2 · aggregation.** The portfolio's days are the union of the assets' dates, with
  value, income and flow summed. An asset with no day on a date contributes 0.
  `SnapshotBuilder`'s rows run on every calendar day, so the only gap is before an asset
  starts. That includes a USD asset before its first FX rate, which the early-flow move
  covers.
  - Test 26 is checked to `1e-20`: `1.025 x 361/410` is not exactly representable.
  - The TWR of a portfolio is value-weighted: `-9.75%`, not the `+0.5%` average of the
    two assets' TWRs.
- **008 · CP2 · FX split.** `Split` returns `null` for BRL (test 24) and for an asset with no
  rows. `r_fx = (1 + total)/(1 + native) - 1`, and the identity is asserted to `1e-10`,
  including a case with a mid-period buy at its own rate and uneven prices.
  - When the native return is `-100%`, both sides of the identity are 0 whatever `r_fx` is.
    It is reported as 0 rather than dividing by zero (spec silent).
- **008 · CP2 · counts.** .NET 667 → 701 (+34: 15 TWR, 10 series, 4 aggregation, 5 FX). Web
  115. E2E 21.
- **008 · CP2 · test-first.** Each of the four test commits failed to compile before its feat
  commit. After the feat, every test in its file passed on the first run.
- **008 · CP2 · diff sizes.** Every commit is under ~200 lines. The largest is `49cb05e`, the
  TWR tests, at 166 lines.
- **008 · CP2 · handoff to CP3** (`MoneyWeightedReturn`, Newton then bisection, and the
  timing effect; tests 9–16).
  - **Open human decision: tests 15 and 16.** As written, XIRR is out of range (CP1 entry
    above). Scenario 3's four-day XIRR root is `1 + r ≈ 5.9e-87`, and the mirror's is about
    `2.7e79`. Both are outside decision 5's bracket `[-0.99, 10]` and the `decimal` range,
    so the solver returns `null`.
    - **CP3's default is option (a):** keep the same moves and returns but spread them over
      a year, for example on days 0, 91, 182 and 365. XIRR is then finite and has the sign
      the spec asks for.
    - TWR stays exactly `0` for scenario 3, because the chain does not depend on the
      spacing. `TimeWeightedReturnTests` has the four-day version.
    - Flag the respaced fixture in the test and in DEFERRED as pending the human.
  - **Timing effect.** `timingEffect = xirr - twr.Annualised`. Either side can be `null`:
    XIRR because of its flows, TWR because of a zero-day period or overflow. Either one
    makes the effect `null`.
  - **XIRR flows** (decision 4) come from the movements at their **real** dates, not the
    moved ones. XIRR has no daily value to mismatch.
    - They use the same cash rules as `ReturnSeries`: fees in on buys, fees out of sells,
      income net of fees, USD at the flow's own rate.
    - `ReturnSeries.RateOn` is private today. Make it `internal` or extract it, rather than
      write the rule a third time.
  - **Exp overflow.** Guard `NPV(-0.99)` on a long history, as CP1 noted. Discount to the
    last flow's date, so the exponents are negative and underflow to 0. Test it.
- **008 · CP2 · notes for CP4.**
  - Load `PortfolioDaily` for `[from - 1, to]` and pass each asset's **whole** movement
    history to `ReturnSeries.InBrl`. Earlier movements land on the base day and do not
    count.
  - Pass USDBRL's benchmark points as the `fxRates`.
  - Portfolio: `PortfolioAggregation.Sum`, then `TimeWeightedReturn.Compute`. Its `Index`
    has one point per day, ready to sample alongside `BenchmarkAccumulator`'s.
  - Per asset: `FxDecomposition.Split`, whose `Total` equals the asset's BRL TWR.

## 008 · checkpoint 3

- **008 · CP3 · no ADR conflict; nothing to stop on.** Everything is in `Domain/Returns/`:
  static pure functions and records, with no EF, no `HttpClient`, no clock and no endpoint
  (ADR-014, ADR-017). There is no migration. CP1's type scans pass over the new files, and
  every value, locals included, is `decimal`. `specs/008-returns.md` is not edited.
- **008 · CP3 · what was built.**
  - `MoneyWeightedReturn.Compute(flows)` returns the annualised XIRR as a `Rate?`.
  - `MoneyWeightedReturn.FlowsInBrl(currency, movements, fxRates, opening, closing)` builds
    decision 4's flows for one asset.
  - `TimingEffect.Of(xirr, twrAnnualised)` returns `xirr - twr.annualised`, or `null` when
    either side is `null`.
  - `MovementCash` (internal) holds the cash rules (buy, sell, income net of fees) and the
    FX lookup, extracted from `ReturnSeries`. TWR and XIRR now read one copy, so nothing is
    duplicated. `SnapshotBuilder` (007) still has its own lookup over `Benchmark` rows. It
    was left alone because it is outside this checkpoint.
- **008 · CP3 · DEVIATION, PENDING HUMAN SIGN-OFF: tests 15 and 16 use option (a).** As
  CP1 found, the spec's four-day scenarios have XIRR roots of `1 + r ≈ 5.9e-87` (test 15)
  and about `2.7e79` (its mirror). Both are outside `[-0.99, 10]` and outside the `decimal`
  range.
  - `TimingEffectTests` keeps the same moves but puts them on days 0, 91, 182 and 365.
    TWR is still exactly `0`, total and annualised. XIRR is `-0.676746844602991` (15) and
    `2.16930501414329` (16), so the timing effect has the sign the spec asks for.
  - Test 16's mirror scenario is: 1000 in, -50%, 10 000 in, +100%. That is the spec's "big
    deposit before the gain", and it shows the TWR `0.5 x 2 - 1 = 0`.
  - A third test pins the literal four-day scenarios, both of them: XIRR `null`, timing
    effect `null`, TWR `0`.
  - The test class carries a remark pointing here. **For the human:** confirm (a), or say
    to switch to (b), which asserts `null` only. Either way the spec text needs a line.
- **008 · CP3 · solver** (decision 5; spec silent on each point below, each pinned by a test):
  - **Overflow.** CP1 suggested discounting to the last flow's date. That only moves the
    overflow to the other end of the bracket: `11^(t/365)` passes the `decimal` range after
    about 28 years. Instead, the NPV and its slope are both multiplied by `exp(-max
    exponent)`, so every discount factor is at most 1 and the smallest underflow to 0. That
    factor is positive, so the NPV's sign (all bisection reads) is kept. It is the same
    factor for the NPV and its slope, so Newton's step is unchanged too. Two 40-year tests
    (14 600 days, both ends of the bracket, and bisection on its own) pin it.
  - **When Newton gives up.** Newton hands over to bisection when:
    - a step leaves `[-0.99, 10]`;
    - the slope is exactly 0;
    - the step itself overflows `decimal` (the "derivative near zero" case);
    - 100 steps pass without two iterates within `1e-8`.
  - **Bisection.** It runs on `[-0.99, 10]`, up to 200 steps, and stops when the half-width
    is below `1e-8`, as the spec says. So a bisected rate is good to about `1e-8`, well
    inside manual step 3's 4 decimals. If the NPV has the same sign at both ends, the
    result is `null`.
  - **`null` rather than an exception** when:
    - there are fewer than two flows;
    - the flows are all of one sign;
    - every flow is on one day;
    - the root is outside the bracket.
  - Flows in two currencies are an `ArgumentException`: that is a caller bug.
  - **More than one root** (flows that change sign more than once): Newton returns the root
    it reaches from 10%, and bisection returns one root in the bracket. When the bracket
    holds an even number of roots, its ends have the same sign and the result is `null`.
    The spec does not address this. It only matters for histories that alternate between
    large deposits and large withdrawals.
- **008 · CP3 · test 13's flow set.** The spec says "large late inflow". A search (Python
  `decimal` at 50 digits for the candidates and in floats for the sweep) found that deposits followed by
  one late inflow (a single sign change) never made Newton from 10% diverge when the root
  is a gain. Newton climbs to it monotonically. A rough bound, estimated and not proven,
  puts that under 100 steps for any ratio `decimal` can hold. Newton diverges on losses,
  where it overshoots below -0.99, and on alternating flows. The test therefore uses
  `-100` (day 0), `+1000` (365), `-1000` (730), `+10 000` (7300). Newton leaves the bracket
  on its fifth step, to -1.03. A sign scan of the NPV over the bracket found exactly one
  root, `7.87298334620742`. **For the human:** the reading of "large late inflow" is mine.
- **008 · CP3 · where the expected values come from.** Every XIRR expected value was
  computed outside the code base twice, and both agree to the 15 digits Calc prints:
  - Python `decimal` at 50 digits;
  - LibreOffice Calc 24.2's `XIRR()`, the spreadsheet the spec names as the authority.
    Calc was installed into the sandbox for this and is not committed.
- **008 · CP3 · XIRR flows** (these follow the CP1/CP2 defaults):
  - The flows are from the investor's side:
    - the opening value out, on the base day;
    - each movement after the base day and up to the closing day, on its **real** date;
    - the closing value in, on the closing day.
  - A buy is `-(qty x price + fees)`, a sell `+(qty x price - fees)`, and a dividend or JCP
    `+(amount - fees)`. A split is nothing.
  - A USD movement converts at 007's rule for its own date, rounded to cents. The opening
    and closing values are `ValueBrl`, BRL already.
  - A zero opening value (inception) or a zero closing value (sold out) adds no flow.
  - A movement on or before the base day is in the opening value already, so it is left
    out. So is a movement after the closing day.
  - A portfolio's flows are its assets' lists put together. XIRR needs no summing by day.
- **008 · CP3 · counts.** .NET 701 → 730 (+29: 16 XIRR, 7 flows, 6 timing effect). Web 115.
  E2E 21. Both verify scripts green.
- **008 · CP3 · test-first.** Each of the three test commits failed to compile before its
  feat commit. After the feat, every test in its file passed on the first run. The
  refactor (`455c3aa`) came first, with the 56 existing returns tests green before and
  after.
- **008 · CP3 · diff sizes.** Every commit is under ~200 lines. The largest is `179aef3`, the
  solver, at 170 lines.
- **008 · CP3 · handoff to CP4** (`ReturnsQueries`, `GET /api/returns/portfolio` and
  `/api/returns/assets/{id}`; tests 27–32).
  - **TWR.** Load `PortfolioDaily` for `[from - 1, to]` and each asset's **whole** movement
    history. Then run `ReturnSeries.InBrl` per asset, `PortfolioAggregation.Sum`, and
    `TimeWeightedReturn.Compute`. The CP2 notes above still apply.
  - **XIRR.** Per asset, call `MoneyWeightedReturn.FlowsInBrl` with:
    - `opening = (from - 1, ValueBrl on from - 1)`, or 0 when there is no row (inception);
    - `closing = (to, ValueBrl on to)`;
    - USDBRL's benchmark points as `fxRates`, which is 007's FX rule.

    For the portfolio, concatenate every asset's flows and call `Compute`. Then
    `timingEffect = TimingEffect.Of(xirr, twr.Annualised)`.
  - **Periods and clamping.**
    - Inception starts at the first movement.
    - A `from` before the first movement clamps to it (test 29).
    - The base day is `from - 1` (CP1 decision).
    - For YTD and 12m, take today from the injected `TimeProvider`, as `SnapshotRebuild`
      does. No clock goes into the domain.
  - **Sampling to ≤ 260 points** (test 30). Sample the portfolio index and every benchmark
    index on the same dates, and always keep the first day (the base, 100) and the last
    day.
  - **Missing benchmarks** (test 31). `BenchmarkAccumulator.Accumulate` returns `null` when
    a benchmark cannot anchor. That benchmark is then `null` in `benchmarks` and absent from
    `series`, and the others are unaffected. `Spread` in config is percent (`6`), so pass
    `new Rate(6 / 100m)`.
  - **Benchmark labels: return codes, not labels.** CLAUDE.md says names shown on screen
    are mapped in `web/src/lib/labels.ts`. So the API returns benchmark **codes** (`CDI`,
    `SELIC`, `IPCA6`, `USDBRL`, `IVVB11`) as keys, and the pt-BR labels live in
    `labels.ts`. The spec's response shape already keys by code and has no label field, so
    this does not contradict it. Only if the spec explicitly required a label on the wire
    would it be otherwise. This supersedes CP1's recorded default ("serve the config's
    `Label`"). **For the human:** `Returns:Benchmarks:*:Label` then has no reader. Remove
    it, or keep it as documentation; CP4 should not start reading it.
  - **Test 32.** Hand-compute the expected TWR, XIRR and benchmark returns outside the code
    (Python `decimal` or Calc) and write them in.

## 008 · checkpoint 4

- **008 · CP4 · no ADR conflict; nothing to stop on.** `ReturnsQueries` in
  `Application/Returns/` uses `AppDbContext` directly (ADR-016). There is no Repository and
  no MediatR, and the global query filters stay on. `Assets`, `Movements` and
  `PortfolioDaily` are read through their filters (ADR-007). `Benchmarks` is shared and
  unfiltered. The endpoint file only parses and maps. All the maths is CP1–CP3's domain
  code, unchanged except that `TimeWeightedReturn.Annualise` is now public (see below).
  **No migration.** CP1's no-`double` scans cover the new files, and the source scan now
  also reads `Endpoints/ReturnsEndpoints.cs`.
- **008 · CP4 · what was built.**
  - `GET /api/returns/portfolio` and `GET /api/returns/assets/{id}`, both
    `?period=inception|ytd|12m|custom&from&to`.
  - `ReturnsQueries` (`PortfolioAsync`, `AssetAsync`), with the response records
    `ReturnsView`, `AssetReturnsView`, `PeriodView`, `TwrView`, `BenchmarkView` and `FxView`.
  - `ReturnsPeriods.Resolve` (pure, and given today) and `SeriesSampling.Positions` (pure).
  - `TimeWeightedReturn.Annualise` went from private to public, so a benchmark's return is
    annualised by the same guarded function as the portfolio's (`null` past the `decimal`
    range). It is not duplicated.
- **008 · CP4 · the query** (spec silent on the details; each point is pinned by a test):
  - `period` is `inception`, `ytd`, `12m` or `custom`, in any case.
    - It is `inception` when absent.
    - `from`/`to` alone mean `custom`.
    - `from`/`to` beside another period are a 400 on `period`, not silently ignored.
  - Dates are `yyyy-MM-dd`.
    - A malformed date is a 400 naming `from` or `to`.
    - `from` after `to` is a 400 on `from`.
    - Every problem is reported at once, as 005 does.
  - **Invented pt-BR copy, for a native review** (`Endpoints/ReturnsEndpoints.cs`):
    - "O período deve ser desde o início (inception), no ano (ytd), 12 meses (12m) ou
      personalizado (custom)."
    - "As datas de início e fim valem apenas para o período personalizado (custom)."
    - "A data inicial/final deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31."
    - "A data inicial deve ser anterior ou igual à data final."
  - Someone else's asset, or one that does not exist, is a 404 (filtered, like 007's
    routes). Both routes need a session (401).
- **008 · CP4 · periods and clamping** (decision 9; tests 28, 29):
  - "Today" is the UTC date from the injected `TimeProvider`, the same day
    `SnapshotRebuild` counts to. No clock goes into the domain.
  - **Inception** runs from the first movement. **YTD** runs from 1 January of today's year.
    **12m** runs from today minus a year plus a day, so the base day is exactly a year ago:
    365 days, and `annualised == total`. **Custom** defaults to inception and today.
  - The start is clamped up to the first movement (test 29). The end is clamped to today
    and to the **last daily row**, so the closing value is always a real one. If the
    nightly rebuild has not run yet today, the period ends yesterday.
  - A period that ends before the first movement is empty (below).
- **008 · CP4 · one base day for the TWR, the benchmarks and the chart.** It is the first
  daily row in `[from - 1, to]`.
  - That is `from - 1` whenever something was held then (YTD, 12m, custom), so the first
    day's return counts (CP1 decision).
  - **At inception it is the first contribution's day.** There is no value on `from - 1`,
    and ARCHITECTURE puts "base 100 on the date of the first contribution". This refines
    CP1's "benchmark indices start at `from - 1`". With `from - 1`, CDI would accrue a day
    the portfolio cannot, which is a one-day bias against the portfolio.
  - `period.days` is `to` minus that base day, and it equals the TWR's `Days`. Every
    benchmark annualises over the same days.
- **008 · CP4 · XIRR opens on `from - 1`**, per asset, as the CP3 handoff says. At
  inception that value is 0, so the first buy counts at its real cash.
  - An asset with **no row on `from - 1`** because its first close came later opens at zero
    instead. All its movements up to `to` then count at their real dates. This mirrors
    TWR's rule of moving an early flow onto the first row.
  - The closing value is each asset's last row on or before `to`, dated `to`.
  - The portfolio's flows are the assets' lists put together.
- **008 · CP4 · assets with no row in the period are left out of both returns.** Their
  value is unknown, for example an asset bought today before its first close. Counting its
  buy with no value would put a false loss in the XIRR. Spec silent. **For the human:**
  confirm.
- **008 · CP4 · the empty response** (test 27) is a 200 with `period`, `twr`, `xirr` and
  `timingEffect` all `null`, every benchmark code `null`, and `series: []`. It is returned
  when the user has no movements, no daily rows, or nothing valued in the period. It is not
  a 404: the page has an empty state to render, not an error.
- **008 · CP4 · FX.** USDBRL points from the latest on or before the earliest movement of the
  period's non-BRL assets, up to `to`. They feed `ReturnSeries.InBrl`,
  `MoneyWeightedReturn.FlowsInBrl` and `FxDecomposition.Split`, which is 007's rule through
  the shared `MovementCash`. The asset test shows `fx.fx` equal to the USDBRL benchmark's
  return over the same days, as it should be.
- **008 · CP4 · benchmarks** (test 31):
  - **Codes, not labels, as the CP3 handoff decided.** The keys are `CDI`, `IPCA6`,
    `IVVB11`, `SELIC` and `USDBRL`. The pt-BR names are CP5's, in `web/src/lib/labels.ts`.
  - **`Returns:Benchmarks:*:Label` is now unread.** It was left in `appsettings.json` as it
    is. **For the human:** remove it, or keep it as documentation.
  - They are **ordered by code**, both in `benchmarks` and in each series point after
    `date` and `portfolio`. Configuration binding keeps no file order (the first run of
    test 32 showed it), so no order but an explicit one is stable.
  - Rate rows are read in `(base, to]`. A `Level` also reads its last value on or before
    the base day, as its anchor. `Spread` is percent (`6`) and is passed as
    `new Rate(6 / 100m)`. Test 32 checks IPCA + 6% against the hand value, so the `/100` is
    covered.
  - A benchmark that cannot anchor is `null` in `benchmarks` and absent from every series
    point. The others are unaffected. That covers a rate with no row in the period, and a
    level first recorded after the base day.
- **008 · CP4 · sampling** (test 30):
  - The stride is `ceil((n - 1) / 259)` whole days, from the base day, and the last day is
    always kept. So at most 260 points on a regular grid.
  - That is daily up to 260 days, and **weekly up to 1 814 days (4.97 years)**. An exact
    5-year range (1 826 days) gets an 8-day stride, 230 points.
  - The spec says "weekly over 5 years" and "at most 260". Strictly weekly over 5 years is
    262 points, so the cap wins. **For the human:** say if the cap should be 262 instead.
  - The portfolio and every benchmark are sampled on the same days.
- **008 · CP4 · numbers on the wire.** Rates are rounded to 10 places and index values to 6,
  half to even, as `decimal`. The rounding is for display and payload size, and XIRR is
  only good to about 1e-8 anyway. JavaScript still parses these as float64 (007 · CP4).
- **008 · CP4 · test host clock.** `InvestmentsApi.StartAsync(..., clock)` replaces **only**
  `ReturnsQueries`' `TimeProvider`. Replacing the host's clock made the cookie handler
  issue the session cookie on the fake date (2026-06-30). By real time that cookie had
  already expired, so the client dropped it and every call was a 401.
- **008 · CP4 · test 32's expected values.** Each was computed outside the code base in
  Python `decimal` at 50 digits. Each XIRR was also computed in LibreOffice Calc's `XIRR()`:
  `3.34541053669079` for test 32 and `0.475054807440193` for the USD asset, and both agree.
  The first USD fixture (29 days, +21%) annualised above 10. It was out of XIRR's bracket, so
  it was lengthened to 179 days.
- **008 · CP4 · counts.** .NET 730 → 759 (+29):
  - 15 unit tests (periods, clamping, sampling);
  - 10 portfolio-endpoint tests (27–30, five 400s, 401);
  - 2 benchmark tests (31, 32);
  - 2 asset-route tests.

  Web 115. E2E 21. Both verify scripts are green.
- **008 · CP4 · test-first.** Each feat commit was preceded by tests that failed:
  - 15/15 period and sampling tests did not compile;
  - 10/10 endpoint tests failed with no route;
  - 4 of the benchmark and asset tests failed, the benchmark ones at their first benchmark
    line, with TWR, XIRR and the timing effect already matching the hand values.

  Two test corrections landed on their own, before the feats that needed them:
  - `2777c71`: the fixed clock applies to the returns only;
  - `5eb985a`: benchmark keys in code order.

  The session test's asset-route half was moved into the asset-route test commit before
  anything was pushed.
- **008 · CP4 · diff sizes.** Every commit is under ~200 lines. The largest is `54fff6e`
  (`ReturnsQueries`) at 167. The first draft of the 27–30 tests was 242 lines, so it was
  split into fixtures (`ed8c53e`, 87) and tests (`dd3a46d`, 154).
- **008 · CP4 · handoff to CP5** (web `/investments/returns`; tests 33–35).
  - **The shape.** Both routes return `{ period: { from, to, days }, twr: { total,
    annualised }, xirr, timingEffect, benchmarks: { CODE: { total, annualised } | null },
    series: [{ date, portfolio, CODE... }] }`. The asset route adds `fx: { native, fx,
    total } | null` after `timingEffect`.
    - Everything but `benchmarks` and `series` is `null` when nothing was held (the empty
      state).
    - A `null` benchmark has no key in `series`. So toggles should be built from
      `benchmarks`' non-null entries.
    - Rates are fractions (`0.1234`), not percents.
  - **Labels.** Add `CDI`, `SELIC`, `IPCA6`, `USDBRL` and `IVVB11` to `labels.ts`. The
    spec's names are "CDI", "SELIC", "IPCA + 6%", "Dólar" and "S&P 500 (IVVB11)".
  - **Period selector.** `inception` (the default), `ytd`, `12m`, or `custom` with
    `from`/`to`. A 400 carries pt-BR messages keyed by `period`, `from` or `to`. Show them
    beside the date inputs.
  - **Annualised TWR for short periods: not shown, pending human.** The API always sends
    `twr.annualised`, and the spec says to annualise only "when the period exceeds one
    year". XIRR is always annual, and `timingEffect = xirr - twr.annualised`.
    - **Default for CP5:** when `period.days <= 365`, the headline shows `twr.total`
      labelled as the period's return. It does not show `twr.annualised`. XIRR and the
      timing effect are labelled "a.a.". The benchmark table shows totals only.
    - When `period.days > 365`, show both TWR figures, as the spec says.
    - Annualising a short period magnifies it (1% over 10 days is about 44% a year). So
      for short periods the timing effect, being a difference of two annual rates, is
      large and noisy. **For the human:** confirm, or say to show annualised always, or to
      hide the timing effect below some number of days.
  - **Per-asset table.** There is no list route. The table needs one
    `/api/returns/assets/{id}` call per asset, with the ids from `/api/investments/assets`.
    That is fine for a handful of assets. If it is slow, a list route is a small API
    change (spec silent).
  - **Chart.** `series` has ≤ 260 points, and the base day is always the first, with every
    series at 100. Dates are `yyyy-MM-dd`.
  - **For CP6 (E2E 36).** Under `FakeProviders` every close is 10 and every benchmark is
    0.05, both dated yesterday. So a real TWR is 0 unless the fakes get a shape (007
    handoff).

## 008 · checkpoint 5

- **008 · CP5 · no ADR conflict; nothing to stop on.** The checkpoint is web only. There is
  no API change and no migration. Every figure on screen is the API's, formatted in pt-BR.
  The one figure made in the browser is the benchmark table's difference, and it is made
  exactly (below). Enum members, periods and benchmark codes stay English on the wire and
  are named in `web/src/lib/labels.ts`, as CLAUDE.md says.
- **008 · CP5 · what was built.**
  - `/investments/returns`, linked from `/investments` ("Rentabilidade" beside the heading)
    and not from the navigation, since the spec says "linked from the positions page".
  - `/investments/$assetId/returns`, the spec's "asset's own returns page". The spec names
    no path for it, so the path is mine. It uses the same report as the portfolio's page
    and adds the FX split for a non-BRL asset. It is reached from the per-asset table only.
    `AssetPage` (007) has no link to it yet (**for the human / CP6:** add one if wanted).
  - In `components/returns/`: `PeriodSelector`, `Headline`, `ComparisonChart`,
    `BenchmarksTable`, `AssetReturnsTable`, `FxSplit`, `ReturnsReport` (shared by both
    pages), `queries.ts` and `benchmarks.ts`.
  - Library code: `lib/rates.ts` (`formatRate`, `formatPoints`, `NO_DATA`, `perYear`,
    `showsAnnualised`, `signTone`), `rateUnits` and `pointsDifference` in `lib/decimal.ts`,
    and the benchmark and period labels in `lib/labels.ts`. The response types and
    `api.portfolioReturns` / `api.assetReturns` are in `api/finance.ts`.
- **008 · CP5 · DECISION PENDING HUMAN: annualised TWR for a year or less is not shown.**
  This is the CP4 handoff's default, and the spec says nothing that overrides it ("annualise
  when the period exceeds one year").
  - When `period.days <= 365`, the headline shows `twr.total` labelled "no período". It does
    not show `twr.annualised`. The benchmark table shows the period's returns only.
  - When `period.days > 365`, the headline adds the annualised TWR ("a.a."), and the
    benchmark table adds an "Ao ano" column.
  - XIRR and the timing effect are always a year's rate and always labelled "a.a.". So for a
    short period the timing effect is still `xirr - twr.annualised`, a difference of two
    annual rates, and it can be large and noisy. For example, 1% over 10 days is about 44% a
    year. **For the human:** confirm, or say to show the annualised TWR always, or to hide
    the timing effect below some number of days.
- **008 · CP5 · PENDING HUMAN: "the same red as expenses".** The app has no red for expenses.
  `Amount` prints an expense in ink, and the charts use `--chart-expense`, which is orange
  (`#eb6834`). 005 · CP3 chose blue and orange over green and red for colour-vision reasons.
  - A negative timing effect is therefore `text-chart-expense`. A positive one is
    `text-green-700 dark:text-green-500`, the same green as `Amount`. Zero and `null` are
    in ink.
  - The sign is always printed, so colour is never the only carrier.
  - The orange on white is about 3:1. That passes WCAG AA only as large text. The headline
    figure is 24px semibold, so it counts as large text. **For the human:** confirm the
    orange, or name a red token.
  - Test 33 asserts the class.
- **008 · CP5 · the benchmark table** (spec silent on the details):
  - The rows are "Carteira" first, then every benchmark the API sent. Benchmarks are in the
    spec's configuration order (CDI, SELIC, IPCA + 6%, Dólar, S&P 500), not the API's code
    order. A code the client does not know is appended and shown as sent.
  - "Carteira menos referência" is `twr.total - benchmark.total` in percentage points
    (`+1,91 p.p.`). It is always on the **period's** returns, including past a year, where
    the annualised columns are also shown. **For the human:** say if past a year the
    difference should be on the annualised figures.
  - The difference is exact. `rateUnits` recovers the API's 10-place decimal from the
    float64 with `toFixed(10)`, which is exact because the float is within half a unit
    of it. The difference is then taken in `bigint` and rounded once, half to even, to a
    hundredth of a point. `0.3 - 0.1` gives exactly 20,00 p.p.
  - A `null` benchmark shows "Sem dados" in every cell, the difference included.
- **008 · CP5 · null and placeholders.**
  - Any `null` rate reads "Sem dados" (`NO_DATA`), never NaN or a blank. XIRR's "a.a."
    suffix is dropped when there is no rate.
  - The empty response (CP4's 200 of nulls) shows "Nenhuma posição valorizada neste
    período." in place of the report.
  - `fx: null` on a BRL asset is not missing data, so the per-asset table says "Ativo em
    reais". On a USD asset with no split it says "Sem dados".
- **008 · CP5 · the chart.**
  - Recharts, base 100. The portfolio's index is drawn in `var(--primary)` at 2.5px. The
    spec asks for "the primary colour", and in the stock neutral theme that is near-black.
  - Each benchmark is `var(--muted-foreground)` at 1.5px, told apart by its dash pattern.
    Five muted colours cannot be told apart, and dashes also survive greyscale.
  - Every benchmark that could anchor has a toggle. All are on by default (spec silent). A
    `null` benchmark has no toggle and no line, since it has no key in `series`.
  - The default tooltip lists every visible series on the hovered date, to two places. A
    visually hidden table gives the same at each month's last point, as 007's value chart
    does.
  - `ResponsiveContainer` gets an `initialDimension`, so jsdom draws the lines. Test 34
    checks that each line's `path` is present or absent in the SVG. It does not only check
    the button state.
- **008 · CP5 · the period selector.**
  - It has four buttons (`aria-pressed`). A preset fetches on click. "Personalizado" shows
    "De" and "Até" date inputs, which fetch only on "Aplicar".
  - The period is part of the query key, so a change is a new request (test 35). Every
    returns query sits under `['investments', 'returns']`, so a movement write refreshes it.
  - A 400's `from` and `to` messages show under their inputs as sent. A `period` message
    shows as an alert. Any other failure shows a pt-BR alert. On an asset's page, a 404
    shows "Ativo não encontrado."
  - Queries do not retry a 4xx, so a 400 shows at once, not after three retries.
  - The period is component state. It is not in the URL, so it is lost on reload and
    cannot be linked to (spec silent).
- **008 · CP5 · the per-asset table.**
  - It lists every position `/api/investments/assets` returns, closed and just-added
    included. It uses one `/api/returns/assets/{id}` call per asset, in the selected period
    (CP4 handoff: no list route). An asset with nothing in the period reads "Sem dados".
    **For the human:** hide those rows, or add a list route if many assets make this slow.
  - The columns are TWR for the period, XIRR "a.a.", the asset's own-currency return, and
    the exchange rate's return. The asset's own page shows the three-way split (asset,
    exchange rate, total in reais).
- **008 · CP5 · invented pt-BR copy, for a native review:**
  - "Rentabilidade"
  - "Rentabilidade (TWR)", "O desempenho dos ativos, sem o efeito de quando você aportou."
  - "Retorno do dinheiro (XIRR)", "O retorno real, com a data e o valor de cada aporte e
    resgate."
  - "no período", "a.a.", "Sem dados"
  - "Desde o início", "No ano", "12 meses", "Personalizado", "De", "Até", "Aplicar"
  - "Carteira e referências, base 100", "Referências no gráfico"
  - "Comparação com referências", "Referência", "No período", "Ao ano", "Carteira menos
    referência", "p.p."
  - "Por ativo", "TWR no período", "Ativo na moeda", "Câmbio", "Ativo em reais", "Ativo em
    USD", "Total em reais"
  - "Nenhuma posição valorizada neste período.", "Ativo não encontrado.", "Não foi possível
    carregar a rentabilidade. Recarregue a página para tentar de novo."

  The spec's own strings ("Efeito do timing" and its sentence) are used verbatim.
- **008 · CP5 · numbers on the wire.** Rates are displayed from JSON's float64 through
  `Intl.NumberFormat` to two places, which is display only. The API rounds them to 10 places
  (CP4), so nothing visible is lost.
- **008 · CP5 · counts.** Web 115 → 142 (+27):
  - 10 library tests (4 decimal, 3 labels, 3 rates);
  - 17 page tests (headline and test 33 ×2, test 34, test 35, 400s, 404, empty state,
    benchmark table, per-asset table, asset page).

  .NET 759 and E2E 21 are unchanged. Both verify scripts are green.
- **008 · CP5 · test-first.** Each feat commit was preceded by a test commit that failed. Three
  test corrections landed on their own, before the feats that needed them:
  - `c84b753`: the half-way values of the points difference were off by 100;
  - `368b530`: the refetched headline is read across its spans, and a literal no-break
    space the linter refused is escaped;
  - `052b1d9`: the asset 404 is stubbed with `{}`. The API's bare 404 has an empty body,
    which the client reads as `{}`. The stub's `null` crashed the client's problem parsing,
    which a real 404 cannot do.
- **008 · CP5 · diff sizes.** Every commit is under ~200 lines except `c4cdc72`, the page tests,
  at 211. It was left as one commit because the tests share their fixtures. The first
  headline feat was 245 lines, so it was split before handoff into `d1a8280` (101) and
  `cbb9ca1` (146).
- **008 · CP5 · handoff to CP6** (E2E test 36 and `docs/handoffs/008.md`).
  - **The fakes need a shape.** Under `MarketData:FakeProviders`,
    `FakeMarketDataProviders` returns one close of `10` dated `to`, and one benchmark value
    of `0.05` dated `to`. So every price is flat, and every TWR, XIRR and asset-only return
    is 0. Test 36 needs "non-zero TWR", so the fake closes have to vary by date.
  - **Do not break 006/007's E2E.** 007's tests 31–33 buy 100 PETR4 today and assert a value
    of `R$ 1.000,00` and a result of `+R$ 45,10`. Both read the latest close, so that close
    must stay `10`.
  - One shape that keeps them: a close per day over the requested `[from, to]`, derived
    from the date, with the close on `to` equal to `10` (for example, 1% lower for each
    day before `to`). Check that the 006 sync test's item counts do not depend on one row
    per asset.
  - **Test 36 itself.** A buy dated today has only one daily row, which is the base day,
    so its TWR is 0 by construction (CP2). Date the buy some days back and make sure the
    sync covers those days, so there are several rows. Then assert a non-zero headline TWR
    and that `comparison-chart` is visible. A real TWR needs a price change after the buy.
  - The E2E's first real-browser pass over these pages is CP6's. Everything here was run
    in jsdom only. The wire shape was checked against CP4's integration tests: camelCase
    fields, benchmark codes as keys, and dates as `yyyy-MM-dd`.
  - **The handoff should list the open items:** CP1–CP4's pending decisions, this
    checkpoint's two (annualised TWR for short periods, and the expense colour), the p.p.
    basis past a year, per-asset rows with no data, and the copy review.

## 008 · checkpoint 6

- **008 · CP6 · no ADR conflict, no API change, no migration.** The fakes are Development
  only, behind 006's switch, refused elsewhere at boot. The web changes are two display
  fixes.
- **008 · CP6 · the fakes' shape.** `FakeMarketDataProviders` now gives a close for every
  calendar day in `[from, to]`: `10 - 0.01 × min(days before to, 500)`. So the close on `to`
  is 10, and from 500 days back it holds at 5, so it never reaches zero over the five-year
  backfill.
  - The latest close is still 10, so 007's `R$ 1.000,00` and `+R$ 45,10` hold. 006's test 26
    counts items, not rows, so it holds too. `MarketDataFakeProvidersTests` (2 items on
    Brapi, rows > 0) passes unchanged.
  - **Benchmarks are unchanged:** one 0.05 per series, dated `to`. IVVB11 is a price, so it
    gets the shape.
  - **Rejected:** *1% lower for each day before `to`* (the CP5 handoff's example). Over the
    five-year backfill that is `0.99^1826 ≈ 1e-8`, below the column's 8 places.
  - **Rejected:** *a close from the date alone.* The latest close could then not always be 10.
  - Cost: a close depends on the run that fetched it, because it is counted back from that
    run's `to`, and the sync never refetches a stored day.
- **008 · CP6 · test 36.** It is in `web/e2e/returns.spec.ts`:
  - It registers a **new ticker each run** (`RET` + 8 hex). A reused PETR4 carries the flat
    closes earlier runs stored, and a sync only fetches days after the latest stored close.
    The new ticker is backfilled in full by this run's sync.
  - It buys 100 at 9,71, with no fees, dated 30 days back in UTC, the API's calendar. The
    close that day is 9,71, 29 days before `to`.
  - It asserts:
    - the period reads "30 dias";
    - the TWR is `+2,99%` (`1000/971 - 1`) in the headline and in the benchmark table;
    - the XIRR is `+43,05% a.a.`, which is `(1000/971)^(365/30) - 1`, computed in Python
      `decimal`;
    - the chart is visible, with at least one line path;
    - "12 meses" refetches and clamps to the buy;
    - the asset's own page shows the same TWR and a chart.
  - Not asserted: the timing effect, which is 0 by construction (a buy at the close with no
    fees), and the benchmarks, which are sparse in the kept database.
  - A buy near UTC midnight could cross days between steps. That is accepted.
- **008 · CP6 · the sync chain.** `returns.spec.ts` is a fourth project, `returns`, which
  depends on `investments`. So the chain is `market-data` → `investments` → `returns`, and
  test 26's 202 is untouched. `syncMarketData`, with its 429 wait, moved from
  `investments.spec.ts` to `support.ts`, unchanged, so both specs share it.
- **008 · CP6 · test-first.**
  - The fakes' unit tests failed 5/6 before the feat. The sixth, the same range giving the
    same closes, passed already; it is a guard.
  - Test 36 was run against the old fakes and failed: "25/08/2026 a 24/09/2026 · 1 dias". It
    passed once the new fakes were in.
- **008 · CP6 · UI bugs found by the first real-browser run.** Each was fixed with a failing
  test first.
  - **"1 dias".** The period line said "1 dias" for a one-day period (the red run above). It
    now says "1 dia". Test in `ReturnsPage.test.tsx`.
  - **Repeated axis ticks.** Seen in a throwaway screenshot pass, not committed. The chart's
    Y axis formatted ticks with no decimals, so a 100–103 axis printed "102" twice (101,5 and
    102,5). `formatIndexTick` keeps up to two decimals. jsdom cannot lay out Recharts' tick
    text, so the test is on the formatter in `lib/rates.test.ts`.
  - **Seen, not changed:**
    - The tooltip's order is Recharts', not portfolio first.
    - When the first close comes after the buy, `period.from` is the buy's date while `days`
      counts from the first close. That is CP4's semantics. **For the human.**
  - Otherwise the pages rendered as the jsdom tests described: headline, chart, benchmark
    table, per-asset table and the asset's page.
- **008 · CP6 · E2E runs.** `verify-e2e.sh` passed twice back to back, 22/22 each, against
  the kept database.
- **008 · CP6 · counts.** .NET 759 → 765 (+6, the fakes). Web 142 → 145 (+1 singular, +2
  ticks). E2E 21 → 22.
- **008 · CP6 · diff sizes.** Every commit is under ~200 lines. The largest code commit is
  `eb25ee5`, test 36 and the config, at 95. The handoff, `f43811c`, is 215 lines. It is one
  document, left whole, as 007's was.

## 008 · handoff

Full handoff: `docs/handoffs/008.md`. **008 is complete in code; these steps need a human.**

- **006's and 007's pending steps first.** Real closes need brapi's token. A USD asset needs
  Twelve Data's key and the real USDBRL series.
- **Manual steps 1–5** against a real portfolio. **Step 3** (a spreadsheet's `XIRR()` to 4
  decimals) is the authority for XIRR, and **step 4** (a CDI calculator) for CDI.
- **Tests 15/16:** sign off option (a), or switch to (b). The spec needs a line either way
  (CP1, CP3).
- **Decisions to confirm or overturn:**
  - Test 13's reading of "a large late inflow" (CP3).
  - Multiple XIRR roots: Newton's root from 10%, or one root from bisection, or `null` (CP3).
  - IPCA + 6% with whole months and no pro-rata (CP1).
  - Annualised TWR hidden for 365 days or less, and the noisy timing effect over short
    periods (CP2, CP4, CP5).
  - The 260-point cap against weekly over 5 years (262) (CP4).
  - Assets with no row in the period left out (CP4).
  - The unread `Returns:Benchmarks:*:Label`: remove it or keep it (CP3, CP4).
  - No expense red: the timing effect's loss is orange, about 3:1 on white (CP5).
  - The p.p. difference on the period's returns, also past a year (CP5).
  - Per-asset "Sem dados" rows kept (CP5).
  - The asset returns route is not linked from `AssetPage` (CP5).
  - The period is not stored in the URL (CP5).
  - USD flows priced at their own date's rate (CP2, CP3).
  - FX reported as 0 after a −100% native return (CP2).
  - `period.from` against the base day when the first close comes after the buy (CP6).
- **Invented pt-BR copy** in 008 · CP4 and CP5, plus "dia", for a native review.
- **Starting spec 009.** Read `specs/009-ai-analysis.md`, `docs/handoffs/008.md` ("Starting
  spec 009") and the 008 entries above. The baseline is .NET 765, web 145, E2E 22.
  - Amend ADR-003 to its final form.
  - `AddAi` needs its SQL shown.
  - The config names differ from ARCHITECTURE's environment block.
  - USDBRL is 0.05 under the E2E fakes, so AI cost in E2E is off by about 100 times.
  - Decide whether the analysis sees 008's returns.

## 009 · checkpoint 1

- **009 · CP1 · the ADR check found no conflict to stop on.** I checked the spec against the
  ADRs, what 005–008 built, and CLAUDE.md's rules:
  - `AiUsage` and `AiAnalysis` implement `IUserOwned` and get their query filter from the loop
    in `AppDbContext`. A test asserts each one declares it. Both cascade from `AspNetUsers`.
  - 009 adds no shared data. It will read `Benchmarks` (USDBRL) for pricing, and that is
    already shared.
  - `CostBrl` is `decimal` in `numeric(10,4)`. A reflection scan covers `Domain.Ai`,
    `Application.Ai` and `Infrastructure.Ai`, so a `double` fails as soon as it is written.
  - `Domain/Ai/` holds POCOs and enums only, with no EF and no `HttpClient`. `IAiProvider` goes
    in `Application/Ai/` (spec) and its adapters in `Infrastructure/Ai/`. There is no
    Repository and no MediatR.
  - ADR-008 (a hard cut-off before every call), ADR-010 (`ai_enabled`, default false since
    002), ADR-012 (AI as the last rung) and ADR-015 (two real adapters, Anthropic and OpenAI)
    all hold as the spec is written.
  - Secrets: `Ai:Anthropic:ApiKey` and `Ai:OpenAi:ApiKey` stay empty in `appsettings.json`
    (CP2 adds them, with a test like 006's).
  - pt-BR: the prompt output, the 402/403/504 problem details and the disclosure text are all
    pt-BR. They land in CP4–CP6.
- **009 · CP1 · ADR-003 amended to its final form (99030b0, a docs-only commit).** The spec's
  "Correction to existing docs" asks for this. Scheduled jobs use `BackgroundService` + Cronos
  (006). On-demand jobs use `BackgroundService` + `Channel<T>`, with the row as durable state
  and `Pending` rows re-enqueued at startup after 5 minutes (009). Hangfire is not adopted.
  The stack table's jobs row now says the same. **For the human:** review the wording.
- **009 · CP1 · ARCHITECTURE's names now follow the spec (814a501).** Three changes:
  - The environment block's `AI_PROVIDER`, `AI_API_KEY` and `AI_MONTHLY_BUDGET_BRL` are now
    `Ai__Provider`, `Ai__MonthlyBudgetBrl`, `Ai__Anthropic__ApiKey`, `Ai__OpenAi__ApiKey` and
    `Ai__UsdBrl`, with a pointer to the spec for model ids and prices.
  - The cost-control block lists `AiUsage` and `AiAnalyses` with the spec's columns, and says
    that a failed call is recorded too.
  - §7's port sketch uses `CompleteAsync(AiRequest, CancellationToken)`.
- **009 · CP1 · for the human: ARCHITECTURE contradicts the spec in substance.** These are not
  ADRs and not naming, so I left them unedited:
  - **The doc says the monthly analysis is scheduled. The spec makes it on demand.** The doc
    says so in the stack table ("scheduled monthly analysis"), in §5's jobs table
    (`monthly | monthly-analysis`), in the AI module and in Phase 5. The spec has only
    `POST /api/ai/analyses` (decision 6) and no schedule, so none will be built. Regenerating
    replaces the month's row but spends again. Only the budget bounds that, not "1×/month".
  - §5 lists `categorize-batch` as an on-demand job. Decision 4 makes it synchronous in the
    request, with a 30 s timeout.
  - The AI module says "Cache by normalized description". Decision 5 says no cache is needed.
  - Principle 6 ("external data … never in a user request") is written about market data. The
    suggest call is an external call inside a request, by decision 4.
  - The environment block's market-data names (`BRAPI_TOKEN`, `COINGECKO_DEMO_KEY`,
    `TWELVEDATA_KEY`) are stale against 006's `MarketData:*` settings. That is not 009's to
    fix.
  - The repository tree shows `Domain/Analysis/`. 009's types live in `Domain/Ai/`, next to
    the spec's `Application/Ai/` and `Infrastructure/Ai/`.
- **009 · CP1 · NEEDS A HUMAN BEFORE CP2: which currency are the configured prices in?** The
  spec contradicts itself:
  - The configuration names `Ai:Pricing:<model>:InputPerMTokBrl` and `…OutputPerMTokBrl`,
    so the prices are in BRL.
  - The next sentence says "prices in BRL derived from USD list prices at the latest `USDBRL`
    benchmark, or the fallback". Test 4 says "cost uses the latest `USDBRL` when present,
    fallback otherwise". `Ai:UsdBrl` exists only for that conversion.

  With BRL prices, test 4 and `Ai:UsdBrl` have no purpose. With USD prices, the key names are
  wrong. The budget cap is the one non-optional control (ADR-008), and at 5.4 BRL per USD a
  wrong guess moves every cost by about 5×. CP1 does not depend on the answer: `CostBrl` is
  BRL either way. The options:
  - **(a), recommended:** rename the keys `InputPerMTokUsd` and `OutputPerMTokUsd`, hold the
    provider's USD list prices there, and convert at the latest USDBRL or `Ai:UsdBrl`. This
    keeps the text, test 4 and the fallback, and the spec's key names get a one-line fix.
  - **(b):** keep the spec's key names but read them as USD. This needs no spec edit, but the
    names mislead.
  - **(c):** read them as BRL, as named, with no FX. Test 4 and `Ai:UsdBrl` would be dropped
    from the spec.

  Under (a) or (b), E2E costs are about 100 times too small, because the fake USDBRL is 0.05.
  E2E 28 and 29 assert shapes, not amounts.
- **009 · CP1 · `StagedTransaction.CategorySource`, a column the spec does not list.** Staged
  rows do not record which rung chose their category. `CategoryId` holds the history pick, the
  sign default or the user's correction, all alike. Test 18 ("rows with history suggestions
  are not sent; default rows are") and the preview's "came from AI" marker both need to know.
  Recomputing at suggest time would misread a row the user set to "Outros" by hand, and it
  would lose the marker on reload. So the column went into `AddAi`, the spec's one migration:
  - The enum is `None = 0`, `History = 1`, `Default = 2`, `Ai = 3`, `User = 4`.
  - The column is `NOT NULL DEFAULT 0`, so rows staged before 009 read `None` and are never
    sent. At most one batch per user is open. **For the human:** re-upload that batch to use
    the AI on it.
  - Nothing writes it yet. CP4 makes staging write `History` or `Default`, the preview's
    category PATCH write `User`, and the suggest write `Ai`.
- **009 · CP1 · names, types and indexes.** Choices the spec leaves open:
  - Tables are `AiUsage` (singular, set explicitly, the spec's name) and `AiAnalyses`. The
    DbSets are `AiUsage` and `AiAnalyses`.
  - `Month` is a `string` in `char(7)`, as the spec says. It always holds exactly 7
    characters, so `char`'s padding never shows. There is no CHECK on its format, the same as
    007: the endpoint and job validate it (CP4/CP5).
  - Enums are stored as `int` with their values written down: `AiPurpose` is
    `Categorisation = 0`, `Analysis = 1`, and `AiAnalysisStatus` is `Pending = 0` to
    `Failed = 3`. There is no database default for `Status`.
  - The indexes are `(UserId, Month)` on `AiUsage` (spec, for the budget's sum) and unique
    `(UserId, Month)` on `AiAnalyses` (spec).
  - There is no index for the startup sweep (`Status = Pending AND CreatedAt < now - 5 min`).
    The table holds one row per user per month, and the sweep runs once per boot.
- **009 · CP1 · commit order.** I followed 007's order: entities unmapped (116aa3c), then two
  failing test commits (e590b82, 67acb5b), then the mapping. Against the unmapped model, 8 of
  the 10 new cases failed. The two type scans passed from the start, because they are guards.
  EF 10 refuses to migrate a model with pending changes. So the red test reached
  `CategorySource` by name through `Entry(row).Property<CategorySource>("CategorySource")`,
  and the property was added with its mapping. The mapping commit (8dafe5b) switched the test
  to the property.
- **009 · CP1 · migration SQL** (`dotnet ef migrations script AddInvestments AddAi`, also in
  `docs/migrations/009-AddAi.sql`):

  ```sql
  ALTER TABLE "StagedTransactions" ADD "CategorySource" integer NOT NULL DEFAULT 0;
  CREATE TABLE "AiAnalyses" (
      "Id" uuid NOT NULL, "UserId" uuid NOT NULL, "Month" char(7) NOT NULL,
      "Status" integer NOT NULL, "Content" text, "Error" character varying(500),
      "PromptVersion" character varying(20) NOT NULL,
      "CreatedAt" timestamp with time zone NOT NULL,
      "StartedAt" timestamp with time zone, "CompletedAt" timestamp with time zone,
      CONSTRAINT "PK_AiAnalyses" PRIMARY KEY ("Id"),
      CONSTRAINT "FK_AiAnalyses_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
  );
  CREATE TABLE "AiUsage" (
      "Id" uuid NOT NULL, "UserId" uuid NOT NULL, "Month" char(7) NOT NULL,
      "Purpose" integer NOT NULL, "Provider" character varying(20) NOT NULL,
      "Model" character varying(100) NOT NULL,
      "InputTokens" integer NOT NULL, "OutputTokens" integer NOT NULL,
      "CostBrl" numeric(10,4) NOT NULL, "Succeeded" boolean NOT NULL,
      "CreatedAt" timestamp with time zone NOT NULL,
      CONSTRAINT "PK_AiUsage" PRIMARY KEY ("Id"),
      CONSTRAINT "FK_AiUsage_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
  );
  CREATE UNIQUE INDEX "IX_AiAnalyses_UserId_Month" ON "AiAnalyses" ("UserId", "Month");
  CREATE INDEX "IX_AiUsage_UserId_Month" ON "AiUsage" ("UserId", "Month");
  ```
- **009 · CP1 · counts.** .NET went from 765 to 775: eight persistence cases in
  `AiPersistenceTests` and two in `AiTypesTests`. Web is 145 and E2E is 22. `verify-e2e.sh`
  applied `AddAi` to `financas_e2e`.
- **009 · CP1 · handoff to CP2** (settings, the fake, cost and budget; tests 1–4):
  - **Get the pricing answer above first.** Tests 3 and 4 depend on it.
  - **Which clock sets a usage row's `Month`?** A call at 22:00 on the 31st in Brazil falls in
    the next month in UTC. 006's container timezone is still pending a human. I suggest
    `TimeProvider` at a fixed UTC-3, as the brapi adapter does (006 · CP2), and saying so.
  - **Refuse the boot when a configured model has no `Ai:Pricing` entry.** A missing price
    would price every call at 0 and bypass the budget. A model id containing `:` cannot be a
    configuration key, so `Ai:Pricing:<model>` would not bind. The Anthropic and OpenAI ids
    in use today have no colon.
  - **The budget refuses at `spent >= budget`** (tests 1 and 2), checked before the call. One
    call can still overshoot by its own cost. That is ADR-008's "cut-off before every call",
    and the handoff should say so.
  - **The job has no signed-in user,** so the filter would hide every row. Open a scope with
    `ActingUser` set to the analysis's user (007's pattern) and keep the filter on. Do not use
    `IgnoreQueryFilters`.
  - **The fake goes behind a switch.** The spec does not name it. I suggest `Ai:FakeProvider`,
    refused outside Development like
    `MarketDataSetup.RefuseFakeProvidersOutsideDevelopment`. The E2E API turns it on the way
    it turns on `MarketData:FakeProviders`.
  - **Keys stay empty in `appsettings.json`, with a test** (006's pattern). The adapters (CP3)
    send them in headers, never in URLs.
  - **Does the analysis see 008's returns?** My default for CP5 is no. Decision 7 says
    "portfolio summary from 007", and the out-of-scope list rules out "any AI on the
    investments side beyond the summary line". `ReturnsQueries.PortfolioAsync` could add TWR
    and XIRR in one call. **Pending human.**

## 009 · checkpoint 2

- **009 · CP2 · decided by the user: AI prices are in USD.** The config keys are now
  `Ai:Pricing:<model>:InputPerMTokUsd` and `…OutputPerMTokUsd`, holding the providers' USD
  list prices. A call is converted to BRL at the latest `USDBRL` benchmark, or at `Ai:UsdBrl`
  when none is stored. Test 4 and the fallback stay as the spec wrote them. This was option (a)
  of CP1's question. The spec's key names got a one-line note in their own commit (2d85ecc).
  ARCHITECTURE names only `Ai:Pricing:<model>:*`, so it needed no edit.
- **009 · CP2 · decided by the user: the monthly analysis does not see 008's returns.** It
  gets only 007's portfolio summary (decision 7). This is for CP5. `ReturnsQueries` is not
  called.
- **009 · CP2 · the ADR check found no conflict.** The layers:
  - `Domain/Ai/AiCost` is pure: the cost, the rate choice and the month.
  - `Application/Ai/` holds `AiOptions`, `IAiProvider` (the spec's shape, verbatim),
    `BudgetGuard`, `AiPricing` and `AiGateway`, all on `AppDbContext` directly (ADR-016).
  - `Infrastructure/Ai/` holds `AiSetup` and `FakeAiProvider`.
  - There is no Repository and no MediatR. Every money value is `decimal`, and CP1's scan of
    the three AI namespaces still passes.
- **009 · CP2 · handoff defaults, taken as suggested:**
  - **A usage row's `Month` is read on a fixed UTC-3 clock** (`AiCost.MonthOf`), the same
    offset as the brapi adapter. A call at 23:30 on the 31st in Brazil counts against that
    month. The container timezone is still pending from 006. This clock ignores it.
  - **The fake's switch is `Ai:FakeProvider`.** It fails the boot outside Development
    (`AiSetup.RefuseFakeProviderOutsideDevelopment`), tested for Production and Staging as
    006 did. `verify-e2e.sh` now starts the E2E API with `Ai__FakeProvider=true`.
  - **A configured model with no price fails the boot**, with the key named
    (`AiOptions.RefuseInvalid`). The boot also refuses a price of zero or less, an unknown
    `Ai:Provider`, an empty model, a negative budget and an `Ai:UsdBrl` of zero or less. A
    budget of 0 is allowed: it refuses every call.
  - **Keys stay empty in `appsettings.json`,** with a test (006's pattern).
- **009 · CP2 · for the human: the model ids, prices and fallback rate in `appsettings.json`.**
  The spec leaves them to configuration, and I picked them:
  - Categorisation uses `claude-haiku-4-5` at USD 1 / 5 per million tokens (input / output).
  - Analysis uses `claude-opus-5` at USD 5 / 25.
  - The prices are Anthropic's list prices from my reference (cached June 2026), not checked
    against the console. A 40k-token analysis costs about R$ 1.2, in line with ARCHITECTURE's
    "~R$1 per run".
  - `Ai:UsdBrl` is `5.40`, an assumed rate.
  - No OpenAI model is configured. Switching to `openai` means setting both models and their
    prices. The boot checks that each model has a price, but not that the model belongs to
    the provider.
  - **Confirm all of these, or overwrite them in the environment.**
- **009 · CP2 · cost.** The formula is `(input × inUsd + output × outUsd) × rate / 1e6`,
  computed in full and rounded once:
  - It rounds to the column's 4 places, half away from zero (as PostgreSQL's `numeric`
    rounds), with the scale fixed at 4.
  - A call of 50 tokens at USD 1 costs 0.0001, not 0.
  - The rate is the `USDBRL` row with the latest `Date`, with no limit on its age. A value of
    zero or less falls back to `Ai:UsdBrl`.
  - Under the E2E fakes USDBRL is 0.05, so E2E costs are about 100 times too small, as noted
    in CP1.
- **009 · CP2 · the budget.** `BudgetGuard.Allows` refuses at `spent >= budget` (tests 1 and
  2).
  - `EnsureWithinBudgetAsync(userId, month)` keeps the spec's signature. It sums
    `CostBrl` through the query filter and the user id, failed calls included.
  - The check comes before the call, so a month can end over the budget by one call's cost.
    That is ADR-008's "cut-off before every call".
  - `AiBudgetExceededException` carries the amount spent and the budget, for CP4's `402`.
- **009 · CP2 · `AiGateway`, a name the spec does not use.** It is the "middleware" of
  decision 3 and ARCHITECTURE's cost-control block. It is the one path to `IAiProvider`, and
  each call runs these steps:
  1. **`ai_enabled`.** It throws `AiDisabledException` (CP4's `403`) before anything else.
     This goes beyond the spec, which gates at the endpoints. It also stops a job whose user
     turned AI off after `POST`.
  2. **The budget.**
  3. **The call.** The model comes from the purpose: `Ai:Categorisation:Model` or
     `Ai:Analysis:Model`.
  4. **One usage row, success or failure.**

  The user comes from `ICurrentUser`, not from a parameter. That is the request's user, or the
  user a job's scope acts for (`ActingUser`, 007's pattern, with the filters on). A caller
  therefore cannot price another user's month.
- **009 · CP2 · usage on failure (decision 3).** A provider signals failure with
  `AiProviderException(message, inputTokens, outputTokens)`.
  - When it reports tokens, those are recorded. `0` means nothing was billed.
  - When it reports nothing, `null`, or when any other exception escapes, the input is
    **estimated at 4 characters a token, rounded up**, and the output is 0. This is rough and
    it errs toward refusing. **For the human:** a failed call that billed nothing still costs
    its estimate unless the adapter says `0`.
  - The row is saved with no cancellation token: the tokens were spent even if the caller
    left. The original exception is then re-thrown unchanged.
  - The row is saved on the scope's `AppDbContext`, so **callers (CP4, CP5) must not hold
    unsaved changes** when they call.
  - `AiUsage.Provider` is `fake` for the fake, and otherwise the lower-cased `Ai:Provider`.
- **009 · CP2 · pt-BR.** The exceptions carry English messages for logs. None of them is
  rendered: CP4 writes the pt-BR problem details for 402, 403 and 504. **CP5 must write a
  pt-BR `AiAnalysis.Error`, not an exception message,** if the card shows it.
- **009 · CP2 · the fake.** `FakeAiProvider` returns one fixed pt-BR markdown answer
  (`## Resumo …`) for every purpose. Its tokens are 4 characters each, rounded up, so usage
  rows get plausible numbers. **CP4 and CP7 must shape it:** categorisation needs a
  `{ rowId: categoryId }` answer built from the request. Without the switch, resolving
  `IAiProvider` throws "Ai:Provider … has no adapter yet". No real call can happen in CP2, and
  `AiFakeProviderTests.Without_the_switch_the_fake_is_not_what_answers` asserts this. CP3
  changes it to assert the configured adapter.
- **009 · CP2 · tests.** Unit tests 1–4 are in `AiCostTests`. Their database halves (1, 2 and
  4) and decision 3 are in `AiGatewayTests`, which runs on the job path and swaps in a
  scripted provider. No test calls a real AI API.
  - `AiGatewayTests` uses a fresh database per test, because `Benchmarks` is shared and
    other tests store USDBRL.
  - It also uses two hosts, because a `FakeTimeProvider` host issues session cookies with
    expiry dates relative to its frozen clock, and the cookie container drops them once
    those dates are past. Users sign in on a real-clock host. The gateway runs on the
    fake-clock host.
- **009 · CP2 · a flaky 006 test (not fixed, out of scope).**
  `MarketDataFakeProvidersTests.With_the_switch_a_manual_sync_succeeds_on_fakes_and_can_run_again_at_once`
  failed once in the full suite and once in 5 runs on its own. The failing call gets a 429.
  `ManualMarketDataSync.RunAsync` writes the run's final status, then rebuilds snapshots,
  then releases the gate. The test sees "not Running" and posts again while the gate is still
  held. The rerun and the E2E runs were green. I queued a separate task for it.
- **009 · CP2 · commit sizes.** Two commits went over about 200 lines. 1445ad2, the settings
  tests, has 221. 7ad6332, the gateway, has 281 changed lines, because it includes moving
  the gateway tests onto two hosts. I split the rest: the port in 7231f36, and the gateway
  tests in 098002f and 30e6f85.
- **009 · CP2 · counts.** .NET went from 775 to 815 (+40), web stayed at 145 and E2E at 22.
  There is no migration and no schema change.
- **009 · CP2 · handoff to CP3.** CP3 builds the Anthropic and OpenAI adapters against
  hand-written fixtures:
  - **Where they go.** Put each adapter in `Infrastructure/Ai/` as a typed `HttpClient`.
    Replace the throwing delegate in `AiSetup.AddAi` with a choice by `Ai:Provider` when the
    fake is off. Flip `Without_the_switch_the_fake_is_not_what_answers` to assert that type.
  - **Keys (ADR-015, 006's pattern).** Send them in headers only: `x-api-key` for Anthropic,
    `Authorization: Bearer` for OpenAI. Keep them out of URLs and logs. An empty key should
    fail the call clearly, not the boot: AI is off by default, and dev and test hosts have no
    key.
  - **Base URLs.** They need settings the spec does not list (`Ai:Anthropic:BaseUrl`,
    `Ai:OpenAi:BaseUrl`), as 006 has.
  - **Timeouts.** Categorisation gets 30 s (decision 4). The analysis runs in the background,
    so a longer timeout is reasonable. CP4 needs to tell a timeout apart (its `504`), for
    example with an `AiProviderException` subtype or flag.
  - **Retries.** Every attempt spends tokens, but the gateway records one row per call. So
    either don't retry, or sum the tokens of all attempts into the exception or completion
    you return. Never retry a 4xx.
  - **Failures.** Map them to `AiProviderException`. Use `InputTokens = 0` where nothing was
    billed (401, 403, 400 before inference, 429). Leave it `null` when unknown (a timeout, a
    dropped connection). Read `usage` from an error body where the provider sends one.
  - **Fixtures.** They are hand-written, like 006's, so a human must check them against real
    responses. Record them for the Messages API (`usage.input_tokens` / `output_tokens`, the
    `content[]` text) and the chat completions shape (`usage.prompt_tokens` /
    `completion_tokens`). Cover a refusal or empty content, and a `max_tokens` stop.
  - **Wrong model or provider.** The boot does not check that a model belongs to
    `Ai:Provider`. Give a bad model id a clear error message.

## 009 · checkpoint 3

- **009 · CP3 · the ADR check found no conflict.** Both adapters sit in `Infrastructure/Ai/`
  and implement `IAiProvider` (ADR-015: two real implementations, config-selected, plus the
  fake). `Domain/` gains nothing. There is no Repository, no MediatR, no migration and no
  schema change. Token counts are `int` and no money is computed in the adapters, so CP1's
  `decimal` scan of the AI namespaces still passes.
- **009 · CP3 · the adapters are typed `HttpClient`s with no SDK package.** The spec does not
  fix this. This follows 006's providers. The network is blocked here, and the fixture tests
  stub `HttpMessageHandler`, which an SDK would hide. Adding an Anthropic or OpenAI SDK later
  would only change these two classes.
- **009 · CP3 · Anthropic, the Messages API** (`AnthropicAiProvider`):
  - `POST {Ai:Anthropic:BaseUrl}v1/messages`, headers `x-api-key` and
    `anthropic-version: 2023-06-01`. The version is a constant in the adapter, not a setting:
    it is the wire contract the code is written against.
  - The body is exactly `model`, `max_tokens`, `system` and one `user` message. A test pins
    the property list. `claude-opus-5` answers 400 to `temperature`, `top_p`, `top_k`,
    `budget_tokens` and an assistant prefill, so none is sent.
  - The text is the concatenation of the `text` blocks. `thinking` blocks, which adaptive
    thinking on `claude-opus-5` can add with empty text, are skipped.
  - Tokens are `usage.input_tokens` and `usage.output_tokens`. No prompt caching is used, so
    `cache_*_input_tokens` are ignored. **If caching is ever turned on, those must be priced
    too.**
  - **Verified as current by the user:** the ids `claude-haiku-4-5` (USD 1 / 5 per MTok) and
    `claude-opus-5` (USD 5 / 25) in `appsettings.json`, with no date suffixes. This settles
    the model-id and price half of CP2's "for the human" item. `Ai:UsdBrl` 5.40 is still an
    assumed rate.
- **009 · CP3 · OpenAI, Chat Completions** (`OpenAiProvider`):
  - `POST {Ai:OpenAi:BaseUrl}v1/chat/completions`, `Authorization: Bearer <key>`.
  - The body is `model`, a `system` and a `user` message, and `max_completion_tokens`. The
    reasoning models refuse the older `max_tokens`. No sampling parameters are sent.
  - The text is `choices[0].message.content`. Tokens are `usage.prompt_tokens` and
    `usage.completion_tokens`, and reasoning tokens are inside the latter.
  - The spec names only "OpenAi", with no endpoint and no model. The Responses API was not
    used, because the brief asked for the Chat Completions shape. **For the human:** no
    OpenAI model or price is configured (CP2), so switching needs both models and their
    `Ai:Pricing` entries.
- **009 · CP3 · what counts as an answer (the spec is silent).** Only these succeed:
  - Anthropic: `end_turn` or `stop_sequence` with some text.
  - OpenAI: `stop` with some content and no `refusal`.

  Everything else is an `AiProviderException` **carrying the billed tokens**, so the gateway
  records exactly what was spent:
  - Anthropic `refusal` (an HTTP 200) and OpenAI `refusal` or `content_filter`.
  - A truncated answer: Anthropic `max_tokens`, OpenAI `length`. **I chose failure over
    returning the partial text.** A cut-off JSON object cannot be parsed by CP4 anyway, and
    a cut-off analysis would be stored as `Completed`. **Pending human.**
  - Any other stop reason, and an empty answer.
- **009 · CP3 · failures, all mapped to CP2's `AiProviderException`** (`AiHttp`, shared):
  - A **4xx** was refused before inference, so it carries `InputTokens = 0`: Anthropic's 400,
    401, 402, 403, 404 and 429, and OpenAI's alike.
  - A **5xx** (Anthropic 500, 529; OpenAI 500, 503), a non-JSON error page, a 200 body that is
    not the documented shape, and a **dropped connection** carry `null`. The gateway then
    estimates the input at 4 characters a token. **For the human:** this errs towards the
    budget refusing. If Anthropic never bills a 529, it could be `0`.
  - The message holds the status and the body's `error.type` and `error.message`, truncated
    to 300 characters. Any occurrence of the key is replaced with `[redacted]`. A 404 also
    names the model id and `Ai:Provider`, the usual cause being the other provider's id.
  - **An empty key fails the call, not the boot,** with `InputTokens = 0` and no request
    sent. AI is off by default, and dev and test hosts have no key.
  - Cancellation is never mapped, so the gateway can tell its own timeout from the caller
    leaving.
  - Error bodies carry no `usage` for either provider, so none is read from them.
- **009 · CP3 · keys, in headers only (ADR-015, 006's pattern).** Tests assert that the key is
  in `x-api-key` or `Authorization` and not in the URL or the body. Both typed clients call
  `RedactLoggedHeaders` on their key header. Nothing in the adapters logs.
- **009 · CP3 · no retries.** The clients have no resilience handler. Every attempt spends
  tokens, and the gateway records one usage row per call. A failure is returned, never
  retried, so tokens are never summed across attempts. A caller that wants a retry makes a
  second gateway call, which is budgeted and recorded.
- **009 · CP3 · timeouts are the gateway's, per purpose.** `AiRequest` carries no purpose (the
  spec's shape, kept verbatim), so an adapter cannot pick its own timeout:
  - The settings are `Ai:Categorisation:TimeoutSeconds` (30, decision 4) and
    `Ai:Analysis:TimeoutSeconds`. **120 is my choice, pending human:** Opus 5's adaptive
    thinking and 400 words of output can take a while, and the job runs in the background.
    Both must be positive at boot.
  - `AiGateway` links the caller's token to a timer on its `TimeProvider`. Past the timeout,
    it records the call with the estimated input and throws `AiProviderTimeoutException`, a
    new subtype of `AiProviderException` (now unsealed). The caller's own cancellation stays
    an `OperationCanceledException` and is still recorded.
  - The typed clients' own `Timeout` is infinite, so `HttpClient` never cuts a call short
    with a cancellation that looks like the caller's.
- **009 · CP3 · the base URLs are new settings the spec does not list.** They are
  `Ai:Anthropic:BaseUrl` and `Ai:OpenAi:BaseUrl`, both in `appsettings.json`. The boot
  refuses one that is not an absolute http(s) URL ending in `/`. `AiKeyOptions` is renamed
  `AiProviderOptions`. **For the human:** ARCHITECTURE's environment block does not list
  them, or the timeouts. Overriding them is optional.
- **009 · CP3 · fixtures are hand-written.** They are in `api.tests/Fixtures/Ai/`, with a
  provenance table in its README:
  - Three Anthropic fixtures: `end_turn` with an empty thinking block and two text blocks,
    `max_tokens`, and `refusal`.
  - Three OpenAI fixtures: `stop`, `length`, and a refusal.

  Error and malformed bodies are inline in the tests. **Pending human: capture each fixture
  against the real API** (same file name, a short prompt), OpenAI's especially. Its error
  `type` values in the tests are my reading of the docs.
- **009 · CP3 · wiring.** Without `Ai:FakeProvider`, `Ai:Provider` (any case) resolves
  `AnthropicAiProvider` or `OpenAiProvider`. `AiFakeProviderTests` now asserts both, and
  that each client's timeout is infinite. The E2E API still runs on the fake. **No test
  touches the network.**
- **009 · CP3 · commit sizes.** Two test commits are over about 200 lines, and most of each is
  fixture JSON: f5a6246 (259) and b1e1ee4 (257). OpenAI's tests stayed in one commit
  because its error paths go through `AiHttp`, which was already green, so a second red
  commit was not possible. The rest are between 31 and 134.
- **009 · CP3 · counts.** .NET went from 815 to 869 (+54): 24 Anthropic cases, 19 OpenAI, 7
  settings, 3 gateway timeout cases, and the fake's "no adapter" test is now two cases. Web
  stayed at 145 and E2E at 22. The flaky 006 test (CP2) passed in this run.
- **009 · CP3 · handoff to CP4** (categorisation: tolerant parsing, `CategorySource`, the
  suggest endpoint, `PATCH /api/auth/me`; tests 5–10 and 15–20, likely as 4a and 4b):
  - **The status map for `POST /api/imports/{id}/suggest`:**
    - `AiDisabledException` → 403.
    - `AiBudgetExceededException` → 402.
    - `AiProviderTimeoutException` → 504. Catch it before its base class.
    - Any other `AiProviderException` is not in the spec. I suggest 502 with a pt-BR
      problem detail.

    All of these are pt-BR problem details, and none may echo the exception message, which
    is English and may name the model.
  - **The gateway saves a usage row on the scope's context,** even on failure. Write the
    suggestions after the call, or in a separate `SaveChanges`, and never hold unsaved
    staging edits across it (CP2's note).
  - **`MaxTokens` for categorisation.** A batch answer that hits the limit now fails (see
    above) instead of returning half a JSON object. Size it from the row count, and consider
    sending batches. Opus 5's thinking counts against `max_tokens` too, which matters for
    CP5's analysis more than for Haiku.
  - **Tolerant parsing (tests 5–10)** should treat the adapter's text as untrusted: code
    fences, prose around the JSON, unknown ids. The adapters only guarantee non-empty text
    on success.
  - **The fake must be shaped for tests 18–20 and E2E 28.** It must answer
    `{ rowId: categoryId }` built from the request's rows. Its current fixed markdown answer
    is right for "garbage → `suggested: 0`" (test 20).
  - **`PATCH /api/auth/me { aiEnabled }`** is the only way to turn AI on from the app. The
    gateway already refuses a user with it off.

## 009 · checkpoint 4a

- **009 · CP4a · the ADR check found no conflict.** The layers:
  - `Domain/Import/AiCategorisation` is pure: the parser, the request, the system prompt,
    the batch size and the answer ceiling. It uses `System.Text.Json` only, with no EF Core
    and no `HttpClient`.
  - `Application/Ai/CategorisationCascade` (the spec's name) runs rung 3 on `AppDbContext` and
    `AiGateway` directly (ADR-016). There is no Repository and no MediatR.
  - There is no migration: `CategorySource` came with `AddAi` in CP1. No money is computed.
    Amounts stay `decimal` and are only compared with zero. CP1's `decimal` scan still
    passes.
  - ADR-012 holds: the AI is the last rung. It only sees rows the sign default filed, and it
    only suggests. Rule 3 is applied to every pair it answers, and the row stays staged until
    the user commits.
- **009 · CP4a · tolerant parsing (unit tests 5–10, `AiCategorisationParseTests`, 24 cases).**
  The answer is untrusted text, and nothing in it throws. It is read in this order:
  1. The text as it is.
  2. The inside of the first code fence.
  3. The span from the first `{` to the last `}`, for prose around the object.

  The first reading that is a JSON **object** wins. Trailing commas and comments are
  tolerated, and ids are read in any case. A pair is kept only when:
  - the row was sent in this batch,
  - the value is a string holding the id of a category the user owns, and
  - its kind passes `TransactionRules.ValidateSign` (rule 3).

  Everything else is dropped pair by pair: a non-string value, an array, a wrapped object
  such as `{ "rows": { … } }`, or a truncated object.

  Spec-silent choices:
  - **When a row is answered twice, its first answer is kept.**
  - **A Transfer category is kept on either sign.** This is rule 3 as amended in 005. The
    spec says "ignore mismatched kind against the row's sign", and I read that through rule
    3, the same way `CategorySuggester` keeps a remembered transfer. **Pending human** if AI
    transfers should be refused.
- **009 · CP4a · what is sent (spec-silent details).** The user message is one JSON document:
  - `categories`: `{ id, name, kind }`. Every category the user has is listed, the defaults
    and Transfer included. A child is named `Parent > Child`. The list is sorted by name,
    ordinal.
  - `rows`: `{ rowId, description, kind }`. The spec lists `rowId` and `description`.
    **`kind` is my addition** (`Expense` for a debit, `Income` for a credit), so the model
    knows which categories rule 3 will accept. **No amount, date, account or raw text is
    sent.**
  - **The description is `NormalizedDescription`, not the raw text.** It is upper-cased,
    without accents, and with digit runs removed, so CPFs, CNPJs, card and account numbers
    and dates never leave. Names in PIX descriptions still do. **CP6's disclosure text must
    say this.**
  - The JSON keeps accents unescaped, which costs fewer tokens.
  - **Ids are the real GUIDs**, as the spec's `{ rowId: categoryId }` reads. **For the
    human:** short aliases (`r1`, `c3`), mapped back on parse, would cut output tokens about
    three times and remove GUID copying errors. With aliases, test 8's "not owned" case
    would become "unknown alias".
- **009 · CP4a · the system prompt is a constant, `AiCategorisation.System`, in English.**
  Decision 8's prompt files are about the monthly analysis. This prompt is short, is not
  shown to anyone, and a unit test pins its key phrases. It asks for a bare
  `{ rowId: categoryId }` object with no prose and no fence. It asks the model to leave out
  rows that fit no better than a generic category. **For the human:** review it like code,
  as decision 8 asks of the analysis prompt. Moving it to
  `Infrastructure/Ai/Prompts/categorisation.md` is easy if you prefer.
- **009 · CP4a · `MaxTokens` and batches (the CP3 handoff).**
  - **Batch size: 40 rows** (`AiCategorisation.BatchSize`). Each batch is one gateway call,
    so each is ai_enabled-checked, budgeted, timed out at 30 s and recorded on its own.
  - **The answer ceiling is `256 + 64 × rows`**, which is 2816 for a full batch. A pair is two
    GUIDs and punctuation, about 50 tokens. The ceiling errs high because an answer cut at
    the ceiling fails in the adapter (CP3), and only the tokens used are billed.
    `MaxTokensFor` refuses 0 rows and more than one batch.
  - **Batches run in sequence, in the one request.** 200 default rows make 5 calls. At worst
    that is 5 × 30 s, which is longer than decision 4's 30 s promise per call. **Pending
    human:** check Caddy's and the browser's timeouts against a real statement (manual step
    3), and lower the batch size or send batches in parallel if needed.
  - **Each batch's suggestions are saved before the next call.** The gateway saves its usage
    row on the same context (CP2's note). A later batch that fails (402, 502 or 504) keeps
    what earlier ones suggested. Asking again sends only the rows still on `Default`.
- **009 · CP4a · the counts (spec-silent).** `suggested` is the number of rows whose category
  the AI changed. Those rows become `CategorySource.Ai`. An answer equal to the row's
  current default is not a suggestion: the row stays `Default` and counts as skipped.
  `skipped` is the rows sent minus `suggested`. Rows not sent (History, User, Ai, None) are
  in neither count.
- **009 · CP4a · the cascade checks `ai_enabled` first**, before the batch lookup and even
  when there is nothing to send. So a disabled user always gets the 403 (test 15), whatever
  the batch holds. The gateway checks it again for each call.
- **009 · CP4a · staging writes `CategorySource`** (`ImportStaging`):
  - `History` when the chosen category is the remembered one. This includes a remembered
    "Outros": history chose it, so it is never sent.
  - `Default` when the sign default was chosen.
  - `None` when nothing was chosen. Invalid rows are `None` too, since the cascade never
    touches them.

  Rows staged before this change read `None` (CP1) and are never sent.
- **009 · CP4a · tests.**
  - Unit: `AiCategorisationParseTests` (24 cases, tests 5–10 and the tolerances) and
    `AiCategorisationRequestTests` (7).
  - Integration: `AiCategorisationTests` (6) on the job path with a scripted provider. It
    covers the staging sources and the application halves of tests 18–20. It also covers
    batching with a failure on the second batch and the retry that sends only the rest, AI
    off with nothing to send, another user's batch (NotFound, through the filter), and a
    committed batch (WrongStatus).
- **009 · CP4a · commit sizes.** 10fbe6c (the rung-3 integration tests) is 242 lines. Most of
  it is the test fixture, a batch with history and a child category. The rest are between 6
  and 153.
- **009 · CP4a · counts.** .NET went from 869 to 906 (+37: 24 + 7 unit, 6 integration). I
  counted that from the added cases. The full scripts ran once, at the end of 4b, below.
  Web and E2E are unchanged.

## 009 · checkpoint 4b

- **009 · CP4b · the ADR check found no conflict.** The endpoints stay thin over
  `CategorisationCascade`. The status map lives once, in `Problems.Ai`. ADR-008 (the cut-off
  before every call, now per batch) and ADR-010 (the user's own flag) hold. There is no
  migration.
- **009 · CP4b · `PATCH /api/auth/me { aiEnabled }`.**
  - It saves through `UserManager.UpdateAsync` and only ever touches the signed-in user.
  - **It answers with the whole of `GET /api/auth/me`** (`id`, `email`, `displayName`,
    `aiEnabled`), which is a superset of the spec's `{ aiEnabled }`, so the web can reuse
    one shape.
  - A body without `aiEnabled` is a 400 naming the field: "Informe se a IA deve ficar ligada
    ou desligada."
  - Signed out, it is a 401. It needs the Origin header, like every other mutating route.
- **009 · CP4b · the preview's row shows its rung.** `RowResponse` gains `categorySource`
  (`None`, `History`, `Default`, `Ai`, `User`, as enum strings). Choosing a category in the
  row PATCH now writes `User`, even when it is the same category. CP6 maps the values in
  `labels.ts` and shows the "came from AI" marker for `Ai`.
- **009 · CP4b · `POST /api/imports/{id}/suggest`** (integration tests 15–20,
  `AiSuggestEndpointTests`, 7 cases):
  - It answers `200 { suggested, skipped }`.
  - **403** when AI is off (checked before the batch lookup, so it says nothing about the
    id).
  - **402** when over the budget, with no provider call.
  - **504** on `AiProviderTimeoutException`, caught before its base class.
  - **502** on any other `AiProviderException`. This status is not in the spec (CP3's
    suggestion).
  - **404** for another user's batch (query filter).
  - **409** for a committed one.

  None of the bodies echoes the exception's message. A test asserts this with a message
  holding "secret". The reason goes to a warning log instead.
- **009 · CP4b · for the human: invented pt-BR copy** (`Problems.Ai`):
  - 403, "IA desligada": "A IA está desligada na sua conta. Ligue-a nas configurações para
    usar este recurso."
  - 402, "Limite de IA atingido": "Você atingiu o limite mensal de gastos com IA. O limite
    renova no próximo mês." It shows no amounts.
  - 504, "A IA demorou demais": "O serviço de IA não respondeu a tempo. Tente novamente em
    instantes."
  - 502, "Falha no serviço de IA": "O serviço de IA não conseguiu responder agora. Tente
    novamente mais tarde."
  - 409 reuses the preview's "Este lote já foi confirmado e não pode mais ser editado."
- **009 · CP4b · there is no rate limit on suggest.** The spec names none, and the budget is
  the bound (ADR-008). Each click re-sends only the rows still on `Default`. **Pending
  human** if a per-minute limit like 006's is wanted.
- **009 · CP4b · the fake, shaped for tests 18–20 and E2E 28.** `FakeAiProvider` recognises
  a categorisation request by rung 3's system prompt. For each row it answers the first
  category of the row's kind, in the order sent, that is not a sign default. With the
  seeded categories, debits get "Alimentação" and credits get "Salário". A row whose
  description holds `FakeAiProvider.GarbageMarker` (`GARBAGE`) makes the whole answer the
  fixed markdown, which is test 20's garbage. Every other request still gets the fixed
  markdown, and CP7 shapes it for the analysis. The fake is still refused outside
  Development.
- **009 · CP4b · test 17 over HTTP.** A provider failure reporting 1234 input tokens gives a
  502 and one usage row with `Succeeded = false` and 1234 tokens. The timeout case gives a
  504 and a failed row. Test 15's analysis half, and tests 13 and 14 over HTTP, are CP5's.
- **009 · CP4b · the flaky 006 test** (CP2,
  `MarketDataFakeProvidersTests.With_the_switch_a_manual_sync_succeeds_on_fakes_and_can_run_again_at_once`)
  failed again in the first `verify.sh` run with the same 429. The rerun was green, with no
  change. The separate task queued in CP2 still stands.
- **009 · CP4b · commit sizes.** 113a76f (the integration tests 15–20) is 216 lines. The rest
  are between 35 and 82.
- **009 · CP4b · counts.** .NET went from 906 to 919 (+13: 4 toggle and preview, 2 fake, 7
  suggest), so CP4 as a whole went from 869 to 919. Web stayed at 145 and E2E at 22. The
  web does not yet read `categorySource` or call suggest (CP6).
- **009 · CP4b · handoff to CP5** (the analysis: `AnalysisInputBuilder`, the versioned
  prompt, `AnalysisJob` on a `Channel<T>` with the startup sweep, `/api/ai/analyses` and
  `/api/ai/usage`; tests 11–14 and 21–24, likely as 5a and 5b):
  - **Reuse `Problems.IsAiFailure` / `Problems.Ai`** for `POST /api/ai/analyses`. Check
    `ai_enabled` (403) and the budget (402, `BudgetGuard.EnsureWithinBudgetAsync`) **at POST
    time, before creating the `Pending` row**. Otherwise a disabled or broke user gets a 202
    and a row that fails later. The job goes through the gateway, which checks both again.
    Test 15's analysis half asserts no usage row.
  - **The job's failures go to `AiAnalysis.Error` in pt-BR**, never the exception message
    (CP2). The four texts in `Problems.Ai` could become shared strings.
  - **Size `MaxTokens` for the analysis.** Opus 5's adaptive thinking counts against it, and
    a `max_tokens` stop fails (CP3). 400 words is about 700 tokens of output, so leave room
    for the thinking: something like 8000, as a constant or a setting.
  - **Run the job in a scope acting for the user** (`ActingUser`, 007's pattern), with the
    filters on and no `IgnoreQueryFilters`. The gateway saves usage on that scope's context,
    so save the `Running` status before the call.
  - **The fake answers any non-categorisation request with its fixed `## Resumo` markdown.**
    That is enough for tests 21–23. A test for the job's failure (22) can swap in a scripted
    provider, as `AiSuggestEndpointTests` does.
  - **For CP6 (web):**
    - `PATCH /api/auth/me` and `GET /api/ai/usage` back the `/settings` toggle.
    - The suggest button posts to `/api/imports/{id}/suggest`, reloads the rows, and renders
    a problem's `detail` verbatim.
    - `categorySource` labels go in `labels.ts`.
    - **The disclosure must say** that categorisation sends normalized descriptions, each
    row's debit or credit and the category names, and no amounts, dates or accounts.

## 009 · checkpoint 5a

- **009 · CP5a · the ADR check found no conflict.** The layers:
  - `Application/Ai/AnalysisInputBuilder` (the spec's name) is a pure static `Build`, unit
    tested. `Application/Ai/AnalysisInputQueries` loads its aggregates on `AppDbContext` and
    005's and 007's query classes directly (ADR-016).
  - `Application/Ai/MonthlyAnalysis` is one run of the job, and `Application/Ai/AnalysisQueue`
    wraps the `Channel<T>`. `Infrastructure/Jobs/AnalysisJob` is the hosted
    `BackgroundService`. This is ADR-003's final form: the channel wakes the job, and the row
    is the state.
  - `Infrastructure/Ai/MonthlyAnalysisPrompt` reads the prompt file. `MonthlyAnalysis`
    (Application) uses it, as Application already uses `Infrastructure.AppDbContext`.
  - There is no Repository and no MediatR. `Domain/` gains nothing. No migration and no
    schema change. Every amount is `decimal`, and CP1's scan of the AI namespaces still
    passes.
  - The one stretch of ADR-003's text is the sweep's schedule, below.
- **009 · CP5a · the analysis input (decision 7; the shape is spec-silent, pending human).**
  One JSON document, the prompt's user message. Test 11 pins it whole:
  - `month`, `currency` (`BRL`).
  - `months`: the analysed month and the two before, oldest first, each `income`, `expense`
    and `net`. These are 005's `MonthlyAsync`: BRL only, with Transfers excluded.
  - `monthOverMonth`: `income`, `expense` and `net`, each `previous`, `current`, `change`
    and `changePercent`. It compares the analysed month with the one before.
  - `categories`: top-level categories rolled up as the dashboard does (005's
    `ByCategoryAsync`), expense and income. Each has `amounts` per month (0.00 where empty),
    `change` and `changePercent`. Expense comes first, then by the analysed month's amount,
    then by name.
  - `topMerchants`: at most 20, `name`, `spent` and `transactions`. Only BRL expense in the
    analysed month counts. The name is the row's `NormalizedDescription`, or the description
    normalized on the fly for a row entered by hand. **Income is never listed**, because its
    descriptions name payers.
  - `accounts`: each account's `name`, `type`, `currency` and `balance`. `balanceTotalBrl`
    is the BRL total.
  - `investments`: 007's summary only (`valueBrl`, `costBrl`, `unrealisedBrl`), as the user
    decided in CP2.

  Formatting choices:
  - **Expense is a positive amount spent**, so the model never has to reason about signs.
  - Money always has two places (`0.00`, never `0`).
  - A percentage has one place, rounded half away from zero, taken of the previous value's
    absolute size. **It is `null` when the previous value is zero**, rather than an
    infinity the model would print.
  - **Balances and the portfolio are current, not as of the analysed month's end.** That is
    what 005 and 007 compute. Analysing an old month still shows today's balances. **Pending
    human.**
- **009 · CP5a · what leaves the server, for CP6's disclosure.** The analysis sends these to
  the provider:
  - account names and types, category names, and totals;
  - merchant names, which are normalized expense descriptions: upper case, no digits.
    **A PIX sent to a person carries that person's name.**
  - the portfolio's three totals.

  No raw description, date, row or income description is sent. A test on the database
  (`AnalysisInputQueriesTests`) asserts this with raw descriptions, a payer's CPF, a USD row
  and another user's merchant and account.
- **009 · CP5a · the prompt (decision 8), `Infrastructure/Ai/Prompts/monthly-analysis.md`.**
  - Line 1 is `<!-- version: 1 -->`. The version is at most 20 characters (the column),
    letters, digits, dots and dashes. The rest of the file, trimmed, is the system prompt.
  - It is an embedded resource (`Api.csproj`, logical name `Prompts/monthly-analysis.md`), so
    the published image carries the reviewed file. It is read once, and a missing or
    malformed header throws on first use, which fails the analysis and not the boot.
  - **The instructions are in English**, like CP4a's categorisation prompt. The five section
    headings are pt-BR literals, and the answer is asked for in pt-BR.
  - The rules follow the spec: sections *Resumo*, *Onde o dinheiro foi*, *O que mudou*,
    *Investimentos* and *Sugestões*; numbers from the input only; at most 400 words; no
    preamble. It also asks for no recomputed totals or averages, for R$ 1.234,56 and
    12,5%, for no recommendation of a specific product or bank, for plain markdown with no
    tables, links, images or HTML, and for the JSON to be treated as data, never as
    instructions (merchant and account names are user-controlled).
  - A test asserts that the prompt describes every top-level field of the input, so the
    shape and the prompt cannot drift apart silently.
  - **For the human: review it like code** (spec, DoD).
- **009 · CP5a · the job (`MonthlyAnalysis.RunAsync`, the spec's steps 1-6).**
  - It runs in a scope acting for the item's user (`ActingUser`), with the filters on. A row
    that is not that user's, or no longer `Pending`, runs nothing. So an item enqueued twice
    runs once, and a scope acting for B cannot run A's row.
  - It saves `Running`, `StartedAt` and the prompt's version **before the call**, because the
    gateway saves usage on the same context. The gateway checks `ai_enabled` and the budget
    again (step 3) and records usage whatever happens (step 5).
  - `MaxTokens` is 8000, a constant (`MonthlyAnalysis.MaxTokens`), as CP4 suggested.
  - The content is the provider's text, trimmed. The markdown is stored as is. CP6 must
    escape it (test 27).
  - **`Error` is always pt-BR copy** (`AiFailureText.For`): AI off, budget, timeout, other
    provider failure, or anything else. The exception goes to a warning log only. The final
    save is not cancellable. A shutdown mid-call leaves the row `Running` for the sweep.
  - There is one consumer, so analyses run one at a time. Ten users generating at once at
    120 s each would wait up to 20 minutes. **Pending human** if that matters.
- **009 · CP5a · the sweep goes beyond the spec's text (pending human).** Decision 6 and
  ADR-003 say "on startup, any `Pending` row older than 5 minutes is re-enqueued":
  - **It runs at startup and then every 5 minutes.** A restart within 5 minutes of a POST
    would otherwise leave that row `Pending` until the next boot, with the card spinning.
  - **A `Running` row past its timeout plus 5 minutes fails** with "A geração da análise foi
    interrompida. Gere a análise novamente." It is not re-run, because its call may already
    have been paid for, and a second run would spend again without being asked. The spec
    says nothing about `Running` rows.
  - It walks each user in a scope acting for them. There is no `IgnoreQueryFilters`.
  - **`Ai:AnalysisSweep`** (default `true`, in `appsettings.json`) switches it off. The test
    hosts turn it off, as they do `MarketData:ScheduledSync`, because they share one
    database. Test 24 turns it on, on a fresh database. The consumer stays on everywhere: it
    only runs what its own host enqueues. The E2E API keeps the sweep on, on its own
    database.
- **009 · CP5a · the fake** (`FakeAiProvider`) recognises the analysis prompt. It answers the
  five sections, quoting the month and the month's expense from the input, and is
  deterministic. Its text is test copy, only ever seen with `Ai:FakeProvider` (Development).
- **009 · CP5a · for the human: invented pt-BR copy** (`AiFailureText`, shared with
  `Problems.Ai` since 2db349b):
  - "Não foi possível gerar a análise. Tente novamente mais tarde." for a failure that is
    not the provider's.
  - "A geração da análise foi interrompida. Gere a análise novamente." for the sweep.
  - The four problem texts from CP4b are reused as the job's errors.
- **009 · CP5a · tests.**
  - Test 11: `AnalysisInputBuilderTests` (unit, the shape pinned whole) and
    `AnalysisInputQueriesTests` (no raw description, on the database).
  - Test 12: `AnalysisInputBuilderTests`.
  - Tests 13 and 14, application halves: B's input holds nothing of A's, and a scope acting
    for B does not run A's row.
  - Tests 21 and 22, job halves: `AnalysisJobTests`, on a scripted provider.
  - Test 24: the sweep, on a fresh database.

  The HTTP halves come in CP5b. No test calls a real AI API.
- **009 · CP5a · commit sizes.** Three commits go over about 200 lines:
  - a333375 (unit tests 11-12) is 283, most of it the pinned JSON document.
  - 2233749 (the job tests) is 286.
  - 5a64a8d (the job) is 228.

  The rest are between 8 and 118.
- **009 · CP5a · counts.** .NET went from 919 to 943 (+24): 3 builder, 2 input queries, 9
  prompt, 9 job and 1 fake. All ran. Web and E2E were not rerun at 5a (there is no web
  change).

## 009 · checkpoint 5b

- **009 · CP5b · the ADR check found no conflict.** The endpoints (`Endpoints/AiEndpoints.cs`)
  stay thin over `Application/Ai/AnalysisCommands`, and the reads go straight to
  `AppDbContext` through the query filter (ADR-016, ADR-007). ADR-008 and ADR-010 are checked
  at `POST`, before any row is written, and again by the gateway in the job. There is no
  migration: `dotnet ef migrations has-pending-model-changes` reports none.
- **009 · CP5b · `POST /api/ai/analyses { month }`.**
  - **400** comes first, with a problem naming `month`. The month is missing or not
    `YYYY-MM`: "Informe o mês no formato AAAA-MM." Or it is after the current month (UTC-3,
    as the usage rows): "Não é possível analisar um mês que ainda não começou." **The
    current month is allowed**, although it is partial.
  - **403** (AI off) and **402** (budget) come next, through `Problems.Ai`, before any row
    or call. The budget is the **current** month's spend, since the call happens now,
    whatever month is analysed.
  - Then **202 `{ analysisId }`**, with `Location: /api/ai/analyses/{id}`.
  - **409 while the month's row is `Pending` or `Running`** is not in the spec: "A análise
    deste mês ainda está sendo gerada. Aguarde ela terminar para gerar outra." A second
    click would otherwise pay twice for one month. A concurrent first request that loses the
    unique index is a 409 too.
  - **Regenerating (test 23)** resets the settled row in place. It keeps the row's id, sets
    `Pending`, a new `CreatedAt` and the current prompt version, and clears `Content`,
    `Error`, `StartedAt` and `CompletedAt`. **The old content is gone as soon as regenerate
    is pressed**, not when the new one completes. **Pending human.**
  - There is no rate limit. The budget bounds it (ADR-008), as with suggest.
  - Spec-silent: the 400 is checked before the 403, where suggest checks the 403 first. No
    id is involved here, so nothing leaks either way.
- **009 · CP5b · the reads.**
  - `GET /api/ai/analyses/{id}` answers 200, or **404 for another user's id** (filter,
    test 14).
  - `GET /api/ai/analyses?month=` answers the user's analyses, newest month first. `month`
    is optional, and filters to that month (zero or one row). **Each item carries its
    `content`**, the same shape as by id.
  - `GET /api/ai/usage?month=` answers `{ month, spentBrl, budgetBrl, calls }`. `month` is
    in the answer as well as the spec's three fields. It defaults to the current UTC-3
    month.
    - `calls` counts every usage row of the month, failed or not, categorisation and
      analysis alike.
    - `spentBrl` is `BudgetGuard.SpentAsync`, the figure the cut-off uses.
    - **It answers with AI off,** so `/settings` can show the spend beside the toggle.
      Test 15's "403 on both endpoints" is read as suggest and `POST` analyses.
  - A malformed `month` in either query is a 400 naming `month`.
- **009 · CP5b · for the human: invented pt-BR copy.** These are the two 400 texts and the
  409 text above.
- **009 · CP5b · tests** (`AiAnalysisEndpointTests`, 14 cases, on the fake or a scripted
  provider):
  - Test 21: 202, `Location`, and `Completed` with the fake's content. The `Pending` row is
    observed deterministically, held behind a running one: the job has one consumer.
  - Test 22: `Failed` with `AiFailureText.ProviderFailed` and the call counted.
  - Test 23: the replace, with one row remaining.
  - Test 15's analysis half, and 16's: a 403 or 402 with no row, no call and no new usage.
  - Tests 13 and 14: another user's list is empty, their usage is 0, and the id is a 404.
  - Also: usage sums and counts, the list filter, the 409, and the 400s.

  With 5a's tests, every analysis test in the spec (11–15 and 21–24) is present. No test
  calls a real AI API. The two job-driven classes ran green three times in a row.
- **009 · CP5b · commit sizes.** 8284dff (the endpoint tests) is 291 lines. c54d9ef (the
  endpoints) is 188.
- **009 · CP5b · counts.** .NET went from 943 to 957 (+14), so CP5 as a whole went from 919 to
  957. Web stayed at 145 and E2E at 22. `verify.sh` and `verify-e2e.sh` were both green on
  the first run. The flaky 006 test did not fail this time. The E2E API ran with the sweep
  on, on its own database, and logged no error from the job.
- **009 · CP5b · handoff to CP6** (web: the `/settings` toggle, disclosure and spend; "Sugerir
  com IA"; the "Análise do mês" card; tests 25–27). The wire shapes:
  - **`POST /api/ai/analyses`**. The body is `{ "month": "YYYY-MM" }`, sent with the Origin
    header like every write. The answers:
    - `202 { "analysisId": "<uuid>" }`, with `Location: /api/ai/analyses/<uuid>`.
    - `400`: a validation problem, `errors.month[0]` in pt-BR.
    - `403` or `402`: a problem whose `title` and `detail` are pt-BR.
    - `409`: a problem, the month already in progress.
    - `401` when signed out.

    Render any problem's `detail` verbatim.
  - **`GET /api/ai/analyses/{id}`**. It answers `200` with:
    ```
    { "id": "<uuid>", "month": "YYYY-MM",
      "status": "Pending" | "Running" | "Completed" | "Failed",
      "content": string | null,   // markdown from the provider, only when Completed: untrusted, escape it (test 27)
      "error": string | null,     // pt-BR, only when Failed: render verbatim
      "promptVersion": string,
      "createdAt": ISO, "startedAt": ISO | null, "completedAt": ISO | null }
    ```
    It answers `404` for an unknown or another user's id.
  - **`GET /api/ai/analyses?month=YYYY-MM`** answers `200 [ …same shape… ]`, newest first. A
    month gives zero or one item. A bad month is a `400`.
  - **`GET /api/ai/usage?month=YYYY-MM`** answers
    `200 { "month": "YYYY-MM", "spentBrl": number, "budgetBrl": number, "calls": number }`.
    `month` is optional and defaults to the current month. `spentBrl` has up to 4 places, so
    round for display. It answers even with AI off.
  - **The card** reads the dashboard's month and calls `GET /api/ai/analyses?month=`:
    - With no item, it offers "Gerar análise". The button is disabled, with a reason, when
      `me.aiEnabled` is false.
    - For `Pending` or `Running`, it shows a spinner and polls `GET /{id}` every 3 s. It
      stops on `Completed` or `Failed`.
    - For `Completed`, it renders `content` as escaped markdown.
    - For `Failed`, it shows `error`.
    - "Regenerar" is the same `POST` with the same month. It answers `202` with the **same
      id**, and the row goes back to `Pending` with its content cleared.
    - A future month is refused with a `400`. Hide the button for one.
  - **Labels.** The status values are identifiers. Map them in `labels.ts` if they are shown.
  - **E2E 29 on the fake.** The content starts with `## Resumo`, holds all five headings and
    quotes the month and its expense from the input.
  - **The disclosure (decision 10)** must say that categorisation sends normalized
    descriptions, each row's debit or credit and the category names (CP4). The analysis
    sends account names, types and balances, category names and totals for three months,
    the top 20 expense merchants as normalized descriptions (a PIX to a person names them),
    and the portfolio's three totals. Neither sends a raw description, a date or an income
    description, and categorisation sends no amounts. The provider is the configured one
    (Anthropic by default).

## 009 · checkpoint 6

- **009 · CP6 · the ADR check found no conflict.** This is web only, with no API change and no
  migration. There is **no new dependency**: the markdown is rendered by a small renderer of
  our own (below). The session stays in its cookie (ADR-009), and nothing is kept in browser
  storage. The labels for `CategorySource` and `AnalysisStatus` are in `labels.ts`.
- **009 · CP6 · the markdown (test 27), `components/ai/Markdown.tsx`.** It builds React
  elements and never builds HTML: no `dangerouslySetInnerHTML`, and no HTML is parsed. Every
  piece of the source is a text child, and React escapes text, so a `<script>` shows as
  characters. It understands only what the prompt asks for: `#` headings, paragraphs, `-`/`*`
  and `1.` lists, `**bold**`, `*italic*` and `` `code` ``. Links, images, tables and HTML stay
  literal text, so no URL from the provider becomes an `href` or a `src`. Headings go down two
  levels (`##` is an `h4`) and sit under the card's `h3`.
- **009 · CP6 · the card polls the list, not the id (spec-silent; the CP5b handoff said by
  id).** It reads `GET /api/ai/analyses?month=` and polls that same read every 3 s while the
  row is `Pending` or `Running`. The interval comes from each answer, so it stops by itself on
  `Completed` or `Failed`. The read has the same shape as by id, and it still finds the row
  after a regenerate or a POST from another tab. A 409 on POST refetches too, so the card picks
  up a generation it had not seen.
- **009 · CP6 · the card (spec-silent choices, pending human).**
  - It sits after the category breakdown and before the recent transactions. It follows the
    month selector and is keyed by the month, so an error shown for one month is not carried
    to the next. It is not shown in the empty dashboard state.
  - **With AI off, "Gerar análise" and "Regenerar" stay visible, disabled, with the reason**,
    as the spec asks of the preview's button. An analysis already made stays readable.
  - "Regenerar" is offered on `Failed` as well as `Completed`. There is no confirmation,
    although the old content is cleared as soon as it is pressed (CP5b).
  - Every settled analysis carries the line "Texto gerado por IA a partir dos seus totais.
    Confira os números antes de tomar uma decisão."
  - A future month is never reached: the selector stops at the local month. A browser east of
    UTC-3 can be a month ahead of the server around the turn of a month. The POST's 400 is
    then shown verbatim.
- **009 · CP6 · "Sugerir com IA" (test 25; spec-silent choices, pending human).**
  - It sits above the rows, beside the notices. With AI off it stays, disabled, with the
    reason as its accessible description. The reason names Configurações with no link, as
    does the card's. That keeps both components free of the router, and their unit tests
    render them alone. **Pending human** if a link is wanted.
  - It is enabled whatever the rows hold. The page shows one page of rows, so it cannot know
    whether any row is still on `Default`. The server answers `0` and the notice says so.
  - After a 200 the rows are refetched. The answer only carries counts. A notice says what
    changed. While the call runs (up to 30 s per batch) it says "Pedindo sugestões à IA…".
  - The marker is a small "IA" badge beside the category. Its accessible name and title are
    "Sugerida pela IA". Picking a category by hand shows as `User` at once (optimistic), so
    the badge goes.
- **009 · CP6 · `/settings` (spec-silent choices, pending human).**
  - It is in the nav, last, as "Configurações". The switch is a native checkbox with
    `role="switch"`. The PATCH answer replaces the cached `me`, so the preview and the card
    see the new flag at once. A refusal is shown and the switch keeps its state.
  - **The provider is named as "a Anthropic (Claude) ou a OpenAI (ChatGPT)"**, because the
    API does not expose `Ai:Provider`. **Pending human:** expose it, or name one in the copy.
  - The disclosure follows CP4a and CP5a to the field. It is hand-written copy, not derived:
    **a change to either request must change it too.**
  - The spend uses the API's default month (UTC-3, the budget's), not the local month. It is
    rounded to the cent for display. It is shown with AI off too.
- **009 · CP6 · for the human: invented pt-BR copy.**
  - Settings: "Configurações", "Inteligência artificial", "Usar IA nesta conta", "Liga
    "Sugerir com IA" na importação e a "Análise do mês" no painel.", "Ligada" / "Desligada",
    "Gasto com IA neste mês", "R$ x de R$ y em <mês> · n chamadas", "Ao atingir o limite, a IA
    fica indisponível até o mês seguinte. Chamadas que falham também contam, porque o provedor
    cobra pelo que recebeu.", "Não foi possível carregar o gasto com IA."
  - The disclosure, in `SettingsPage.tsx`, whole. **Read it as a family member would** (manual
    step 2).
  - Preview: "Sugerir com IA", "A IA está desligada na sua conta. Ligue-a em Configurações para
    receber sugestões.", "Pedindo sugestões à IA…", "1 categoria sugerida pela IA." / "n
    categorias sugeridas pela IA.", "A IA não sugeriu nenhuma categoria.", " 1 linha continuou
    na categoria padrão." / " n linhas continuaram na categoria padrão.", "Nenhuma linha na
    categoria padrão para a IA sugerir.", and the "IA" badge.
  - Card: "Análise do mês", "Nenhuma análise de <mês> ainda. A IA lê os totais do mês e escreve
    um resumo com sugestões.", "Gerar análise", "Regenerar", "<status>… a análise de <mês>
    aparece aqui assim que ficar pronta.", "A IA está desligada na sua conta. Ligue-a em
    Configurações para gerar a análise.", "Não foi possível carregar a análise do mês.", and
    the line under the content.
  - Labels: `CategorySource` ("Sem categoria", "Pelo histórico", "Categoria padrão",
    "Sugerida pela IA", "Escolhida por você") and `AnalysisStatus` ("Na fila", "Gerando",
    "Concluída", "Falhou").
- **009 · CP6 · pre-existing, for the human.** A failure without a problem body, such as a
  proxy's own 504 on a slow suggest, shows the client's English fallback "Request failed
  (504)" (`request()` in `web/src/api/finance.ts`, since 003). That is on-screen English.
  I did not change it here.
- **009 · CP6 · a real-browser run** (Chromium, a throwaway spec, not committed) went through
  the toggle, "Sugerir com IA" on `extrato.ofx` and "Gerar análise" on the fake. It found one
  bug: the switch only moved after the PATCH answered, so Playwright's `check()` failed and a
  person saw no feedback. It was fixed test-first (9ec5e0c, 7974424). The switch now shows the
  state it is saving and goes back on a refusal. Screens checked at 1280 and 375 px.
- **009 · CP6 · tests.** Test 25 is in `PreviewStep.test.tsx`, with the marker, and in
  `ImportPage.test.tsx`: the POST, the refetch, and 403/402/504/502 verbatim. Test 26 is in
  `AnalysisCard.test.tsx`: fake timers count the reads at exact instants, and `vi.waitFor`
  waits for what is on screen, because a response body is read on the real event loop. Test
  27 is in `Markdown.test.tsx` and again on the card. Also covered: generate, regenerate,
  Failed, AI off, 409 picking up the running row, `/settings` (6 cases) and the dashboard
  following the month.
- **009 · CP6 · commit sizes.** One commit goes over about 200 lines: 9a88957 (the preview,
  the client calls and the labels) is 215. The rest are between 5 and 194.
- **009 · CP6 · counts.** Web went from 145 to 172 (+27). .NET stayed at 957 and E2E at 22.
- **009 · CP6 · handoff to CP7** (E2E 28–29 on the fake, in the `chromium` project; no new
  project is needed, because neither test triggers a sync). New users have AI **off**, so each
  test turns it on first:
  - **Turning AI on:** `page.goto('/settings')`, then
    `getByRole('switch', { name: 'Usar IA nesta conta' }).check()`. Wait for
    `getByTestId('ai-state')` to read `Ligada`. The nav link is
    `getByRole('link', { name: 'Configurações' })`.
  - **Test 28:** upload `fixtures/extrato.ofx` as `import.spec.ts`'s `uploadOfx` does. Then
    click `getByTestId('import-step').getByRole('button', { name: 'Sugerir com IA' })`, which
    is disabled until AI is on. The rows change in place: expect
    `getByTestId('ai-marker')` to have count 3. The fake gives debits "Alimentação" and the
    credit "Salário", so `getByRole('combobox', { name: 'Categoria da linha 1' })` has
    Alimentação selected. `getByRole('status')` reads "3 categorias sugeridas pela IA." A row
    with `GARBAGE` in its description gives `suggested: 0`.
  - **Test 29:** after committing, go to `/`. Click
    `getByTestId('analysis-card').getByRole('button', { name: 'Gerar análise' })`. While the
    row is pending, `getByTestId('analysis-progress')` shows. Then
    `getByTestId('analysis-content')` holds the heading `Resumo` and the other four
    headings. The fake quotes the month as `YYYY-MM` and the month's expense as
    `R$ 1290.46` for `extrato.ofx`, when the fixture's dates fall in the current month. Allow a few seconds: the card polls every 3 s.
  - The throwaway spec above did both flows in about 3 s on the fake, and needs no timing
    change. The `Regenerar` button, in the same card, is a cheap extra.

## 009 · checkpoint 7

- **009 · CP7 · no ADR conflict, no API change, no migration.** One web fix, found by the
  E2E. No test calls a real AI API: the E2E API runs on `Ai:FakeProvider` (Development only).
- **009 · CP7 · tests 28 and 29, `web/e2e/ai.spec.ts`,** in the `chromium` project. Neither
  syncs market data, so the `market-data` → `investments` → `returns` chain is unchanged.
  - **Each test signs in as a new user** and turns AI on through the real switch on
    `/settings`, reached by the nav link. Nothing is shared between tests or runs, so there
    is no state to put back and no marker is needed. AI stays on for that throwaway user.
  - **Test 28:** it uploads `extrato.ofx` and checks that row 1 is on "Outros" with no AI
    marker. Then it presses "Sugerir com IA". It expects "3 categorias sugeridas pela IA.",
    three markers, row 1 on "Alimentação" and the credit on "Salário". `/settings` then
    shows "1 chamada".
  - **Test 29:** it commits `extrato.ofx` and **pages the dashboard back to September
    2026**, the fixture's month. The number of clicks comes from the browser's own clock, the
    one the selector opens on. It presses "Gerar análise" and waits up to 15 s for the
    content. It expects the five headings, "Análise de teste de 2026-09" and "R$ 1290.46"
    (55,90 + 1.234,56). "Regenerar" is enabled.
  - **Date independence, checked.** A throwaway copy (not committed) ran with the browser
    clock fixed at 15 March 2027. It went six months back and passed. The fixture month is in
    the past, so the server never refuses it as a future month. Before September 2026 the
    test fails on purpose.
  - **Not asserted:** `analysis-progress`. On the fake the row settles in milliseconds, so
    the spinner may never render.
  - **Not added:** the `GARBAGE` case. Integration test 20 covers it.
- **009 · CP7 · test-first.** Test 29 passed on the first run. **Test 28 failed:** the
  switch's `check()` reported "Clicking the checkbox did not change its state". The page
  snapshot a moment later showed it on. That is a real bug, below. The tests were committed
  red (62a2873).
- **009 · CP7 · the switch went back to off for one tick after a click (fixed).** CP6 made it
  show `toggle.variables` while `toggle.isPending`. TanStack Query delivers the pending state
  on its next tick. In between, the controlled checkbox re-rendered from `me`, unchecked.
  CP6's unit test used `userEvent`, whose awaits let that tick pass, so it was green.
  - A new unit test in `SettingsPage.test.tsx` asserts the switch is checked synchronously
    after `fireEvent.click`. It failed, then passed with the fix.
  - The fix (0b50936): the state being saved is the page's own `useState`, set in the change
    handler and cleared `onSettled`. A refusal still puts the switch back (CP6's test).
- **009 · CP7 · runs.**
  - `verify.sh`: the flaky 006 test
    (`MarketDataFakeProvidersTests…can_run_again_at_once`, 429) failed once, 956/957. The
    rerun was green with no change.
  - `verify-e2e.sh`: green twice back to back after the fix, 24/24 each, against the kept
    database.
- **009 · CP7 · counts.** .NET stayed at 957. Web went from 172 to 173. E2E went from 22 to 24.
- **009 · CP7 · commit sizes.** Each is under ~200 lines. The largest is 62a2873 (the tests),
  at 142. The handoff document (089b39c) is one file, left whole, as 007's and 008's were.

## 009 · handoff

Full handoff: `docs/handoffs/009.md`. **009 is complete in code (tests 1–29 present); these
need a human.** Nothing has called a real AI API yet.

- **Provider keys:** `Ai__Anthropic__ApiKey`, and OpenAI's only if switching. No OpenAI
  model or price is configured (CP2, CP3).
- **Fixture capture:** the six files in `api.tests/Fixtures/Ai/` against the real APIs. The
  OpenAI error `type`s in the tests are my reading of the docs (CP3).
- **Model ids and prices:** `claude-haiku-4-5` (USD 1 / 5) and `claude-opus-5` (USD 5 / 25),
  confirmed by you in CP3. Check them against the console when capturing.
- **`Ai:UsdBrl` 5.40** is an assumed fallback rate (CP2).
- **pt-BR copy review:** the problem texts (CP4b), the job's errors (CP5a), the 400s and 409
  (CP5b), and the screens and labels (CP6).
- **The disclosure review:** read `/settings` as a family member (manual step 2). It is
  hand-written against CP4a's and CP5a's requests.
- **The prompts:** review `Prompts/monthly-analysis.md` (v1) and `AiCategorisation.System`
  like code.
- **Manual steps 1–7** with a real key and a budget of R$ 1,00. **Step 4 decides:** no
  number may be invented. Step 3 also checks suggest's timeouts through Caddy (CP4a).
- **ADR-003's wording (flag, not edited).** The ADR says the sweep re-enqueues on startup.
  It runs **at startup and every 5 minutes**, and it fails a stale `Running` row instead of
  re-running it (CP5a). Amend the sentence, or say to cut the sweep back.
- **ARCHITECTURE.md is stale (flag, not edited, from CP1):** the scheduled monthly analysis
  (stack table, §5 jobs, AI module, Phase 5), the categorisation cache by normalized
  description, `categorize-batch` as a job, principle 6, `Domain/Analysis/`, and the
  environment block (no `BaseUrl` or timeouts).
- **Decisions to confirm or overturn** (all spec-silent, listed in the handoff):
  - Gates and money: the UTC-3 month, the 4-characters-a-token estimate, the gateway's own
    `ai_enabled` check, and no rate limits (CP2, CP4b, CP5b).
  - Providers: no retries, failure on truncation, the 120 s analysis timeout, and 4xx
    billed 0 (CP3).
  - Categorisation: GUID ids against short aliases, batches of 40 in sequence, Transfer on
    either sign, `suggested` counts, and 502 (CP4a, CP4b).
  - Analysis: the input's shape with current balances, PIX names, `MaxTokens` 8000, one
    consumer, the 409, regenerate clearing at once, and the current month allowed (CP5a,
    CP5b).
  - Web: polling the list, disabled buttons with no link, "Regenerar" on `Failed`, the
    provider named as either, and the spend's month (CP6).
- **Starting spec 010.** Read `specs/010-deploy.md`, `docs/handoffs/009.md` ("Starting spec
  010") and the 009 entries above. The baseline is .NET 957, web 173, E2E 24.
  - The production `.env` needs the AI key, and no fake switch.
  - The prompt is embedded, so publish carries it.
  - Check suggest's multi-batch requests against Caddy's and Cloudflare's timeouts.

## 010 · checkpoint 1

- **010 · CP1 · no ADR conflict, no migration.** No model change. Swagger is not present,
  so there is nothing to switch off in Production.
- **010 · CP1 · the spec's reason for `ForwardedHeaders` is off for this codebase (spec not
  edited).** The session cookie is already `SecurePolicy.Always` (002), so it is `Secure`
  whatever the scheme. What `X-Forwarded-Proto` actually fixes is the **Google
  `redirect_uri`**: it is built from `Request.Scheme`, and without the header it is `http://`
  and never matches the console. Google's correlation cookie depends on the scheme as well. Test 4
  (`ForwardedHeadersTests`) asserts both, from a trusted and from an untrusted peer.
- **010 · CP1 · how "trusting the Docker network" is configured (spec silent, pending
  human).** `ForwardedHeaders:KnownNetworks` (CIDR list) is added to the loopback default.
  It is empty everywhere except the production compose file, which pins `172.30.0.0/24`.
  - `X-Forwarded-For` is honoured as well as the proto, with no forward limit. Otherwise
    ADR-010's per-IP sign-in limit would count Caddy, and ten sign-ins a minute would be the
    whole site's allowance. Tested.
- **010 · CP1 · decision 2 needed a code change, and the spec's migrate command cannot run
  (pending human).**
  - `Program.cs` migrated on every boot by default. `appsettings.Production.json` now sets
    `Database:MigrateOnStartup` to false. Development, E2E and the test hosts still migrate
    on startup.
  - The spec's `docker compose run --rm api dotnet ef database update` needs the SDK and
    `dotnet-ef`, and the runtime image has neither. The published app now takes a `migrate`
    argument that applies the migrations and exits before serving: `docker compose run --rm
    api migrate`. It runs the same `MigrateAsync` as Development. An EF migration bundle
    was the alternative, but it is a second artifact whose tool version must match.
- **010 · CP1 · `Google:*` and `DataProtection:*` from `__` environment variables: no new
  code or test.** `WebApplication.CreateBuilder` already reads them. A test that sets
  process-wide environment variables would race the parallel suite. The container boot
  proves it instead: the boot refuses to start without either, and CP2's container started
  with both coming only from the env file.
- **010 · CP1 · counts.** .NET 957 → 962, web 173. `verify.sh` green first time.
- **010 · CP1 · commit size.** a79f260 (the two test files) is 220 lines, over ~200.

## 010 · checkpoint 2

- **010 · CP2 · no ADR conflict.** ADR-001 (one machine, built on the server), ADR-004 (one
  process) and ADR-005 (Tunnel, no port published) are unchanged.
- **010 · CP2 · deviations from the spec's text (pending human):**
  - **Caddyfile:** a global `servers { trusted_proxies static private_ranges }` was added.
    Without it, Caddy overwrites cloudflared's `X-Forwarded-Proto: https` with its own
    `http`, and CP1's fix never sees https. The rest is the spec's block.
  - **`webdist` is a bind mount of `deploy/webdist`, not a named volume.** A named volume
    cannot receive the host's build without an extra copy container. `deploy/webdist/` is
    git-ignored.
  - **Health check:** it uses bash's `/dev/tcp` instead of curl. The runtime image has no
    HTTP client, and `apt` would add a layer (in this sandbox, `apt` cannot reach the
    plain-HTTP mirrors at all).
  - `DataProtection__KeysPath=/keys` and the known network are **pinned in the compose
    file**, over `.env`, so a typo in `.env` cannot move the key ring off the volume.
- **010 · CP2 · spec-silent choices (pending human):**
  - Compose project `name: finance` and network `172.30.0.0/24`. Pick another if the
    instance already uses that range.
  - `cloudflare/cloudflared:latest` is not pinned, as in ARCHITECTURE. Pin a version once
    it works.
  - `.dockerignore` lives next to the Dockerfile (`Dockerfile.api.dockerignore`, a BuildKit
    feature, the default builder since Docker 23).
- **010 · CP2 · `deploy/.env.example` (placeholders only).**
  - It follows the spec's `.env`: database and user `finance`. Dev and ARCHITECTURE use
    `financas`/`dev`.
  - Additions:
    - `GSS Encryption Mode=Disable`: Npgsql probes for `libgssapi_krb5`, which the image
      lacks, and logs an error.
    - `Ai__Provider=anthropic` instead of empty: an empty value fails the boot.
    - `AGE_PUBLIC_KEY` for backup.sh.
  - It lists the two fake switches as must-be-absent.
- **010 · CP2 · timeouts on the suggest path (009's note; pending human, nothing changed).**
  - Caddy's `reverse_proxy` has no response timeout by default.
  - Cloudflare's proxy read timeout is **100 s** (error 524), and it cannot be raised below
    Enterprise.
  - A 200-row suggest is up to 5 batches × 30 s = 150 s in the worst case. Real batches
    should take seconds, but a worst case gives a 524, and the web shows 003's English
    "Request failed (524)".
  - Options: accept it, cap the rows per request, or make suggest a job like the analysis.
    Check through the tunnel in 009's manual step 3.
- **010 · CP2 · container timezone still unset (006, pending human).** The containers run in
  UTC, so the nightly sync's `0 3 * * *` fires at 00:00 in Brasília. Set `TZ` on `api`, or
  rewrite the cron in UTC. The first boot runs a sync at once (no run in 26 h), by 006's
  design.
- **010 · CP2 · validated locally (Docker 29.3, amd64):**
  - The image builds for amd64. It also builds for **arm64** under QEMU (4 min), after
    mounting `binfmt_misc` and registering `qemu-aarch64` in this sandbox. That arm64 image
    boots on aarch64.
  - Sandbox only, with the Dockerfile unchanged: the sandbox's TLS-intercepting egress
    needed `--build-context` to swap the SDK base for a copy trusting its CA, plus
    `--network host`. Docker Hub answered 429 for `caddy`, `node` and `binfmt`, so they came
    from `mirror.gcr.io` and were tagged locally.
  - The stack came up with postgres, api and caddy. cloudflared was never started, since
    there is no tunnel.
  - What was checked on it: the migrate step, `https` redirect_uri through Caddy, non-root
    `app` user, keys on the volume kept across a restart, memory limits enforced (1 GiB,
    512 MiB), no published port, and both fake switches refusing the boot (exit 255 with the
    message).
- **010 · CP2 · commit size.** afec144 is 210 lines, over ~200.

## 010 · checkpoint 3

- **010 · CP3 · `deploy.sh` departs from the spec's sketch (pending human):**
  - **The web is built in a `node:22-alpine` container.** The runbook installs only Docker,
    so the spec's host `npm ci` would fail at step 7.
  - The build goes to `webdist.next`, and is **published only after `up -d`**. A failed
    migration then leaves the old web and the old API serving together. It is published in
    place (emptied and refilled), because Caddy bind-mounts the directory.
  - **`git pull --ff-only` only on a branch.** Decision 8's rollback checks out a tag, which
    is a detached HEAD, where the spec's unconditional pull fails.
  - **It re-executes the freshly checked-out `deploy.sh`** after the git step. Otherwise
    the steps after git run the old copy of the script.
  - Guards: the env file must be `chmod 600`, set no fake switch, and have `App__Origin`.
  - `compose build --pull`, then `docker image prune` at the end.
  - Smoke runs against `App__Origin`, which is the whole Cloudflare → Caddy → API chain. If
    Cloudflare's bot protection challenges curl, the smoke fails there.
- **010 · CP3 · `smoke.sh`.**
  - It first waits up to 60 s for `/health` (`SMOKE_WAIT_SECONDS`).
  - The dev-login POST carries `Origin: <base>`, so the origin check passes and routing
    answers the 404.
  - Checked against the local production stack (passes), and against a Development API
    and a dead port (fail).
- **010 · CP3 · `backup.sh` (pending human):**
  - **The spec says to put the public key in `backup.sh`.** It reads `AGE_PUBLIC_KEY` from
    the environment or `.env` instead. Editing a tracked script on the server would dirty
    the checkout and block `git pull`.
  - Remote `backup:finance-backup` (the runbook's), with `daily/`, `weekly/` (Sundays) and
    `monthly/` (the 1st), in UTC. Pruned with `--min-age 7d`, `4w` and `6M`, only after
    the day's upload succeeded. No local copy is kept (ARCHITECTURE's sketch kept 2 days in
    `/tmp`).
  - Runbook step 10's path becomes `backup:finance-backup/daily/<latest>`. Add
    **`--no-owner`** to its `pg_restore`: the dump's owner role `finance` does not exist in a
    bare `postgres` container.
  - The cron user must be in the `docker` group, and `/var/log/finance-backup.log` must be
    writable by it.
- **010 · CP3 · `scripts/verify-deploy.sh` (added; the spec has no such script).** It is a
  repeatable local check of test plan 1–2, not run by `verify.sh`.
  - It checks the migrate step, the smoke, the fake switches and the key ring across a
    restart.
  - It also runs a backup with rclone stubbed and a throwaway age key, decrypts it, and
    restores it into a scratch database with the same 8 migrations.
  - `VERIFY_API_IMAGE` takes a prebuilt image. Do not run it on the server, because it pins
    the same subnet.
  - Green twice here, with the sandbox-built image.
- **010 · CP3 · `deploy.sh` was dry-run, not run for real.** The runs were in a scratch
  clone with a logging `docker` shim. They covered both guards, the branch and the tag,
  the re-exec, a failed migrate stopping before `up -d` with the old web still live, and
  the web published in the same inode.
  - Its real Docker commands are the ones `verify-deploy.sh` runs, except
    `build --pull` and the Node container build.
  - The Node build ran on its own in a clone. It needed sandbox proxy settings to reach
    npm; on the server it needs none.
- **010 · CP3 · lint.** `bash -n` and shellcheck 0.11 (installed in the scratchpad, not
  committed) are clean on all four scripts.

## 010 · handoff

Full handoff: `docs/handoffs/010.md`. **010 is complete in code (automated test plan 1–5
covered); nothing has been deployed.** These need a human:

- **Runbook steps 1–12** on the real instance, including the required restore drill (step
  10, with `--no-owner` and the `daily/` path). Changes from the spec's text are listed in the
  handoff.
- **Confirm or overturn** the CP1–CP3 decisions above: the `migrate` argument, Caddy's
  `trusted_proxies`, `X-Forwarded-For`, the subnet, the bind-mounted `webdist`, the Node
  container build, the deploy order, the key in `.env`, and the backup layout.
- **Open:** the container `TZ` (from 006), and suggest's worst case against Cloudflare's
  100 s (from 009).
- **ARCHITECTURE.md's deploy text** needs updating once it has shipped (a DoD item; flagged,
  not edited).
- **The whole run 005–010:** see the handoff's last section.

## 019 · market data without the owner's keys

- **019 · live checks, 2026-09-25.** Binance klines work with no key (`BTCBRL` from
  2020-10-13). brapi without a token serves PETR4, VALE3, MGLU3 and ITUB4 only; every
  other ticker, IVVB11 and unknown ones included, is HTTP 401 `MISSING_TOKEN`, so 006's
  "unknown ticker is a 404" only holds with a token. Twelve Data is HTTP 401 without a
  key. CoinGecko works keyless up to 365 days. Four responses are now real captures in
  `api.tests/Fixtures/MarketData/`; the 006 files are still hand-written.
- **019 · live end-to-end check, 2026-09-25.** The API with real providers against the
  agent's own E2E database: registering `btcbrl` on Binance stored `BTCBRL`, and a manual
  sync wrote 1826 closes from 2021-09-25 to 2026-09-24, matching the captured fixture on
  20–24 Sep. A second run with `BBAS3` on brapi and no token ended `PartialFailure` with
  "BBAS3: O brapi exige um token para BBAS3. Configure MarketData:Brapi:Token."
- **019 · no migration.** `ProviderKind.Binance = 3` is stored in the existing `int`
  column, which has no check constraint; `dotnet ef migrations has-pending-model-changes`
  reports no changes.
- **019 · pending human (spec 019, out of scope):** brapi's free token serves 3 months of
  history, and its docs say a request beyond the plan is HTTP 403. A new asset's first
  sync asks for 5 years, so with a free token it would fail as "O provedor recusou a chave
  de acesso." every night. Not observable without a token. Decide between a paid plan and
  a configurable maximum range for the brapi adapter.
- **019 · pending human:** the Binance registration rule (BRL only, symbol ending in BRL)
  and the provider descriptions in the registration select are spec decisions 6 and 12,
  marked (review).
