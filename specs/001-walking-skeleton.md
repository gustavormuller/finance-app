# 001 — Walking skeleton

## Goal

Prove the full request path works end to end: browser → Vite proxy → API → Postgres → back. This is infrastructure validation, not a product feature — it exists so that every later feature is built on a path already known to work.

## Out of scope

- Authentication, users, sessions — arrives in 002
- Any domain entity. Per ADR-007 every domain entity carries `UserId` with a global query filter, and the first real tables come with Identity in 002.
- Styling, layout, navigation beyond a single route
- Retry, backoff, or caching on the readiness check
- `Microsoft.Extensions.Diagnostics.HealthChecks` middleware. A plain endpoint is more reviewable at this stage and avoids a package whose response shape we would immediately customise. Revisit at 010 when the Oracle deploy needs a probe format.
- The Vite proxy — already configured in bootstrap (`web/vite.config.ts` proxies `/api` and `/health` to `http://localhost:5080`). This spec proves it works rather than rewriting it.
- `GET /health` — already exists as the liveness probe and is asserted by `e2e/smoke.spec.ts` and polled by `verify-e2e.sh`. Do not modify it.

## Naming

| Endpoint | Role | Touches DB |
|---|---|---|
| `GET /health` | liveness — the process is up | no |
| `GET /api/health` | readiness — the process can serve traffic | yes |

## Data model changes

No entities.

- `AppDbContext` in `api/Infrastructure`, registered with Npgsql, no `DbSet<>` properties
- Initial migration named `InitialCreate`, with empty `Up` and `Down`

The empty migration is deliberate: applying it creates `__EFMigrationsHistory` and proves the migration pipeline works before anything is at stake.

Packages required (`api/Api.csproj`):
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Microsoft.EntityFrameworkCore.Design`

`dotnet-ef` is installed as a **local** tool (`dotnet new tool-manifest` + `dotnet tool install dotnet-ef`) so the version is pinned in the repo and reproducible on the deploy host. Do not rely on a global install.

Connection string comes from configuration (`ConnectionStrings:Default`), sourced from user secrets in development. No connection string in `appsettings.json`.

## API surface

### `GET /api/health`

Executes a real round trip: `SELECT 1` against Postgres via `AppDbContext`. A configuration read or connection-string check does not satisfy this spec.

**200 OK** — database reachable

```json
{ "status": "ok", "database": "ok" }
```

**503 Service Unavailable** — database unreachable

```json
{ "status": "degraded", "database": "unreachable" }
```

Any `DbException` (including `NpgsqlException`) is caught and mapped to the 503 response. The exception must not propagate to the client, and the error is logged at `Warning`.

Returning 503 rather than 200-with-a-flag is what lets the integration test assert on status code, which is a stronger check than parsing the body.

## UI behaviour

One route rendering the result of `GET /api/health`:

- while in flight: `Checking…`
- on 200: the status and database values
- on 503 or network failure: a visible degraded state, not a blank page and not a thrown error boundary

No styling beyond legible text. TanStack Query for the fetch, matching what later features will use.

## Test plan

### Integration — `api.tests/Integration`

`WebApplicationFactory<Program>` + `Testcontainers.PostgreSql` against real Postgres. Per ADR notes, the EF Core in-memory provider is not acceptable here: it does not enforce the behaviour we are proving.

1. **Healthy path** — `GET /api/health` returns 200 and `database == "ok"`.
2. **Unreachable database** — a factory configured with a connection string pointing at a closed port returns 503 and `database == "unreachable"`. Use a bad port rather than stopping the container, so the test is deterministic and fast.
3. **Migration applied** — after the fixture runs migrations, `__EFMigrationsHistory` exists and contains the `InitialCreate` row.

Test 2 is the one that matters. A readiness endpoint that returns `"ok"` unconditionally passes test 1 and proves nothing. Without a failing case, the check has no teeth.

### Unit — `web`

Vitest + Testing Library:

1. Renders status and database values from a mocked 200 response.
2. Renders the degraded state from a mocked 503 response.

### E2E — `web/e2e`

Playwright: load the route, assert the status text is rendered. This is also what proves the Vite proxy works, since the request goes through it.

Do not modify `e2e/smoke.spec.ts`.

## End-to-end verification

Automated:

```
bash scripts/verify.sh        # build, dotnet test, typecheck, lint, vitest
bash scripts/verify-e2e.sh    # playwright
```

Both must exit 0.

Manual, once:

1. `docker compose -f deploy/docker-compose.dev.yml up -d`
2. `cd api && dotnet run`, `cd web && npm run dev`
3. Open the route — status shows `ok`
4. `docker compose -f deploy/docker-compose.dev.yml stop postgres`
5. Reload — the page shows the degraded state, the API returns 503, and no unhandled exception appears in the API logs
6. Start Postgres again, reload, status returns to `ok`

Step 5 is the actual proof that the walking skeleton reaches the database. Everything before it can pass with a hardcoded response.

## Definition of done

- Both verify scripts green
- The six manual steps above behave as described
- `git ls-files` shows no `bin/`, `obj/`, or `TestResults/` entries
- Migration SQL was reviewed before it was applied
