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
