# 015 — Accounts as the home of import and its history

## Goal

A statement belongs to one bank account, so it is imported from inside that account: pick
the account, drop the file, and see that account's past imports beside it. `/accounts`
becomes a master-detail page — the accounts with their balances on the left, the selected
account's transactions, import and details on the right — and `/import` stops being a page.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | Routes | `/accounts` is a layout: header, the account list, and an outlet. `/accounts/$accountId` is the selected account; its tab is the `tab` search parameter, `transactions`, `import` or `details`. `/accounts` alone opens the first account (the API's order, by name). An unknown account id says "Conta não encontrada". |
| 2 | Default tab | **Lançamentos** (`tab` absent). The import is one click away and survives a reload in the URL. **(review)** |
| 3 | Balances | From `GET /api/dashboard/summary` `balances` (all-time: opening balance plus every transaction), sharing the dashboard's query cache; "Total em contas" is its `total`, which is BRL only (005). `GET /api/accounts` keeps giving only the opening balance, used by the details form. No API change. |
| 4 | Last import line | Computed from `GET /api/imports`: the account's staged batch reads `Extrato em revisão`; otherwise the latest committed batch reads `Último extrato em DD/MM` (its `committedAt`, local day; `DD/MM/AAAA` outside the current year); otherwise `Sem importações`. **(review)** |
| 5 | History per account | `GET /api/imports` filtered by `accountId` in the browser — the list is already loaded for decision 4 and a person's import history is short. No query parameter is added. The history drops its "Conta" column. |
| 6 | File step | No account picker: the account is the page's. A drop zone, `Arraste o extrato do <conta> aqui`, with a `Escolher arquivo` button; **choosing or dropping a file starts at once** (the "Enviar" button goes), and the extension still decides mapping or review. **(review)** |
| 7 | Hint | `OFX, CSV ou planilha do Excel (.xls, .xlsx), até 2 MB e 5.000 lançamentos. Lançamentos repetidos ficam de fora sozinhos.` and, smaller, 011's `Um OFX vai direto para a revisão; CSV e planilhas passam antes pelo mapeamento das colunas.` |
| 8 | One open batch | The API keeps one staged batch per user (004). If it belongs to **another** account, the import tab replaces the drop zone with `Há um extrato em revisão na conta <outra>. Conclua ou descarte essa importação antes de importar outro.` and a link to that account's import tab. If it belongs to **this** account, the drop zone is replaced by `O extrato <arquivo> ainda está em revisão.` and `Continuar a revisão`. A 409 that slips through (another tab) still offers "Abrir a importação em andamento". **(review)** |
| 9 | `/import` | A redirect, not a page: to `/accounts/<account of the staged batch>?tab=import` when there is one, to `/accounts` otherwise. Removed from the sidebar. `/transactions?importBatchId=…` from the done step is unchanged. The dashboard's empty state links to `/accounts`. |
| 10 | Lançamentos tab | The account's ten latest transactions (`GET /api/transactions?accountId=…`), newest first as the API orders them, and `Ver todos os lançamentos da conta` → `/transactions?accountId=…`, which opens filtered to the account **with no date range** (as `importBatchId` does), not on the current month. **(review)** |
| 11 | Details tab | The existing form, validation and messages: name, type, currency, opening balance, `Salvar conta`; `Excluir conta`, with the API's 409 sentence shown as before and, as before, no confirmation step. After a delete the page goes back to `/accounts`. |
| 12 | New account | `Nova conta` in the header opens the existing create form above the list; after `Criar conta` the new account is selected. With no accounts, an empty state explains and its button opens the same form. |
| 13 | Layout | Two columns from `xl` (1280 px: the sidebar leaves too little room at `lg`), list over detail below it, the list two cards wide from `sm`. Cards with icon by type (Landmark, PiggyBank, CreditCard, Banknote, TrendingUp), name, type label, the line of decision 4 and the balance; a credit card in debt uses the destructive colour, as on the dashboard. Every grid child has `min-w-0`; at 390 px nothing scrolls sideways, and the tab strip scrolls inside itself if it must. |
| 14 | Freshness | A commit, discard or undo invalidates `imports`, `transactions` and `dashboard` (balances); an account write invalidates `accounts` and `dashboard`. |
| 15 | What does not change | The wizard's steps, copy, `data-testid`s (`import-step`, `mapping-preview`, `preview-counts`, `commit-summary`, `staged-row-*`, `ai-marker`), templates, AI suggestion and undo confirmation. |

