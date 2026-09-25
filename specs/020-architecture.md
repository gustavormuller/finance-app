# 020 — Architecture: a review against the ADRs, and one home for duplicated logic

## Goal

Check the code against `docs/ARCHITECTURE.md` (§6 Layers, §7 Architectural style, ADR-014 to
ADR-017) and `CLAUDE.md`, and fix what is low-risk and clearly better: business rules out of
`Endpoints/`, one home for logic written more than once, one way to answer each kind of error.
What is large, subjective or a product decision is written down here as proposed, not done.

## Out of scope

- Any change to routes, API shapes, status codes or pt-BR messages, except the two bug fixes
  below, each proven by a test that failed first.
- Anything that contradicts an ADR. Such a finding is listed as proposed, with the ADR it
  conflicts with.
- New ports, a Repository, MediatR or domain events (ADR-014, ADR-015, ADR-016).
- Splitting `web/src/api/finance.ts` and reworking the import endpoints (findings 17 and 18).

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | How a statement that is not UTF-8 is decoded | **Windows-1252**, not Latin-1. A BOM still wins and valid UTF-8 stays UTF-8. The two encodings agree on every letter Portuguese has; they differ only in 0x80–0x9F, where Windows-1252 has punctuation (en dash, curly quotes, `€`) and ISO-8859-1 has control characters no statement contains. So an ISO-8859-1 file decodes as before, and a Windows-1252 file with that punctuation no longer puts invisible control characters into descriptions. The code-page provider ships in the shared framework; `StatementText` registers it itself instead of relying on `SpreadsheetStatementReader`'s static constructor having run. The files in `samples/statements/` that are not UTF-8 (Bradesco, Inter, Itaú) contain no byte in 0x80–0x9F, so they decode exactly as before. **(review)** |
| 2 | Which of the two `Optional` helpers is right | The **trimming** one. A template stores its column references trimmed, and resolving a column already ignored the padding, so the only visible difference was the refusal naming `"  Valor  "` for an inline mapping. The two copies are now one, and the inline mapping names the column it looked for. **(review)** |
| 3 | Where the rules that need a query live | `Application/Categories/CategoryRules` (the two-level tree, the parent's kind, the delete guards) and `Application/Transactions/TransactionInputRules` (ids under the filter, the currency against the account, then `TransactionRules`' pure checks). Scoped services on `AppDbContext`, as the other use cases (ADR-016). The endpoints keep the input checks (a name is required) and map a `RuleViolation` to a 400. |
| 4 | The transaction request type | The endpoint binds `Application.Transactions.TransactionInput`, as the movement routes bind `MovementInput`. Same property names, so the same JSON. |
| 5 | Where the USDBRL code lives | `Benchmark.UsdBrl` in `Domain/MarketData`, beside the entity whose `Code` it is. The rate load that 007's FX rule needs is `UsdBrlRatesAsync` in `Application/MarketData`. |

## Findings

