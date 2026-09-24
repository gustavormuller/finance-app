# 011 — Import from Excel

## Goal

Accept `.xlsx` and `.xls` statements in the import wizard, through the same column mapping,
templates, dedupe and review that CSV already has. A spreadsheet becomes the same table a
CSV becomes, and nothing after that table knows the difference.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | Formats | `.xlsx` (Office Open XML) and `.xls` (BIFF8, Excel 97–2003). The **content** decides, not the extension: a ZIP signature is `.xlsx`, an OLE2 signature is `.xls`. Anything else is not a spreadsheet. |
| 2 | Library | **`ExcelDataReader`** (MIT), which reads both formats. `.xls` needs `CodePagesEncodingProvider` registered, which ships in the shared framework, so no second package. **(review)** |
| 3 | Which sheet | The first worksheet with at least one non-empty row. Choosing among sheets is out of scope. **(review)** |
| 4 | Table | The sheet's rows become the same `(RowNumber, Fields)` records the CSV reader produces, and `CsvStatementParser`'s table election (preamble, modal width, ragged rows) runs on them unchanged. `RowNumber` is the Excel row number. |
| 5 | Typed cells | A text cell is used as written. A **number** cell becomes a `decimal` through the reader's `GetDecimal` — Excel stores numbers in binary floating point, the conversion keeps 15 significant digits (Excel's own precision), and no `double` is ever held by our code. A **date** cell becomes a `DateOnly` (the time of day is dropped). Both are then written as text **in the culture and date format the mapping declares**, so they parse back to exactly the value in the sheet under any declared culture. An error cell (`#N/A`) is empty. |
| 6 | Formulas | The cached result the file carries. Nothing is recalculated. |
| 7 | Preview | `POST /api/imports/preview-csv` also takes spreadsheets (the route name is historical; renaming it would break nothing but churn). Optional `culture` and `dateFormat` form fields render typed cells, defaulting to `pt-BR` and `dd/MM/yyyy`. |
| 8 | Upload | `source = Spreadsheet`, with the same mapping fields or `templateId` as CSV. The delimiter is ignored. `ImportSource.Spreadsheet = 2`, stored as `int` like the others: **no migration**. |
| 9 | Templates | Shared with CSV. A template saved from a spreadsheet keeps whatever delimiter the form had, and it is ignored when applied to one. |
| 10 | Limits | Unchanged: 2 MB and 5 000 rows. Also, an `.xlsx` whose ZIP entries declare more than **20 MB** uncompressed is refused before it is opened (a 2 MB ZIP can hold gigabytes). |
| 11 | Unreadable files | An `.xls` that is really an HTML page (some banks do this), a CSV renamed to `.xlsx`, a password-protected or corrupt file: 400 with a pt-BR message that suggests exporting CSV. No exception escapes. |

### Why render typed cells into the declared culture instead of a fixed one

The mapping step's live preview parses sample **text** in the browser, and the server parses
the same text with `AmountParser` and `DateParser`. A typed number written in a fixed form —
say `1234.56` — would be read back as `123456` under a `pt-BR` mapping by a lenient parser, or
rejected by a strict one. Writing it with the declared separators and no grouping makes the
round trip exact for every supported culture, and it keeps a single parsing path.

The preview re-renders when culture or date format change, so what the screen shows is what
the upload will stage.

## Out of scope

- Choosing a sheet, or importing several sheets
- `.ods`, `.xlsb`, `.numbers`, Google Sheets links
- Password-protected files (refused, not decrypted)
- Reading the cell's display format to guess a culture — the mapping still declares it
- Built-in bank layouts (still no; see 004)

## Data model changes

None. `ImportSource` gains `Spreadsheet = 2`; `ImportBatches.Source` is an `int` column.

## API surface

```
POST /api/imports/preview-csv     multipart: file, delimiter?, culture?, dateFormat?
     200 { headers, sampleRows, delimiter, skippedRows, rowCount }
         delimiter is null for a spreadsheet; culture/dateFormat only affect one
     400 not a readable CSV or spreadsheet · 413 over 2 MB or 20 MB uncompressed

POST /api/imports                 multipart: file, accountId, source=Spreadsheet,
                                  templateId | inline mapping fields
     201 as for CSV
     400 source says Spreadsheet but the content is not one, or the file is unreadable
```

