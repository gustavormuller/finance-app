# Personal Finance — Architecture

> Personal project. Dual goal: a tool I actually use + going deeper into .NET, Docker and financial modelling.
> My own use first, then family and friends (5–10 people). SaaS is a possibility, not a plan.
> Budget: **R$50/month** for personal use, ceiling of R$100 with the small group.
> Revision: 2026-08 (rev. 3). Replaces the multi-tenant SaaS plan that cost R$190–270/month.

---

## Principles

1. **If it does not fit the budget, it does not go in.** Cost is a requirement, not a consequence.
2. **Few users do not justify a distributed system.** No separate worker, external queue, load balancer or managed database.
3. **Movements, not balances.** Financial state is derived from dated events. A balance is a query, not a column.
4. **Money is `decimal`, never `float`.** No exceptions, in any layer.
5. **Data isolation from the very first migration.** Cheap now, expensive later, and getting it wrong leaks someone else's bank statement.
6. **External data comes in through a daily job, never in a user request.** The dashboard only reads from the local Postgres.
7. **Learning is a declared goal.** On a cost tie, I pick whatever teaches more.

---

## Stack

| Layer | Choice | Reason |
|---|---|---|
| Host | Oracle Cloud Always Free (2 OCPU ARM, 12 GB) | R$0; Hetzner CX22 (~R$25) as plan B |
| Orchestration | Docker Compose | one machine, no Kubernetes |
| Reverse proxy | Caddy | automatic TLS, minimal config |
| Exposure | Cloudflare Tunnel | no open port, no public IP |
| Backend | .NET 10 Minimal API | native `decimal`, low footprint, aligned with my day job |
| ORM | EF Core | good migrations, typed LINQ, global query filters |
| Auth | ASP.NET Core Identity + Google OAuth | native to the framework, R$0, scales to SaaS |
| Database | PostgreSQL 16 (container) | R$0, relational, good with time series |
| Queue / jobs | pg-boss (or Hangfire) | runs on the existing Postgres, zero extra services |
| Frontend | Vite + React + TanStack Router | SPA, no SSR needed |
| UI | shadcn/ui + Tailwind | ready-made components, no Figma |
| Charts | Recharts | enough for dashboard and series |
| Shared types | OpenAPI → `openapi-typescript` | TS client generated at build time |
| E-mail | Resend (free tier) or SES | alerts and notifications |
| AI | Claude or OpenAI API, with a spending ceiling | scheduled monthly analysis |
| Backup | `pg_dump` + `age` + rclone → B2/R2 | encrypted, off the machine |

### Removed from the previous plan

| Removed | Reason |
|---|---|
| ECS Fargate | ~R$55/month in idle compute alone |
| Aurora Serverless v2 / RDS | minimum of R$82–235/month |
| ALB | R$88/month fixed, with no traffic to justify it |
| BullMQ + Upstash Redis | pg-boss solves it on the existing database |
| Worker in a separate service | there is no independent scaling to optimise |
| better-auth | TypeScript library, incompatible with a .NET backend |
| Astro landing page | there is no audience |
| Turborepo | overhead for 2 folders |
| WhatsApp / Meta Cloud API | requires business verification, cost and friction |
| OCR / Textract | postponed; pay-per-use, cents when it comes in |
| React Native | a PWA solves it; a native app is a separate project |

---

## Authentication

### Decision

**ASP.NET Core Identity + the Google provider**, cookie session, open registration.

```
Microsoft.AspNetCore.Identity.EntityFrameworkCore
Microsoft.AspNetCore.Authentication.Google
```

`AddIdentityApiEndpoints<AppUser>()` is **not** used: it ships `/register`, `/login` and `/refresh`, and there are no local accounts to register or log in. Sign-in is Google only (002), so the endpoints are hand-written and there is no password, no password reset and no e-mail confirmation to support.

### Cookie, not JWT

Caddy serves the frontend and proxies `/api` on the same domain → same-origin.
In that scenario an `HttpOnly` cookie is strictly superior to a JWT in localStorage: a token in localStorage is readable by JavaScript, so any XSS becomes session theft.

```
HttpOnly · Secure · SameSite=Lax
+ CSRF protection on write endpoints
```

Revisit only if one day there is a native app (not a PWA) consuming the API from another origin.

### Open registration, AI behind a flag

Anyone creates an account with Google. No allowlist, no invitation.

The gate sits on the only resource that costs money:

```
AspNetUsers.ai_enabled BOOLEAN DEFAULT false
```

Dashboard, transactions, import and investments are open to everyone — marginal cost ~R$0, because market data is shared between users and rows in Postgres are cheap. AI ships disabled and is enabled per person.

