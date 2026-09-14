# 002 — Identity

## Goal

Users sign in with Google and get a same-origin cookie session; the API knows who is calling. This feature also lays down the multi-tenancy mechanism (`ICurrentUser`, `IUserOwned`, the generic query filter) that every domain entity from 003 onward relies on.

## Decisions made in this spec

| Decision | Choice | Why |
|---|---|---|
| Sign-in methods | **Google only** | No password reset, no email verification, no password policy, no email provider in this feature. Local accounts can be added later if a real need appears. |
| User key type | `Guid` | Cleaner foreign keys than Identity's default `string`. |
| Roles | none — `IdentityUserContext<AppUser, Guid>` | No role tables to migrate or review. Four Identity tables instead of seven. |
| Cookie `SameSite` | `Lax` | `Strict` breaks the top-level redirect back from Google. `Lax` still blocks cross-site POSTs. |
| OAuth callback path | `/api/auth/google/callback` | Already covered by the Vite proxy. `/signin-google` (mentioned in ARCHITECTURE.md) would need a proxy rule; this supersedes it. |
| Unauthenticated API response | `401`, never a redirect | Identity's cookie default redirects to `/Account/Login`. That is wrong for a JSON API and confuses the SPA. |
| CSRF | `SameSite=Lax` + Origin check on mutating requests | Small, deterministic, testable. Antiforgery tokens are not needed for a same-origin SPA. |
| `ai_enabled` | column only, default `false` | ADR-010. No endpoint to change it in 002 — set manually in the database for now. |
| E2E authentication | dev-only login endpoint | Playwright cannot complete a real Google flow. The endpoint is provably absent outside Development. |

## Out of scope

- Email/password sign-in, password reset, email verification
- Roles, permissions, admin UI
- Any endpoint that changes `ai_enabled`
- Account deletion, profile editing
- Any user-owned domain entity — the first one arrives in 003 and carries the mandatory two-user isolation test
- Publishing the Google consent screen — stays in *Testing* mode until 010
- Refresh tokens or long-lived Google access — Google is used to establish identity once; the session is ours (see ARCHITECTURE.md, Authentication)

## Data model changes

### `AppUser : IdentityUser<Guid>`

| Column | Type | Notes |
|---|---|---|
| `AiEnabled` | `bool` | default `false` |
| `DisplayName` | `string?` | from Google `name` claim |
| `CreatedAt` | `timestamptz` | UTC, set on creation |

Identity tables via `IdentityUserContext<AppUser, Guid>`: `AspNetUsers`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`. No role tables.

Migration: `AddIdentity`. Review the SQL before applying.

### Multi-tenancy mechanism (no consumer yet)

```csharp
// Domain/
public interface IUserOwned { Guid UserId { get; } }

// Application/
public interface ICurrentUser { Guid? Id { get; } }

