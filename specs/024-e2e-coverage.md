# 024 — E2E coverage of the whole site

## Goal

Every flow a person can drive through the screens — create, edit, delete, filter, page,
import, export, toggle, register, sync, ask the AI, configure, sign in and out, and the
empty, error and refused states on the way — is exercised by at least one Playwright test
against the real API and a real PostgreSQL. A flow that cannot be driven through the
screens is listed, with the reason, so "not covered" is always a decision and never an
oversight. Then the unit and integration tests that E2E makes redundant are named, and
removed under the policy below.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | What counts as a flow | Something a person does or reads on a page, with an outcome they can see: a write and what it changes, a filter and what it hides, a refusal and its sentence, an empty state. Visual layout (phone width, colours, chart drawing) is not a flow, except the two E2E tests 012 already has. **(review)** |
| 2 | Where a new test goes | One spec file per area of the site: the existing files grow, and `accounts.spec.ts`, `categories.spec.ts` and `navigation.spec.ts` are new. A flow that touches two areas goes where its subject is (pagination of the list, driven through an import, is a transactions test). |
| 3 | Extend or add | An existing test is extended when the new assertions follow its own story (the `.xlsx` test also runs on the `.xls`); otherwise a new test. No near-duplicates. |
| 4 | Dates | Fixed dates and explicit filters, never "this month" by default: the transactions list is narrowed to the fixture's dates, and the dashboard is paged back to the fixture's month (`showDashboardMonth`). A feature defined relative to today (the dashboard's current month and its net-worth chips, the categories' last 12 months, the returns' 30-day fixture and No ano) gets dates computed from today at a safe distance from any boundary: the 15th of a month, 14 months back, 20 days back. Nothing assumes which month it is. **(review)** |
| 5 | Each test's user | A fresh user per test (`devLogin` + `uniqueEmail`), so every test starts from the default categories and nothing else, and tests run in parallel. |
| 6 | Shared market data | The catalogue, prices, benchmarks and sync runs are shared by every user. A test that syncs, or that asserts a price is missing, runs in the serial syncing projects (`market-data` → `investments` → `returns`, `playwright.config.ts`); every other test runs in parallel in `chromium`. New tickers are unique per run (`uniqueTicker`). Figures that depend on how many days of fake series the database has kept (the benchmarks' returns) are not asserted, only their rows. |
| 7 | Fakes | The API runs with `MarketData__FakeProviders=true` and `Ai__FakeProvider=true`: no request leaves the machine, and the figures are fixed (a close of 10 on the sync's day, one cent less per day back; USDBRL 0,05; the AI's answers derived from its input, prose for a row marked `GARBAGE`). |
| 8 | Fixtures | The documented sample statements of `samples/statements/` (README there lists each file's expected outcome) are copied byte for byte into `web/e2e/fixtures/` under their own names, as `bb-extrato.xlsx` already was. Files a test needs only once (120 or 5 001 rows, over 2 MB, header-less, not an OFX, empty) are built in the test (`ofxStatement`). |
| 9 | A real bug found by a test | Not fixed here. The test stays, marked `test.fixme` with a one-line reason, and the bug is listed below. |
| 10 | Two tabs | A refusal a person reaches only with a second tab open (an import started in another tab, a session ended in another tab, AI switched off in another tab) is driven with a second page in the same browser context: it is the person's own browser, and the refusal's sentence is what they would read. **(review)** |
| 11 | The PWA suite | `web/e2e-pwa` (`npm run e2e:pwa`, spec 022) stays separate: it builds and serves a production build and stops the server mid-run. It is counted in the inventory, not merged, and no unit test is removed on its strength alone (it is not part of the routine E2E run). |
| 12 | Which unit and integration tests go | Gustavo's choice: "remove only the redundant tests". Policy in Phase 2 below. |
| 13 | API integration tests and ADR-002 | ADR-002 makes the integration tests the contract of the hand-written TS client ("the integration tests declare the same shapes independently and fail when the endpoints drift"). E2E sees what a page renders, never a status code, a `Location` header, or a field the page ignores, so an endpoint test is redundant only when it asserts none of those. **(review)** |

## Out of scope

- Fixing the bugs the new tests found (listed below, each with a `test.fixme`)
- Visual checks beyond 012's two: phone width, colours, chart rendering
- Load, performance and accessibility audits
- Moving the PWA suite into the dev-server suite (decision 11)
- The real Google sign-in, the nightly schedule, and the other flows listed as not E2E-reachable

## Inventory

The site's routes (`web/src/routeTree.tsx`): `/login`, and behind the sign-in `/`, `/transactions`,
`/import` (a redirect), `/accounts`, `/accounts/$accountId` (tabs `transactions`, `import`,
`details`), `/categories`, `/market-data`, `/investments`, `/investments/returns`,
`/investments/$assetId`, `/investments/$assetId/returns`, `/settings`. The shell around them
(`App.tsx`, `ProtectedLayout.tsx`) has the theme toggle, the sidebar with the sign-out, and the
health footer.

**Before:** 33 tests in 13 files of `web/e2e`, plus 5 in the separate PWA suite.
**After:** 86 tests in 16 files of `web/e2e` (80 run, 6 `test.fixme` for the bugs below), plus the
same 5 in the PWA suite.

**Flows:** 108 listed below. Before this spec 33 were covered, 15 partly and 60 were gaps. After
it, 104 map to a passing test and 4 (AUTH-5, NAV-4, TX-10, CAT-5) only to `test.fixme` tests naming
5 of the 6 bugs below; the sixth sits beside IMP-16's passing test. 16 more flows are listed as not
E2E-reachable (the last table).

"Before" names the test that covered the flow when this spec was written; **GAP** means none
did, *partial* means a test touched the flow without asserting its outcome. "After" names the
test that covers it now, by file and title.

### Shell, sign-in, navigation

| # | Flow | Before | After |
|---|---|---|---|
| AUTH-1 | A signed-out visit to any protected page lands on `/login` | partial (only `/`) | `auth` "an unauthenticated visit to any page lands on the login page" |
| AUTH-2 | Signed in, the sidebar shows the display name, or the e-mail when Google gave none | partial | `auth` "a signed-in visitor sees their display name on /" |
| AUTH-3 | Sair returns to `/login`, and the cookie is gone | `auth` | `auth` "logging out returns to the login page and leaves / protected" |
| AUTH-4 | `/login?error=unverified\|cancelled\|auth_failed` explains itself; `/login`, an unknown code or notice show nothing | GAP | `auth` "the login page explains each way a sign-in can come back, and nothing else" |
| AUTH-5 | A write, and Sair, from a tab whose session ended in another tab | GAP | **bugs 5 and 6**: `auth` "a write after the session ended in another tab is explained in Portuguese", "Sair from a tab whose session already ended lands on the login page" (`fixme`) |
| NAV-1 | The sidebar's six pages, no Importar, the current one marked | GAP | `navigation` "the sidebar opens every page and marks the one shown" |
| NAV-2 | `/market-data` and `/investments/returns` open by address | partial | `navigation` "the pages outside the sidebar open by their address" |
| NAV-3 | An account or asset id that does not exist says so (`/accounts/$id`, `/investments/$id`, `/investments/$id/returns`) | GAP | `navigation` "an account or an asset that does not exist says so" |
| NAV-4 | An address that matches no page | GAP | **bug 1**: `navigation` "an address that matches no page answers in Portuguese" (`fixme`) |
| THEME-1 | Claro and Escuro survive a reload, the toolbar colour follows | `theme` | `theme` "the theme chosen is kept across a reload" |
| THEME-2 | Sistema follows the operating system while the page is open, toolbar colour included | GAP | `theme` "Sistema follows the operating system, live" |
| THEME-3 | The selected account card has the primary outline | `theme` | `theme` "the selected account card has the primary outline" |
| HEALTH-1 | The footer shows API and Banco `ok` through the dev proxy | `health` | `health` (unchanged) |
| HEALTH-2 | `/health` answers through the dev proxy | `smoke` | `smoke` (unchanged) |
| HEALTH-3 | The footer shows `degraded` when the API is unreachable | PWA suite test 10 | PWA suite test 10 (unchanged) |

### Dashboard (`/`)

| # | Flow | Before | After |
|---|---|---|---|
| DASH-1 | A new user's empty dashboard, and its two ways out | partial | `dashboard` "signing in lands on the dashboard, empty until there is an account" |
| DASH-2 | The hero: net worth, Em contas, one row per account | `dashboard` 24, 25, 014's 14 | unchanged |
| DASH-3 | The hero's 1 mês chip | `dashboard` 014's 14 | unchanged |
| DASH-4 | The hero's No ano and 12 meses chips with a year of history | GAP | `dashboard` "with more than a year of history the hero also shows the year's and twelve months' change" |
| DASH-5 | The hero's "investido" with a portfolio held, and its link | GAP | `investments` "PETR4 is added, bought 100, and its position shows with a value" |
| DASH-6 | A credit card in debt, and an account in another currency outside the total | GAP | `dashboard` "a credit card in debt counts against the total, an account in dollars stays out of it" |
| DASH-7 | The current month's totals | `dashboard` 25 | unchanged |
| DASH-8 | The month selector: back to a fixed month and its totals; no paging past the current month | partial (`ai` 29) | `dashboard` "the month selector shows a past month, what it kept, and both sides of its breakdown" |
| DASH-9 | "X% do que entrou" | GAP | same test |
| DASH-10 | The breakdown by category, transfers excluded | `dashboard` 26 | unchanged |
| DASH-11 | The breakdown's Receitas side, and a month with nothing | GAP | `dashboard` "the month selector shows a past month…" |
| DASH-12 | Recentes, its empty message and its link to the list | partial | `dashboard` "signing in lands on the dashboard…", 25 |
| DASH-13 | A write elsewhere shows on the dashboard without a reload | `dashboard` 018 | unchanged |
| DASH-14 | The 12-month chart and the sparkline are drawn | `dashboard` 24, 014's 14 | unchanged |

### Transactions (`/transactions`)

| # | Flow | Before | After |
|---|---|---|---|
| TX-1 | An expense and an income are created with the category's sign | `transactions` 1 | unchanged |
| TX-2 | A Transferência asks Saída (the default) or Entrada, and an edit opens on its own | partial (`dashboard` 26) | `dashboard` 26; `transactions` "editing a transaction updates its row", "the form checks what it can before sending…" |
| TX-3 | Editing a transaction, into another kind of category too | partial | `transactions` "editing a transaction updates its row" |
| TX-4 | Deleting a transaction | GAP | `transactions` "deleting a transaction removes it from the list" |
| TX-5 | The date range narrows the list; a range with nothing says so | `transactions` 2 | unchanged |
| TX-6 | The account and category filters | GAP | `transactions` "the account and category filters narrow the list" |
| TX-7 | The list opens on the current month; with nothing ever recorded it says so differently | GAP | `transactions` "the list opens on the current month, and an empty ledger says so" |
| TX-8 | More than 50 rows page 50 at a time | GAP | `transactions` "a list longer than a page pages 50 at a time" |
| TX-9 | The form's own checks, categories grouped by kind, a typed minus ignored, Cancelar | GAP | `transactions` "the form checks what it can before sending, and Cancelar writes nothing" |
| TX-10 | A transaction the API refuses (a date out of range) shows the API's sentence | GAP | **bug 2**: `transactions` "a date the API refuses is explained in Portuguese" (`fixme`) |
| TX-11 | Opened from an import: only its rows, and Mostrar todos | partial | `import` "an OFX is uploaded, reviewed, committed and its rows appear in the list" |
| TX-12 | Opened from an account: only its rows, whatever the dates | GAP | `accounts` "the Lançamentos tab lists the account's own rows and leads to all of them" |
| TX-13 | A subcategory is offered as "Principal / Sub" | GAP | `categories` "a category and a subcategory are created, renamed and deleted" |
| EXP-1 | Exportar CSV: BOM, `;`, decimal comma, quoting, named by the range | `export` (current month) | `export` "Exportar CSV downloads the rows the filters select, as Excel pt-BR reads them" (fixed dates) |
| EXP-2 | The export follows the filters, an import's included | GAP | same; `import` "an OFX is uploaded…" (`lancamentos.csv`) |
| EXP-3 | Nothing to export: the button is disabled | GAP | `export` |

### Accounts (`/accounts`)

| # | Flow | Before | After |
|---|---|---|---|
| ACC-1 | No accounts: the empty state creates the first, Cancelar writes nothing | GAP | `accounts` "a new user creates the first account from the empty state" |
| ACC-2 | Creating an account, with or without an opening balance | `support.createAccount` | unchanged; `accounts` "the tab is in the address…" (empty balance is zero) |
| ACC-3 | Creating refused: a name in use, a blank name, a currency that is not ISO, a balance that is not a number | GAP | `accounts` "an account the form or the API refuses says why, under the field" |
| ACC-4 | The cards: balance, type, last import (none, in review, its day), total | partial | `accounts` "a new user creates…", "Detalhes da conta…"; `import` "the account shows the import in its card…" |
| ACC-5 | The Lançamentos tab: latest rows, empty message, link to all of the account's | GAP | `accounts` "the Lançamentos tab…", "a new user creates…" |
| ACC-6 | Detalhes da conta: editing, and saving again unchanged | GAP | `accounts` "Detalhes da conta edits the name, the type and the opening balance" |
| ACC-7 | Deleting an account, and the refusal while it holds transactions or a statement in review | GAP | `accounts` "an account is deleted only once nothing holds it" |
| ACC-8 | The tab is in the address; `/accounts` and `/import` open the first account | partial | `accounts` "the tab is in the address: a reload and another account keep it" |

### Import (`/accounts/$id?tab=import`)

| # | Flow | Before | After |
|---|---|---|---|
| IMP-1 | OFX uploaded, reviewed, committed, listed; the tab names its account and the list steps aside | `import` 63 | `import` "an OFX is uploaded, reviewed, committed and its rows appear in the list" |
| IMP-2 | The same OFX again: every row a duplicate, commit disabled, discard | `import` 64 | unchanged |
| IMP-3 | CSV mapped with a live preview that names what is missing | `import` 65 | `import` "a CSV is mapped with a live preview, reviewed and committed" |
| IMP-4 | Undo from the done step, with its confirmation | `import` 66 | unchanged |
| IMP-5 | `.xlsx` mapped and committed; a new number format re-read by the API | `import` 011's 18 | `import` "a spreadsheet is mapped, reviewed and committed (bb-extrato.xlsx)" |
| IMP-6 | `.xls` mapped and committed; the lines above the table are named | GAP | `import` "… (bb-extrato-2026-08.xls)" |
| IMP-7 | A review in progress is found from another account and from `/import`; each history is its own | `import` 015's 20 | `import` "the account shows the import in its card, and a review in progress is found from anywhere" |
| IMP-8 | Review: let a duplicate in, leave a row out, refile a row, the categories per sign, filter by status, invalid rows with their reasons; Windows-1252 and a preamble | GAP | `import` "the review lets a duplicate in, leaves a row out, refiles one and filters by status" |
| IMP-9 | Debit and credit in separate columns, two-digit years | GAP | `import` "a statement with debit and credit columns and two-digit years" |
| IMP-10 | A card statement whose purchases are positive (inverted sign) | GAP | `import` "a card statement whose purchases are positive reads with the sign inverted" |
| IMP-11 | The date format is declared: changing it changes the live preview; each invalid row names its reason | GAP | `import` "the date format changes the live preview, and every invalid row names its reason" |
| IMP-12 | A mapping saved as a template, applied on the next file; a name in use refused | GAP | `import` "a mapping saved as a template fills the next mapping" |
| IMP-13 | A file without a header row; another delimiter | GAP | `import` "a file without a header row, and a delimiter typed by hand" |
| IMP-14 | The next month's OFX: the overlap is duplicate, a foreign-currency row is invalid, a merchant seen before keeps its category | GAP | `import` "next month's statement: the overlap is duplicate, a dollar row is invalid, a merchant keeps its category" |
| IMP-15 | More than 100 staged rows page in the review | GAP | `transactions` "a list longer than a page pages 50 at a time" |
| IMP-16 | Refused files: not an OFX, no transactions, not a spreadsheet, over 5 000 rows, over 2 MB | GAP | `import` "a file the import cannot take is refused with the reason"; **bug 4**: "a file over 2 MB is refused with its size written in Portuguese" (`fixme`) |
| IMP-17 | The history: undo (and cancel it), continue and discard a statement in review; Nova importação, Recomeçar, Voltar | GAP | `import` "the history undoes a batch, and resumes or discards a statement in review" |
| IMP-18 | A second tab's upload while a statement is in review elsewhere | GAP | `import` "an upload from a tab that missed a review started elsewhere is sent to that review" |

### Categories (`/categories`)

| # | Flow | Before | After |
|---|---|---|---|
| CAT-1 | The default categories by kind, the kind filter, an accent-blind search | GAP | `categories` "the default categories are grouped by kind, filtered by kind and found without accents" |
| CAT-2 | A category and a subcategory created, renamed in place and deleted; the kind follows the parent; a search keeps a matching subcategory's parent | GAP | `categories` "a category and a subcategory are created, renamed and deleted" |
| CAT-3 | Refused: a name in use, a category with subcategories or transactions; a kind left empty and filled again | GAP | `categories` "a name in use and a category with transactions are refused with the reason" |
| CAT-4 | Use over the last 12 months: count, total, share, rollup; Só as sem uso | GAP | `categories` "each category's use over the last 12 months, its share, and the unused ones" |
| CAT-5 | A refusal the API answers with a 400 (a blank name, a parent made a child) | GAP | **bug 3**: `categories` "a refusal the API sends as a 400 is explained in Portuguese" (`fixme`) |

### AI and settings (`/settings`, the import, the dashboard card)

| # | Flow | Before | After |
|---|---|---|---|
| AI-1 | Sugerir com IA, AI on (fake provider) | `ai` 28 | `ai` "with AI on, "Sugerir com IA" changes the preview rows in place (fake provider)" |
| AI-2 | Gerar análise (fake provider) | `ai` 29 | `ai` ""Gerar análise" on the dashboard fills the card…" |
| AI-3 | AI off: both buttons disabled, each saying why | GAP | `ai` "with AI off, "Sugerir com IA" and "Gerar análise" are disabled and say why" |
| AI-4 | Asking again leaves nothing to suggest and costs nothing; an unusable answer leaves the rows on the default | GAP | `ai` 28; "an answer the AI gets wrong leaves every row on its default" |
| AI-5 | Regenerar; an analysis stays readable with AI off | GAP | `ai` 29 |
| AI-6 | AI switched off in another tab: the import's request is refused with the reason | GAP | `ai` "AI switched off in another tab: the suggestion is refused with the reason" |
| SET-1 | The AI switch, on and back off, kept after a reload | partial | `ai` "the AI switch is saved both ways, beside its spend and what it sends" |
| SET-2 | This month's AI spend line (0, 1 and 2 calls) | partial | same; `ai` 28, 29 |
| SET-3 | What is sent to the AI provider, spelled out | GAP | same |
| SET-4 | Excluir minha conta | `delete-account` 12 | `delete-account` "a user deletes their account…" |
| SET-5 | The delete waits for the exact e-mail; the danger zone says what goes | GAP | same |
| SET-6 | The delete refused because the session ended in another tab; nothing deleted, no second set of categories on returning | GAP | `delete-account` "a delete from a tab whose session ended in another one says so, and deletes nothing" |

### Market data, investments, returns

| # | Flow | Before | After |
|---|---|---|---|
| MKT-1 | Register a ticker, find it, refuse it twice; each provider's coverage; a Binance pair in capitals | `market-data` | `market-data` "registers a ticker, finds it by search, and refuses it twice" |
| MKT-2 | A manual sync on the fakes, by provider | `market-data` 26 | unchanged |
| MKT-3 | Registration refused under its fields, on `/market-data` and in the investments' form | GAP | `market-data` "a registration a provider cannot price is refused under the field that is wrong"; `investments` "an asset found in the catalogue…" |
| MKT-4 | A search with no match | GAP | `market-data` "a search with no match says so" |
| INV-1 | PETR4 added, bought, valued; positions and total | `investments` 31 | unchanged |
| INV-2 | A dividend raises Proventos; a movement's note | `investments` 32 | `investments` "a dividend raises Proventos and leaves the quantity alone" |
| INV-3 | US$, kept after a reload, the asset's page too | `investments` 016's 15 | `investments` "US$ shows the position and the total at the latest dollar, and survives a reload" |
| INV-4 | Deleting the buy hides the position; Mostrar ativos sem posição; no cards | `investments` 33 | `investments` "deleting the buy makes the position disappear from the list" |
| INV-5 | An empty portfolio | GAP | `investments` "an empty portfolio says so, with no returns to show" |
| INV-6 | Adding from the catalogue search; no match; already held | GAP | `investments` "an asset found in the catalogue is added once, and removed once nothing is recorded on it" |
| INV-7 | Removing an asset: refused while it has movements | GAP | same |
| INV-8 | A movement refused under its field; the fields per kind | GAP | `investments` "a movement the rules refuse is explained under its field" |
| INV-9 | Sell, JCP, split and an edit change the position; a delete that uncovers a sell is refused | GAP | `investments` "a sell, a JCP, a split and an edit move the position; a delete that uncovers a sell is refused" |
| INV-10 | Sem cotação before the first sync | GAP | `investments` "a new ticker shows "Sem cotação" until a sync prices it" |
| INV-11 | O que mais contribuiu, Patrimônio investido and its allocation, the value chart | partial | `investments` 31 |
| RET-1 | TWR, XIRR, the chart, 12 meses, the asset's own page | `returns` 36 | `returns` "a buy 30 days back shows a non-zero TWR and the comparison chart" |
| RET-2 | The hero on `/investments` | `returns` 016's 14 | same |
| RET-3 | No ano; a custom period, one day in the singular; a custom period refused | GAP | same |
| RET-4 | Nothing held, or nothing valued: nothing to measure | GAP | `returns` "with nothing held, the returns page has nothing to measure"; `investments` "a new ticker shows "Sem cotação"…" |
| RET-5 | The benchmark rows and a chart reference toggled | partial | `returns` 36 |

### Bugs the new tests found

Each is kept as a `test.fixme` with its reason, and none is fixed here.

| # | Bug | Where | Test |
|---|---|---|---|
| 1 | An address that matches no page shows TanStack Router's English "Not Found": the router has no `notFoundComponent`. | web, `router.ts` | `navigation` "an address that matches no page answers in Portuguese" |
| 2 | A transaction the API refuses with a 400 shows the problem's English title ("One or more validation errors occurred.") instead of its pt-BR field message: `TransactionsPage` renders `error.message`. | web, `TransactionsPage.tsx` | `transactions` "a date the API refuses is explained in Portuguese" |
| 3 | The same on `/categories` for a blank name and for a main category with subcategories made a child. | web, `CategoriesPage.tsx` | `categories` "a refusal the API sends as a 400 is explained in Portuguese" |
| 4 | The 2 MB refusal reads "O arquivo tem 2.1 MB": the API formats the size under `InvariantGlobalization`, so the pt-BR sentence carries a decimal point. | API, `ImportEndpoints.cs` | `import` "a file over 2 MB is refused with its size written in Portuguese" |
| 5 | After the session ends in another tab, a write shows "Request failed (401)": a 401 has no problem body and every page but `DeleteAccount` falls back to the client's English message. | web, `api/finance.ts` and the pages' error paths | `auth` "a write after the session ended in another tab is explained in Portuguese" |
| 6 | Sair from a tab whose session already ended shows "Não foi possível sair." and stays: the logout answers 401 and the sidebar treats any non-2xx as a failure. **(review)** | web, `ProtectedLayout.tsx` | `auth` "Sair from a tab whose session already ended lands on the login page" |

### Not E2E-reachable

| Flow | Why |
|---|---|
| The Google sign-in round trip | Google's consent screen cannot be driven by Playwright. `dev-login` stands in for it, and the callback's outcomes are reached through their `/login?error=` addresses (AUTH-4). |
| The nightly market-data sync and snapshot rebuild on their schedule | A cron in the API process; E2E runs with `MarketData__ScheduledSync=false` and triggers the same job manually (MKT-2). |
| The monthly AI analysis job's schedule | Same: the on-demand path (AI-2) runs the same job. |
| A sync refused by the ten-minute window, and "Nenhuma sincronização ainda" / "Nenhum ativo cadastrado ainda" | The fakes have no window, a run lasts milliseconds while the button is disabled, and the sync history and the catalogue are shared by every test. |
| A provider failure listed under a sync run | The fake providers never fail. |
| A stale price ("Cotação desatualizada") | The fakes always close on the sync's day. |
| No dollar rate synced ("Sem cotação do dólar sincronizada") | Every fake sync stores USDBRL, and benchmarks are shared by every test. |
| Returns in US$, an asset's FX split, and the benchmarks' figures | The fakes store one benchmark point per sync day, so whether a period's first day has a rate depends on the database's history, not on the test. |
| The AI budget spent (402), the provider timing out (504) or failing (502), an analysis that fails, a 409 from another tab's generation | The fake provider always answers at once, and a call costs cents against R$ 15. |
| An analysis for a month that has not begun | The month selector stops at the current month. |
| A page failing to load ("Não foi possível carregar…") and the in-flight states ("Carregando…", "Verificando…", "Gerando…") | Needs the API to fail or hang mid-suite; it is shared by every test, and an in-flight state lasts milliseconds. |
| The footer's `degraded` / `unreachable` on the dev server | Same; the PWA suite covers the unreachable API (HEALTH-3). |
| Storage that cannot be read (theme, currency choice) | A private window's blocked storage is not something a test context offers. |
| A password-protected spreadsheet | Needs an encrypted workbook fixture; the API's integration tests cover it. |
| Dropping a file on the zone | Playwright's synthetic drop is not an operating-system drag; it reaches the same handler as Escolher arquivo, which E2E drives. |
| A row with both a debit and a credit, and other parser edge cases | Each is a line of a file; the parsers' unit tests hold them, and E2E drives the documented samples. |

## Test plan

E2E only. Every test signs in as its own user; nothing is shared but the market-data
catalogue (decision 6).

| File | Tests before → after | New or extended |
|---|---|---|
| `auth.spec.ts` | 3 → 6 | AUTH-1 to AUTH-5 |
| `navigation.spec.ts` (new) | 0 → 4 | NAV-1 to NAV-4 |
| `theme.spec.ts` | 2 → 3 | THEME-2 |
| `dashboard.spec.ts` | 5 → 8 | DASH-1, 4, 6, 8, 9, 11, 12 |
| `transactions.spec.ts` | 3 → 9 | TX-2 to TX-10, IMP-15 |
| `export.spec.ts` | 1 → 1 | EXP-1 on fixed dates, EXP-2, EXP-3 |
| `accounts.spec.ts` (new) | 0 → 6 | ACC-1 to ACC-8, TX-12 |
| `import.spec.ts` | 6 → 18 | IMP-1, 3, 5 to 18, TX-11 |
| `categories.spec.ts` (new) | 0 → 5 | CAT-1 to CAT-5, TX-13 |
| `ai.spec.ts` | 2 → 6 | AI-3 to AI-6, SET-1 to SET-3 |
| `delete-account.spec.ts` | 1 → 2 | SET-5, SET-6 |
| `market-data.spec.ts` | 2 → 4 | MKT-1, MKT-3, MKT-4 |
| `investments.spec.ts` | 4 → 9 | INV-2 to INV-11, DASH-5, MKT-3 |
| `returns.spec.ts` | 1 → 2 | RET-3 to RET-5 |
| `smoke.spec.ts`, `health.spec.ts` | 3 → 3 | unchanged |

`support.ts` gains `fixture`, `uploadStatement`, `ofxStatement`, `importStep`, `commitImport`,
`uniqueTicker`, `utcDaysAgo`, `localToday`, `localMonthDay` and `showDashboardMonth`, and
`createAccount` takes a type and a currency, so the specs stop keeping their own copies.

## Phase 2 — the tests E2E makes redundant

### Policy

Gustavo chose "remove only the redundant tests". A test file is classified:

- **(a) fully redundant:** every scenario in it is exercised by a named E2E test, which drives the
  same inputs to the same observable outcome. The file goes.
- **(b) partly redundant:** some of its scenarios are; only those test cases go, and the rest stay.
- **(c) not E2E-reachable**, with the reason. Everything stays.

A scenario counts as covered only if an E2E test drives the same inputs to the same observable
outcome; "the page renders" covers no rule. Where a unit test was the only thing pinning an
outcome a person can see (a pt-BR sentence, a label, a value typed back), the assertion was first
added to E2E (024 CP13 and CP14), and only then does the unit test count as redundant.

Never deleted, even where an E2E test touches the same screen, because E2E does not reproduce
their inputs:

- the returns module: TWR, XIRR, FX decomposition, snapshot builder and rebuild, benchmark
  accumulation (ADR-017);
- money and decimal rounding: `DecimalMath`, `Money`, amount and sign rules, the web's `decimal`,
  `money`, `currency`, `rates`, `netWorth` and `Amount`;
- tenancy and security: query filters, isolation between users, CSRF/Origin, 401s, the completeness
  of a user's deletion;
- parsers and their edge cases: CSV, OFX, XLS/XLSX, encodings, `FieldParser`, the duplicate
  matcher, the web's `csvPreview` and `fileNameFrom`;
- time zones and dates;
- provider adapters and their resilience against canned HTTP;
- migrations and the EF model;
- the test infrastructure's self-checks;
- any test that is the only one pinning an exact pt-BR message or a status code E2E does not
  assert, and (decision 13) an endpoint's status code, `Location` or response shape.

### Status: classified, deletions not applied

The deletions below were approved, but this session's permission classifier refused deleting
test files ("Irreversible Local Destruction"), and the refusal covers the same outcome by any
other route. Nothing has been deleted. The lead applies the plan below, or grants the
permission and has it applied. Every E2E assertion the plan relies on is already committed.

### Web — `web/src/**/*.test.ts(x)`

Before: 41 files, 298 tests. After the plan: 32 files, 170 tests (128 removed: 9 files whole,
and 82 cases out of 16 more).

| File | Class | Planned deletion | Covered by (E2E) | Kept, and why |
|---|---|---|---|---|
| `App.test.tsx` (1) | a | whole file | `smoke` "application shell renders" | — |
| `components/EmptyState.test.tsx` (3) | a | whole file | `transactions` "the list opens on the current month, and an empty ledger says so", "narrowing the date range…" | — |
| `routes/ImportPage.test.tsx` (2) | a | whole file | `import` "the account shows the import in its card…" (in review); `accounts` "the tab is in the address…" (nothing in review) | — |
| `routes/CategoriesPage.test.tsx` (9) | a | whole file | `categories`, all four passing tests (kind labels, Transfer category created, tree, rollup and share, edit in place, Só as sem uso, search keeping a parent) | — |
| `components/market-data/AssetCatalogue.test.tsx` (4) | a | whole file | `market-data` "registers a ticker…", "a registration a provider cannot price…" | — |
| `components/import/MappingStep.test.tsx` (3) | a | whole file | `import` "the date format changes the live preview…", "the review lets a duplicate in…" (description order), "a CSV is mapped…" (Continuar waits), "a statement with debit and credit columns…", "a mapping saved as a template…" | — |
| `components/import/PreviewStep.test.tsx` (8) | a | whole file | `import` 64, "the review lets a duplicate in…" (counts, invalid rows, duplicate ticked, categories per sign), "next month's statement…" (history row unmarked); `ai` 28 and "with AI off…" | — |
| `components/TransactionForm.test.tsx` (9) | a | whole file | `transactions` 1, "editing a transaction…" (Entrada opens on its own), "the form checks…" (zero, groups by kind, Saída default, no direction for an expense, a typed minus ignored); `dashboard` 26 (both directions) | — |
| `routes/AssetPage.test.tsx` (7) | a | whole file | `investments` 31, 32 (movements in Portuguese, note), US$ (asset page in dollars), "a sell, a JCP…", "a movement the rules refuse…", "an asset found in the catalogue…"; `navigation` "an account or an asset that does not exist…" | — |
| `routes/AccountsPage.test.tsx` (14) | b | 11: "says so when the account in the address does not exist", the 8 of "creating and editing", the 2 of "the Lançamentos tab" | `navigation`; `accounts` (all six) | 3: the cards' figures with the credit card's destructive colour (colour), the year of a last import from another year (dates), `aria-current` on the opened card |
| `routes/TransactionsPage.test.tsx` (4) | b | 3: export by the filter bar, export of an import, disabled while empty | `export`; `import` "an OFX is uploaded…" | 1: an export the API refuses (the UI never sends a filter the API refuses) |
| `routes/DashboardPage.test.tsx` (10) | b | 7: month query parameter, kind toggle, empty state, accounts with zero months, analysis card's month, investments-only series, Em contas and investido | `dashboard` "signing in…", "the month selector…"; `ai` 29; `investments` 31 | 3: the credit card's colour, the chips' tones on a long series, the chips and chart a short or single-point series cannot give (unit inputs) |
| `routes/SettingsPage.test.tsx` (13) | b | 8: in the navigation, the disclosure, AI on, the switch holding from the click, the danger zone's place and copy, the exact e-mail, the delete landing on `/login`, the 401 row of "shows %s and stays on the page" | `navigation`; `ai` "the AI switch is saved both ways…" (its `setChecked` fails on a switch that lags the click); `delete-account` (both) | 5: a non-zero spend's rounding and plural, a refused PATCH, the switch while the PATCH is in flight, the 409 and 500 rows |
| `routes/ProtectedLayout.test.tsx` (10) | b | 9: everything but the in-flight test | `auth` (all passing tests), `navigation` "the sidebar…" | 1: nothing renders while `/api/auth/me` is in flight (lasts milliseconds) |
| `routes/HealthRoute.test.tsx` (2) | b | 1: the healthy API | `health` | 1: a 503 reporting the database unreachable (the shared database cannot go down mid-suite) |
| `routes/InvestmentsPage.test.tsx` (23) | b | 12: in the navigation, positions at zero hidden and shown, nothing held, every position closed, added from the catalogue, no match, registered in one step, a 400 under its field and a 409, nothing valued in the period, no returns while nothing is held, no cards while nothing is open, the R$ / US$ toggle remembered | `navigation`; `investments` 31, 33, US$, "an empty portfolio…", "an asset found in the catalogue…", "a new ticker shows "Sem cotação"…" | 11: figures for two assets and a USD asset with a stale price, the hero past a year and its tiles' tones, the period buttons' order and "em N dias", the ranking of eight results, two classes' allocation, every figure in dollars, the missing rate, the hero in dollars (all need inputs the fakes cannot give) |
| `routes/ReturnsPage.test.tsx` (21) | b | 8: linked from the positions page, a year or less without an annual rate, one day in the singular, nothing held, the period refetched, a custom range's 400, no FX split for a BRL asset, an asset not found | `returns` (both); `navigation` | 13: timing effect tones, past a year, "Sem dados", toggles' composition, benchmark figures, per-asset table, the FX split, in dollars (returns module and fake-data limits) |
| `routes/MarketDataPage.test.tsx` (5) | b | none | `market-data` 26 overlaps the run list | 5: a partial failure and a missing key (fakes never fail), no runs yet (shared), polling while Running (a fake run ends at once), a 429 |
| `components/import/AccountImport.test.tsx` (13) | b | 8: upload to the account named by the tab, this account's imports only, none yet, the other account in review, resuming, the 403 row, the spreadsheet's mapping without a delimiter, the format re-preview | `import` "an OFX…", "the account shows the import…", "a spreadsheet…"; `ai` "AI switched off in another tab…" | 5: the dropped file, the singular "1 categoria sugerida pela IA.", the 402, 504 and 502 rows |
| `components/import/UndoButton.test.tsx` (2) | b | 1: the confirmation naming the count | `import` 66, "the history undoes a batch…" | 1: Cancelar with the singular "excluir 1 lançamento?" |
| `components/ThemeToggle.test.tsx` (4) | b | 3: Escuro remembered, Sistema followed, the toolbar colours | `theme` (first two tests) | 1: storage that cannot be read |
| `components/investments/MovementForm.test.tsx` (7) | b | 2: a dividend's fields, a split's fields | `investments` "a movement the rules refuse…", "a sell, a JCP, a split…" | 5: the exact total preview (decimal rounding), a USD total, the payload's numbers, an edit typed back with commas, a message for a field not on screen |
| `components/ai/AnalysisCard.test.tsx` (10) | b | 3: Gerar análise when there is none, disabled with AI off, readable with AI off | `ai` 29, "with AI off…" | 7: polling, a Failed analysis, HTML as text (security), the in-progress state while regenerating, the 403 and 402 rows, a 409 |
| `lib/refusal.test.ts` (5) | b | 2: no sentence when every field is shown, the detail when no field is named | `accounts` "an account the form or the API refuses…" | 3: a field not on screen moved above the form, several messages joined, a non-error |
| `lib/categoryTree.test.ts` (4) | b | 3: largest first then by name, each kind to itself, `fold` | `categories` (defaults by name, use first, sections, accent-blind search) | 1: the rollup with subcategories ordered by size |
| `glass.node.test.ts` (4) | b | 1: the `border-primary` row | `theme` "the selected account card has the primary outline" | 3: `border-destructive/40`, `border-2`, `border-dashed` |
| `components/Amount.test.tsx` (4) | b | none | every amount E2E asserts (sign glyph, two places) | all: money display (policy) |
| `components/ai/Markdown.test.tsx` (2) | b | none | `ai` 29 (headings) | all: lists, emphasis, code and escaping (security) |
| `lib/csvPreview.test.ts` (10) | b | none | `import` live previews | all: parser (policy) |
| `lib/currency.test.ts` (13) | b | none | `investments` US$ | all: money (policy) |
| `lib/decimal.test.ts` (13) | b | none | `investments` movement totals | all: decimal (policy) |
| `lib/download.test.ts` (9) | b | none | `export`, `import` (file names) | all: `fileNameFrom` edge cases (parser), the URL released |
| `lib/labels.test.ts` (6) | b | none | labels E2E reads | all: no case is fully reproduced (order, unknown keys, Posições da carteira) |
| `lib/money.test.ts` (6) | b | none | money E2E reads | all: money and dates (policy) |
| `lib/months.test.ts` (8) | b | none | `dashboard` month labels | all: dates (policy) |
| `lib/netWorth.test.ts` (4) | b | none | `dashboard` chips | all: cents arithmetic, January (policy) |
| `lib/queryClient.test.ts` (4) | b | none | `dashboard` 018 (a write marks the cache stale) | all: the other cache branches are not observable |
| `lib/rates.test.ts` (5) | b | none | `returns` rates | all: rate formatting (policy) |
| `lib/serviceWorker.test.ts` (2) | b | none | PWA suite test 8 | all: decision 11 |
| `pwa.test.ts` (6) | b | none | PWA suite test 11 | all: decision 11 |
| `sw/routing.test.ts` (9) | b | none | PWA suite tests 9, 10, 12 | all: decision 11 |

### API — `api.tests/Unit`

53 test files and 2 harnesses (`AiProviderHarness`, `MarketDataProviderHarness`), all kept.

| Files | Class | Why kept |
|---|---|---|
| `TimeWeightedReturn`, `MoneyWeightedReturn`, `XirrFlows`, `TimingEffect`, `FxDecomposition`, `ReturnSeries`, `PortfolioAggregation`, `BenchmarkAccumulator`, `SnapshotBuilder`, `DecimalMath` | c | the returns module (ADR-017); E2E checks one ordinary TWR and XIRR end to end |
| `ReturnsPeriod` | b | YTD, 12 months and custom clamping are E2E-exercised (`returns` 36); kept by ADR-017, and sampling is unit-only |
| `Money`, `TransactionRules`, `MovementRules`, `PositionCalculator` | b | the rules and figures E2E reaches (an ISO code, a transfer's either sign, oversold, average cost, realised, split) are kept by the money and amount/sign policy; rounding, currency mismatch and same-day order are unit-only |
| `AiCost` | c | the budget and the cost's rounding: E2E never reaches the budget, and asserts no cost (money) |
| `OfxParser`, `CsvStatementParser`, `CsvRowInterpreter`, `FieldParser`, `SpreadsheetReader`, `SignResolver`, `DescriptionNormalizer`, `DuplicateMatcher`, `TransactionCsv`, `AiCategorisationParse` | b | the documented samples drive their common paths through E2E; kept as parsers and writers (policy), whose edge cases are unit-only |
| `CategorySuggester` | b | history and sign defaults are E2E-exercised (`import` next month); wrong-kind history, missing defaults, zero and transfers are unit-only |
| `AnalysisInputBuilder` | b | the month's expense reaches the fake's text (`ai` 29); three months, top merchants and zero months are unit-only |
| `BrapiProvider`, `BcbSgsProvider`, `CoinGeckoProvider`, `BinanceProvider`, `TwelveDataProvider`, `AnthropicAiProvider`, `OpenAiProvider`, `MarketDataResilience` | c | adapters against canned HTTP; E2E runs on fakes |
| `AiOptions`, `MarketDataOptions`, `ReturnsOptions`, `MarketDataSetup`, `PriceProviderRegistry`, `MonthlyAnalysisPrompt`, `AiCategorisationRequest`, `GoogleUserInfo`, `MarketDataSyncJob`, `SyncErrorText` | c | configuration, wiring, prompts, the Google claim, the schedule, and failure texts the fakes never produce |
| `AiTypes`, `InvestmentsTypes`, `MarketDataTypes`, `ReturnsTypes`, `FakeAiProvider`, `FakeMarketDataProviders`, `HarnessSanity` | c | no-float guards and the test infrastructure's self-checks |

### API — `api.tests/Integration`

61 test files and 9 fixtures (`DashboardFixtures`, `IdentityApiFactory`, `ImportFixtures`,
`InvestmentsApi`, `MarketDataApi`, `MarketDataSyncFakes`, `PostgresFixture`, `ReturnsFixtures`,
`TransactionsFixtures`).

| File | Class | Planned deletion | Covered by (E2E) | Kept, and why |
|---|---|---|---|---|
| `DefaultCategoriesTests` (3) | b | 2: "Signing_in_again_does_not_seed_a_second_set", "A_new_user_has_a_top_level_transfer_category" | `delete-account` "a delete from a tab whose session ended…" (nine categories after signing in again); `categories` "the default categories are grouped…" | 1: the exact set, all top level, every row the user's (tenancy) |
| `AccountEndpointTests` (11) | b | none | `accounts` (create, edit, refusals, deletes, opening balance) | status codes, `Location`, the account shape (decision 13), a committed import holding the account, another user's account |
| `CategoryEndpointTests` (10) | b | none | `categories` | every refusal's status and words, a grandchild and a kind the form never sends, another user's parent |
| `CategoryUsageTests` (4) | b | none | `categories` use | the window's boundaries (dates), `from`/`to` (API only), isolation |
| `TransactionEndpointTests` (15) | b | none | `transactions` | exact amounts (money), refusals the form never sends, boundaries (dates), paging contract, malformed filters, same-day order |
| `TransactionExportTests` (8) | b | none | `export`, `import` 63 | the content type, order, subcategory column, formula guard (writer), malformed filters, isolation, the round trip |
| `DashboardEndpointTests` (7) | b | none | `dashboard` | the summary's shape and flags (decision 13), a 400, the 401, isolation |
| `DashboardNetWorthTests` (9) | b | none | `dashboard` chips, `investments` 31 | month-ends and the window (dates), decimal columns, 400, 401, isolation |
| `DashboardSeriesTests` (5) | b | none | `dashboard` breakdown shares | zero-filling, the month's last day (dates), clamping, a child's rollup, shares summing to one (decimal) |
| `ImportEndpointTests` (19) | b | none | `import` | 409/413/422/400 codes, byte-identical amounts, truncation, manual rows outside the history, staging cleared, pagination parameters |
| `CsvImportEndpointTests` (7), `SpreadsheetImportEndpointTests` (5) | b | none | `import` CSV and spreadsheet tests | parsers (policy), nothing persisted by a preview, a column not in the file, isolation |
| `CsvTemplateEndpointTests` (4) | b | none | `import` "a mapping saved as a template…" | delete (no screen deletes a template), the 409 code, invalid templates the form never sends |
| `InvestmentAssetEndpointTests` (5) | b | none | `investments` "an asset found in the catalogue…" | 409/400/404 codes, another user holding the same asset |
| `InvestmentMovementValidationTests` (4) | b | none | `investments` "a movement the rules refuse…", "a sell…" | 400 codes, an edit uncovering a sell, stored zeros, inclusive ranges |
| `MarketDataAssetEndpointTests` (6) | b | none | `market-data` | 409/400/401 codes, every invalid field at once, prices and benchmarks by range |
| `MarketDataSyncEndpointTests` (5) | b | none | `market-data` 26, `investments` "a new ticker…" | the 429s (no window on fakes), the last twenty runs |
| `AiAnalysisEndpointTests` (9), `AiSuggestEndpointTests` (7), `AiToggleEndpointTests` (4), `AiCategorisationTests` (6) | b | none | `ai` | 202/403/402/502/504/404/409 codes, the rung recorded on each row, batches, isolation, the 401 |
| `HealthEndpointTests` (3) | b | none | `health` | the 200, an unreachable database, the migration history |
| `InvestmentSummaryTests` (7) | b | none | `investments` 31 and US$ (positions, summary, allocation, the latest USDBRL) | FX per event, the calculator's figures, allocation summing to one, isolation, the rebuild (money, snapshots, tenancy) |
| `ReturnsEndpointTests` (7) | b | none | `returns` 36 (inception, No ano, 12 meses, custom and its refusal) | the returns module (ADR-017): YTD mid-year, clamping, sampling to 260 points, the 400's codes, isolation, the 401 |
| `InvestmentMovementEndpointTests` (4), `SnapshotRebuildTests` (5), `SnapshotRebuildAfterSyncTests` (4), `ReturnsAssetEndpointTests` (2), `ReturnsBenchmarkTests` (2) | c | — | — | snapshots and returns (ADR-017), the FX split, benchmarks by hand |
| `AuthSessionTests`, `GoogleCallbackTests`, `ForwardedHeadersTests`, `SessionKeyPersistenceTests`, `ImportSecurityTests`, `TransactionSecurityTests`, `UserOwnedEntityIsolationTests`, `UserOwnedQueryFilterTests`, `DashboardSqlTests`, `UserDeletionTests`, `UserDeletionJobTests` | c | — | — | tenancy and security (policy) |
| `AiPersistenceTests`, `ImportPersistenceTests`, `InvestmentsPersistenceTests`, `MarketDataPersistenceTests`, `TransactionPersistenceTests`, `ReferentialIntegrityTests`, `IdentitySchemaTests`, `MigrationStepTests` | c | — | — | the EF model, constraints and migrations (policy) |
| `AiBootTests`, `ReturnsBootTests`, `AiFakeProviderTests`, `MarketDataFakeProvidersTests`, `MarketDataSyncWiringTests`, `PostgresContainerTests` | c | — | — | boot checks, wiring and the infrastructure's self-checks |
| `AiGatewayTests`, `AiGatewayGateTests`, `AiGatewayTimeoutTests`, `AnalysisJobTests`, `AnalysisInputQueriesTests`, `MarketDataSyncTests`, `MarketDataSyncFailureTests` | c | — | — | the budget, timeouts and job lifecycle, what the AI is sent, and sync windows and failures: none reachable on the fakes |

### Summary

| Suite | Test files | (a) | (b) | (c) | Tests before | Tests after the plan |
|---|---|---|---|---|---|---|
| web (Vitest) | 41 | 9 | 32 | 0 | 298 | 170, in 32 files |
| API unit | 53 | 0 | 17 | 36 | 1 101 together (`dotnet test`) | 1 099 together, in the same 114 files |
| API integration | 61 | 0 | 24 | 37 | | |

### Proposed "Testing" note for `docs/ARCHITECTURE.md`

> **Testing.** E2E (Playwright, `web/e2e`) owns the user flows: every flow a person can drive
> through the screens has an E2E test against the real API and PostgreSQL, on the fake market-data
> and AI providers, with fixed dates. Unit and integration tests cover what E2E cannot reach: the
> returns module's mathematics (ADR-017), money and decimal rounding, tenancy and security,
> parsers and their edge cases, dates and time zones, provider adapters against canned HTTP, the EF
> model and migrations, and states the fakes or a shared database cannot produce. The API's
> integration tests also stay the contract of the hand-written client (ADR-002): status codes,
> headers and response shapes. A unit test that only repeats an E2E flow is removed; one that is
> the only pin on a pt-BR message, a status code or a shape stays. Spec 024 holds the inventory.

## End-to-end verification

Against an isolated PostgreSQL and API, never the owner's `financas`, with the fakes on
(decision 7):

```
docker run -d --rm --name e2e-coverage-pg -e POSTGRES_USER=dev -e POSTGRES_PASSWORD=dev -p 55441:5432 postgres:16-alpine
dotnet build api -o <scratch>/api-e2e
# from <scratch>/api-e2e, with ASPNETCORE_ENVIRONMENT=Development, MarketData__ScheduledSync=false,
# MarketData__FakeProviders=true, Ai__FakeProvider=true, App__Origin=http://localhost:5201 and the
# connection string to localhost:55441:
dotnet Finance.Api.dll --urls http://localhost:5101
cd web && API_URL=http://localhost:5101 npx vite --port 5201 --strictPort
cd web && BASE_URL=http://localhost:5201 npx playwright test     # twice in a row, both green
```

## Definition of done

- Every flow above maps to a passing E2E test, to a `test.fixme` naming a bug, or to the
  not-E2E-reachable list
- The full suite passes twice in a row on an isolated database
- `bash scripts/verify.sh` green
- Phase 2: every non-E2E test file classified; the planned deletions applied by the lead (not
  applied in this session, see Status)
- The (review) decisions confirmed or changed by a person