Blocking sign-up would put the gate in the wrong place: the R$15/month ceiling is a limit, not a charge. An account created and never used costs zero.

Guards required with open registration:

- per-IP rate limit on the sign-up endpoint (prevents a bot loop creating rows)
- size limit on the import upload
- Google consent screen **published** (*Testing* mode caps you at 100 manually registered test users)

Becoming a SaaS = `ai_enabled` stops being a manual flag and starts deriving from the active plan.

### LGPD

With strangers storing bank statements on the server, the framing changes: I become the controller of data belonging to people I have no prior relationship with. That starts to require:

- a published privacy policy
- a documented legal basis (consent, in this case)
- account and data deletion on request, actually working
- encrypted backup (already planned) and a record of who accesses what

It blocks nothing, but it is real work that has to enter the roadmap before the link is shared outside the circle of people I know.

### Known pitfalls

**Duplicate accounts by e-mail.** Someone creates an account with a password and later signs in with Google using the same e-mail → two accounts. Handle it explicitly with `AddLoginAsync` to link the external login to the existing account. It is not automatic and it *will* happen.

**Google redirect URI.** It has to match exactly what is registered in the Google Cloud Console, including scheme and trailing slash: `https://domain.com/api/auth/google/callback` (002 chose that path over the framework default `/signin-google`, because `/api/*` is already proxied). The cause of most integration errors. ASP.NET builds it from the incoming `Host` header, so anything rewriting Host — a proxy with `changeOrigin` — produces a URI that will not match.

**Do not depend on Google's refresh token.** A consent screen in "Testing" mode has a refresh token that expires in 7 days. Use Google only to establish identity on first login and issue my own cookie from then on — session lifetime becomes mine.

---

## Multi-tenancy

**Revised decision.** The previous version of this document said "no `user_id`". That was wrong the moment family and friends entered the scope.

The expensive part is not the migration — it is auditing every query to add the tenant filter, and the risk of forgetting one and leaking someone else's statement. That risk grows with every line written without the filter.

EF Core makes this nearly free:

```csharp
// AppDbContext.OnModelCreating — one line per entity
modelBuilder.Entity<Transaction>()
    .HasQueryFilter(t => t.UserId == _currentUser.Id);
modelBuilder.Entity<Account>()
    .HasQueryFilter(a => a.UserId == _currentUser.Id);
// ... same for categories, assets, movements
```

Every query becomes filtered automatically, joins included. You cannot forget it because you do not have to remember it.

**Do now (~2h):** `user_id` on every domain table, global query filter in the DbContext, composite index `(user_id, date)` where it makes sense.
**Do not do now:** plans, billing, per-tier limits, subscription portal.

`prices` and `benchmarks` are shared market data — **no** `user_id` and **no** query filter.

---

## Repository structure

```
finance-app/
├── api/                       # .NET 10 Minimal API
│   ├── Domain/
│   │   ├── Identity/          # AppUser, ai_enabled
│   │   ├── Transactions/
│   │   ├── Investments/
│   │   └── Analysis/
│   ├── Application/           # use cases, orchestration
│   ├── Infrastructure/        # EF Core, HTTP clients, jobs
│   ├── Endpoints/
│   ├── Migrations/
│   └── Program.cs
├── api.tests/                 # xUnit + Testcontainers.PostgreSql
├── web/                       # Vite + React
│   ├── src/
│   │   ├── routes/
│   │   ├── components/
│   │   └── api/               # client generated from OpenAPI
│   └── e2e/                   # Playwright
├── deploy/
│   ├── docker-compose.yml
│   ├── Caddyfile
│   └── backup.sh
├── docs/
│   └── ARCHITECTURE.md
├── specs/                     # one spec file per feature
├── scripts/                   # verify.sh, verify-e2e.sh
└── .claude/                   # workflow skills
```

---

## Infrastructure

```
Browser
   └── Cloudflare Tunnel (no exposed port, TLS at the edge)
         └── VPS
               ├── Caddy          → frontend static files + /api proxy
               ├── api (.NET)     → API + jobs in the same process
               └── postgres       → data + pg-boss queues
```

### docker-compose (skeleton)

```yaml
services:
  postgres:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: financas
      POSTGRES_USER: ${DB_USER}
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${DB_USER}"]
      interval: 10s

  api:
    build: ../api
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy
    env_file: .env

  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - ../web/dist:/srv
      - caddydata:/data

  cloudflared:
    image: cloudflare/cloudflared:latest
    restart: unless-stopped
    command: tunnel --no-autoupdate run
    environment:
      TUNNEL_TOKEN: ${CLOUDFLARE_TUNNEL_TOKEN}

volumes:
  pgdata:
  caddydata:
```

