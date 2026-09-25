# 023 — Delete my account

## Goal

A person can delete their own account from `/settings`, and everything they stored goes with it,
at once and for good: the working account deletion ADR-013 requires before the link circulates
outside family and friends. Shared market data stays, and no other user's row is touched.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | Route | `DELETE /api/auth/me`: the resource `GET` reads and `PATCH` changes (002, 009). The brief named `/api/me`; no route lives there, and a second spelling of "me" would be the only one. **(review)** |
| 2 | What is deleted | Every row of every user-owned table (`IUserOwned`): `Accounts`, `Categories`, `Transactions`, `ImportBatches`, `StagedTransactions`, `CsvTemplates`, `Assets`, `Movements`, `PortfolioDaily`, `AiUsage`, `AiAnalyses`. Then the Identity user: `AspNetUserTokens`, `AspNetUserLogins`, `AspNetUserClaims`, `AspNetUsers`. |
| 3 | What stays | Shared market data: `MarketAssets`, `Prices`, `Benchmarks`, `SyncRuns`. A catalogue row only this user held stays too, with its prices: the catalogue is shared and a later holder reuses it (006). A sync summary holds counts only, never a user (007). **(review)** |
| 4 | How | One database transaction of set-based `ExecuteDeleteAsync` statements, children before parents: staged rows, transactions, import batches, accounts, categories, CSV templates, daily snapshots, movements, assets, AI usage, AI analyses, then the four Identity tables. The keys between user tables are RESTRICT (003, 004, 007), and PostgreSQL fires the cascades from `AspNetUsers` in an order nobody chose, so the cascades are not relied on. Categories go in one statement: its RESTRICT self-reference is checked at the end of the statement. |
| 5 | Tenancy | Every statement is scoped twice: by the global query filter (the signed-in user) and by an explicit `UserId` predicate. The user id comes from the session only; the deleting class takes no id, so no caller can point it at someone else. |
| 6 | Confirmation | The page asks for the signed-in e-mail, typed exactly (surrounding spaces ignored, case kept), before "Excluir definitivamente" is enabled. **(review)** |
| 7 | Server-side confirmation | None: the endpoint's guards are the session and 002's Origin check, like every other write. A script that can call it can also read the e-mail from `GET /api/auth/me`, so a body echoing it would stop only a bug. **(review)** |
| 8 | The session | The response clears the cookie (`SignOutAsync`). A copy of the old cookie is not revoked on the server, because a cookie session holds no server state: `GET /api/auth/me` answers 401 because the user row is gone (002), reads find nothing, writes fail on the key to `AspNetUsers`, and Identity's security-stamp check rejects the cookie within 30 minutes. **(review)** |
| 9 | Signing in again | The same Google account creates a new, empty account: a new id, the eight default categories, AI off. |
| 10 | AI analysis job | A `Pending` analysis whose user is gone is dequeued and runs nothing: the job reads its row through the filter and finds none (009). A `Running` one whose user goes mid-call ends quietly: the call finishes and is paid, but its usage row and outcome cannot be written, because the user is gone. The job logs one Information line and moves on, with no Warning, no Error and no row. **(review)** |
| 11 | Nightly snapshot rebuild | A user deleted after the rebuild listed them: the first of their assets that fails ends that user's part of the run with one Information line. It is not counted as a failure, so the sync summary every user sees on `/market-data` does not report one. The other users rebuild as before. |
| 12 | A concurrent write | A write by the same person in another tab can race the delete (a new transaction on an account being deleted). The RESTRICT key refuses the delete, the transaction rolls back and nothing is deleted. The answer is a 500, and the page says to try again. |
| 13 | After success | Go to `/login?notice=deleted`, then clear the whole query cache: in that order, so no guard refetches `me` on the way and redirects without the notice. The login page reads "Sua conta e todos os dados dela foram excluídos." **(review)** |
| 14 | Failure copy | The API's problem `detail` when it sends one. A 401 reads "Sua sessão terminou. Entre de novo para excluir a conta." A failure without a message (403, 5xx, network) reads "Não foi possível excluir a conta. Tente de novo.", without claiming that nothing was deleted, because a lost response may follow a commit. |

## Out of scope

