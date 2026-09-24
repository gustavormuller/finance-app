# 010 — Deploy

## Goal

The app runs on the Oracle Always Free instance, reachable at your domain through Cloudflare Tunnel, with real Google sign-in, nightly backups that have been restored at least once, and a way to deploy again without thinking.

This spec is a **runbook**, not a feature. Most of it is sequential human work on a server. The code changes are small and listed first.

## Decisions made in this spec

| # | Question | Decision |
|---|---|---|
| 1 | Build location | **On the server.** ARM64 native build avoids cross-compilation from Windows entirely. |
| 2 | Migrations | **Explicit step in the deploy script**, never auto-migrate on startup. A migration failing at 3 a.m. after a restart is the wrong time to find out. |
| 3 | Secrets in production | `.env` at `/opt/finance/.env`, `chmod 600`, owned by the deploy user. Not user-secrets, not Secrets Manager. |
| 4 | Web serving | Vite build output served as static files by Caddy from the same origin as `/api`. Same-origin is what 002's cookie depends on. |
| 5 | TLS | Cloudflare terminates at the edge. Caddy serves plain HTTP on the private network. No certificates on the box. |
| 6 | Process supervision | Docker Compose with `restart: unless-stopped`. No systemd unit beyond Docker itself. |
| 7 | Deploy mechanism | A `deploy.sh` run over SSH: pull, build, migrate, restart, smoke-test. Not CI. Add CI when this becomes annoying, not before. |
| 8 | Rollback | `git checkout <previous tag>` + `deploy.sh`. Migrations are forward-only; a rollback that needs a down-migration is a restore from backup. |
| 9 | Monitoring | UptimeRobot (free) on `/api/health` every 5 minutes, alerting to email. `SyncRun` rows for the job. Nothing else. |
| 10 | E2E in production | **Not run.** `verify.sh` gates the deploy; `verify-e2e.sh` runs locally. Production gets a smoke script that hits `/health`, `/api/health`, and `/api/auth/me` expecting `401`. |

## Out of scope

- CI/CD pipeline
- Staging environment
- Multi-instance, load balancing, high availability
- Log aggregation beyond `docker compose logs`
- Metrics dashboards
- Automated restore drills — one manual restore is required, recurring ones are not

## Code changes

Small, all in the repo before touching the server.

### `deploy/Dockerfile.api`

Multi-stage. `mcr.microsoft.com/dotnet/sdk:10.0` → publish → `mcr.microsoft.com/dotnet/aspnet:10.0`. Both images have `arm64` variants and Docker picks the host's. Non-root user. `EXPOSE 8080`. Health check hitting `/health`.

### `deploy/docker-compose.yml` — production

Four services: `postgres`, `api`, `caddy`, `cloudflared`. Volumes: `pgdata`, `dpkeys` (Data Protection), `caddydata`, `webdist` (built assets). `postgres` publishes **no** port. `api` reads `/opt/finance/.env` via `env_file`. Memory limits: `api` 512 MB, `postgres` 1 GB — 12 GB is available, but limits catch a leak before it takes the box.

### `deploy/Caddyfile`

```
:80 {
    encode gzip
    handle /api/* { reverse_proxy api:8080 }
    handle /health { reverse_proxy api:8080 }
    handle {
        root * /srv
        try_files {path} /index.html
        file_server
    }
}
```

Same shape as dev, which is the point.

### `deploy/deploy.sh`

```
set -euo pipefail
cd /opt/finance/src
git fetch && git checkout "${1:-main}" && git pull --ff-only
(cd web && npm ci && npm run build)          # into ../deploy/webdist
docker compose -f deploy/docker-compose.yml build api
docker compose -f deploy/docker-compose.yml run --rm api dotnet ef database update   # explicit
docker compose -f deploy/docker-compose.yml up -d
./deploy/smoke.sh
```

Migration before `up -d`, so a failed migration leaves the old containers running.

### `deploy/smoke.sh`

Curl `/health` → `200`, `/api/health` → `200` with `"database":"ok"`, `/api/auth/me` → `401`, `/api/auth/dev-login` → `404`. Exit non-zero on any miss. The last check proves `ASPNETCORE_ENVIRONMENT=Production` took effect.

### `deploy/backup.sh`

From ARCHITECTURE.md, with `age` encryption and rclone to B2 or R2. Retention 7 daily / 4 weekly / 6 monthly via rclone's `--min-age` deletes in three passes.

### Application

- `ASPNETCORE_ENVIRONMENT=Production` disables `dev-login` (already tested in 002) and Swagger if present
- `ForwardedHeaders` middleware trusting the Docker network, so `Request.Scheme` is `https` from Cloudflare's `X-Forwarded-Proto` — **required**, or the cookie's `Secure` flag makes it unsendable and every login silently fails
- Data Protection keys path from config, pointed at the `dpkeys` volume
- `Google:*` and `DataProtection:*` read from environment variables with the `__` separator

## Runbook

Each step ends with a check. Do not proceed past a failed check.

### 1 — Instance

- Oracle Cloud → Compute → Create instance → shape `VM.Standard.A1.Flex`, 2 OCPU, 12 GB, Ubuntu 24.04 minimal, Always Free eligible
- If *Out of host capacity*: try another availability domain, then another region. Retry later if all fail — it clears
- Boot volume 50 GB
- Add your SSH public key; **no password auth**

