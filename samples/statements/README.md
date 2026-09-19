# Sample statements for 004

Hand-written to mirror the layouts Brazilian banks actually export, each with the
quirks the parser has to survive, and each with a closing balance so the total the
app shows can be checked against the file. Nothing here is real data.

Import all of them into the same user to see dedupe across formats. The expected
outcome of each upload is listed so a difference is a bug, not a shrug.

| File | Format | Account | Mapping | Rows | Expected |
|---|---|---|---|---|---|
| `nubank-conta-2026-08.ofx` | OFX 1.x, UTF-8, `MEMO` only, uuid `FITID`, `[-3:BRT]` | Nubank | none | 12 | 12 ready. Net **+1.043,61**; opening 2.500,00 → `LEDGERBAL` 3.543,61. Rows 4 and 5 are two identical purchases with different `FITID`s: both import, because the id decides. |
| `nubank-conta-2026-09.ofx` | same | Nubank | none | 12 | Overlaps the August file by 5 rows (rows 8–12). **5 duplicates, 6 ready, 1 invalid** (`Compra internacional` carries `CURSYM USD`: "Moeda diferente da conta (USD)"). Commit writes 6, net **+1.621,54**; `LEDGERBAL` 5.165,15 = 3.543,61 + 1.621,54. |
| `nubank-conta-2026-08.csv` | Nubank account CSV: `Data,Valor,Identificador,Descrição`, comma, `dd/MM/yyyy`, **dot decimals** | Nubank | `en-US`, `dd/MM/yyyy`, Signed, date `Data`, amount `Valor`, description `Descrição` | 12 | The same month as the OFX without ids. After the OFX is committed: **12 duplicates** by date + amount + normalized description. Before it: 11 ready + 1 duplicate (the second Pão de Açúcar, identical within the batch). |
| `nubank-cartao-2026-08.csv` | Nubank card CSV: `date,category,title,amount`, ISO dates, **purchases positive** | Cartão Nubank | `en-US`, `yyyy-MM-dd`, **SignedInverted**, date `date`, amount `amount`, description `title` | 9 | 9 ready. Purchases become negative, `Pagamento recebido` becomes +1.250,00. Net **+616,57**. The quoted title with a comma stays one field. |
| `inter-extrato-2026-08.csv` | Inter: **4 preamble lines**, semicolon, `1.234,56`, `Saldo` beside `Valor`, **Windows-1252** | Inter | `pt-BR`, `dd/MM/yyyy`, Signed, date `Data Lançamento`, amount `Valor`, description `Histórico` + `Descrição` | 11 | Preview says "4 linhas ignoradas". **8 ready, 1 duplicate, 2 invalid**: `Saldo anterior` has no value, `TARIFA` is 0,00, and the second `JOÃO DA SILVA` on 13/08 is a within-batch duplicate (the `Saldo` column proves it is real; tick it to include). Net of the 8: **+752,15**; with the duplicate included **+502,15** = 3.002,15 − 2.500,00. |
| `bradesco-extrato-2026-08.csv` | Bradesco: 1 preamble line, semicolon, **`dd/MM/yy`**, separate `Crédito (R$)` / `Débito (R$)` with **negative debits**, trailing `;`, Windows-1252 | Bradesco | `pt-BR`, `dd/MM/yy`, **DebitCredit**, date `Data`, debit `Débito (R$)`, credit `Crédito (R$)`, description `Histórico` | 8 | **6 ready, 2 invalid** (`SALDO ANTERIOR` and `SALDO` have no value). Net **+1.333,80** = 2.568,36 − 1.234,56. |
| `itau-extrato-2026-08.ofx` | Itaú: `CHARSET:1252`, `TRNTYPE OTHER`, `CHECKNUM`, `MEMO` only, `[-03:EST]`, accented memos in Windows-1252 | Itaú | none | 8 | 8 ready. Net **+1.226,05**; `LEDGERBAL` 2.726,05 = 1.500,00 + 1.226,05. `JOÃO` and `SERVIÇOS` must show their accents. |
| `problemas.csv` | semicolon, `pt-BR`, one problem per row | any | `pt-BR`, `dd/MM/yyyy`, Signed, date `Data`, amount `Valor`, description `Descrição` | 10 | **3 ready, 7 invalid**, each row naming its reason. Switch the format to `MM/dd/yyyy` in the mapping step and the live preview turns `31/12/2026` into "Data inválida" while `01/08/2026` silently becomes 8 January: that is why the format is declared, never guessed. |

## Suggested order

1. `nubank-conta-2026-08.ofx` → commit. Every row lands in `Outros` or `Outras receitas`.
2. In the review of the next file, change the category of one row (say NETFLIX to Lazer) before committing; the next import of the same merchant will suggest it.
3. `nubank-conta-2026-09.ofx` → 5 duplicates already excluded, 1 invalid, commit the 6.
4. `nubank-conta-2026-08.csv` → all duplicates, commit is disabled, discard.
5. The rest, in any order. Save the mapping of each CSV as a template; the second time it is one dropdown.
6. Undo one batch from the history and watch `/transactions` lose exactly those rows.

## Checking the numbers

```sql
SELECT b."FileName", COUNT(*) AS rows, SUM(t."Amount") AS net
FROM "Transactions" t JOIN "ImportBatches" b ON b."Id" = t."ImportBatchId"
GROUP BY b."FileName" ORDER BY b."FileName";
```

The `net` column must match the table above to the cent, and `pg_typeof(t."Amount")` is
`numeric`.