## Out of scope

- Any API or data-model change
- The mockup's "Modelo salvo" button on the drop zone: a template is still chosen in the mapping step
- The mockup's credit-card line "Fatura fecha dia 05": there is no closing day in the data
- Reordering accounts, archiving them, or per-account colours
- Importing one file into several accounts

## Data model / API

None. The page reads `GET /api/accounts`, `GET /api/dashboard/summary`, `GET /api/imports` and
`GET /api/transactions?accountId=…`, all existing.

## UI behaviour

- **Header.** `Contas`, the line `O extrato entra pela conta a que ele pertence: sem escolher conta de novo, com o histórico dela ao lado.` and `Nova conta`.
- **List.** One card per account (decision 13), then `Total em contas`.
- **Detail.** The type label over the account's name, `Saldo` with the current balance, and the three tabs: `Lançamentos`, `Importar extrato`, `Detalhes da conta`.
- **Importar extrato.** The wizard as in 004 and 011, minus the account picker, with the drop zone of decision 6 as its first step; the step headings (`1. Arquivo` … `4. Concluído`) and `Recomeçar` stay. Under it, `Importações desta conta`: file, when, status and source, rows, imported, and the existing `Continuar`, `Descartar` and `Desfazer`.

## Test plan

### Unit — `web`

1. The list shows each account with its type label and its **current** balance from the summary (not the opening balance), and `Total em contas` from the summary's total
2. The last-import line reads `Último extrato em DD/MM` from the latest committed batch, `Extrato em revisão` for the account holding the staged batch, and `Sem importações` otherwise
3. `/accounts` opens the first account, whose card is marked current
4. With no accounts, the empty state's button opens the create form; creating selects the new account
5. Create sends the opening balance typed in pt-BR with its sign, zero when left alone, refuses a non-number without a round trip, and shows a 400's field message under the field (the four existing tests)
6. The details tab opens on the stored opening balance and sends it back unchanged (existing test)
7. The details tab shows the API's 409 sentence when a delete is refused
8. The Lançamentos tab asks for the account's transactions; its link opens `/transactions` filtered to the account, with no date range
9. The import tab has no account picker, names the account in the drop zone, and choosing an OFX uploads it with this account's id
10. Dropping a file on the zone starts the same upload
11. The history lists only this account's batches
12. With the staged batch on another account: the notice, no drop zone, and a link to that account's import tab
13. With the staged batch on this account: `Continuar a revisão` opens its review
14. "Sugerir com IA" posts, refetches and says what changed; its 403/402/504/502 details are shown verbatim (moved from `ImportPage.test.tsx`)
15. 011 tests 16 and 17, through the account's import tab (moved)
16. `/import` goes to the staged batch's account import tab; with none, to `/accounts` and so the first account
17. The sidebar has no `Importar` link

The step components' own tests (`MappingStep`, `PreviewStep`, `UndoButton`) keep passing unchanged.

### E2E — `web/e2e`

18. 004 tests 63–66 and 011 test 18 go through the account's import tab (a shared `openImportTab` helper in `support.ts`) with every outcome assertion kept
19. 009 test 28's upload goes through the same helper
20. After a commit, the account's card shows the new balance and `Último extrato em …`, and `/import` with a staged batch lands on that account's review tab

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual, in both themes, at 1440 px and at 390 px: open `/accounts`, pick an account, import an
OFX from its tab, commit; the card's balance and last-import line change, the history lists the
batch, and "Ver lançamentos" opens the batch in `/transactions`. Start an import on one account,
open another account's import tab: the notice links back.

## Definition of done

- Both verify scripts green
- Tests 1–20 exist and pass
- The manual steps behave as described, with no horizontal scroll at 390 px
