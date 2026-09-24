#!/usr/bin/env bash
#
# Nightly encrypted backup (spec 010, ARCHITECTURE.md "Backup", runbook step 9). Cron,
# as the deploy user:
#
#   0 3 * * * /opt/finance/src/deploy/backup.sh >> /var/log/finance-backup.log 2>&1
#
# pg_dump (custom format) is piped straight into age, so no plaintext dump touches the
# disk, and the encrypted file goes to the rclone remote. Nothing is kept on this
# machine: a backup that lives on the server it protects is not a backup.
#
# Layout and retention (7 daily, 4 weekly, 6 monthly), in UTC:
#   <remote>/daily/    every run                 pruned past 7 days
#   <remote>/weekly/   Sunday's run as well      pruned past 4 weeks
#   <remote>/monthly/  the 1st's run as well     pruned past 6 months
#
# Restore: rclone copy <remote>/daily/<file> . && age -d -i backup.key <file> > restore.dump
#
# The recipient is AGE_PUBLIC_KEY (the public half of /opt/finance/backup.key), from the
# environment or the env file. Keep a copy of backup.key somewhere that is not this
# server: an encrypted backup whose key died with the instance is not a backup.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${FINANCE_ENV_FILE:-/opt/finance/.env}"
REMOTE="${BACKUP_REMOTE:-backup:finance-backup}"

log() { printf '%s backup: %s\n' "$(date -u +%FT%TZ)" "$1"; }
fail() { log "FAILED: $1" >&2; exit 1; }

env_value() { sed -n "s/^$1=//p" "$ENV_FILE" | tail -n 1; }

[ -f "$ENV_FILE" ] || fail "$ENV_FILE does not exist."
RECIPIENT="${AGE_PUBLIC_KEY:-$(env_value AGE_PUBLIC_KEY)}"
[[ "$RECIPIENT" == age1* ]] || fail "AGE_PUBLIC_KEY is not an age public key (age1...)."
command -v age >/dev/null || fail "age is not installed."
command -v rclone >/dev/null || fail "rclone is not installed."

export FINANCE_ENV_FILE="$ENV_FILE"
compose() { docker compose --env-file "$ENV_FILE" -f "$ROOT/deploy/docker-compose.yml" "$@"; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

NAME="finance_$(date -u +%F).dump.age"
FILE="$WORK/$NAME"

log "dumping to $NAME"
compose exec -T postgres pg_dump --username finance --dbname finance --format custom \
  | age --recipient "$RECIPIENT" --output "$FILE"
[ -s "$FILE" ] || fail "the encrypted dump is empty."

rclone copyto "$FILE" "$REMOTE/daily/$NAME"
if [ "$(date -u +%u)" = 7 ]; then
  rclone copyto "$FILE" "$REMOTE/weekly/$NAME"
fi
if [ "$(date -u +%d)" = 01 ]; then
  rclone copyto "$FILE" "$REMOTE/monthly/$NAME"
fi
log "uploaded $(stat -c %s "$FILE") bytes to $REMOTE"

# Retention, three passes. Only after today's upload succeeded (set -e).
rclone delete "$REMOTE/daily" --min-age 7d
rclone delete "$REMOTE/weekly" --min-age 4w
rclone delete "$REMOTE/monthly" --min-age 6M
log "done"
