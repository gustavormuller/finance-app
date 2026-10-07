# 028 — Refusals in Portuguese, and a late save closes only its own form

## Goal

No refusal reaches the screen in English: a refusal without a sentence of its own, a network
failure and the health footer's status words all read in pt-BR. A save that lands after the person
cancelled its form, or opened another, closes only the form it came from, on every page with that
pattern.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | Where a refusal gets its sentence | In `web/src/api/finance.ts`, the one door every call goes through: `request()` and `download()` share the fetch and `refusal()`. |
| 2 | Which sentence | The problem's `detail`, the API's own pt-BR sentence; else a 400's field messages, joined; else a sentence for the status. Never the problem's `title`: every problem the API writes by hand has a `detail`, and the one it sends without, ASP.NET's 400, is titled in English ("One or more validation errors occurred."). |
| 3 | The sentences | 401: "Sua sessão terminou. Entre de novo para continuar." (027). 404: "Este item não existe mais; talvez tenha sido excluído em outra aba. Recarregue a página." 5xx: "O servidor não conseguiu concluir a operação. Tente de novo em instantes." Any other: "Não foi possível concluir a operação (erro N)." The network: "Não foi possível falar com o servidor. Confira a conexão e tente de novo." **(review)** |
| 4 | What a network failure is | A plain `Error` with that sentence, not an `ApiError`: "Excluir minha conta" keeps its own sentence for it (023, decision 14), and the returns keep retrying it, as they retry anything that is not an `ApiError` below 500. |
| 5 | The health footer | The API's words (`ok`, `degraded`, `unreachable`) stay on the wire and under the footer's test ids, and are shown through `lib/labels.ts`, as CLAUDE.md asks of identifiers: "ok", "com falha", "inacessível"; a word the map does not know is shown as sent. 001 and 022 showed the wire words, and `HealthRoute` said so in a comment; CLAUDE.md's rule wins. **(review)** |
| 6 | English that stays | Messages no one sees: `/api/auth/me answered N` (the guard takes any error as signed out), the logout's (the sidebar shows its own sentence), `main.tsx`'s missing root element. |
| 7 | How a save knows its form | `lib/openForm.ts`, `useOpenForm`: the page's open form as an opening, a new one each time a form opens, even the same form again. A save carries the opening it was sent from, and when it lands it closes the form only if that opening is still the open one. Cancelar closes whatever is open. One home for the five pages below (020). |
| 8 | Which pages | Every page whose save closes a form on success: `/categories` (create and edit; and a delete closes only the edit form of the category it deleted, not whatever is open), `/transactions`, an asset's movements, the market-data catalogue's Editar, and `/accounts`, where Nova conta is hidden while a create is in flight but Cancelar and Nova conta open a second form. Checked and left: account details (kept per account, closes nothing), the import wizard, adding an asset (it opens the asset), the catalogue's registration (one form, always open). |
| 9 | A late save whose form is gone | It only refreshes the data. On `/accounts` the created account appears in the list and is not selected: the person cancelled that form, or is filling in another. **(review)** |
| 10 | The transaction form's values | The form is keyed by what it edits. react-hook-form reads its defaults once, so Editar while the new-transaction form or another row's form was open kept the values typed there, and Salvar wrote them onto the row now named. |
| 11 | When a failure's sentence goes | When the next write starts (categories, transactions, movements, accounts), no longer when the form closes: a late save that leaves another form open must not clear that form's sentence. |
| 12 | Tests | E2E, each defect reproduced in the test's own page with `page.route`: a write answered with a bare 500 or 403, or aborted; a save held back until another form is open (`support.ts` gains `holdBack`). A bare 404 is real: the row is deleted in a second tab (024, decision 10). Unit, only where E2E cannot reach: a 400's message is its field messages, never the title, which no page shows on its own. |

## Out of scope

- A 2xx answer whose body is not JSON: the proxies send `/api` only to the API, which always
  answers JSON
- Field-by-field API messages in the transaction and category forms (027, decision 2)
- Disabling a page's other forms while a save is in flight
- Spec 024's inventory, which lists the footer's `degraded` on the dev server as not reachable

## Data model changes

None. No migration.

## API surface

None.

## UI behaviour

- A refusal with no sentence of its own reads the sentence of decision 3, wherever the page shows
  a refusal; a network failure reads the network sentence.
- The footer reads "API ok · Banco ok", or "API com falha · Banco inacessível".
- Editar while a save is in flight opens that row's form, with that row's values, and the save
  landing leaves it open. Cancelar, then a new form, while a save is in flight: the new form stays.
  On `/categories`, deleting a row leaves another category's open form alone.

## Test plan

### E2E — `web/e2e`

| File | Test | Reproduces |
|---|---|---|
| `transactions.spec.ts` | "a refusal that comes without a sentence still reads in Portuguese" | a 404 from a row deleted in another tab, a bare 500 and 403 on a create, a create and an export whose request fails |
| `health.spec.ts` | "the footer says in Portuguese when the database cannot be reached" | the 503 the API sends then, answered in the test's page |
| `categories.spec.ts` | "a write that lands late closes only its own form" | a create held back while Editar is opened; a row deleted while another form is open |
| `transactions.spec.ts` | "Editar while a new transaction is saving opens that row, and the save leaves it open" | decisions 7 and 10 |
| `investments.spec.ts` | "a movement saved late leaves the form opened since" | a new movement held back while Editar is opened |
| `market-data.spec.ts` | "a source saved late leaves another entry's editor open" | one entry's save held back while another's Editar is opened |
| `accounts.spec.ts` | "an account created after its form was cancelled leaves the next form open" | decision 9 |

Each failed first for the reason in its row. The PWA suite's test 10 reads "com falha".

### Unit — `web`

`api/finance.test.ts`: a 400 with fields and ASP.NET's English title is an `ApiError` whose message
is the field messages. `HealthRoute.test.tsx` keeps its 503 test, now expecting the pt-BR words.

## End-to-end verification

As 027's, against an isolated PostgreSQL (`bugs-pg` on 55445), the API on 5105 and Vite on 5205,
never the owner's `financas`; the full suite twice in a row, nothing skipped. The PWA suite
(`npm run e2e:pwa`) against the same API, with `App__Origin` set to its preview port.

## Definition of done

- The seven tests above pass, and failed first; the full suite passes twice in a row
- `bash scripts/verify.sh` green
- The (review) decisions confirmed or changed by a person
