# 021 — Export transactions

## Goal

Take the transactions the `/transactions` filters select out of the app as a CSV that a
Brazilian Excel opens as a table of dates, text and numbers, with no import wizard. The
file holds every matching row, not only the page on screen, and it can be imported back
through 004's own CSV mapping.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | Where it is built | **On the server**: `GET /api/transactions/export` streams the file. The browser has only the page it is showing, and building the file from 50-row pages would mean totalling and formatting money in JavaScript (`web/src/api/finance.ts` forbids it). |
| 2 | Which rows | Exactly the list's filters: `from`, `to`, `accountId`, `categoryId`, `importBatchId`. `page` and `pageSize` are ignored: every matching row goes in the file. The list has no text search, so there is none here; one added later goes into the shared filter and the export gets it with no extra work. |
| 3 | One filter, two routes | The list's filtering moves into `TransactionFilter` (parse the query, apply it to the query), and both routes call it, so what the screen shows and what the file holds cannot drift. A category filter matches that category only, not its children, as the list already does. |
| 4 | Malformed filters | A date that is not `AAAA-MM-DD` or an id that is not a GUID is a **400 naming the field, in pt-BR**, on both routes. Until now the list bound them as typed parameters and answered the framework's bare 400. This is the only change to the list's behaviour. **(review)** |
| 5 | `from` after `to`; ids that resolve to nothing | Not errors. They select no rows, as they do in the list today, so the file is the header alone. Another user's account id is one of these: the query filter hides it, so it is neither a 403 nor a 404 (003, rule 4). **(review)** |
| 6 | Order | The list's: `Date DESC, CreatedAt DESC`, so the file reads like the screen. A bank statement is usually oldest first; sorting a column in Excel is one click. **(review)** |
| 7 | Encoding and layout | UTF-8 **with a BOM** (Excel ignores UTF-8 without it and shows `DescriÃ§Ã£o`), `;` between fields (the list separator of a pt-BR Windows), CRLF after every record, including the last. No `sep=;` first line: Excel then ignores the BOM, and 004's table election would count it as preamble. An en-US Excel shows one column; Data > From Text/CSV reads it. **(review)** |
| 8 | Columns | `Data;Descrição;Valor;Moeda;Conta;Categoria;Subcategoria`. `Categoria` is always the main category and `Subcategoria` the child, empty for a transaction on a main category. Two columns rather than `Principal › Sub` in one, because a pivot table or `SOMASE` by main category then works on the column as it is, with no text splitting, and the spending under a child adds into its parent. `Moeda` is there because accounts may be in another currency (003), and a `Valor` column summing reais and dollars with nothing to tell them apart is a wrong total. **(review)** |
| 9 | Dates | `dd/MM/yyyy`, invariant culture. Excel pt-BR reads them as dates. |
| 10 | Amounts | Signed, two decimals, a decimal comma and no thousands separator: `-1234,56`, `3000,00`, `-0,01`. Written from the stored `decimal` with the invariant rules and `,` as the decimal separator. No `double` on the way. |
| 11 | Quoting | RFC 4180: a field containing `;`, `"`, CR or LF is wrapped in `"`, and each `"` inside is doubled. Other fields are written bare. The writer is about 40 lines of our own, not CsvHelper's `CsvWriter`: writing is the easy half of CSV (004 took CsvHelper for the reading half), and CsvHelper's injection escaping applies to every field, so it would turn negative amounts into text. |
| 12 | CSV injection | A **text** field (`Descrição`, `Conta`, `Categoria`, `Subcategoria`, `Moeda`) that starts with `=`, `+`, `-`, `@`, a tab or a CR gets a leading `'`, OWASP's mitigation. Excel then shows it as text, apostrophe included, and never runs it. `Data` and `Valor` are written by the app and never prefixed, so a negative amount stays a number. **(review)** |
| 13 | File name | `Content-Disposition: attachment`, ISO dates so the files sort: `lancamentos-2026-09-01-a-2026-09-30.csv`; `lancamentos-desde-2026-09-01.csv` with only `from`; `lancamentos-ate-2026-09-30.csv` with only `to`; `lancamentos.csv` with neither (an import batch or an account reached from its page). ASCII only, so no header encoding is needed. **(review)** |
| 14 | Round trip | The file imports back through 004's CSV path with this mapping: delimiter `;`, header, `pt-BR`, `dd/MM/yyyy`, `Signed`, date `Data`, amount `Valor`, description `Descrição`. Dates, amounts and descriptions come back identical, with three known exceptions: a description guarded by decision 12 comes back with its `'`; the category is not carried (import suggests one, 004 and 009); and one import takes at most 5 000 rows and 2 MB, so a larger export is imported a period at a time. **(review)** |
| 15 | How the browser saves it | `fetch`, then a `Blob` saved through a temporary link with `download`, rather than navigating to the URL. A refusal then shows its pt-BR message on the page instead of being saved as a `.csv` holding JSON. The file name is the one `Content-Disposition` gives. The browser holds the whole file in memory while saving; for a personal ledger that is a few MB at most. **(review)** |
| 16 | Isolation | The rows come from `Transactions`, joined to `Accounts` and `Categories` (twice: the category and its parent), and each set brings its query filter into the join. Nothing is read without it. |
| 17 | Memory on the server | The rows are read with `AsAsyncEnumerable` and written as they arrive, so a large export does not build the file in memory. |

## Out of scope

- Writing `.xlsx` (no new library); the CSV opens in Excel
- Exporting investments, positions or movements
- Scheduled or e-mailed exports
- A text search on the list (decision 2)
- Choosing columns, order or format
- Stripping the `'` of decision 12 on import

