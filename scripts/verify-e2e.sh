#!/usr/bin/env bash
#
# Playwright end-to-end verification. Separate from verify.sh because it needs
# the whole application running: PostgreSQL, the API, and the web dev server.
#
# Playwright starts the web dev server itself (see web/playwright.config.ts).
# This script is responsible for PostgreSQL and the API.

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

API_URL="${API_URL:-http://localhost:5080}"
COMPOSE_FILE="$ROOT_DIR/deploy/docker-compose.dev.yml"
API_PID=""

step() { printf '\n\033[1m==> %s\033[0m\n' "$1"; }

# Runs from whatever directory the script happened to reach, so every path
# used here is absolute.
cleanup() {
  exit_code=$?
  set +e
  if [ -n "$API_PID" ] && kill -0 "$API_PID" 2>/dev/null; then
    step "stopping API (pid $API_PID)"
    kill "$API_PID" 2>/dev/null
    wait "$API_PID" 2>/dev/null
  fi
  step "stopping PostgreSQL"
  docker compose -f "$COMPOSE_FILE" down
  exit $exit_code
}
trap cleanup EXIT

step "starting PostgreSQL"
docker compose -f "$COMPOSE_FILE" up -d

# Built and launched as a dll rather than with `dotnet run`, because `dotnet run`
# spawns the application as a child process: killing the pid this script holds would
# leave that child alive, still bound to the port, and the next run would silently
# test against a stale build. The content root has to be pinned explicitly, because
# it otherwise follows the working directory and the app would not find
# appsettings.Development.json.
step "building API"
dotnet build "$ROOT_DIR/api" --nologo

step "starting API at $API_URL"
ASPNETCORE_ENVIRONMENT=Development \
  dotnet "$ROOT_DIR/api/bin/Debug/net10.0/Finance.Api.dll" \
  --contentRoot "$ROOT_DIR/api" --urls "$API_URL" &
API_PID=$!

step "waiting for $API_URL/health"
for attempt in $(seq 1 60); do
  if curl --silent --fail "$API_URL/health" >/dev/null 2>&1; then
    echo "API is healthy after ${attempt}s"
    break
  fi
  if [ "$attempt" -eq 60 ]; then
    echo "API did not become healthy within 60s" >&2
    exit 1
  fi
  sleep 1
done

step "playwright"
cd "$ROOT_DIR/web"
if [ ! -d node_modules ]; then
  npm ci
fi
npm run e2e

printf '\n\033[1;32m==> verify-e2e.sh: all checks passed\033[0m\n'
