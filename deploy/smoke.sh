#!/usr/bin/env bash
#
# Post-deploy smoke test (spec 010). deploy.sh runs it last; it also runs by hand:
#
#   deploy/smoke.sh https://<your-domain>
#
# Checks: /health 200, /api/health 200 with "database":"ok", /api/auth/me 401 and
# /api/auth/dev-login 404. The last one proves ASPNETCORE_ENVIRONMENT=Production took
# effect: the route exists only in Development. Exits non-zero on any miss.

set -euo pipefail

BASE_URL="${1:-${SMOKE_URL:-}}"
if [ -z "$BASE_URL" ]; then
  echo "usage: $0 <base-url>   e.g. $0 https://finance.example" >&2
  exit 2
fi
BASE_URL="${BASE_URL%/}"

# Seconds to wait for /health after a restart before checking anything.
WAIT_SECONDS="${SMOKE_WAIT_SECONDS:-60}"

BODY="$(mktemp)"
trap 'rm -f "$BODY"' EXIT

failures=0

# Prints the status code of a request, or 000 when there is no answer at all.
status() {
  curl --silent --show-error --max-time 15 --output "$BODY" --write-out '%{http_code}' "$@" \
    || true
}

check() {
  local name="$1" expected="$2" actual="$3"
  if [ "$actual" = "$expected" ]; then
    printf 'ok    %-28s %s\n' "$name" "$actual"
  else
    printf 'FAIL  %-28s expected %s, got %s\n' "$name" "$expected" "$actual"
    failures=$((failures + 1))
  fi
}

printf 'smoke: %s\n' "$BASE_URL"

for _ in $(seq 1 "$WAIT_SECONDS"); do
  [ "$(status "$BASE_URL/health" 2>/dev/null)" = 200 ] && break
  sleep 1
done

check "GET /health" 200 "$(status "$BASE_URL/health")"

check "GET /api/health" 200 "$(status "$BASE_URL/api/health")"
if grep -q '"database":"ok"' "$BODY"; then
  check 'body has "database":"ok"' yes yes
else
  check 'body has "database":"ok"' yes "no: $(head -c 200 "$BODY")"
fi

check "GET /api/auth/me" 401 "$(status "$BASE_URL/api/auth/me")"

# With the Origin the API expects, so the origin check lets it through to routing.
check "POST /api/auth/dev-login" 404 "$(status --request POST \
  --header "Origin: $BASE_URL" --header 'Content-Type: application/json' \
  --data '{"email":"smoke@example.com"}' "$BASE_URL/api/auth/dev-login")"

if [ "$failures" -gt 0 ]; then
  printf 'smoke: %d check(s) failed\n' "$failures" >&2
  exit 1
fi
printf 'smoke: all checks passed\n'