- Data export or portability
- The privacy-policy text and the legal basis (ADR-013's other two items)
- Confirmation by e-mail, a grace period, soft delete, or restoring an account
- Revoking copies of old cookies on the server (decision 8)
- Removing a catalogue row nobody holds any more (decision 3)

## Data model changes

None. No migration.

## API surface

```
DELETE /api/auth/me          authenticated; Origin check (002)
    204   everything the user owned and the user itself deleted, in one transaction;
          Set-Cookie clears the session
    401   no session, or a cookie for a user row that no longer exists (nothing is deleted)
    403   Origin missing or foreign (nothing is deleted)
```

`Application/UserDeletion.DeleteAsync()` does the work for the signed-in user (decisions 4 and 5).

Jobs (decisions 10 and 11): `MonthlyAnalysis.RunAsync` and `SnapshotRebuildAfterSync` check,
when a step fails, whether the user still exists. If the user is gone, they stop quietly.

## UI behaviour

At the bottom of `/settings`, after the AI sections, a danger-zone card (`aria-labelledby`
`delete-account-heading`), bordered and headed in the destructive token:

- Heading **Excluir minha conta**.
- "Excluir a conta apaga para sempre tudo o que você guardou aqui: contas, lançamentos,
  importações, investimentos e análises de IA, além das suas categorias e modelos de importação.
  Não dá para desfazer."
- "Você sai do app logo em seguida. Entrar de novo com o mesmo Google cria uma conta nova, vazia."
- A field **Digite seu e-mail para confirmar**, with the address shown above it
  ("Para confirmar, digite <strong>ada@example.com</strong>."), `autoComplete="off"`.
- **Excluir definitivamente**, the destructive button, disabled until the field matches
  (decision 6) and while the request is in flight, when it reads "Excluindo…".
- A failure shows in the card's alert (decision 14) and the page stays as it is.

`/login?notice=deleted` shows the notice of decision 13 above the sign-in button, as a status,
not an alert. An unknown `notice` shows nothing, as an unknown `error` does.

Both themes through the existing tokens; at 390 px nothing scrolls sideways.

## Test plan

### Integration — `api.tests/Integration`

1. `DELETE /api/auth/me` without a session is 401. With a foreign Origin it is 403, and the session and the user's rows are intact.
2. Signed in: 204 with a `Set-Cookie` clearing the session. The same client's `GET /api/auth/me` is then 401, and so is a replay of the old cookie. Signing in again with the same e-mail gives a new id with only the default categories.
3. **Every user-owned type, from the EF model.** The test enumerates the entity types implementing `IUserOwned` and seeds at least one row of each for the user being deleted and for a bystander. A type with no seed fails the test by name, so a new user-owned table fails until someone seeds it and the deletion covers it. After the delete, no row of any of those types, and no Identity row (user, login, claim, token), remains for the deleted user. Every one of the bystander's rows remains. The shared rows (a market asset both held, its prices, a benchmark, a sync run) are untouched.
4. A `Pending` analysis whose user was deleted is dequeued and runs nothing: no provider call.
5. A `Running` analysis whose user is deleted while the provider call is in flight writes no row and no usage, and logs nothing at Warning or above. The job then runs the next user's analysis.
6. The rebuild after a sync, with a user deleted while their first asset waits for its lock: the summary counts no failure and has no error, and the bystander's asset is rebuilt.

### Unit — `web`

7. The danger zone is the last section of `/settings` and names what goes: contas, lançamentos, importações, investimentos, análises de IA.
8. "Excluir definitivamente" stays disabled until the typed text is the signed-in e-mail: another address or a different case does not enable it, and surrounding spaces are ignored.
9. Confirming sends `DELETE /api/auth/me`, clears the query cache and lands on `/login` with the notice.
10. A refusal with a problem detail shows it. A 401 shows the session sentence, and a 500 without a body shows the generic one. The page stays.
11. The login page shows the notice for `?notice=deleted`, and nothing for an unknown notice.

### E2E — `web/e2e`

12. A fresh user types their e-mail on `/settings` and deletes the account. They land on `/login` with the notice. `GET /api/auth/me` with the browser's session, and with a replay of the cookie saved before the delete, is 401. `/` redirects to `/login`.

Every existing test keeps passing.

## End-to-end verification

```
bash scripts/verify.sh
```

E2E against an isolated database (never the owner's `financas`).

Manual, once, on a throwaway account in the dev database:

1. Sign in, create an account, a transaction, an import and an asset with a movement.
2. Delete the account from `/settings`. You land on `/login` with the notice.
3. In `psql`, for that user's id: `SELECT count(*)` over every user table is 0 and the
   `AspNetUsers` row is gone. The catalogue row and its prices are still there.
4. Sign in again with the same Google account: an empty dashboard and the default categories.

## Definition of done

- `verify.sh` green; E2E green on an isolated database
- Tests 1–12 exist and pass
- No migration; no `double` or `float` in the code this spec touches
- The (review) decisions confirmed or changed by a person