Check: `ssh ubuntu@<ip>` works.

### 2 — Hardening

```
sudo apt update && sudo apt upgrade -y
sudo ufw default deny incoming && sudo ufw default allow outgoing
sudo ufw allow OpenSSH && sudo ufw enable
sudo apt install -y fail2ban && sudo systemctl enable --now fail2ban
```

Oracle's VCN security list also needs only port 22 inbound — **remove the default 80/443 rules**; Cloudflare Tunnel is outbound-only.

Check: `sudo ufw status` shows only 22. Oracle console security list shows only 22.

### 3 — Docker

```
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu && newgrp docker
docker run --rm hello-world
```

Check: hello-world prints on `arm64`.

### 4 — Repo and secrets

```
sudo mkdir -p /opt/finance && sudo chown ubuntu:ubuntu /opt/finance
git clone <repo> /opt/finance/src
```

Create `/opt/finance/.env`, `chmod 600`:

```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Default=Host=postgres;Database=finance;Username=finance;Password=<generated>
POSTGRES_PASSWORD=<same>
Google__ClientId=
Google__ClientSecret=
DataProtection__KeysPath=/keys
App__Origin=https://<your-domain>
MarketData__Brapi__Token=
MarketData__CoinGecko__DemoKey=
MarketData__TwelveData__Key=
Ai__Provider=
Ai__Anthropic__ApiKey=
Ai__MonthlyBudgetBrl=15
TUNNEL_TOKEN=
```

Check: `ls -l /opt/finance/.env` shows `-rw-------`. `git status` in `src` does not list it.

### 5 — Cloudflare Tunnel

- Cloudflare Zero Trust → Networks → Tunnels → Create → name `finance`
- Copy the token into `TUNNEL_TOKEN`
- Public hostname: your domain → `http://caddy:80`

Check: the tunnel shows *Healthy* after step 7 brings `cloudflared` up.

### 6 — Google OAuth for production

- Add `https://<your-domain>/api/auth/google/callback` as a redirect URI on the existing client
- Publish the consent screen (ADR-010: open registration). Basic scopes need no verification review
- Remove nothing from the dev URIs

Check: the URI appears in the console exactly, including scheme.

### 7 — First deploy

```
cd /opt/finance/src && chmod +x deploy/*.sh
./deploy/deploy.sh
```

Check: `smoke.sh` exits 0. `docker compose ps` shows four services `Up`. `docker compose logs api | tail` has no errors.

### 8 — First login

Open `https://<your-domain>`. Sign in with Google. Sign out. Sign in again.

Then `docker compose restart api` and reload — **still signed in**. This is the Data Protection check from 002 and the one that fails most often in production.

Check: `SELECT "Email" FROM "AspNetUsers"` shows you.

### 9 — Backups

```
sudo apt install -y age rclone
rclone config          # B2 or R2 remote named `backup`
age-keygen -o /opt/finance/backup.key && chmod 600 /opt/finance/backup.key
```

Put the public key in `backup.sh`. Install the cron:

```
0 3 * * * /opt/finance/src/deploy/backup.sh >> /var/log/finance-backup.log 2>&1
```

**Keep a copy of `backup.key` somewhere that is not this server.** An encrypted backup with a key that only lived on the deleted instance is not a backup.

Check: run `backup.sh` by hand; `rclone ls backup:finance-backup` shows today's file.

### 10 — Restore drill — required, once

On your local machine:

```
rclone copy backup:finance-backup/<latest> .
age -d -i backup.key <latest> > restore.dump
docker run -d --name restore-test -e POSTGRES_PASSWORD=x -p 5433:5432 postgres:16-alpine
pg_restore -h localhost -p 5433 -U postgres -d postgres --create restore.dump
psql -h localhost -p 5433 -U postgres -d finance -c 'SELECT COUNT(*) FROM "Transactions"'
```

Check: the count matches production. **Until this step passes, 010 is not done.**

### 11 — Monitoring

UptimeRobot: HTTP monitor on `https://<your-domain>/api/health`, 5-minute interval, keyword `"database":"ok"`, alert to email.

Check: pause the API for a minute; an alert arrives.

### 12 — Second deploy

Make a trivial change locally, push, `./deploy/deploy.sh` on the server.

Check: the change is live; no downtime beyond the container restart; you are still signed in.

## Test plan

Automated, in the repo:

1. `Dockerfile.api` builds on `arm64` (a GitHub-free check: `docker buildx build --platform linux/arm64` locally succeeds, even if slow)
2. `smoke.sh` against the dev stack with `ASPNETCORE_ENVIRONMENT=Production` → passes
3. Integration test from 002 (`dev-login` is `404` in Production) still green
4. `ForwardedHeaders`: a request with `X-Forwarded-Proto: https` from the trusted network yields a `Secure` cookie; from an untrusted source, the header is ignored
5. `verify.sh` green before every deploy — `deploy.sh` does not run it, you do

Manual: the twelve runbook checks above.

## Definition of done

- Runbook steps 1–12 all pass their checks
- Restore drill completed and the count matched
- `backup.key` exists off the server
- Oracle security list and `ufw` expose only port 22
- Still signed in after `docker compose restart api`
- `deploy.sh` has been run at least twice
- ARCHITECTURE.md's deploy section updated to match what shipped
