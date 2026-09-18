# 004 — Import

## Goal

Bring bank statements in from OFX and CSV without creating duplicates and without forcing the user to categorise every row by hand. Staging with a preview makes every import reviewable before it touches `Transactions`, and undoable afterwards.

## Size warning

Larger than 003, which already overran the diff rule. Seven checkpoints, each deliberately small. Three produce no database work at all.

## Decisions made in this spec

| # | Question | Decision |
|---|---|---|
| 1 | Category on imported rows | `staged_transactions.CategoryId` is **nullable**; `Transactions.CategoryId` stays NOT NULL. Preview fills it via history lookup, then a sign-based default. Rule 3 untouched. |
| 2 | `ImportBatchId` | Nullable FK on `Transactions`, `RESTRICT`. Undo deletes rows then the batch, explicitly, in one DB transaction. No cascade. |
| 3 | Staging | Persisted, per ARCHITECTURE.md §2. One open batch per user at a time. |
| 4 | Sign mapping | OFX: `TRNAMT` is already signed. CSV: template declares `Signed`, `SignedInverted`, or `DebitCredit`. |
| 5 | Decimal parsing | `decimal.Parse` with an explicit `CultureInfo` from the template. Never `double`, never `Convert`. |
| 6 | CSV dates | `DateOnly.ParseExact` with a format string declared by the template and `InvariantCulture`. No inference, ever. |
| 7 | Dedupe | OFX with `FITID` → exact match on `ExternalId`. Otherwise date + amount + `NormalizedDescription`. |
| 8 | Transfers | Out of scope. Imported as-is, double-counted. Documented as a forward dependency on 005. |
| 9 | Currency | Import targets one account and inherits its currency. Foreign-currency lines are marked invalid **per row**, not per file. |
| 10 | Description | `NAME` and `MEMO` joined with ` — `. Full text kept in staging (`text`), truncated to 300 on commit. |
| 11 | Sync or job | **Synchronous.** A few hundred rows in-request is fine. Job infrastructure waits for 009, which actually needs it. |
| 12 | Upload limit | 2 MB and 5 000 rows. Both rejected at the request boundary. |
| 13 | Packages | `CsvHelper` for CSV. OFX parser **hand-written** — see below. |

### Why hand-roll OFX but not CSV

CSV quoting and escaping is where hand-rolled parsers break, and `CsvHelper` is mature and maintained. Use it.

OFX 1.x — the common Brazilian export — is SGML with unclosed tags, not XML. The subset needed here is small: the `STMTTRN` block and its `DTPOSTED`, `TRNAMT`, `FITID`, `NAME`, `MEMO`, `TRNTYPE` fields, plus `CURDEF`. That is a tolerant tag-scanner of roughly 150 lines, fully unit-testable, with no dependency on a package whose maintenance status we would have to keep checking. Write it.

### Corrections to existing docs, in this feature

- **ADR-003 is wrong.** It names pg-boss, a Node.js library, which cannot run inside the .NET process ADR-004 requires. Amend it: background jobs will use Hangfire or a hosted `BackgroundService`, decided in 009. Record that 004 is synchronous.
- **ARCHITECTURE.md §7 specifies a dedupe hash.** Amend to `NormalizedDescription` + `ExternalId`, with the reasoning above.
- **ARCHITECTURE.md claims the TS client is generated from OpenAPI.** It is hand-written. Correct the doc — do not add the build machinery.

## Out of scope

- Aggregations and dashboard — 005
- Transfer detection or linking between accounts
- User-defined categorisation rules (ADR-012 rung 1). Only rung 2, history lookup, is built here.
- AI categorisation (rung 3) — 009
- PDF or image statements, OCR
- Scheduled or automatic imports, bank APIs, Open Finance
- Editing a committed batch. Undo and re-import instead.

## Data model changes

Migration: `AddImport`. Review the SQL before applying.

### New columns on `Transactions`

| Column | Type | Notes |
|---|---|---|
| `ImportBatchId` | `uuid NULL` | FK → `import_batches`, `RESTRICT` |
| `ExternalId` | `varchar(100) NULL` | OFX `FITID` |
| `NormalizedDescription` | `varchar(300) NULL` | see below |

Indexes:
- Unique `(UserId, AccountId, ExternalId)` **where `ExternalId is not null`** — a partial index, so manual rows are unaffected
- `(UserId, Date, NormalizedDescription)`

Nullable on purpose. Rows created in 003 keep `NULL` and are simply never matched by dedupe or history lookup, which is correct — they were entered by hand. **No backfill.** Accepted debt, recorded here so it is a decision rather than an oversight.