### Caddyfile

```
:80 {
    handle /api/* {
        reverse_proxy api:8080
    }
    handle {
        root * /srv
        try_files {path} /index.html
        file_server
    }
}
```

TLS at the Cloudflare edge, Caddy serves internal HTTP. Once off the Tunnel, swap `:80` for the domain and Caddy issues the certificate itself.

### Host: Oracle Cloud Always Free

2 OCPU ARM (Ampere A1) + 12 GB since 2026-06-15. Plenty of headroom for this workload, cost R$0.

**Operational caveats:**

*ARM64.* The .NET, Postgres, Caddy and cloudflared images have arm64 variants — nothing breaks. But a build made on an x86 machine needs `docker buildx build --platform linux/arm64`, otherwise the image comes up and does not run. Simpler: build directly on the server.

*"Out of host capacity".* A classic problem when provisioning A1 on the free tier; some regions are permanently full. If São Paulo does not free up, try another AD or region — 100ms more latency is irrelevant here.

*Idle instance reclamation.* Oracle reclaims idle Always Free resources. A real app with a daily job takes care of that by itself.

*Unilateral rule changes.* Oracle has already halved Always Free without notice, shutting down instances outside the new limit. It can happen again.

**Real exposure with a working external backup:** 1–2 hours to migrate (provision a VPS, install Docker, restore the dump, bring the compose up, repoint DNS). The architecture is portable by construction — that is what makes the risk acceptable.

**The only catastrophic scenario** is a deleted instance with the only backup inside it. Avoided by the backup policy below, which is not optional in this arrangement.

*Plan B:* Hetzner CX22, ~R$25/month, x86, no surprises.

---

## System design

The structuring decision: **there is a line between what is fact and what is conclusion.**

Above the line sit dated events, immutable, never overwritten. Below it sits everything computed from them. If anything below is wrong, delete it and recompute without losing a thing.

```
  INPUT        OFX/CSV import · manual entry            daily market job
                          ↓                                    ↓
  STAGING      staged_transactions
               dedupe · preview · commit
                          ↓                                    ↓
─── SOURCE OF TRUTH ──────────────────────────────────────────────────────
               transactions        movements          prices · benchmarks
──────────────────────────────────────────────────────────────────────────
                          ↓                                    ↓
  DERIVATION   cascading categorization         portfolio_daily (snapshots)
                          ↓                                    ↓
  READ         dashboard              TWR / XIRR            AI analysis
```

### 1. Daily snapshot instead of on-demand computation

Computing portfolio value day by day from `movements` × `prices` on every dashboard load gets slow fast. The nightly job writes:

```
portfolio_daily   user_id, date, asset_id, quantity, price, value_brl
```

The screen reads from there. Corrected a movement from August? Recompute from August onward, not the whole history.

Without this, TWR is unfeasible — it needs the portfolio value on every contribution date.

### 2. Staging before commit on import

```
upload → parse (OFX/CSV) → normalize → deduplicate
       → staged_transactions (with import_batch_id)
       → preview on screen → confirm → transactions
```

Preview and undo come free out of this structure. They are not separate features to build later.

### 3. Cascading categorization, AI last

```
1. user rule          (description contains "IFOOD" → Food)       free, deterministic
2. history            (has this normalized description been seen?) free
3. AI                 only what the previous two left over          costs
```

What makes AI cheap is not batching — it is the cascade. In practice ~90% of transactions stop at step 2, because purchases repeat at the same places.

**Normalizing the description before comparing is what makes step 2 work.** `PAG*IFOOD 12/03` and `PAG*IFOOD 15/04` have to become the same key: strip digits, dates and transaction identifiers.

### 4. `Money` as a value object

A record with amount and currency, not a bare `decimal`. Stops you adding BRL to USD by accident — a bug that shows up the moment US stocks and crypto come in.

```csharp
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money operator +(Money a, Money b) =>
        a.Currency == b.Currency
            ? new(a.Amount + b.Amount, a.Currency)
            : throw new InvalidOperationException("currency mismatch");
}
```

### 5. Jobs

| When | Job | Produces |
|---|---|---|
| nightly | `sync-market-data` | `prices`, `benchmarks` |
| nightly (after sync) | `rebuild-snapshots` | `portfolio_daily` |
| monthly | `monthly-analysis` | AI analysis, only for `ai_enabled` |
| on demand | `process-import` | `staged_transactions` |
| on demand | `categorize-batch` | categories |
| on demand | `rebuild-snapshots-from(date)` | `portfolio_daily` from the date onward |

