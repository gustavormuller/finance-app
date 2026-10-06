# 024 — E2E coverage of the whole site

## Goal

Every flow a person can drive through the screens — create, edit, delete, filter, page,
import, export, toggle, register, sync, ask the AI, configure, sign in and out, and the
empty, error and refused states on the way — is exercised by at least one Playwright test
against the real API and a real PostgreSQL. A flow that cannot be driven through the
screens is listed, with the reason, so "not covered" is always a decision and never an
oversight.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | What counts as a flow | Something a person does or reads on a page, with an outcome they can see: a write and what it changes, a filter and what it hides, a refusal and its sentence, an empty state. Visual layout (phone width, colours, chart drawing) is not a flow, except the two E2E tests 012 already has. **(review)** |
| 2 | Where a new test goes | One spec file per area of the site: the existing files grow, and `accounts.spec.ts`, `categories.spec.ts` and `navigation.spec.ts` are new. A flow that touches two areas goes where its subject is (pagination of the list, driven through an import, is a transactions test). |
| 3 | Extend or add | An existing test is extended when the new assertions follow its own story (the `.xlsx` test also runs on the `.xls`); otherwise a new test. No near-duplicates. |
| 4 | Dates | Fixed dates and explicit filters, never "this month" by default: the transactions list is narrowed to the fixture's dates, and the dashboard is paged back to the fixture's month (`showDashboardMonth`). A feature defined relative to today (the dashboard's current month and its net-worth chips, the categories' last 12 months, the returns' 30-day fixture) gets dates computed from today at a safe distance from any boundary: the 15th of a month, or 20 days back. Nothing assumes which month it is. **(review)** |
| 5 | Each test's user | A fresh user per test (`devLogin` + `uniqueEmail`), so every test starts from the default categories and nothing else, and tests run in parallel. |
| 6 | Shared market data | The catalogue, prices and sync runs are shared by every user. A test that syncs, or that asserts a price is missing, runs in the serial syncing projects (`market-data` → `investments` → `returns`, `playwright.config.ts`); every other test runs in parallel in `chromium`. New tickers are unique per run (`uniqueTicker`). |
| 7 | Fakes | The API runs with `MarketData__FakeProviders=true` and `Ai__FakeProvider=true`: no request leaves the machine, and the figures are fixed (a close of 10 on the sync's day, one cent less per day back; USDBRL 0,05; the AI's answers derived from its input). |
| 8 | Fixtures | The documented sample statements of `samples/statements/` (README there lists each file's expected outcome) are copied into `web/e2e/fixtures/` under their own names, as `bb-extrato.xlsx` already was. Files a test needs only once (too large, too many rows, header-less, not an OFX) are built in the test. |
| 9 | A real bug found by a test | Not fixed here. The test stays, marked `test.fixme` with a one-line reason, and the bug is listed below. |
| 10 | Two tabs | A refusal a person reaches only with a second tab open (an import started in another tab, a session ended in another tab, AI switched off in another tab) is driven with a second page in the same browser context: it is the person's own browser, and the refusal's sentence is what they would read. **(review)** |
| 11 | The PWA suite | `web/e2e-pwa` (`npm run e2e:pwa`, spec 022) stays separate: it builds and serves a production build and stops the server mid-run. It is counted in the inventory, not merged. |

## Out of scope

- Fixing the bugs the new tests found (listed below, each with a `test.fixme`)
- Visual checks beyond 012's two: phone width, colours, chart rendering
- Load, performance and accessibility audits
- Deleting unit or integration tests (Phase 2 classifies them; what is deleted is decided with the owner)
- The real Google sign-in, the nightly schedule, and the other flows listed as not E2E-reachable

## Inventory

The site's routes (`web/src/routeTree.tsx`): `/login`, and behind the sign-in `/`, `/transactions`,
`/import` (a redirect), `/accounts`, `/accounts/$accountId` (tabs `transactions`, `import`,
`details`), `/categories`, `/market-data`, `/investments`, `/investments/returns`,
`/investments/$assetId`, `/investments/$assetId/returns`, `/settings`. The shell around them
(`App.tsx`, `ProtectedLayout.tsx`) has the theme toggle, the sidebar with the sign-out, and the
health footer.

Before this spec: **33** tests in `web/e2e` (13 files), plus **5** in the separate PWA suite.

"Before" names the test that covered the flow when this spec was written; **GAP** means none did,
*partial* means a test touched the flow without asserting its outcome.

### Shell, sign-in, navigation

| # | Flow | Before |
|---|---|---|
| AUTH-1 | A signed-out visit to any protected page lands on `/login` | partial: `auth` "an unauthenticated visit to /" (only `/`) |
| AUTH-2 | Signed in, the sidebar shows the display name | `auth` "a signed-in visitor sees their display name on /" |
| AUTH-3 | Sair returns to `/login`, and the cookie is gone | `auth` "logging out returns to the login page…" |
| AUTH-4 | `/login?error=unverified\|cancelled\|auth_failed` explains itself; an unknown code or notice shows nothing | GAP |
| NAV-1 | The sidebar opens each of its six pages and marks the current one | GAP |
| NAV-2 | `/market-data` and `/investments/returns` open by address | partial (each reached by its own spec on the way) |
| NAV-3 | An account or asset id that does not exist says so (`/accounts/$id`, `/investments/$id`, `/investments/$id/returns`) | GAP |
| NAV-4 | An address that matches no page | GAP |
| THEME-1 | Claro and Escuro survive a reload, the toolbar colour follows | `theme` "the theme chosen is kept across a reload" |
| THEME-2 | Sistema follows the operating system while the page is open | GAP |
| THEME-3 | The selected account card has the primary outline | `theme` "the selected account card has the primary outline" |
| HEALTH-1 | The footer shows API and Banco `ok` through the dev proxy | `health` |
| HEALTH-2 | `/health` answers through the dev proxy | `smoke` |
| HEALTH-3 | The footer shows `degraded` when the API is unreachable | PWA suite test 10 (server stopped) |

### Dashboard (`/`)

| # | Flow | Before |
|---|---|---|
| DASH-1 | A new user's empty dashboard, and its two ways out | partial: `dashboard` test 24 (no links followed) |
| DASH-2 | The hero: net worth, Em contas, one row per account | `dashboard` tests 24, 25, 014's test 14 |
| DASH-3 | The hero's chips: 1 mês | `dashboard` 014's test 14 |
| DASH-4 | The hero's chips with a year of history: No ano and 12 meses | GAP |
| DASH-5 | The hero's "investido" with a portfolio held | GAP |
| DASH-6 | A credit card in debt, and an account in another currency shown outside the total | GAP |
| DASH-7 | The current month's totals | `dashboard` test 25 |
| DASH-8 | The month selector: back to a fixed month and its totals; no paging past the current month | partial: `ai` test 29 pages back |
| DASH-9 | "X% do que entrou" | GAP |
| DASH-10 | The breakdown by category, transfers excluded | `dashboard` test 26 |
| DASH-11 | The breakdown's Receitas side, and a month with nothing | GAP |
| DASH-12 | Recentes, its link to the list, and its empty message | partial: `dashboard` test 25 (list only) |
| DASH-13 | A write elsewhere shows on the dashboard without a reload | `dashboard` 018's test |
| DASH-14 | The 12-month chart and the sparkline are drawn | `dashboard` tests 24 and 014's 14 (visible) |

### Transactions (`/transactions`)

| # | Flow | Before |
|---|---|---|
| TX-1 | An expense and an income are created with the category's sign | `transactions` test 1 |
| TX-2 | A Transferência asks Saída or Entrada | `dashboard` test 26 |
| TX-3 | Editing a transaction | partial: `transactions` test 3 (amount only) |
| TX-4 | Deleting a transaction | GAP |
| TX-5 | The date range narrows the list; a range with nothing says so | `transactions` test 2 |
| TX-6 | The account and category filters | GAP |
| TX-7 | The list opens on the current month; with nothing ever recorded it says so differently | GAP |
| TX-8 | More than 50 rows page 50 at a time | GAP |
| TX-9 | The form's own checks (category, amount, zero, number, date, description), and Cancelar | GAP |
| TX-10 | A transaction the API refuses (a date out of range) shows the API's sentence | GAP |
| TX-11 | Opened from an import: only its rows, and Mostrar todos | partial: `import` test 63 follows the link |
| TX-12 | Opened from an account: only its rows | GAP |
| TX-13 | A subcategory is offered as "Principal / Sub" | GAP |
| EXP-1 | Exportar CSV: BOM, `;`, decimal comma, quoting, named by the range | `export` test 21 (on the current month) |
| EXP-2 | The export follows the filters | GAP |
| EXP-3 | Nothing to export: the button is disabled | GAP |

### Accounts (`/accounts`)

| # | Flow | Before |
|---|---|---|
| ACC-1 | No accounts: the empty state creates the first | GAP |
| ACC-2 | Creating an account, with an opening balance | `support.createAccount`, every spec |
| ACC-3 | Creating refused: a name in use, a currency that is not ISO, a blank name, a balance that is not a number; Cancelar | GAP |
| ACC-4 | The cards: balance, type, last import, total | partial: `import` test 20 (last import) |
| ACC-5 | The Lançamentos tab: latest rows, empty message, link to all of the account's | GAP |
| ACC-6 | Detalhes da conta: editing name, type, currency and balance | GAP |
| ACC-7 | Deleting an account, and the refusal while it holds transactions or an import | GAP |
| ACC-8 | The tab is in the address: a reload and another account keep it | partial: `import` test 20 |

### Import (`/accounts/$id?tab=import`)

| # | Flow | Before |
|---|---|---|
| IMP-1 | OFX uploaded, reviewed, committed, listed | `import` test 63 |
| IMP-2 | The same OFX again: every row a duplicate, commit disabled, discard | `import` test 64 |
| IMP-3 | CSV mapped with a live preview | `import` test 65 |
| IMP-4 | Undo from the done step, with its confirmation | `import` test 66 |
| IMP-5 | `.xlsx` mapped and committed | `import` 011's test 18 |
| IMP-6 | `.xls` mapped and committed; the lines above the table are named | GAP |
| IMP-7 | A review in progress is found from another account and from `/import` | `import` 015's test 20 |
| IMP-8 | Review: let a duplicate in, leave a row out, recategorise a row, filter by status, invalid rows with their reasons; a Windows-1252 file with a preamble | GAP |
| IMP-9 | Debit and credit in separate columns, two-digit years | GAP |
| IMP-10 | A card statement whose purchases are positive (inverted sign) | GAP |
| IMP-11 | The date format is declared: changing it changes the live preview; each invalid row names its reason | GAP |
| IMP-12 | A mapping saved as a template, applied on the next file | GAP |
| IMP-13 | A file without a header row; another delimiter | GAP |
| IMP-14 | The next month's OFX: the overlap is duplicate, a foreign-currency row is invalid, a merchant seen before keeps its category | GAP |
| IMP-15 | More than 100 staged rows page in the review | GAP |
| IMP-16 | Refused files: not an OFX, no transactions, not a spreadsheet, over 2 MB, over 5 000 rows | GAP |
| IMP-17 | The history: undo (and cancel the undo), continue and discard a statement in review; Nova importação, Recomeçar, Voltar | GAP |
| IMP-18 | A second tab's upload while a statement is in review elsewhere | GAP |

### Categories (`/categories`)

| # | Flow | Before |
|---|---|---|
| CAT-1 | The default categories by kind, the kind filter, an accent-blind search | GAP |
| CAT-2 | A category and a subcategory created, renamed and deleted; the kind follows the parent | GAP |
| CAT-3 | Refused: a name in use at that level, a category with subcategories, a category with transactions | GAP |
| CAT-4 | Use over the last 12 months: count, total, share; Só as sem uso | GAP |
| CAT-5 | A refusal the API answers with a 400 (a blank name, a parent made a child) shows its sentence | GAP |

### AI and settings (`/settings`, the import, the dashboard card)

| # | Flow | Before |
|---|---|---|
| AI-1 | Sugerir com IA, AI on (fake provider) | `ai` test 28 |
| AI-2 | Gerar análise (fake provider) | `ai` test 29 |
| AI-3 | AI off: both buttons disabled, each saying why | GAP |
| AI-4 | Asking again leaves nothing to suggest; an unusable answer leaves the rows on the default | GAP |
| AI-5 | Regenerar | GAP |
| AI-6 | AI switched off in another tab: the import's request is refused with the reason | GAP |
| SET-1 | The AI switch, on and back off, kept after a reload | partial: `ai` turns it on |
| SET-2 | This month's AI spend line | partial: `ai` test 28 ("1 chamada") |
| SET-3 | What is sent to the AI provider, spelled out | GAP |
| SET-4 | Excluir minha conta | `delete-account` test 12 |
| SET-5 | The delete waits for the exact e-mail | GAP |
| SET-6 | The delete refused because the session ended in another tab | GAP |

### Market data, investments, returns

| # | Flow | Before |
|---|---|---|
| MKT-1 | Register a ticker, find it, refuse it twice | `market-data` |
| MKT-2 | A manual sync on the fakes, by provider | `market-data` test 26 |
| MKT-3 | Registration refused under its fields (a Binance pair not in reais, Binance or CoinGecko in the wrong currency) | GAP |
| MKT-4 | A search with no match | GAP |
| INV-1 | PETR4 added, bought, valued; positions and total | `investments` test 31 |
| INV-2 | A dividend raises Proventos | `investments` test 32 |
| INV-3 | US$, kept after a reload | `investments` 016's test 15 |
| INV-4 | Deleting the buy hides the position; Mostrar ativos sem posição | `investments` test 33 |
| INV-5 | An empty portfolio | GAP |
| INV-6 | Adding from the catalogue search; no match; already held | GAP |
| INV-7 | Removing an asset: refused while it has movements | GAP |
| INV-8 | A movement refused under its field (quantity, fees, date, sold more than held, amount) | GAP |
| INV-9 | Sell, JCP, split and an edit change the position; a delete that uncovers a sell is refused | GAP |
| INV-10 | Sem cotação before the first sync | GAP |
| INV-11 | O que mais contribuiu, Patrimônio investido and its allocation, the asset's value chart | partial: `investments` 016's test 15 (holdings total) |
| RET-1 | TWR, XIRR, the chart, 12 meses, the asset's own page | `returns` test 36 |
| RET-2 | The hero on `/investments` | `returns` 016's test 14 |
| RET-3 | No ano; a custom period; a custom period refused | GAP |
| RET-4 | Nothing held: nothing to measure | GAP |
| RET-5 | The benchmark table and the chart's reference toggles | partial: `returns` test 36 (portfolio row) |

### Not E2E-reachable

| Flow | Why |
|---|---|
| The Google sign-in round trip | Google's consent screen cannot be driven by Playwright. `dev-login` stands in for it, and the callback's outcomes are reached through their `/login?error=` addresses (AUTH-4). |
| The nightly market-data sync and snapshot rebuild on their schedule | A cron in the API process; E2E runs with `MarketData__ScheduledSync=false` and triggers the same job manually (MKT-2). |
| The monthly AI analysis job's schedule | Same: the on-demand path (AI-2) runs the same job. |
| A sync refused by the ten-minute window | The fakes have no window, and a run lasts milliseconds while the button is disabled. |
| A provider failure listed under a sync run | The fake providers never fail. |
| A stale price ("Cotação desatualizada") | The fakes always close on the sync's day. |
| No dollar rate synced ("Sem cotação do dólar sincronizada") | Every fake sync stores USDBRL, and benchmarks are shared by every test. |
| Returns in US$ and an asset's FX split | The fakes store one USDBRL point per sync day, so whether a period's first day has a rate depends on the database's history, not on the test. |
| The AI budget spent (402), the provider timing out (504) or failing (502), an analysis that fails | The fake provider always answers, and a call costs cents against R$ 15. |
| An analysis for a month that has not begun | The month selector stops at the current month. |
| The dashboard, accounts, positions or returns failing to load ("Não foi possível carregar…") | Needs the API to fail mid-suite; it is shared by every test. |
| The footer's `degraded` / `unreachable` on the dev server | Same; the PWA suite covers the unreachable API (HEALTH-3). |
| Sair failing ("Não foi possível sair.") | Needs the logout endpoint to fail. |
| A password-protected spreadsheet | Needs an encrypted workbook fixture; the API's integration tests cover it. |
| Dropping a file on the zone | Playwright's synthetic drop is not an operating-system drag; it reaches the same handler as Escolher arquivo, which E2E drives. |

## Test plan

E2E only. Every test signs in as its own user; nothing is shared but the market-data
catalogue (decision 6).

| File | New or extended |
|---|---|
| `auth.spec.ts` | AUTH-1 extended to every protected page; AUTH-4 |
| `navigation.spec.ts` (new) | NAV-1 to NAV-4 |
| `theme.spec.ts` | THEME-2 |
| `dashboard.spec.ts` | DASH-1, DASH-4, DASH-6, DASH-8, DASH-9, DASH-11, DASH-12 |
| `transactions.spec.ts` | TX-3, TX-4, TX-6 to TX-13 |
| `export.spec.ts` | EXP-1 on fixed dates, EXP-2, EXP-3 |
| `accounts.spec.ts` (new) | ACC-1 to ACC-8 |
| `import.spec.ts` | IMP-6, IMP-8 to IMP-18 |
| `categories.spec.ts` (new) | CAT-1 to CAT-5 |
| `ai.spec.ts` | AI-3 to AI-6, SET-1 to SET-3 |
| `delete-account.spec.ts` | SET-5, SET-6 |
| `market-data.spec.ts` | MKT-3, MKT-4 |
| `investments.spec.ts` | INV-5 to INV-11, DASH-5 |
| `returns.spec.ts` | RET-3 to RET-5 |

`support.ts` gains `fixture`, `uploadStatement`, `importStep`, `commitImport`, `uniqueTicker`,
`utcDaysAgo`, `localToday`, `localMonthDay` and `showDashboardMonth`, so the specs stop
keeping their own copies.

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
- The (review) decisions confirmed or changed by a person