// Infrastructure/
// HttpCurrentUser reads the NameIdentifier claim from HttpContext.
```

In `AppDbContext.OnModelCreating`, iterate every entity type implementing `IUserOwned` and apply `HasQueryFilter(e => e.UserId == currentUser.Id)` generically. One loop, not one line per entity — so it is impossible to forget for a new entity.

`ICurrentUser` is a port under ADR-015: one real implementation (HTTP) and a test fake.

## Configuration

User secrets in `api/`:

```
Google:ClientId
Google:ClientSecret
DataProtection:KeysPath        # e.g. ./.keys  — gitignored
```

Cookie:

```
HttpOnly = true
Secure   = Always
SameSite = Lax
Sliding expiration, 14 days
OnRedirectToLogin      → 401
OnRedirectToAccessDenied → 403
```

Data Protection keys persisted to `DataProtection:KeysPath`. Without this, every restart invalidates every session.

## API surface

### `GET /api/auth/google`

Issues the OAuth challenge and redirects to Google. Rate limited: 10 requests per minute per IP, `429` beyond that.

### `GET /api/auth/google/callback`

Handled by the Google authentication handler. On success:

1. Require `email_verified == true`. If not, reject — no user created, redirect to `/login?error=unverified`.
2. Look up by external login (`provider = "Google"`, `providerKey = sub`).
3. If not found, look up by email. If a user exists with that email, **link** the Google login to it (`AddLoginAsync`). This is the duplicate-account case.
4. If still not found, create `AppUser` with `Email`, `UserName = Email`, `DisplayName`, `AiEnabled = false`, `CreatedAt = now`, and add the external login.
5. Sign in with the application cookie.
6. Redirect to `/`.

### `GET /api/auth/me`

- `200` — `{ "id": "...", "email": "...", "displayName": "...", "aiEnabled": false }`
- `401` — not authenticated

### `POST /api/auth/logout`

- `204` — cookie cleared
- `401` — not authenticated
- `403` — Origin check failed

### `POST /api/auth/dev-login` — **Development only**

Body: `{ "email": "...", "displayName": "..." }`. Find-or-create the user, sign in, return `204`.

Mapped only when `env.IsDevelopment()`. In any other environment the route does not exist — `404`, not `403`. This is tested.

### Origin check middleware

For every request with method other than `GET`, `HEAD`, `OPTIONS`: if the `Origin` header is absent or does not match the configured app origin, respond `403` before reaching the endpoint. Applies to all `/api/*` routes.

## UI behaviour

Routes:

- `/login` — a "Sign in with Google" link to `/api/auth/google`. If `?error=unverified`, show a message.
- `/` — protected. Shows `displayName` (or email) and a Logout button.

Protection: a layout route that queries `/api/auth/me`. On `401`, redirect to `/login`. On `200`, render children. While loading, render nothing or a minimal placeholder — do not flash the login page.

Logout: `POST /api/auth/logout` with `credentials: "same-origin"`, then invalidate the `me` query and navigate to `/login`.

TanStack Query for `me`; TanStack Router for the guard. No styling beyond legible text.

## Test plan

### Integration — `api.tests/Integration`

`WebApplicationFactory<Program>` + Testcontainers Postgres. For the Google flow, the test factory replaces the `Google` authentication scheme with a test handler that produces a ticket from claims the test supplies — so the callback logic runs for real without contacting Google.

1. `GET /api/auth/me` unauthenticated → `401` and no `Location` header.
2. Callback with a new `sub` and verified email → user row created, `AspNetUserLogins` row created, cookie set, `me` → `200` with `aiEnabled == false`.
3. Callback again with the same `sub` → still exactly one user.
4. Callback with a different `sub` but an existing email → login linked to the existing user; still exactly one user, two login rows.
5. Callback with `email_verified == false` → no user created, redirect to `/login?error=unverified`.
6. `POST /api/auth/logout` authenticated with correct Origin → `204`; subsequent `me` → `401`.
7. `POST /api/auth/logout` with a wrong Origin → `403`, session still valid.
8. Two factories sharing the same `DataProtection:KeysPath` → a cookie issued by A is accepted by B. (Proves key persistence.)
9. Eleven `GET /api/auth/google` from one IP within a minute → the eleventh is `429`.
10. Factory with environment `Production` → `POST /api/auth/dev-login` is `404`.
11. Migration applied → `AspNetUsers` has `AiEnabled` with default `false`; no `AspNetRoles` table exists.
12. **Query filter mechanism** — a test-only `TestDbContext : AppDbContext` adds one `IUserOwned` entity. With `ICurrentUser.Id = A`, rows inserted; with `ICurrentUser.Id = B`, the same query returns zero rows. This proves the generic filter before any real entity depends on it.

Test 12 is the one the whole architecture rests on. It does not go in the main project — it lives in `api.tests` only.

### Unit — `web`

Vitest + Testing Library:

1. Protected layout renders children when `me` resolves `200`.
2. Protected layout redirects to `/login` when `me` resolves `401`.
3. Login page shows the unverified message when `?error=unverified` is present.

### E2E — `web/e2e`

Playwright, using `POST /api/auth/dev-login` to establish a session:

1. Visiting `/` unauthenticated lands on `/login`.
2. After dev-login, visiting `/` shows the display name.
3. Logout returns to `/login`, and `/` redirects again.

Do not modify `e2e/smoke.spec.ts`.

## End-to-end verification

Automated:

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual, once, with real Google:

1. `docker compose -f deploy/docker-compose.dev.yml up -d`, `dotnet run`, `npm run dev`
2. Open `http://localhost:5173/` → redirected to `/login`
3. Click Sign in with Google → complete the Google flow with your own account → back on `/`, name shown
4. In `psql`: `SELECT "Email", "AiEnabled" FROM "AspNetUsers";` → one row, `false`
5. Stop the API, start it again, reload `/` → still signed in (Data Protection keys persisted)
6. Logout → `/login`. Reload `/` → still `/login`.

Step 5 is the one that catches the most common production embarrassment.

## Definition of done

- Both verify scripts green
- Manual steps 1–6 behave as described
- Migration SQL reviewed before applying
- `DataProtection:KeysPath` directory is gitignored
- `git ls-files` shows no secrets, no `.keys/`, no `bin/`, `obj/`, `TestResults/`