The last one closes the loop: editing an old event triggers recomputation of everything that came after. It is the only way to keep derivation consistent with the source of truth without recomputing everything every time.

### 6. Layers

```
Endpoints/         thin HTTP — input validation, no business rules
Application/       use cases, orchestration, database transactions
Domain/            entities, Money, TWR and XIRR computation — no infra dependency
Infrastructure/    EF Core, HTTP clients, jobs, AI providers
```

Rule: `Domain/` does not reference EF Core or `HttpClient`. It is where the financial maths lives, and it is what needs to be testable without bringing up a database.

Dashboard aggregation reads (`GROUP BY` over date ranges) may come from direct SQL via Dapper instead of LINQ — more readable and faster. This is not CQRS with separate stores, it is just not forcing every read through the ORM.

### 7. Architectural style

**Lean Clean Architecture with ports where they pay for themselves. This is not DDD.**

The dependency arrows point inward: input adapters (Endpoints, Jobs) and output adapters (EF Core, HTTP clients) depend on the core; the core depends on none of them. Data flows in the opposite direction.

**Ports declared in the core, implemented in infrastructure:**

```csharp
// Application/ or Domain/
public interface IMarketDataProvider { Task<IReadOnlyList<Price>> GetDailyAsync(...); }
public interface IAiProvider         { Task<string> AnalyzeAsync(...); }
```

Concrete justification, not dogma: there are already four market sources (brapi, BCB, CoinGecko/Binance, Twelve Data) and the AI provider may change. This is exactly the case ports-and-adapters was invented for.

**Deliberately not adopted:**

| Pattern | Reason |
|---|---|
| Repository over EF Core | `DbContext` is already a Unit of Work and `DbSet<T>` is already a repository. Wrapping it loses `IQueryable` composition and gains nothing. |
| Aggregates with strict boundaries | A transaction has a date, an amount and a category — there is no interesting invariant to protect. An anaemic entity is the right answer here. |
| Domain events / MediatR | Indirection between layers that could call each other directly, in a single process. |
| CQRS with separate stores | Dapper on the aggregations solves it. It is not the same pattern. |
| Bounded contexts | There is only one context. |

**Where modelling rigour is worth it:** the returns module. TWR with sub-periods, XIRR by Newton-Raphson, asset × FX decomposition, snapshot reconstruction — there are real invariants there and maths that fails silently. Value objects, pure functions and exhaustive tests pay for themselves in that module and nowhere else.

Applying the same rigour to transaction CRUD would be cargo cult. Complexity is not evenly distributed.

---

## Data model

### Identity

```
AspNetUsers            (Identity default)
AspNetUserLogins       (link to Google)
```

### Transactions

```
accounts        id, user_id, name, type, currency
categories      id, user_id, name, parent_id, kind (income|expense)
transactions    id, user_id, account_id, category_id, date,
                amount NUMERIC(18,2), description, import_batch_id, created_at
import_batches  id, user_id, source, filename, imported_at, row_count
```

`amount` is always `NUMERIC`, never `float8`. The sign defines inflow/outflow.
`import_batch_id` makes it possible to undo an entire import.
Index: `(user_id, date DESC)`.

### Investments

```
assets          id, user_id, ticker, name,
                class (stock_br|stock_us|fii|etf|bdr|fixed_income|crypto),
                currency, benchmark_hint

movements       id, user_id, asset_id, date,
                kind (buy|sell|dividend|jcp|split|amortization),
                quantity NUMERIC(18,8), unit_price NUMERIC(18,8),
                fees NUMERIC(18,2), notes
```

### Market data (shared, no user_id)

```
prices          asset_key, date, close NUMERIC(18,8), currency   -- PK (asset_key, date)
benchmarks      code, date, value NUMERIC(18,8)                  -- CDI, IPCA, USD, IVVB11
```

`asset_key` is the market identifier (ticker + source), not `assets.id` — that way two people holding PETR4 share the same price series.

**Never store a consolidated position as a column.** The position on any date is derived from the sum of `movements` up to that date. That is what makes it possible to recompute everything when an old entry is corrected — and it will be.

---

## Returns calculation

The technical core of the project. Getting it wrong is the market default.

### The common mistake

`(final_value - initial_value) / initial_value` only holds with no contributions or withdrawals. With monthly contributions the number becomes noise: contributing before a rise inflates it, before a fall sinks it, regardless of the actual performance of the assets.

### The two correct metrics

**TWR — Time-Weighted Return**
Breaks the period into sub-periods at every cash movement, computes the return of each one and compounds them:

