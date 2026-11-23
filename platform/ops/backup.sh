#!/usr/bin/env bash
set -euo pipefail
umask 077
cd "$(dirname "$0")/.."
: "${KRYLYA_BACKUP_MOUNT:?Set the full path to a separately mounted backup volume}"
mountpoint -q "$KRYLYA_BACKUP_MOUNT" || { echo "Backup volume is offline" >&2; exit 1; }
dc=(docker compose --env-file .env.host -f compose.host.yml exec -T postgres)
target="$KRYLYA_BACKUP_MOUNT/krylya-$(date -u +%Y%m%dT%H%M%SZ)-$$.dump"
tmp=$(mktemp "$target.XXXXXX")
check="krylya_restore_$(date +%s)_$$"
cleanup() { rm -f -- "$tmp"; }
trap cleanup EXIT
"${dc[@]}" pg_dump -Fc -U krylya -d krylya > "$tmp"
"${dc[@]}" createdb -T template0 -U krylya "$check"
trap '"${dc[@]}" dropdb -U krylya "$check"; cleanup' EXIT
"${dc[@]}" pg_restore --exit-on-error -U krylya -d "$check" < "$tmp"
"${dc[@]}" psql -X -v ON_ERROR_STOP=1 -U krylya -d "$check" -c 'SELECT count(*) FROM "__EFMigrationsHistory"' > /dev/null
mv -- "$tmp" "$target"
