# 027 — The bugs the E2E suite found

## Goal

Fix the seven bugs spec 024's E2E suite found and left as `test.fixme`, five of which put English
on screen against CLAUDE.md's pt-BR rule, so that each of those tests passes and the suite runs
with nothing skipped. Pin with an integration test the portfolio's refusal of an index or an
exchange rate (025), which no test covered.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | Where an address that matches no page is answered | The router's `defaultNotFoundComponent`, `routes/NotFoundPage.tsx`, so a not-found reached from any route reads the same. An unmatched address matches only the root route, so the page renders in the shell's `main`, under "Finanças Pessoais", outside the sidebar and the sign-in guard: it shows nothing but the address typed, and "Voltar ao Início" sends a visitor who is not signed in on to `/login`. **(review)** |
| 2 | How a refused transaction or category reads | `refusalMessage()` from `lib/refusal.ts` (020), as the transactions export already does: a 400's field messages become the sentence above the list, and a 409's detail reads as before. Not under the fields: the transaction form checks its fields on the client with its own messages, the category form shows none, and giving either the API's messages per field is a form change, not this fix. |
| 3 | The size in the 2 MB refusal | Written with the pt-BR separators `AmountParser` already declares in `Domain/Import/FieldParsers.cs`, where they are written down because `CultureInfo.GetCultureInfo("pt-BR")` throws under `InvariantGlobalization`. `AmountParser.NumberFormat(culture)` exposes them, and the refusal is formatted with `string.Create(provider, …)`. One decimal place, as before: "O arquivo tem 2,1 MB; o limite é 2 MB." |
| 4 | A 401's sentence | The API answers a 401 with no body. The client's `refusal()` (`api/finance.ts`) gives it "Sua sessão terminou. Entre de novo para continuar.", so every page that shows a refusal reads it. `DeleteAccount` keeps its own, "…para excluir a conta" (023, decision 14). |
| 5 | After a write refused with a 401, go to `/login`? | No. The sentence stays on the page, beside what the person typed, as the E2E expects; the next read of the session (a refocus, a reload) sends them to `/login` through the guard. **(review)** |
| 6 | Sair when the session already ended | A 401 from `POST /api/auth/logout` means the person is already signed out: the page lands on `/login`, as a normal Sair does. Any other failure (a 403 from the Origin check, a 5xx, the network) still reads "Não foi possível sair." The route stays behind the sign-in. **(review)** |
| 7 | An account created while `/accounts` is still opening its first account | The new account is selected before the form closes. Closing the form brings the outlet back, and on `/accounts` alone that is the index route, whose redirect to the first account won. `navigate` resolves once the new route is committed and rendered, so the outlet comes back on the new account. |
| 8 | The refused fixmes' assertions | Kept as 024 wrote them; none was wrong. |
| 9 | Unit tests | None added: each bug has its E2E test (024, phase 2: E2E owns the flows). The refusal of an index or a rate is an endpoint's status and field, which E2E does not see (024, decision 13), so it gets an integration test. |

## The bugs

| # | Bug (024) | Cause | Fix | Test now passing |
|---|---|---|---|---|
| 1 | An unknown address shows TanStack Router's English "Not Found". | `createRouter` sets no `defaultNotFoundComponent` and no route a `notFoundComponent`, so the root route renders the library's `<p>Not Found</p>`. | Decision 1. | `navigation` "an address that matches no page answers in Portuguese" |
| 2 | A transaction the API refuses with a 400 shows "One or more validation errors occurred." | `TransactionsPage` shows `error.message`, which for a 400 is the problem's English `title`; the pt-BR sentence is in `errors.date`. | Decision 2, for saving and deleting. | `transactions` "a date the API refuses is explained in Portuguese" |
| 3 | The same on `/categories`. | `CategoriesPage` shows `error.message`. | Decision 2, for saving and deleting. | `categories` "a refusal the API sends as a 400 is explained in Portuguese" |
| 4 | The 2 MB refusal reads "O arquivo tem 2.1 MB". | `{size:0.0}` formats with the current culture, the invariant one under `InvariantGlobalization`. | Decision 3. | `import` "a file over 2 MB is refused with its size written in Portuguese" |
| 5 | A write after the session ended in another tab shows "Request failed (401)". | The 401 has no body, and `refusal()` falls back to its English text. | Decisions 4 and 5. | `auth` "a write after the session ended in another tab is explained in Portuguese" |
| 6 | Sair from a tab whose session already ended shows "Não foi possível sair." and stays. | The logout route needs a session, so it answers 401, and the sidebar takes any non-2xx as a failure. | Decision 6. | `auth` "Sair from a tab whose session already ended lands on the login page" |
| 7 | An account created while `/accounts` is still opening its first account is not the one selected. | `create.onSuccess` closes the form, then navigates; the index route's redirect, mounted by the close, wins. | Decision 7. | `accounts` "an account created before the list has loaded is the one selected" |
| 8 | Not a bug: `POST /api/investments/assets` refuses an index or an exchange rate (025, decision 15), and no test pins it. | — | An integration test. | `InvestmentAssetEndpointTests.An_index_or_an_exchange_rate_is_refused_and_nothing_is_held` |

