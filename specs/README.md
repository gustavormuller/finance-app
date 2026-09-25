# Specs

One file per feature: `specs/NNN-name.md`. The spec is written in Session A, reviewed
by a human, and is the contract Session B implements against. If there is no spec for
a task, there is no task.

Each spec contains:

- Goal, in two sentences
- Out of scope, explicitly
- Data model changes (tables, columns, indexes, migrations)
- API surface (method, path, request, response, status codes)
- UI behaviour, if any
- Test plan: unit tests, integration tests, and the E2E path if applicable
- An end-to-end verification step that proves the feature works
- Open questions, if any remain

## Feature sequence

| # | Feature | Covers |
|---|---|---|
| 000 | bootstrap | repo, tooling, verification harness |
| 001 | walking-skeleton | `/api/health` + Vite proxy + one DB round trip |
| 002 | identity | Identity, Google OAuth, cookie session, `UserId` filters |
| 003 | transactions | `Money` value object, CRUD, dashboard list |
| 004 | import | OFX/CSV parse, staging, dedupe, preview, undo |
| 005 | dashboard | balances, category breakdown, monthly series |
| 006 | market-data | provider ports, daily sync job, backfill |
| 007 | investments | assets, movements, `portfolio_daily` snapshots |
| 008 | returns | TWR, XIRR, benchmark comparison |
| 009 | ai-analysis | categorization cascade, monthly analysis, budget cap |
| 010 | deploy | Oracle instance, Caddy, Tunnel, backups |
| 011 | import-excel | `.xls`/`.xlsx` through the CSV mapping |
| 012 | design-obsidiana | the Obsidiana look, dark and light themes |
| 013 | categories-table | categories as a table with usage, edited in place |
| 014 | net-worth | net worth over time, compact in the dashboard hero |
| 015 | accounts-import | accounts as the home of import and its history |
| 016 | investments-returns | returns first on the investments page, R$ and US$ |
| 019 | market-data-keyless | Binance crypto in BRL, a missing key explains itself, the keys guide |

One number per session set. If a feature needs more than three sessions, it was scoped
too large — split it.