```
TWR = Π (1 + r_i) - 1,  where  r_i = (V_end - flow_i) / V_start - 1
```

Removes the effect of *when* the contribution happened. **This is the one you compare against CDI, IPCA, the S&P 500 and the dollar** — the only fair benchmark comparison.

**MWR / XIRR — Money-Weighted Return**
The rate that zeroes the present value of the cash flow:

```
Σ CF_t / (1 + r)^(d_t/365) = 0
```

Newton-Raphson with a bisection fallback (Newton diverges with irregular flows). It is the real return of the money, including good or bad timing.

**Show both side by side.** The difference between them is exactly how much the contribution decisions helped or hurt. It is the most useful insight in the module.

### Assets in foreign currency

US stocks and crypto have two return components: the asset and the exchange rate.

```
value_brl = quantity × price_usd × ptax_of_the_day
```

Store `prices` in the native currency and convert on read, never on write — that way the return can be shown decomposed:

- asset return (in USD)
- FX return (PTAX variation)
- total return (in BRL)

It does not apply to BDRs: they already come in BRL with the exchange rate embedded.

### Benchmark comparison

Normalize everything to base 100 on the date of the first contribution:

- Portfolio (TWR)
- Accumulated CDI
- IPCA + 6%
- IVVB11 (S&P 500 proxy in BRL, with FX effect)
- PTAX dollar

---

## External data sources

| Source | Covers | Free tier |
|---|---|---|
| BCB / SGS | CDI, SELIC, IPCA, PTAX | unlimited, no key |
| brapi.dev | BR stocks, FIIs, ETFs, BDRs | 15,000 req/month |
| CoinGecko (Demo) | crypto | 10,000 req/month, 100/min |
| Binance public | BTCBRL and pairs | no key, no sign-up |
| Twelve Data | US stocks | 800 req/day |

### BCB / SGS — free, official, no key

```
https://api.bcb.gov.br/dados/serie/bcdata.sgs.{codigo}/dados?formato=json
  &dataInicial=01/01/2020&dataFinal=31/12/2026
```

Series: daily CDI, SELIC, monthly IPCA, PTAX dollar.
**Confirm the codes on the SGS portal before hardcoding them.**

### brapi.dev

B3 stocks, FIIs, ETFs, BDRs and indices, with history, dividends and FX.
PETR4, VALE3, MGLU3 and ITUB4 work without a token; the rest require a free-plan token.

```
GET https://brapi.dev/api/quote/{tickers}?range=1mo&interval=1d&token={TOKEN}
```

### Crypto

**CoinGecko Demo** — free key from the dashboard, broad coverage:
```
GET https://api.coingecko.com/api/v3/coins/bitcoin/market_chart
    ?vs_currency=brl&days=365&x_cg_demo_api_key={KEY}
```

**Binance** — no key, no sign-up, BTCBRL pair directly:
```
GET https://api.binance.com/api/v3/klines?symbol=BTCBRL&interval=1d&limit=365
```

Binance for the simple daily history; CoinGecko if more assets or metadata are needed.

### US stocks — Twelve Data

800 requests/day against the ~10 needed. Returns in USD; convert using PTAX.

```
GET https://api.twelvedata.com/time_series
    ?symbol=AAPL&interval=1day&apikey={KEY}
```

Finnhub (60 req/min) is an equivalent alternative.

**Do not use Yahoo Finance.** No official API — the libraries scrape undocumented endpoints, may violate the terms of use and break without warning.

### Consumption strategy

A daily overnight job fetches quotes and indicators and writes to `prices` / `benchmarks`. The dashboard **only reads from the local Postgres**.

Estimated consumption: ~40 requests/day across all sources. Enormous headroom in every free tier, even with 10 users — market data is shared, so more users do not increase the calls.

Initial backfill: a one-off historical load of 5 years per asset, respecting rate limits.

---

## AI module

### Splitting the tracks

Two needs with opposite cost profiles:

**In the app, via API — scheduled monthly analysis.**
A 30–50k token context, once a month per user, ~R$1 per run. Renders as a card on the dashboard. Predictable cost, limited by construction.

**In Claude, via an MCP server — exploratory conversation.**
"Why did I spend more in August?", "compare my half-year against the CDI". Here the per-API cost would explode; the subscription already paid for covers it all.

An MCP server exposes data *to* MCP clients (the Claude app, Claude Code). **There is no supported path for my own app to use the subscription programmatically** — inside the site, it is API key billed per token only. They are separate tracks, with no bridge.

The expensive thing was never the scheduled analysis, it was the open-ended conversation.

### Categorization

