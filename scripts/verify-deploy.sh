#!/usr/bin/env bash
#
# Local check of the production deploy files (spec 010, test plan 1-2). Separate from
# verify.sh because it builds images. Needs Docker, age, Node, and the web's
# node_modules (verify.sh installs them).
#
# Brings postgres, api and caddy up from deploy/docker-compose.yml as their own compose
# project, with a throwaway env (ASPNETCORE_ENVIRONMENT=Production, dummy Google keys)
# and Caddy published on 127.0.0.1:$VERIFY_PORT. Then: the migrate step, smoke.sh, both
# fake switches refused at boot, the key ring kept across an API restart, and
# backup.sh's dump decrypted and restored into a scratch database (rclone stubbed, a
# throwaway age key). Tears everything down, volumes included.
#
# Do not run it on the server: it pins the same subnet as the production stack.
#
# VERIFY_API_IMAGE=<image> uses an image built elsewhere instead of building one, for
# a machine whose egress needs a CA the plain build cannot trust.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT="${VERIFY_PORT:-8088}"
WORK="$(mktemp -d)"

export COMPOSE_PROJECT_NAME=finance-verify
export FINANCE_ENV_FILE="$WORK/.env"

step() { printf '\n\033[1m==> %s\033[0m\n' "$1"; }
fail() { printf 'verify-deploy: %s\n' "$1" >&2; exit 1; }
compose() {
  docker compose --env-file "$FINANCE_ENV_FILE" \
    -f "$ROOT/deploy/docker-compose.yml" -f "$WORK/override.yml" "$@"
}

cleanup() {
  exit_code=$?
  set +e
  step "tearing down"
  compose down --volumes --remove-orphans >/dev/null 2>&1
  rm -rf "$WORK"
  exit $exit_code
}
trap cleanup EXIT

cat > "$FINANCE_ENV_FILE" <<EOF
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Default=Host=postgres;Database=finance;Username=finance;Password=verify-only;GSS Encryption Mode=Disable
POSTGRES_PASSWORD=verify-only
Google__ClientId=verify-dummy-client-id
Google__ClientSecret=verify-dummy-client-secret
App__Origin=http://127.0.0.1:$PORT
MarketData__ScheduledSync=false
TUNNEL_TOKEN=verify-unused
EOF

cat > "$WORK/override.yml" <<EOF
services:
  api:
    image: finance-api-verify
  caddy:
    ports: ["127.0.0.1:$PORT:80"]
    volumes:
      - $WORK/webdist:/srv:ro
EOF

step "api image"
if [ -n "${VERIFY_API_IMAGE:-}" ]; then
  docker tag "$VERIFY_API_IMAGE" finance-api-verify
else
  compose build api
fi

step "web build"
(cd "$ROOT/web" && npm run build -- --outDir "$WORK/webdist" --emptyOutDir >/dev/null)

step "migrate step"
compose run --rm api migrate | grep -E "Applying migration|No migrations" || fail "migrate step failed"

step "stack up"
compose up -d postgres api caddy

step "smoke"
"$ROOT/deploy/smoke.sh" "http://127.0.0.1:$PORT"

step "fake switches refused in Production"
for switch in Ai__FakeProvider MarketData__FakeProviders; do
  if compose run --rm --no-deps -e "$switch=true" api > "$WORK/boot.log" 2>&1; then
    fail "the API booted with $switch=true"
  fi
  grep -q "is on in the Production environment" "$WORK/boot.log" || fail "no refusal message for $switch"
  echo "refused: $switch"
done

step "key ring survives a restart"
before="$(compose exec -T api ls /keys)"
compose restart api >/dev/null
SMOKE_WAIT_SECONDS=60 "$ROOT/deploy/smoke.sh" "http://127.0.0.1:$PORT" >/dev/null
after="$(compose exec -T api ls /keys)"
[ -n "$before" ] && [ "$before" = "$after" ] || fail "keys changed: [$before] -> [$after]"
echo "kept: $after"

step "backup, decrypt, restore"
mkdir -p "$WORK/bin" "$WORK/remote"
cat > "$WORK/bin/rclone" <<'EOF'
#!/usr/bin/env bash
# rclone stand-in: "name:path" is $STUB_REMOTE_ROOT/path.
set -euo pipefail
target() { printf '%s/%s' "$STUB_REMOTE_ROOT" "${1#*:}"; }
case "$1" in
  copyto) mkdir -p "$(dirname "$(target "$3")")" && cp "$2" "$(target "$3")" ;;
  delete) echo "$*" >> "$STUB_REMOTE_ROOT/deletes.log" ;;
  *) exit 1 ;;
esac
EOF
chmod +x "$WORK/bin/rclone"
age-keygen -o "$WORK/backup.key" 2>/dev/null
PATH="$WORK/bin:$PATH" STUB_REMOTE_ROOT="$WORK/remote" BACKUP_REMOTE=stub:finance-backup \
  AGE_PUBLIC_KEY="$(age-keygen -y "$WORK/backup.key")" "$ROOT/deploy/backup.sh"
[ "$(wc -l < "$WORK/remote/deletes.log")" = 3 ] || fail "expected three retention passes"
dump="$(ls "$WORK"/remote/finance-backup/daily/finance_*.dump.age)"
compose exec -T postgres createdb --username finance restore_check
age --decrypt --identity "$WORK/backup.key" "$dump" \
  | compose exec -T postgres pg_restore --username finance --dbname restore_check --no-owner
count() { compose exec -T postgres psql --username finance --dbname "$1" -tAc 'SELECT COUNT(*) FROM "__EFMigrationsHistory"'; }
[ "$(count finance)" = "$(count restore_check)" ] || fail "restored migrations differ"
echo "restored: $(count restore_check) migrations, same as the source"

printf '\n\033[1;32m==> verify-deploy.sh: all checks passed\033[0m\n'