Messages (rendered verbatim):
- not a spreadsheet: `O arquivo não é uma planilha do Excel (.xls ou .xlsx) válida. Se o banco oferece CSV ou OFX, exporte nesse formato.`
- password: `A planilha está protegida por senha. Remova a senha no Excel e envie de novo.`
- too large uncompressed: `A planilha é grande demais depois de descompactada; o limite é 20 MB. Exporte um período menor.`

## UI behaviour

- **Step 1.** The file input accepts `.ofx,.csv,.txt,.xls,.xlsx`. Help text: `OFX, CSV ou planilha do Excel (.xls, .xlsx) exportada do banco, até 2 MB e 5.000 lançamentos. Um OFX vai direto para a revisão; CSV e planilhas passam antes pelo mapeamento das colunas.`
- **Step 2.** A spreadsheet goes to the mapping step. The delimiter field is hidden. Changing culture or date format re-requests the preview (debounced), so typed cells are shown the way the upload will read them. A one-line note under the format fields: `Células de data e número da planilha são convertidas para o formato escolhido; células de texto precisam estar nesse formato.`
- Steps 3 and 4, history and undo: unchanged.

## Test plan

Fixtures in `samples/statements/`, generated by a committed script so they can be rebuilt:
`bb-extrato-2026-08.xlsx` (Banco do Brasil-like: 2 preamble rows, typed dates and amounts, a
text `Saldo` line) and `itau-extrato-2026-08.xls` (the same rows in BIFF8).

### Unit — `api.tests/Unit`

1. An `.xlsx` with typed dates and numbers renders `dd/MM/yyyy` dates and `pt-BR` amounts with no grouping
2. **Round trip:** interpreting the same sheet under `pt-BR`/`dd/MM/yyyy` and under `en-US`/`MM/dd/yyyy` yields identical `ParsedRow`s
3. Typed numbers convert exactly: `1234.56` → `1234.56m`, `-58` → `-58m`, `0.1 + 0.2` stored by Excel → `0.3m`
4. Text cells are untouched: `"1.234,56"` parses under `pt-BR` and is rejected under `en-US`, as in a CSV
5. Preamble rows above the header are skipped and counted; blank rows are ignored
6. The `.xls` fixture yields the same records as the `.xlsx`
7. An empty first sheet falls through to the next; an all-empty workbook yields an empty table
8. HTML saved as `.xls`, CSV text and random bytes are "not a spreadsheet", without an exception
9. A ZIP declaring more than 20 MB uncompressed is refused before parsing
10. No `double` or `float` in the declared types, fields or locals of the spreadsheet reader

### Integration — `api.tests/Integration`

11. Upload the `.xlsx` with `source=Spreadsheet` and an inline mapping → 201 with the expected counts; commit → amounts byte-identical to the sheet
12. `preview-csv` with the `.xlsx` → headers and sample rows, `delimiter` null; with `culture=en-US` the typed amounts use `.`
13. `source=Spreadsheet` with a CSV body → 400 with the message above
14. The same statement imported as CSV, then as `.xlsx` → every spreadsheet row is a duplicate
15. Isolation: B cannot see or commit A's spreadsheet batch (same guarantees as 004 tests 36–38; one test is enough, the path is shared)

### Unit — `web`

16. Choosing an `.xlsx` goes to the mapping step with no delimiter field
17. Changing the culture on a spreadsheet re-requests the preview with that culture

### E2E — `web/e2e`

18. Upload the `.xlsx` → map → review → commit → the rows are in `/transactions`

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual, with a real export:

1. Export one month as `.xls` or `.xlsx` from a real bank
2. Import it; in the mapping step check that the dates in the live preview match the bank's screen, especially days 1–12
3. Commit and compare the net against the statement
4. Import the CSV or OFX of the same month → every row is a duplicate

## Definition of done

- Both verify scripts green
- Tests 1–18 exist and pass
- Manual steps 1–4 behave as described
- No `double`, `float` or culture-less `Parse` in our code on the spreadsheet path
