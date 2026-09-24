# 005 — Dashboard

## Goal

The home page: where the money is, where it went this month, and how the last year looks. Also resolves 004's forward dependency — transfers between the user's own accounts must stop counting as income and expense.

## Decisions made in this spec

| # | Question | Decision |
|---|---|---|
| 1 | Transfers double-counting | New `Category.Kind = Transfer`. Rows in a Transfer category affect account balances but are excluded from income/expense aggregations. **No linking, no pairing logic.** |
| 2 | Rule 3 for Transfer | Any sign allowed. The rule only constrains `Income` and `Expense`. |
| 3 | Opening balance | `Account.OpeningBalance numeric(18,2) default 0` — the balance before all recorded transactions. No date. If older data is imported later, the user adjusts it. |
| 4 | Aggregation | Computed on read with SQL via Dapper (ARCHITECTURE.md §6). Thousands of rows over an indexed `GROUP BY` is milliseconds. Nothing materialised. |
| 5 | "Month" | Calendar month of the `Date` column. `DateOnly` means no timezone is involved anywhere. |
| 6 | Category breakdown | Top-level only; children roll up into their parent. |
| 7 | Recurrence detection | **Cut.** ARCHITECTURE.md lists it under this phase, but it is a separate feature and 009's analysis covers the use case better. |
| 8 | Home route | `/` becomes the dashboard. The protected layout's landing page moves. |

## Out of scope

- Transfer linking or auto-detection between accounts
- Investments on the dashboard — 007 adds a portfolio card
- Budgets, goals, alerts
- Recurrence detection
- Any export

## Data model changes

Migration: `AddDashboard`. Review the SQL.

### `Category.Kind`

Add `Transfer = 2`. Integer column, no schema change. Existing rows unaffected.

Seed a top-level `Transferência` category with `Kind = Transfer` on user creation, alongside the eight from 003. Existing users create it by hand — consistent with the no-backfill policy from 004.

### `Account.OpeningBalance`

`numeric(18,2) NOT NULL DEFAULT 0`.

## Domain

### Rule changes — `Domain/Transactions/TransactionRules.cs`

Rule 3 (sign versus kind):

| Kind | Allowed sign |
|---|---|
| `Income` | `> 0` |
| `Expense` | `< 0` |
| `Transfer` | any non-zero |

004's `CategorySuggester` never suggests a Transfer category by default. History lookup (rung 2) may return one — if the user categorised "PAGAMENTO FATURA" as Transferência once, that is exactly what should happen next time.

### Aggregations — `Application/Dashboard/`

Three queries, all Dapper, all parameterised by `UserId` explicitly — Dapper bypasses the EF query filter, so **every query in this folder carries `WHERE "UserId" = @userId` and there is an integration test proving it.**

**Balances:**
```sql
SELECT a."Id", a."Name", a."Type", a."Currency",
       a."OpeningBalance" + COALESCE(SUM(t."Amount"), 0) AS "Balance"
FROM "Accounts" a
LEFT JOIN "Transactions" t ON t."AccountId" = a."Id"
WHERE a."UserId" = @userId
GROUP BY a."Id"
```

**Monthly series** — last N months, income and expense per month, **zero-filled** for months with no rows, Transfer excluded:
```sql
-- generate_series over months, LEFT JOIN aggregated transactions
-- WHERE c."Kind" <> 2 (Transfer)
```

**By category** — one month, one kind, top-level rollup:
```sql
-- COALESCE(c."ParentId", c."Id") AS top-level id
-- SUM(t."Amount") GROUP BY top-level, ordered by ABS(sum) DESC
```

Each returns `decimal`. Assert on the Dapper mapping types.

## API surface

```
GET /api/dashboard/summary?month=YYYY-MM
    200 {
      balances: [{ accountId, name, type, currency, balance }],
      total: 12345.67,
      month: { income, expense, net }
    }

GET /api/dashboard/monthly?months=12
    200 [{ month: "2026-09", income, expense }]     oldest first, zero-filled

GET /api/dashboard/by-category?month=YYYY-MM&kind=Expense
    200 [{ categoryId, name, amount, share }]      share = amount / total, 4dp
```

`month` defaults to the current calendar month. `months` capped at 36. `kind` is `Income` or `Expense` — `Transfer` is `400`.