A mechanical task, cheap model, in batches. ~200 transactions/month cost cents.
Cache by normalized description: same store → same category without a new call.

### Cost control

```
ai_usage    id, user_id, month, input_tokens, output_tokens, cost_brl
```

Middleware checks `ai_enabled` and the month's accumulated total **per user** before every call, and cuts off on reaching `AI_MONTHLY_BUDGET_BRL`. No exceptions.

With open registration, `ai_enabled` defaults to `false` — whoever creates an account and is not enabled generates no cost at all.

This is the only cost that scales linearly with users. With the per-user ceiling and analysis limited to 1×/month, 10 people cost ~R$10/month, not R$100.

---

## Roadmap

### Phase 1 — Foundation
- [ ] VPS, Docker Compose, Caddy, Cloudflare Tunnel
- [ ] .NET Minimal API + EF Core + Postgres, migrations running
- [ ] Identity + Google OAuth, cookie session
- [ ] `user_id` + global query filters on every domain entity
- [ ] Open registration + per-IP rate limit on sign-up
- [ ] `ai_enabled` flag per user
- [ ] CRUD for accounts, categories and transactions
- [ ] Manual deploy working end to end

### Phase 2 — Import
- [ ] OFX parser (the most reliable format from BR banks)
- [ ] CSV parser with column mapping
- [ ] Templates: Nubank, Inter, Itaú
- [ ] Duplicate detection (hash of date + amount + normalized description)
- [ ] Preview before confirming + undo batch

### Phase 3 — Dashboard
- [ ] Consolidated balance and per-account balance
- [ ] Inflows × outflows per month
- [ ] Spending by category with drill-down
- [ ] Month-over-month comparison and moving average
- [ ] Detected recurrences (subscriptions, fixed bills)

### Phase 4 — Investments
- [ ] Registering assets and movements
- [ ] Daily job: brapi, BCB, CoinGecko/Binance, Twelve Data
- [ ] Historical backfill
- [ ] TWR computation
- [ ] XIRR computation
- [ ] Asset × FX decomposition for USD assets
- [ ] Base-100 chart: portfolio × CDI × IPCA+6% × IVVB11 × dollar
- [ ] Portfolio composition by class

### Phase 5 — AI
- [ ] Automatic batch categorization
- [ ] Token counter and per-user spending ceiling
- [ ] Scheduled monthly analysis, rendered on the dashboard
- [ ] MCP server for exploratory conversation in the Claude app

### Phase 6 — Refinement
- [ ] Goals and budget per category
- [ ] E-mail alerts
- [ ] CSV/Excel export
- [ ] Installable PWA
- [ ] (Optional) Receipt OCR via Textract

---

## Operations

### Backup

Daily, encrypted, **off the machine**. A backup that lives on the server it protects is not a backup.

```bash
# deploy/backup.sh — cron 3am
pg_dump -Fc financas | age -r $AGE_PUBLIC_KEY > /tmp/fin_$(date +%F).dump.age
rclone copy /tmp/fin_*.dump.age remote:financas-backup/
find /tmp -name "fin_*.dump.age" -mtime +2 -delete
```

**Encrypt before uploading** — this is other people's financial data too. `age` or `gpg`.

**Tiered retention:** 7 daily, 4 weekly, 6 monthly. Personal finance dumps are a few MB; even 17 copies come to hundreds of MB. B2 and R2 give 10 GB free → cost R$0.

Retention matters more than it seems: a bad migration noticed five days later has already contaminated yesterday's backup. The one from two weeks ago saves you.

**Test the restore once.** Bring the dump up on a local Postgres and check it. A backup never restored is faith.

### Deploy

Phase 1: `git pull && docker compose up -d --build` over SSH. Good enough.
Later, if it starts to hurt: GitHub Actions with SSH. Do not build an elaborate pipeline before feeling the pain.

### Monitoring

- `/health` healthcheck
- UptimeRobot free or Uptime Kuma in a container
- Logs: `docker compose logs`. No observability stack.

---

## Environment variables

```env
# Database
DB_USER=
DB_PASSWORD=
ConnectionStrings__Default=Host=postgres;Database=financas;Username=...;Password=...

# Auth
Google__ClientId=
Google__ClientSecret=
DataProtection__KeyPath=/keys        # persistent volume, otherwise cookies invalidate on every deploy

# E-mail
RESEND_API_KEY=
MAIL_FROM=

# Cloudflare
CLOUDFLARE_TUNNEL_TOKEN=

# Market data
BRAPI_TOKEN=
COINGECKO_DEMO_KEY=
TWELVEDATA_KEY=

# AI
AI_PROVIDER=anthropic
AI_API_KEY=
AI_MONTHLY_BUDGET_BRL=15             # per user, hard ceiling

# Backup
AGE_PUBLIC_KEY=
RCLONE_CONFIG_REMOTE_TYPE=b2
RCLONE_CONFIG_REMOTE_ACCOUNT=
RCLONE_CONFIG_REMOTE_KEY=
```

