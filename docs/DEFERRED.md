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