### `ImportBatch : IUserOwned`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | |
| `UserId` | `uuid` | query-filtered |
| `AccountId` | `uuid` | FK, `RESTRICT` |
| `Source` | `int` | `Ofx`, `Csv` |
| `FileName` | `varchar(260)` | |
| `Status` | `int` | `Staged`, `Committed` |
| `RowCount` | `int` | rows parsed from the file |
| `CommittedCount` | `int NULL` | rows actually written |
| `CreatedAt` | `timestamptz` | |
| `CommittedAt` | `timestamptz NULL` | |

Partial unique index on `(UserId)` **where `Status = Staged`** — the database enforces one open batch per user, rather than a pre-check that races.

### `StagedTransaction : IUserOwned`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | |
| `UserId` | `uuid` | query-filtered |
| `ImportBatchId` | `uuid` | FK, `CASCADE` — staging rows have no life of their own |
| `RowNumber` | `int` | 1-based position in the file, for error messages |
| `Date` | `date NULL` | null when unparseable |
| `Amount` | `numeric(18,2) NULL` | null when unparseable |
| `Currency` | `char(3) NULL` | |
| `RawDescription` | `text` | untruncated, as it came from the file |
| `NormalizedDescription` | `varchar(300) NULL` | |
| `ExternalId` | `varchar(100) NULL` | |
| `CategoryId` | `uuid NULL` | FK, `RESTRICT` — **nullable, unlike Transactions** |
| `Status` | `int` | `Ready`, `Duplicate`, `Invalid` |
| `Issues` | `text NULL` | JSON array of pt-BR messages |

### `CsvTemplate : IUserOwned`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | |
| `UserId` | `uuid` | query-filtered |
| `Name` | `varchar(100)` | unique per `(UserId, Name)` |
| `Delimiter` | `char(1)` | |
| `HasHeader` | `bool` | |
| `Culture` | `varchar(10)` | e.g. `pt-BR` — governs decimal parsing |
| `DateFormat` | `varchar(20)` | e.g. `dd/MM/yyyy` — exact, never inferred |
| `SignMode` | `int` | `Signed`, `SignedInverted`, `DebitCredit` |
| `DateColumn` | `varchar(100)` | header name or index |
| `AmountColumn` | `varchar(100) NULL` | for `Signed` / `SignedInverted` |
| `DebitColumn` | `varchar(100) NULL` | for `DebitCredit` |
| `CreditColumn` | `varchar(100) NULL` | for `DebitCredit` |
| `DescriptionColumns` | `varchar(300)` | comma-separated, joined with ` — ` |
| `CreatedAt` | `timestamptz` | |

No built-in bank templates ship. The user maps columns once against the real headers of their own file and saves the mapping. Hardcoding a guess at Nubank or Itaú's current layout would be inventing facts that go stale.

## Domain logic

### Normalization — `Domain/Import/DescriptionNormalizer.cs`

Pure, deterministic, no dependencies. Serves both dedupe and ADR-012 rung 2 — that is why it is one column and not a hash.

Applied in order:
1. Uppercase, invariant culture
2. Strip accents (`FormD`, drop non-spacing marks)
3. Replace any run of digits with a single space
4. Strip `*`, `#`, `-`, `/`, `.`, `:` and `,`
5. Collapse whitespace runs to one space, trim
6. Truncate to 300

`PAG*IFOOD 12/03` and `PAG*IFOOD  15/04` both become `PAG IFOOD`. That is the whole point.

### Sign resolution — `Domain/Import/SignResolver.cs`

| Mode | Rule |
|---|---|
| `Signed` | Value as given |
| `SignedInverted` | Negated. Card statements commonly list purchases as positive. |
| `DebitCredit` | Debit → negative, credit → positive. Both populated on one row is an `Invalid` row. |

### Dedupe — `Domain/Import/DuplicateMatcher.cs`

Two passes, both required.

**Within the batch** — two staged rows matching on the key are both marked `Duplicate` after the first.
**Against existing** — a staged row matching a committed `Transaction` is marked `Duplicate`.

Key, in order of preference:
1. `ExternalId` present → match on `(AccountId, ExternalId)`. Exact; OFX `FITID` is institution-unique.
2. Otherwise → match on `(Date, Amount, NormalizedDescription)`.

A `Duplicate` row is excluded from commit by default but the user may include it from the preview — re-imports are not always wrong, and two identical coffees on one day are real.

### Category suggestion — `Domain/Import/CategorySuggester.cs`

ADR-012, rungs 2 and the default. Rung 1 (user rules) does not exist yet; rung 3 (AI) is 009.

1. **History** — most recent committed `Transaction` for this user with the same `NormalizedDescription`. Use its category. Only accept it if its kind agrees with the row's sign.
2. **Default by sign** — negative → `Outros`, positive → `Outras receitas`, both resolved by name among the user's top-level categories.