In production these live in the VPS `.env`, outside git, `chmod 600`.

> **Watch out for `DataProtection__KeyPath`:** without a persistent volume, ASP.NET Core regenerates the Data Protection keys on every deploy and all session cookies are invalidated. Everyone is logged out on every `docker compose up`.

---

## Costs

### Personal use

| Item | Monthly |
|---|---|
| Oracle Cloud Always Free | R$0 |
| .com.br domain (R$40/year) | ~R$3 |
| Cloudflare Tunnel, Caddy, Postgres, pg-boss | R$0 |
| Identity + Google OAuth | R$0 |
| Resend / SES | R$0 |
| BCB, brapi, CoinGecko, Binance, Twelve Data | R$0 |
| Backup on B2/R2 | <R$1 |
| AI (with ceiling) | R$10–15 |
| **Total** | **~R$14–19** |

### With 10 people

Infrastructure barely changes — 2 OCPU and 12 GB handle 10 users without breaking a sweat. Market data is shared, so calls to external APIs do not increase.

What scales is the AI. With a per-user ceiling and analysis 1×/month: ~R$10/month total.

| Scenario | Monthly |
|---|---|
| 10 users with AI enabled | ~R$25–35 |
| Sign-ups without `ai_enabled` | R$0 marginal |
| 10 users, AI with no control | ~R$100+ |

The per-user ceiling is what keeps this inside the budget. It is not optional.

**If money gets tight:** the first cut is AI via API — move the analysis to the MCP server. That takes out R$10–15.

---

## ADRs

### ADR-001 — Single machine instead of ECS/Aurora, on Oracle Always Free
Few users do not generate load that justifies managed compute. ECS + RDS would cost R$190–270/month against a R$50 budget.
Oracle Always Free zeroes the host cost. The risk of a unilateral rule change is acceptable because the architecture is portable: migrating is 1–2h with a working external backup.
*Trade-off:* no high availability, and ARM64 needs care at build time. Acceptable.
*Plan B:* Hetzner CX22 (~R$25/month).

### ADR-002 — .NET instead of NestJS
Reverted from the previous plan, which prioritised delivery speed for a SaaS. With learning as a goal, .NET composes with existing professional experience. Technically: native `decimal` (JS only has float64) and EF Core's `HasQueryFilter` applying tenant isolation automatically on every query, joins included — a guarantee that matters with open registration.
*Correction:* memory consumption was used as an argument in an earlier revision; with 12 GB on Oracle, it stopped being a factor.
*Trade-off:* loses shared types. Mitigated with a TS client generated from OpenAPI.

### ADR-003 — pg-boss instead of BullMQ + Redis
BullMQ requires Redis, one more service to run and pay for. pg-boss uses the existing Postgres.
*Trade-off:* no Bull Board. A jobs table with status solves it.

### ADR-004 — Jobs in the same process as the API
A separate worker existed to scale independently. With few users there is nothing to scale.
*Re-evaluate if:* any job competes for CPU with requests.

### ADR-005 — Cloudflare Tunnel instead of a public IP
Financial data does not need to be exposed. The Tunnel eliminates open ports and gives TLS for free.

### ADR-006 — Movements instead of balances
A consolidated position as a column prevents retroactive recomputation. TWR and XIRR depend on the complete flow with dates — an event model is a requirement.

### ADR-007 — Multi-tenancy from the first migration *(revised)*
**The previous version said "no `user_id`". Wrong the moment family and friends entered the scope.**
The migration is trivial; the expensive part is auditing every query and the risk is leaking someone else's statement. EF Core's global query filter solves it with one line per entity and leaves no room for forgetting.
Cost now: ~2h. Cost later: days, with a security risk.
*Not included:* plans, billing, per-tier limits.

### ADR-008 — Per-user AI spending ceiling
The only cost that scales linearly with users and has no natural limit. A counter in the database by `user_id`, hard cut-off before every call.

### ADR-009 — Identity + Google, cookie session
better-auth is TypeScript, incompatible with a .NET backend. Identity is native, free and scales to SaaS without a rewrite.
`HttpOnly` cookie instead of JWT: frontend and API are same-origin behind Caddy, and a token in localStorage is readable by JS (XSS becomes session theft).
*Revisit if:* a native app appears consuming the API from another origin.

