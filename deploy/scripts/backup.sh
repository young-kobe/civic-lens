#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

if [[ $EUID -ne 0 ]]; then
  echo "Run this script with sudo." >&2
  exit 1
fi

umask 077
backup_directory="$CIVIC_LENS_ROOT/backup"
exec 9>"$backup_directory/lock"
if ! flock --nonblock 9; then
  echo "Another backup is running." >&2
  exit 1
fi

dump="$backup_directory/operational.dump"
trap 'rm -f "$dump.partial"' EXIT

compose exec -T postgres sh -c 'pg_dump --format=custom --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"' > "$dump.partial"
mv "$dump.partial" "$dump"

compose run --rm -T restic backup "$dump" "$CIVIC_LENS_ROOT/config" "$CIVIC_LENS_ROOT/captures" "$CIVIC_LENS_ROOT/releases"
compose run --rm -T restic forget --keep-daily 7 --keep-weekly 4 --keep-monthly 6 --prune

date --utc +%Y-%m-%dT%H:%M:%SZ > "$backup_directory/last-success.partial"
mv "$backup_directory/last-success.partial" "$backup_directory/last-success"