| # | Finding | Where | Proposal | Status | Reason |
|---|---|---|---|---|---|
| 1 | Three commit assertions compared `(committed, 1)` with `(x.Committed, 1)`: the second element compared the literal 1 with itself | `api.tests/Integration/ImportEndpointTests.cs` (`An_included_duplicate_is_committed`, `A_foreign_currency_row_is_invalid_…`, `An_invalid_row_cannot_be_included`) | Compare `Skipped`, which the expected tuples describe | **Done** | Test bug. The fixed assertions pass: the code was right, the tests checked less than they said. |
| 2 | `RepositoryRoot()` copied into six test files; the floating-point member scan (with `IsBinaryFloatingPoint`) into four | `api.tests/Unit/*TypesTests.cs`, `*OptionsTests.cs`, `SpreadsheetReaderTests.cs`, `Integration/DashboardSqlTests.cs` | `api.tests/Support/TestPaths` and `FloatingPointMembers` | **Done** | Pure duplication. |
| 3 | The `"USDBRL"` code declared twice | `AiPricing.UsdBrlCode`, `SnapshotRebuild.UsdBrl` | `Benchmark.UsdBrl` | **Done** | Decision 5. |
| 4 | 007's FX-rate load ("from the latest rate on or before the first date") written three times | `SnapshotRebuild`, `PositionQueries`, `ReturnsQueries` | `UsdBrlRatesAsync` in `Application/MarketData` | **Done** | One rule, one query. `SnapshotBuilder` orders the rates itself, so returning them ordered changes nothing. |
| 5 | `TrySaveAsync` copied into two endpoint files; the same try/catch inlined in three more routes | `AccountEndpoints`, `CategoryEndpoints`; `CsvTemplateEndpoints`, `MarketDataEndpoints`, `InvestmentEndpoints` | `Problems.TrySaveAsync`, beside `IsDuplicate` | **Done** | Same behaviour at every site. |
| 6 | `Optional` in two endpoint files, only one trimming | `ImportEndpoints`, `CsvTemplateEndpoints`; the nickname in `InvestmentEndpoints` spelled it out a third time | `RequestText.Optional`, trimming | **Done** | Decision 2. Proven by `A_padded_optional_column_is_reported_trimmed_as_a_template_would_store_it`, which failed before. |
| 7 | The category tree and kind rules and the delete guards ran in the endpoint (§6: "thin HTTP — input validation, no business rules") | `CategoryEndpoints.ValidateAsync`, `MapDelete` | `Application/Categories/CategoryRules` | **Done** | Decision 3. `Each_category_refusal_keeps_its_status_and_its_words` pins every refusal's status and exact words; it passed before the move and after. |
| 8 | The transaction rules that need a query ran in the endpoint, while `TransactionRules` said they lived in `Application/` | `TransactionEndpoints.ResolveAsync` | `Application/Transactions/TransactionInputRules` | **Done** | Decisions 3 and 4. `Each_transaction_refusal_keeps_its_fields_and_its_words` pins the order, fields and words; it passed before the move and after. A `Problems.Validation` overload over plain violations replaces four `(RuleViolation?)` casts. |
| 9 | The open-import 409 built its problem by hand, repeating the title every other 409 gets from `Problems.Conflict` | `ImportEndpoints.UploadAsync` | `Problems.Conflict(reason, extensions)` | **Done** | Same response; one source for the 409 shape. |
| 10 | A statement that is not UTF-8 was decoded as Latin-1: Windows-1252 punctuation became invisible control characters | `Domain/Import/StatementText.cs` | Windows-1252 fallback | **Done** | Decision 1. `Windows_1252_punctuation_decodes_to_the_characters_it_stands_for` failed before; `Utf8_punctuation_stays_utf8`, `Every_byte_decodes_to_one_character` and the existing BOM and Latin-1 tests guard the detection. |
| 11 | `e2e/auth.spec.ts` kept its own `devLogin` and `uniqueEmail` | `web/e2e/auth.spec.ts` | Import them from `e2e/support.ts` | **Done** | Pure duplication. |
| 12 | The rule that turns a refused write into a sentence written five times | `AnalysisCard`, `AccountImport`, `lib/accounts.ts`, `AddAsset`, `AssetCatalogue` | `web/src/lib/refusal.ts` (`refusal`, `refusalMessage`), with tests | **Done** | Same output at every site. |
| 13 | `shortDay` in two charts | `ValueChart`, `ComparisonChart` | `lib/chart.ts` | **Done** | Pure duplication. |
| 14 | The `['accounts']` and `['categories']` queries declared inline, key and fetcher each time | `TransactionsPage`, `CategoriesPage`, `AccountImport` | `useAccounts` (existing), `useCategories`, `useCategoryUsage` | **Done** | One place pairs each key with its fetcher. The keys themselves were already consistent (see "Checked"). |
| 15 | `TransactionsPage` had a private `currentMonth()` returning a date range, shadowing `lib/months`' `currentMonth`, and its own local-day formatter | `web/src/routes/TransactionsPage.tsx` | `monthDays(currentMonth())` in `lib/months.ts`, tested | **Done** | Same range; no second meaning for one name. |
| 16 | The investments route reaches into another endpoint class for the catalogue: `MarketDataEndpoints.AssetRequest`, `Validate` (with the CoinGecko currency rule), `NewAsset`, `DuplicateSymbol` | `InvestmentEndpoints` `POST /assets` | Catalogue registration (validation, find-or-register) in `Application/MarketData`, both routes calling it | Proposed (not done) | Medium, and the find-or-register flow is tied to telling which unique index refused the save; it deserves its own tests. It works today. |
| 17 | The import endpoint still holds use-case logic: the row patch rules (batch status, an invalid row cannot be included, the category's sign) and the upload's parse orchestration (template or inline mapping, CSV or spreadsheet, validation against the real headers) | `ImportEndpoints.PatchRowAsync`, `UploadAsync`, `ParseTableAsync` | `ImportCommands.PatchRowAsync`, and an `Application/Import` parse step returning rows or violations | Proposed (not done) | Large (about 250 lines with the tests to pin every message). One pass over the import endpoint is better than two partial ones. |
| 18 | `web/src/api/finance.ts` is one 764-line file with every resource's types and calls | `web/src/api/finance.ts` | Split by resource, `request` and `ApiError` in a shared module, re-exported from `finance.ts` during the move | Proposed (not done) | Large (about 60 import sites) and subjective: the file is ordered by section and works. ADR-002 only asks for a hand-written client in `web/src/api/`. |
| 19 | Four query parsers, four wordings for the same mistake: `"Data inválida."` (categories), `"A data inicial deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31."` (returns), `"O mês deve estar no formato AAAA-MM, por exemplo 2026-09."` (dashboard), `"Informe o mês no formato AAAA-MM."` (AI). Transactions, `/investments/assets/{id}/daily` and the market-data series bind `DateOnly?` directly, so a malformed date there is the framework's 400 (plain text in Development, an empty body in production) with no pt-BR message | `CategoryEndpoints.ParseRange`, `ReturnsEndpoints`, `DashboardEndpoints`, `AiEndpoints`; `TransactionEndpoints`, `InvestmentEndpoints`, `MarketDataEndpoints` | One query-parameter parser in `Endpoints/` with one message per kind of value | Proposed (not done) | Changes on-screen copy and the answer to malformed input: a product decision. The UI never sends a malformed date. |
| 20 | The opening balance is rounded "the way `Money` rounds" by a copy that leaves out `Money`'s two-place scale, so create and update answer `3000` where a read answers `3000.00` | `AccountEndpoints.Round` | `new Money(amount, currency).Amount` | Proposed (not done) | A wire change. 003's two-decimal rule is about `amount`; the values are equal as JSON numbers and the UI is unaffected. |
| 21 | The account and asset delete guards (transactions, movements) stay in their endpoints | `AccountEndpoints`, `InvestmentEndpoints` `MapDelete` | Move with finding 16 or 17 if a second rule joins them | Proposed (not done) | One count query each, a courtesy before the `RESTRICT` key. A class per one-line guard is indirection (ADR-017: CRUD stays CRUD). The category guards moved because they sit with the tree rules. |
| 22 | `IPriceProviderRegistry` has one implementation; the tests register the same class with other providers | `Application/MarketData/IPriceProviderRegistry.cs` | Inject `PriceProviderRegistry` directly | Proposed (not done) | ADR-015's criterion would call it directly, but ADR-015's 006 amendment names the interface. Changing it means amending the ADR: the owner's call. |
| 23 | `POST /api/market-data/sync` depends on a job class in `Infrastructure/Jobs` | `MarketDataEndpoints` → `ManualMarketDataSync` | An `Application/MarketData` trigger owning the interval rule, the background run staying in `Infrastructure` | Proposed (not done) | The interval rule and the background launch share a scope and the host's stopping token; splitting them cleanly needs a port for the launch, which would have one implementation (ADR-015). |

### Checked and left as is

- `Domain/` references neither EF Core nor `HttpClient`. `AppUser` derives from `IdentityUser<Guid>`
  (`Microsoft.Extensions.Identity.Stores`, no EF), where the repository structure puts it.
- Every user-owned entity implements `IUserOwned` and gets the global filter; `market_assets`,
  `prices`, `benchmarks` and `sync_runs` are shared and have none.
- No Repository, MediatR or domain events. The ports are `IPriceProvider` (three providers and a
  fake), `IBenchmarkProvider` (BCB and a fake; a second source foreseen by ADR-015), `IAiProvider`
  (two providers and a fake) and `ICurrentUser` (the HTTP one and a test fake).
- Errors: a missing or foreign row is a bare 404, a bad field a `Problems.Validation` 400 naming it,
  a conflict `Problems.Conflict` (all of them after finding 9), an AI failure `Problems.Ai`.
- Web query keys follow one prefix per area (`['dashboard', …]`, `['investments', …]` with the
  returns under it, `['transactions', query]`, `['imports', …]`, `['categories', …]`), and no query
  sets a stale time, so a page reads fresh data after a write made on another page.
- The latest USDBRL is read twice (`AiPricing`, `PositionQueries`), in two shapes of four lines each;
  not worth a helper.

## Test plan

New tests, each in the commit it belongs to:

- `ImportEndpointTests`: the three fixed assertions (finding 1).
- `CsvImportEndpointTests.A_padded_optional_column_is_reported_trimmed_as_a_template_would_store_it`
  (finding 6, failed first).
- `CategoryEndpointTests.Each_category_refusal_keeps_its_status_and_its_words` and
  `TransactionEndpointTests.Each_transaction_refusal_keeps_its_fields_and_its_words` (findings 7
  and 8, green before and after the move).
- `CsvStatementParserTests`: `Windows_1252_punctuation_decodes_to_the_characters_it_stands_for`
  (failed first), `Every_byte_decodes_to_one_character`, `Utf8_punctuation_stays_utf8` (finding 10).
- `web/src/lib/refusal.test.ts` (finding 12) and `monthDays` in `months.test.ts` (finding 15).

Everything else is proven unchanged by the existing suites:

```
bash scripts/verify.sh
E2E, isolated (never scripts/verify-e2e.sh: it stops the shared database)
```

## Definition of done

- `verify.sh` green, E2E green.
- Every pt-BR message, status code, route and API shape unchanged, except decisions 1 and 2.
- Every finding above is either done or proposed with its reason.
