#!/usr/bin/env bash
set -euo pipefail
: "${KRYLYA_HOST:?Missing host}" "${KRYLYA_BACKUP_MOUNT:?Missing backup mount}"
curl -fsS --max-time 15 "https://$KRYLYA_HOST/health" >/dev/null
mountpoint -q "$KRYLYA_BACKUP_MOUNT"
recent=$(find "$KRYLYA_BACKUP_MOUNT" -maxdepth 1 -type f -name 'krylya-*.dump' -size +0c -mmin -1560 -print -quit)
[[ -n "$recent" ]] || { echo "No verified backup in 26 hours" >&2; exit 1; }