`total` sums balances across accounts **in BRL only**. Foreign-currency accounts are listed but excluded from the total, with `excludedFromTotal: true` on the row. Currency conversion for cash accounts is not this feature's problem.

## UI behaviour

Route `/`, replacing the current landing.

Top to bottom:

1. **Total balance** — one large number, BRL. Below it, one row per account with balance, credit cards visibly negative.
2. **This month** — income, expense, net, side by side. Month selector to go back.
3. **Last 12 months** — grouped bars, income and expense, Recharts. Hover shows the values.
4. **This month by category** — horizontal bars for expenses, top-level, sorted by amount, share as percentage. Toggle to income.
5. **Recent** — last 10 transactions, linking to `/transactions`.

Same design direction as 003 — stock shadcn. `Amount` component reused everywhere. All numbers tabular.

Empty state when there are no transactions at all: a message and links to `/transactions` and `/import`.

## Test plan

### Unit — `api.tests/Unit`

1. Rule 3 with `Transfer` accepts positive
2. Rule 3 with `Transfer` accepts negative
3. Rule 3 with `Transfer` rejects zero
4. `Income` and `Expense` behaviour unchanged from 003

### Integration — `api.tests/Integration`

**Isolation — mandatory, and doubly so because Dapper bypasses the filter:**
5. A has transactions, B calls all three endpoints → B sees zero balances and empty series
6. A grep-style test: every `.sql` or SQL string in `Application/Dashboard/` contains `"UserId" = @userId`. A missing clause fails the build, not production.

**Balances:**
7. `OpeningBalance` 1000 + transactions −200 and +50 → 850
8. Account with no transactions → balance equals opening balance
9. Foreign-currency account listed, excluded from `total`, flagged

**Transfers:**
10. A −3000 Transfer in checking and a +3000 Transfer on the card → both balances move, `month.expense` and `month.income` unchanged
11. `by-category` never returns a Transfer category
12. `kind=Transfer` → `400`

**Monthly:**
13. 12 months requested, 3 have data → 12 entries, 9 with zeros, oldest first
14. A transaction on the last day of a month lands in that month, not the next
15. `months=100` → clamped to 36

**By category:**
16. Child category amounts roll into the parent; the child does not appear
17. `share` values sum to 1.0000 within 0.0001
18. Ordered by absolute amount descending

**Seeding:**
19. New user has `Transferência` with `Kind = Transfer`

### Unit — `web`

20. Credit card balance renders negative with the distinct style
21. Month selector changes the query parameter
22. Kind toggle switches the category chart
23. Empty state renders when `balances` is empty and `monthly` is all zeros

### E2E — `web/e2e`

24. Sign in → `/` shows the dashboard, not the old landing
25. Create an expense → total balance decreases, current month expense updates
26. Categorise a transaction as Transferência → month totals do not change, account balance does

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual:

1. Set an opening balance on your real checking account to match the bank's balance before your first imported transaction
2. Dashboard total now matches the bank's current balance — if it does not, the difference is what you have not imported
3. Categorise your last card-bill payment as Transferência on both sides
4. Month expense drops by that amount; balances unchanged
5. Compare "this month by category" against your own sense of where the money went

Step 2 is the check that tells you whether the ledger is complete.

## Definition of done

- Both verify scripts green
- Manual steps 1–5 behave as described
- Test 6 passes — no Dapper query without a `UserId` predicate
- Migration SQL reviewed
- `/` is the dashboard

## Amendments

Added by the orchestrator of the autonomous run after reconciling with docs/handoffs/004.md.

1. **The web assumes two category kinds.** `TransactionForm` derives the sign from the kind, and
   `PreviewStep` and `CategoriesPage` offer only Income and Expense (handoff 004). E2E 26 and the
   "existing users create Transferência by hand" policy both require the UI to accept
   `Transfer`: the categories page must let a user create a Transfer category, the transaction
   form must let the user choose the sign freely for a Transfer category, and `labels.ts` maps
   `Transfer` → "Transferência". Done in checkpoint 3.
2. **`CategorySuggester` discards a history match whose kind does not match the sign**
   (handoff 004). §Domain says a Transfer history match must be kept. Rule: a history match
   in a Transfer category is always kept; the sign default still never picks Transfer.
3. **No way to set `OpeningBalance` exists.** Manual step 1 requires it. The existing account
   create/update endpoints and account form accept `openingBalance` (decimal, default 0).
