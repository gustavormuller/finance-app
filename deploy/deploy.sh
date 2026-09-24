#!/usr/bin/env bash
#
# Deploy (spec 010, decision 7). On the server, over SSH, as the deploy user:
#
#   /opt/finance/src/deploy/deploy.sh [git-ref]     # default: main
#
# Rollback (decision 8): deploy.sh <previous tag>. Migrations are forward-only, so a
# rollback that needs a down-migration is a restore from backup instead.
#
# verify.sh gates a deploy but does not run here (test plan 5): run it before pushing.
#
# Order: web build, API image, migrate, restart, publish web, smoke. The migration runs
# before `up -d` (decision 2), so a failed one leaves the old containers running; the
# new web build is only published once the new API is up, so a failed deploy leaves
# the old pair serving together.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${FINANCE_ENV_FILE:-/opt/finance/.env}"
REF="${1:-main}"

step() { printf '\n==> %s\n' "$1"; }
fail() { printf 'deploy: %s\n' "$1" >&2; exit 1; }

# The last value of a key in the env file, as Docker reads it (no shell evaluation).
env_value() { sed -n "s/^$1=//p" "$ENV_FILE" | tail -n 1; }

[ -f "$ENV_FILE" ] || fail "$ENV_FILE does not exist (runbook step 4)."
[ "$(stat -c %a "$ENV_FILE")" = 600 ] || fail "$ENV_FILE must be chmod 600."
if grep -qE '^(Ai__FakeProvider|MarketData__FakeProviders)=' "$ENV_FILE"; then
  fail "$ENV_FILE sets a fake-provider switch; remove it (the API refuses to boot with it)."
fi
ORIGIN="$(env_value App__Origin)"
[ -n "$ORIGIN" ] || fail "App__Origin is not set in $ENV_FILE."

export FINANCE_ENV_FILE="$ENV_FILE"
compose() { docker compose --env-file "$ENV_FILE" -f "$ROOT/deploy/docker-compose.yml" "$@"; }

cd "$ROOT"

if [ "${FINANCE_DEPLOY_CHECKED_OUT:-}" != "$REF" ]; then
  step "git: $REF"
  git fetch --tags origin
  git checkout "$REF"
  # A tag (a rollback) is a detached HEAD, with nothing to pull.
  if git symbolic-ref --quiet HEAD >/dev/null; then
    git pull --ff-only
  fi
  git log --oneline -1

  # Go on with the deploy.sh just checked out, not with this older copy of it.
  FINANCE_DEPLOY_CHECKED_OUT="$REF" exec bash "$ROOT/deploy/deploy.sh" "$REF"
fi

# In a Node container, so the server needs nothing but Docker (runbook step 3). Built
# into webdist.next and published after the API is up.
step "web: build"
rm -rf deploy/webdist.next
docker run --rm --user "$(id -u):$(id -g)" --env HOME=/tmp \
  --volume "$ROOT:/src" --workdir /src/web node:22-alpine \
  sh -c 'npm ci --no-audit --no-fund && npm run build -- --outDir ../deploy/webdist.next --emptyOutDir'

step "api: build image"
compose build --pull api

step "database: migrate"
compose run --rm api migrate

step "restart"
compose up -d --remove-orphans

# Emptied and refilled in place: Caddy bind-mounts this directory, and a replaced
# directory would leave it serving the old, deleted one.
step "web: publish"
mkdir -p deploy/webdist
find deploy/webdist -mindepth 1 -delete
cp -a deploy/webdist.next/. deploy/webdist/
rm -rf deploy/webdist.next

step "smoke: $ORIGIN"
bash "$ROOT/deploy/smoke.sh" "$ORIGIN"

# Old API images left dangling by the rebuild; 50 GB of boot volume is not much.
docker image prune --force >/dev/null

step "deployed $(git rev-parse --short HEAD)"