### ADR-010 — Open registration, with AI behind a per-user flag
Invitations and allowlists were considered and discarded: both put the gate at sign-up, which is not where the cost is. The AI ceiling is a limit, not a charge — an account created and idle costs zero.
The correct gate is `ai_enabled` per user: one column, one check, and the only resource with relevant marginal cost stays controlled without stopping anyone from using the app.
*Requires:* per-IP rate limit on sign-up, upload size limit, published Google consent screen.
*Becoming a SaaS =* `ai_enabled` derives from the active plan instead of being manual.

### ADR-011 — Daily derived snapshots, recomputable
Portfolio value per day is expensive to compute on demand and mandatory for TWR. `portfolio_daily` is a derivation materialised by the nightly job, never the source of truth.
Editing an old movement triggers `rebuild-snapshots-from(date)`.
*Consequence:* the table can be truncated and rebuilt at any time without loss.

### ADR-012 — Cascading categorization with AI on the last rung
User rule → normalized description history → AI. ~90% stops before the AI.
It is the cascade, not the batching, that keeps the AI cost negligible.
*Requires:* description normalization (stripping digits, dates, IDs) for rung 2 to work.

### ADR-013 — LGPD enters the roadmap before public release
With users outside the known circle, the project becomes the controller of third-party financial data. A privacy policy, a legal basis and working account deletion stop being optional.
*It does not block development,* but it has to be ready before the link circulates outside family and friends.

### ADR-014 — Lean Clean Architecture, not DDD
Dependency rule pointing inward: adapters depend on the core, the core does not depend on adapters. `Domain/` with no reference to EF Core or `HttpClient`.
Reason: the financial computations become testable in milliseconds, without bringing up a database. Cost is near zero.
*Adopted:* the dependency rule, value objects (`Money`, `Ticker`, `DateRange`), pure domain services.
*Not adopted:* aggregates with strict boundaries, domain events, MediatR, CQRS with separate stores, bounded contexts — the domain is mostly data entry and transformation, with no invariants that justify the indirection.

### ADR-015 — Ports only for genuinely pluggable dependencies
`IMarketDataProvider` and `IAiProvider` declared in the core, implemented in infrastructure.
Concrete justification: four market sources already mapped (brapi, BCB, CoinGecko/Binance, Twelve Data) and an AI provider subject to change.
*Criterion for new ports:* is there more than one real or foreseen implementation? If not, call directly.

### ADR-016 — No Repository over EF Core
`DbContext` is already a Unit of Work and `DbSet<T>` is already a repository. A repository layer would forward calls, lose `IQueryable` composition and add no testability that integration tests with Postgres in a container do not already deliver.
*Consequence:* `Application/` uses `AppDbContext` directly.

### ADR-017 — Modelling rigour concentrated in the returns module
TWR with sub-periods, XIRR by Newton-Raphson, asset × FX decomposition and snapshot reconstruction have real invariants and fail silently. That module gets value objects, pure functions and exhaustive tests.
Transaction CRUD stays CRUD.
*Reason:* the system's complexity is not evenly distributed; applying the same rigour everywhere is cargo cult and hides where the risk actually is.

---

## Next steps

```
[ ]  1. VPS: SSH with a key, ufw, fail2ban
[ ]  2. Docker + Docker Compose
[ ]  3. Postgres in a container, external connection blocked
[ ]  4. .NET Minimal API project, /health responding
[ ]  5. EF Core + Identity, first migration
[ ]  6. Google OAuth (Cloud Console, redirect URI, cookie)
[ ]  7. user_id + global query filters
[ ]  8. Rate limit on sign-up + ai_enabled flag
[ ]  9. Caddy serving static files + /api proxy
[ ] 10. Cloudflare Tunnel pointing at the domain
[ ] 11. Persistent volume for Data Protection keys
[ ] 12. Transaction CRUD (API + UI)
[ ] 13. backup.sh + first restore test
```

---

## References

- [Banco Central — SGS](https://www3.bcb.gov.br/sgspub/)
- [brapi.dev](https://brapi.dev/docs)
- [CoinGecko API](https://docs.coingecko.com/)
- [Twelve Data](https://twelvedata.com/docs)
- [ASP.NET Core Identity](https://learn.microsoft.com/aspnet/core/security/authentication/identity)
- [Google OAuth on ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/authentication/social/google-logins)
- [EF Core — Global Query Filters](https://learn.microsoft.com/ef/core/querying/filters)
- [Caddy](https://caddyserver.com/docs/)
- [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)
- [pg-boss](https://github.com/timgit/pg-boss)