## Data model changes

None.

## API surface

```
GET /api/transactions/export?from=&to=&accountId=&categoryId=&importBatchId=
    200 text/csv; charset=utf-8
        Content-Disposition: attachment; filename=lancamentos-2026-09-01-a-2026-09-30.csv
        body: BOM, the header, one record per transaction, CRLF after each
    400 a malformed filter, problem details naming the field (below)
    401 not signed in

GET /api/transactions?<same filters>&page=&pageSize=
    unchanged, except that a malformed filter is now the same 400 as above
```

Example body (after the BOM):

```
Data;Descrição;Valor;Moeda;Conta;Categoria;Subcategoria
30/09/2026;"Mercado; ""Pão de Açúcar""";-1234,56;BRL;Nubank;Alimentação;Supermercado
28/09/2026;'=1+1;-0,01;BRL;Nubank;Outros;
```

Messages (field, `detail` rendered verbatim):
- `from`: `A data inicial deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31.`
- `to`: `A data final deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31.`
- `accountId`: `A conta do filtro não é um identificador válido.`
- `categoryId`: `A categoria do filtro não é um identificador válido.`
- `importBatchId`: `A importação do filtro não é um identificador válido.`

The date messages are 008's, word for word.

## UI behaviour

**`/transactions`** — an **Exportar CSV** button (outline) in the header, beside
"Novo lançamento", and still there while the form is open.

- It exports what the filter bar selects: the dates, the account, the category, and an
  import batch or account the page was opened with (`?importBatchId=`, `?accountId=`).
- It is disabled while the list shows no rows, and reads **Exportando…** while the request
  runs.
- On success the browser saves the file under the server's name. Nothing else changes on
  the page.
- A refusal shows its pt-BR message in the page's alert, and nothing is saved.

## Test plan

### Unit — `api.tests/Unit` (`TransactionCsvTests`)

1. Amounts: `-1234.56` → `-1234,56`, `1234567890.12` → `1234567890,12`, `-0.01` → `-0,01`, `3000` → `3000,00`; no grouping, no `double` in the writer's declared types
2. Dates: 3 April 2026 → `03/04/2026`
3. Quoting: a field with `;`, one with `"`, one with a newline are quoted and the quotes doubled; a plain field is bare
4. Injection: `=`, `+`, `-`, `@`, tab and CR at the start get a `'`, and a guarded field containing `;` is also quoted; `-` in the middle is untouched
5. The record: seven fields in order, an empty `Subcategoria` for a main category
6. File names: both ends, only `from`, only `to`, neither

### Integration — `api.tests/Integration` (`TransactionExportTests`)

7. The response: `200`, `text/csv; charset=utf-8`, the file name from `from`/`to`, the body starts with the UTF-8 BOM, the header line, CRLF after every record
8. Every matching row, beyond one list page: 60 rows in the range, 1 outside it → 60 records, in the list's order (compared with `GET /api/transactions?pageSize=200` over the same filter)
9. Each filter narrows the file as it narrows the list: account, category, import batch
10. A transaction on a child category → `Categoria` is the parent and `Subcategoria` the child
11. Amounts are exact: `1234567890.12`, `-0.01`, `-1234.56` as `1234567890,12`, `-0,01`, `-1234,56`
12. A description with `;`, `"` and a newline, and one starting with `=`, arrive quoted and guarded
13. Malformed `from`, `to`, `accountId`, `categoryId` or `importBatchId` → `400` naming the field with the pt-BR message; the list answers the same `400` for the same query
14. Isolation: B's export holds none of A's rows; B filtering by A's account gets the header alone; anonymous → `401`
15. **Round trip:** export an account, upload the file to a second account through the CSV path with decision 14's mapping, commit → dates, amounts and descriptions equal the originals, except the guarded description, which comes back with its `'`

### Unit — `web`

16. "Exportar CSV" requests `/api/transactions/export` with the filter bar's values (the current month by default, then an account picked) and no `page`/`pageSize`, and saves the body under the `Content-Disposition` name
17. Opened with `?importBatchId=`, the export carries it and no dates
18. A `400` shows its field message in the alert and nothing is saved
19. The button is disabled while the list is empty
20. The file name is read from `filename*=` first, then `filename=`, quoted or bare; none → `lancamentos.csv`

### E2E — `web/e2e`

21. Create an account and a transaction this month with `;` and `"` in the description, click "Exportar CSV": the download is named `lancamentos-<first>-a-<last>.csv`, starts with the BOM, and holds the header and the row quoted as decision 11 says

Every existing test keeps passing.

## End-to-end verification

```
bash scripts/verify.sh
```

and the E2E suite (21) against an isolated API and database.

Manual, once, with Excel set to Portuguese (Brasil):

1. Export a month with expenses and income. Open the file with a double click.
2. Accents are right, every field is in its own column, `Data` is a date and `Valor` sums with `=SOMA()`.
3. A description typed as `=1+1` shows as `'=1+1`, not `2`.
4. Import the file into a new account with decision 14's mapping: the preview shows the same dates and amounts.

## Definition of done

- `bash scripts/verify.sh` green; E2E 21 passes
- Tests 1–21 exist and pass
- Manual steps 1–4 behave as described
- No `double`, `float` or culture-less formatting on the export path
