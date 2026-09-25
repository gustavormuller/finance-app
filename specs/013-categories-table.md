# 013 — Categories as a table with usage

## Goal

Make the categories page answer "how is my money categorised" instead of only listing names:
one table with each category's use over the last 12 months, main categories and their
subcategories in one tree, and every edit happening on the row being edited — never in a form
at the top of the page. This is variation C4 of the exploration.

## Decisions made in this spec

| # | Question | Decision |
|---|---|---|
| 1 | Layout | One table: Categoria (the tree), Tipo, Lançamentos 12m, Total 12m, Participação, actions. Main categories are rows; their subcategories are indented rows under them, collapsed by default and opened by clicking the main row. |
| 2 | Filters | Kind: Todas (default, grouped under Receitas / Despesas / Transferências headings), Despesas, Receitas, Transferências. A search box that keeps a main category visible when one of its subcategories matches. A "Só as sem uso" switch that shows only categories with no transaction in the 12 months (with their main category as context). |
| 3 | Usage | `GET /api/categories/usage` returns, per category, the number of transactions and their sum in a date range, default the 12 months ending today. A main category's row shows its own use plus its subcategories'. |
| 4 | Participação | A category's share of its kind's total in the range: expenses of all expenses, income of all income. Transfers have none. A bar and the percentage. |
| 5 | Editing | "Editar" opens a form in the row right under the category: Nome, Tipo, "Fica dentro de" (Nenhuma — é uma categoria principal, or a main category of the same kind). Salvar, Cancelar, Excluir. Only one form is open at a time. |
| 6 | Creating | "Nova categoria" opens the same form as the first row of the table. "Nova subcategoria" on a main row opens it under that category's subcategories, with "Fica dentro de" already set. |
| 7 | Wording | "Categoria mãe" becomes "Fica dentro de"; "Nenhuma (nível principal)" becomes "Nenhuma — é uma categoria principal". |
| 8 | What does not change | The two-level rule, the kind rules and every message the API already returns. |

## API surface

```
GET /api/categories/usage?from=&to=
    200 [{ categoryId, count, total }]   one entry per category with at least one
                                         transaction in [from, to]; total is decimal,
                                         signed as the transactions are
    400 from after to (pt-BR message), or a date that does not parse
```

`from` and `to` are optional ISO dates; without them the range is the 12 months ending today
(`today − 12 months + 1 day` to `today`). Filtered by user like every query.

## Test plan

### Integration — `api.tests/Integration`

1. Usage counts and sums each category's transactions in the default range, and leaves out older ones
2. `from`/`to` narrow the range
3. Another user's transactions are never counted
4. `from` after `to` is a 400 with its message

### Unit — `web`

5. The table lists main categories with their 12-month count, total and share
6. Clicking a main category shows its subcategories
7. "Editar" opens the form right after that category's row, not at the top
8. "Nova subcategoria" opens the form with "Fica dentro de" set to that category
9. "Só as sem uso" leaves only categories without transactions
10. The search keeps a main category whose subcategory matches

The three existing page tests keep passing (create a Transfer category; the Transferências heading).

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual: rename a subcategory, move it to another main category, create one under a main
category, and check that none of it happens far from the row clicked.

## Definition of done

- Both verify scripts green; tests 1–10 exist and pass
