# 003 — Transactions

## Goal

Record cash flow: accounts, categories, and transactions, with `Money` as a first-class domain type. This is the first feature with user-owned entities, so it is also the first real exercise of the `IUserOwned` query filter built in 002.

## Size warning

This is larger than 002. Four checkpoints, and it may take two sessions. Commit at every checkpoint so a `/clear` mid-feature loses nothing.

## Decisions made in this spec

| Decision | Choice | Why |
|---|---|---|
| `Money` persistence | EF `ComplexProperty` | Value object with no identity of its own; maps to two columns, no separate tracking. `OwnsOne` would create a fake identity. |
| Amount storage | `numeric(18,2)` | ADR: money is `decimal`. Never `float8`. |
| Sign convention | Signed amount; negative = expense | ARCHITECTURE.md. The sign is the source of truth for direction. |
| Category `kind` vs sign | Must agree; mismatch is a `400` | Prevents "Salary: −3000" and "Rent: +1200" from ever reaching the database. |
| Transaction date | `DateOnly` → `date` | A transaction happens on a day, not an instant. Bank statements give dates. `timestamptz` would invite timezone bugs for no benefit. |
| `CreatedAt` | `timestamptz`, UTC | This one *is* an instant. |
| Currency | On the account; transaction currency must match its account | Makes `Money` earn its place now rather than in 007. Only BRL in practice until investments. |
| Category depth | Exactly two levels — parent or child, no grandchildren | Matches how people categorise. Unbounded trees invite recursive queries for no gain. |
| Default categories | Seeded at user creation, same transaction | Without them the UI opens empty and unusable. Touches the 002 callback; additive and tested. |
| `ImportBatchId` | **Not in this feature** | `import_batches` arrives in 004. A nullable column is a trivial migration then; a dangling FK now is not. |
| Deletion | Hard delete | Undo is 004's problem, and only for imports. |
| Transfers | Out of scope | A transfer is two linked transactions and deserves its own modelling. |

## Out of scope

- Aggregations, charts, category breakdowns, monthly series — 005
- Import of any kind — 004
- Transfers between accounts
- Recurring or scheduled transactions
- Attachments, receipts, OCR
- Multi-currency beyond the account-match invariant
- Editing `ai_enabled`, still database-only

## Data model changes

Migration: `AddTransactions`. Review the SQL before applying.

### `Money` — `Domain/`

```csharp
public readonly record struct Money(decimal Amount, string Currency);
```

Rules:
- `Currency` is a 3-letter uppercase ISO 4217 code. Constructor rejects anything else.
- `Amount` is rounded to 2 decimal places on construction, `MidpointRounding.ToEven`.
- `+` and `-` on different currencies throw `InvalidOperationException`.
- Comparison across currencies throws.
- Not an entity, no `Id`, no EF configuration of its own beyond `ComplexProperty`.

### `Account : IUserOwned`

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `UserId` | `Guid` | FK, query-filtered |
| `Name` | `varchar(100)` | required |
| `Type` | `int` | enum: `Checking`, `Savings`, `CreditCard`, `Cash`, `Investment` |
| `Currency` | `char(3)` | default `BRL` |
| `CreatedAt` | `timestamptz` | |

Unique index: `(UserId, Name)`.

### `Category : IUserOwned`

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `UserId` | `Guid` | FK, query-filtered |
| `Name` | `varchar(100)` | required |
| `Kind` | `int` | enum: `Income`, `Expense` |
| `ParentId` | `Guid?` | FK to `Category`, `RESTRICT` on delete |
| `CreatedAt` | `timestamptz` | |

Unique index: `(UserId, ParentId, Name)`, **`NULLS NOT DISTINCT`**.
The qualifier is not optional: `ParentId` is null for every top-level category, and
PostgreSQL's default treats each of those nulls as distinct from the others — so
without it the index constrains child names only, and a second top-level `Food` sits
happily next to the seeded one. Requires PostgreSQL 15 or later.

A child's `Kind` must equal its parent's. A category whose `ParentId` is set may not itself be a parent.

### `Transaction : IUserOwned`

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `UserId` | `Guid` | FK, query-filtered |
| `AccountId` | `Guid` | FK, `RESTRICT` |
| `CategoryId` | `Guid` | FK, `RESTRICT` |
| `Amount` | `numeric(18,2)` | from `Money` |
| `Currency` | `char(3)` | from `Money` |
| `Date` | `date` | |
| `Description` | `varchar(300)` | required |
| `CreatedAt` | `timestamptz` | |

Index: `(UserId, Date DESC)` — the access pattern for essentially every dashboard query.

### Default categories

Seeded on user creation, all with `ParentId = null`. The names are Portuguese
because they are data a person reads, not identifiers:

- Income: `Salário`, `Outras receitas`
- Expense: `Moradia`, `Alimentação`, `Transporte`, `Saúde`, `Lazer`, `Outros`

## Validation rules

Every rule below returns `400` with a problem-details body naming the offending field.
The field names are English (they are the wire contract); the messages beside them are
pt-BR, because the UI renders them verbatim.

1. `Amount` may not be zero.
2. Transaction `Currency` must equal its account's `Currency`.
3. Sign must match category kind: `Income` requires a positive amount, `Expense` a negative one.
4. `AccountId` and `CategoryId` must resolve **under the current user's filter**. A row belonging to another user must behave as if it does not exist — `400`, never `403`, and never a successful write.
5. `Date` between `1900-01-01` and one year from today. A sanity bound, not a business rule — it exists to catch parse errors in 004.
6. A category with a `ParentId` may not be given children.