If neither resolves, the row is `Invalid` with the issue *"Categoria não encontrada"* — which can only happen if the user deleted the default categories.

## Row validation

Row-level, never file-level. A file with 3 bad rows out of 200 imports 197. Each issue appends a pt-BR message to `Issues` and sets `Status = Invalid`.

| Check | Message |
|---|---|
| Date unparseable in the declared format | `Data inválida: "…"` |
| Amount unparseable in the declared culture | `Valor inválido: "…"` |
| Amount rounds to zero | `Valor não pode ser zero` |
| Date outside 1900-01-01 … today + 1 year | `Data fora do intervalo permitido` |
| Row currency differs from the account's | `Moeda diferente da conta (…)` |
| Debit and credit both populated | `Linha tem débito e crédito` |
| Description empty after normalization | `Descrição vazia` |

Rule 5 of 003 stays as the backstop it was described as. With formats declared rather than inferred, a wrong format now fails loudly on row one instead of silently swapping day and month.

## API surface

All routes authenticated. All mutations require `Origin` (002's CSRF check).

```
POST   /api/imports/preview-csv          multipart: file
       200 { headers: [...], sampleRows: [[...]] }   first 5 rows, no persistence
       400 unparseable, 413 over 2 MB

POST   /api/imports                      multipart: file, accountId, source,
                                         templateId | inline template fields
       201 { batchId, rowCount, ready, duplicates, invalid }
       409 an open batch already exists → { openBatchId }
       413 over 2 MB, 422 over 5 000 rows

GET    /api/imports/{id}                 200 batch + staged rows, paginated
GET    /api/imports                      200 batch history, newest first

PATCH  /api/imports/{id}/rows/{rowId}    200  { categoryId?, include? }
POST   /api/imports/{id}/commit          200 { committed, skipped }
DELETE /api/imports/{id}                 204 discard a staged batch
POST   /api/imports/{id}/undo            200 { deleted }  committed batch only

GET    /api/csv-templates                200
POST   /api/csv-templates                201
DELETE /api/csv-templates/{id}           204
```

**Commit** — one DB transaction. Every `Ready` row, plus any `Duplicate` the user included, becomes a `Transaction` carrying `ImportBatchId`, `ExternalId`, `NormalizedDescription`, and `Description` truncated to 300. Batch goes to `Committed`. Staged rows are deleted. `Invalid` rows are skipped and counted.

**Undo** — one DB transaction. Delete `Transactions` where `ImportBatchId = id`, then the batch. Explicit two-step, not a cascade: an accidental batch delete must not silently remove transactions.

A committed batch whose transactions were partly deleted by hand still undoes cleanly — it deletes what remains.

## UI behaviour

Route `/import`, in the nav.

**Step 1 — file.** Pick account, drop file. Extension decides the path: `.ofx` goes straight to step 3; `.csv` goes to step 2.

**Step 2 — mapping (CSV only).** Show the parsed headers and the first 5 rows as a table. Dropdowns for date, amount (or debit/credit), and description columns — multi-select, joined in order. Inputs for delimiter, culture, date format, sign mode. A live preview of the first 5 rows *as they would be interpreted*: parsed date, resolved signed amount, joined description. Optionally save as a named template.

The live preview is the feature that makes this usable. Choosing `dd/MM/yyyy` versus `MM/dd/yyyy` is invisible until you see `03/04` render as 3 April rather than 4 March.

**Step 3 — preview.** Table of staged rows: row number, date, description, suggested category (editable), amount, status. Filter by status. Counts per status at the top. `Invalid` rows show their issues and cannot be included. `Duplicate` rows are excluded by default with a checkbox to include. Commit is disabled while `Ready + included` is zero.

**Step 4 — done.** Committed and skipped counts, an Undo button, and a link to `/transactions` filtered to that batch.

**History.** `/import` lists past batches with date, file, account, counts, and Undo for committed ones.

Undo asks for confirmation naming the row count. It is the only destructive action in the app so far.

## Test plan

### Unit — `api.tests/Unit`, no database

**Normalizer** — 1 `PAG*IFOOD 12/03` and `PAG*IFOOD  15/04` produce the same output · 2 accents stripped · 3 case-insensitive · 4 long input truncated to 300 · 5 idempotent: `f(f(x)) == f(x)`

**Decimal parsing** — 6 `"1.234,56"` + `pt-BR` → `1234.56m` exactly · 7 `"1,234.56"` + `en-US` → `1234.56m` exactly · 8 `"1.234,56"` + `en-US` → parse failure, not a wrong number · 9 `"0,001"` rounds to zero and is rejected · 10 no `double` anywhere in the path — assert on the declared return types

**Dates** — 11 `"03/04/2026"` + `dd/MM/yyyy` → 3 April · 12 same input + `MM/dd/yyyy` → 4 March · 13 `"31/12/2026"` + `MM/dd/yyyy` → failure, not a rollover

**Sign** — 14 `Signed` passes through · 15 `SignedInverted` negates · 16 `DebitCredit` maps each side · 17 both populated → invalid

**OFX parser** — 18 a minimal 1.x document with unclosed tags parses · 19 `NAME` + `MEMO` join with ` — ` · 20 `MEMO` absent → `NAME` alone · 21 `FITID` extracted · 22 `CURDEF` extracted · 23 malformed document fails with a message, no exception escaping · 24 `DTPOSTED` with a timezone suffix yields the right calendar day

**CSV parser** — 25 quoted field containing the delimiter · 26 quoted field containing a newline · 27 header row skipped when `HasHeader` · 28 ragged row → invalid, rest of file still parses

**Duplicate matcher** — 29 `ExternalId` match wins over heuristic · 30 heuristic matches on date + amount + normalized description · 31 same amount, different day → not duplicate · 32 two identical rows in one batch → second marked duplicate

**Category suggester** — 33 history match applied · 34 history match with wrong kind rejected, default used · 35 no history → sign default

### Integration — `api.tests/Integration`

**Isolation — mandatory:** 36 A's batch invisible to B on `GET /api/imports` · 37 B `GET /api/imports/{A's id}` → 404 · 38 B commits A's batch → 404, nothing written · 39 B's `csv-templates` isolated

**Batch lifecycle:** 40 upload → 201 with correct counts · 41 second upload while one is open → 409 with `openBatchId` · 42 discard, then upload succeeds · 43 commit writes correct rows, batch `Committed`, staged rows gone · 44 undo deletes exactly those transactions and the batch · 45 undo after some rows were deleted by hand still succeeds · 46 the partial unique index rejects a second `Staged` batch at the database level

**Correctness:** 47 committed amounts byte-identical to the file · 48 `ImportBatchId`, `ExternalId`, `NormalizedDescription` populated · 49 description over 300 chars truncated on commit, full text still in staging · 50 re-importing the same OFX → every row `Duplicate`, commit writes zero · 51 a second import overlapping by 5 rows → exactly 5 duplicates · 52 foreign-currency row → `Invalid`, others still import · 53 file with 3 bad rows of 20 → 17 committed

**Limits:** 54 2.1 MB → 413 · 55 5 001 rows → 422

**Suggestion end to end:** 56 categorise a transaction manually, import a row with the same normalized description, suggestion matches · 57 no history → sign default

### Unit — `web`

58 mapping preview re-renders when date format changes · 59 commit disabled at zero included rows · 60 invalid rows cannot be included · 61 duplicates excluded by default, checkbox includes · 62 undo confirmation names the row count

### E2E — `web/e2e`

Via `dev-login`, with fixture files in `web/e2e/fixtures/`:

63 upload OFX → preview → commit → rows visible in `/transactions`
64 upload the same OFX again → all duplicates → commit writes nothing
65 upload CSV → map columns → preview → commit
66 undo a committed batch → rows gone from `/transactions`

Do not modify `e2e/smoke.spec.ts`.

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual, with a real statement from your own bank:

1. Export OFX for one month from a real account
2. Import it — check the preview dates against the bank's own screen, especially anything in the first twelve days of a month
3. Commit, then compare the total against the bank's closing balance movement
4. Import the *same file* again → every row duplicate, commit writes nothing
5. Export an overlapping range → only the new rows are `Ready`
6. Undo the second batch → `/transactions` returns to its prior state
7. Export CSV from the same bank, map the columns, confirm the live preview shows the dates you expect before committing
8. In `psql`: `SELECT "Amount" FROM "Transactions" WHERE "ImportBatchId" IS NOT NULL LIMIT 5;` → values match the file exactly

Step 2 is the one that catches the day/month swap, and it is the failure mode most likely to reach production silently.

## Known forward dependency

Importing statements from two accounts double-counts transfers between them. Paying a card bill from checking appears as an expense in checking *and* the card's transactions appear as expenses. **005 must handle this** — either by excluding a designated transfer category from totals, or by introducing transfer linking. It is recorded here so 005's spec is not surprised by it.

## Definition of done

- Both verify scripts green
- Manual steps 1–8 behave as described
- Migration SQL reviewed before applying, partial indexes confirmed present
- ADR-003, ARCHITECTURE.md §7 and the OpenAPI-client claim corrected
- No `double`, `float`, `real`, or culture-less `Parse` anywhere in the import path