## Found on the way, not fixed

- Any other refusal that comes without a sentence still reads the client's English
  `Request failed (N)`: a bare 404 for a row another tab deleted (Editar on a deleted transaction),
  a 5xx with no body. Proposal: a pt-BR sentence in the same fallback. Outside the seven bugs, and
  no test reaches it yet.

## Out of scope

- Field-by-field API messages in the transaction and category forms (decision 2)
- Sending a person to `/login` after a refused write (decision 5)
- Allowing `POST /api/auth/logout` without a session; the web handles its 401 (decision 6)
- Editing spec 024's inventory and bug table, which name these tests as `fixme`: 024 is merged
  separately

## Data model changes

None. No migration.

## API surface

No route, status or shape changes. One message: the 413 of `POST /api/imports` and
`POST /api/imports/preview-csv` writes the size with a decimal comma.

## UI behaviour

- An address that matches no page: a glass card like the sign-in page's, headed
  "Página não encontrada", reading "Nenhuma página do app fica em `/nao-existe`.", and a primary
  link **Voltar ao Início** to `/`. Both themes through the existing tokens; nothing scrolls
  sideways at 390 px.
- A transaction or category refused with a 400 shows the API's pt-BR sentence in the page's alert.
- A refused write after the session ended reads "Sua sessão terminou. Entre de novo para continuar."
- Sair from a tab whose session already ended lands on `/login`.
- An account created at any moment is the one selected.

## Test plan

### E2E — `web/e2e`

The seven `test.fixme` lines are removed. Each test failed first for the reason its fixme gave,
and passes after its fix, unchanged otherwise. The suite then runs 98 tests in 18 files, none
skipped.

### Integration — `api.tests/Integration`

`InvestmentAssetEndpointTests.An_index_or_an_exchange_rate_is_refused_and_nothing_is_held`: an
index found in the catalogue (`marketAssetId`) and an exchange rate registered in the same call
(`class: Currency`) are each a 400 naming that field with "Índices e câmbio servem para
comparação e não entram na carteira."; nothing is held, and the refused registration leaves no
catalogue row.

## End-to-end verification

Against an isolated PostgreSQL and API, never the owner's `financas`, with the fakes on:

```
docker run -d --rm --name bugs-pg -e POSTGRES_USER=dev -e POSTGRES_PASSWORD=dev -p 55445:5432 postgres:16-alpine
dotnet build api -o <scratch>/api-e2e
# from <scratch>/api-e2e, with ASPNETCORE_ENVIRONMENT=Development, MarketData__ScheduledSync=false,
# MarketData__FakeProviders=true, Ai__FakeProvider=true, App__Origin=http://localhost:5205 and the
# connection string to localhost:55445, database financas_e2e_bugs:
dotnet Finance.Api.dll --urls http://localhost:5105
cd web && API_URL=http://localhost:5105 npx vite --port 5205 --strictPort
cd web && BASE_URL=http://localhost:5205 npx playwright test     # twice in a row, nothing skipped
```

## Definition of done

- The seven tests pass, with no `test.fixme` left in `web/e2e`; the full suite passes twice in a
  row on an isolated database
- The integration test above exists and passes
- `bash scripts/verify.sh` green
- The (review) decisions confirmed or changed by a person