Rule 4 is the security-relevant one. It is the case where a valid-looking request from user B references user A's account.

## API surface

All routes require authentication; unauthenticated is `401`.

### Accounts

```
GET    /api/accounts              200  [{ id, name, type, currency, createdAt }]
POST   /api/accounts              201  Location + body
PUT    /api/accounts/{id}         200
DELETE /api/accounts/{id}         204 | 409 if transactions reference it
```

### Categories

```
GET    /api/categories            200  flat list including parentId
POST   /api/categories            201
PUT    /api/categories/{id}       200
DELETE /api/categories/{id}       204 | 409 if referenced or has children
```

### Transactions

```
GET    /api/transactions          200
       ?from=YYYY-MM-DD&to=YYYY-MM-DD&accountId=&categoryId=&page=1&pageSize=50
       { items: [...], page, pageSize, total }
POST   /api/transactions          201
PUT    /api/transactions/{id}     200
DELETE /api/transactions/{id}     204
```

`pageSize` capped at 200. Default ordering: `Date DESC, CreatedAt DESC`.

Transaction response shape:

```json
{
  "id": "...",
  "accountId": "...", "accountName": "Nubank",
  "categoryId": "...", "categoryName": "Food",
  "amount": -42.90, "currency": "BRL",
  "date": "2026-09-13",
  "description": "Supermarket",
  "createdAt": "2026-09-13T18:04:11Z"
}
```

`amount` serialises as a JSON number with two decimals. It must round-trip exactly — no float in the pipeline.

## UI behaviour

Three routes, all behind the existing protected layout. All copy is pt-BR.

**`/accounts`** — list, create, edit, delete. Delete shows the `409` reason rather than failing silently.

**`/categories`** — list grouped by kind, parents with children indented. Create, edit, delete.

**`/transactions`** — the main screen.

- Filter bar: date range (default: current month), account, category
- Table: date, description, category, account, amount — amount right-aligned, negative in a distinct colour
- Create and edit in a form: date, account, category, amount, description
- The amount field takes an unsigned number; the sign is derived from the selected category's kind. The user never types a minus sign.
- Category dropdown grouped by kind
- Pagination when `total > pageSize`
- Empty state distinguishes "no transactions yet" from "no results for this filter"

React Hook Form + Zod for forms, TanStack Query for data. No styling beyond legible, aligned, readable.

## Test plan

### Unit — `api.tests/Unit` (no database)

**`Money`:**
1. Construction rounds to 2 places, `ToEven` — `2.345` → `2.34`, `2.355` → `2.36`
2. Lowercase or 2-letter currency rejected
3. Same-currency addition and subtraction
4. Cross-currency addition throws
5. Cross-currency comparison throws
6. `0.1 + 0.2 == 0.3` exactly — the decimal guarantee, asserted explicitly

**Validation rules as pure functions:**
7. Sign-versus-kind rule, all four combinations
8. Zero amount rejected
9. Date bounds, both ends

### Integration — `api.tests/Integration`

**Isolation — mandatory for every user-owned entity:**
1. A creates account, category, transaction. B lists all three endpoints → empty in each.
2. B `POST /api/transactions` referencing A's `accountId` → `400`, and A's transaction count is unchanged.
3. B `PUT /api/transactions/{A's id}` → `404`. B `DELETE` the same → `404`.

**Persistence:**
4. `POST` then `GET` returns `amount` byte-identical. Include `1234567890.12` and `-0.01`.
5. `Date` round-trips as the same calendar day regardless of server timezone.
6. Default categories exist immediately after user creation, with the expected kinds.

**Rules:**
7. Currency mismatch with account → `400`
8. `Income` category with negative amount → `400`; `Expense` with positive → `400`
9. Zero amount → `400`
10. Grandchild category → `400`
11. Child category kind differing from parent → `400`

**Queries:**
12. Date-range filter excludes boundaries correctly — `from` and `to` are inclusive
13. Pagination: 120 rows, `pageSize=50`, page 2 returns rows 51–100 and `total == 120`
14. `pageSize=500` is clamped to 200
15. Ordering is `Date DESC, CreatedAt DESC` for same-day rows

**Referential integrity:**
16. `DELETE` an account with transactions → `409`, nothing deleted
17. `DELETE` a category with children → `409`

### Unit — `web`

1. Amount input plus `Expense` category produces a negative amount in the submitted payload
2. Amount input plus `Income` category produces a positive one
3. Form rejects zero
4. Empty state text differs between no-data and no-results
5. Negative amounts render in the distinct style

### E2E — `web/e2e`

Via `dev-login`:

1. Create an account, create a transaction, see it in the list with the correct sign
2. Filter by date range, see the list narrow
3. Edit the transaction's amount, see the list update

Do not modify `e2e/smoke.spec.ts`.

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual, once:

1. Sign in with real Google on a fresh account
2. Confirm the eight default categories are present, in Portuguese
3. Create a `Checking` account in BRL
4. Add an expense of `42,90` under Alimentação — the list shows `−42,90`
5. Add income of `3000,00` under Salário — shows `+3.000,00`
6. In `psql`: `SELECT "Amount", pg_typeof("Amount") FROM "Transactions";` → values exact, type `numeric`
7. Try deleting the account → `409` with a readable reason
8. Change the date filter to the previous month → the list empties, with the no-results message, not the no-data one

Step 6 is the one that proves the decimal chain held from the browser to the disk.

## Definition of done

- Both verify scripts green
- Manual steps 1–8 behave as described
- Migration SQL reviewed before applying
- `pg_typeof("Amount")` is `numeric`
- No `float`, `double`, or `real` appears anywhere in the transaction path
