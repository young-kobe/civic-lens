#!/usr/bin/env bash
# Nightly Postgres backup + rclone-to-R2 with client-side encryption. Runs as
# civic-lens (sandboxed unit, no docker access), so it uses the host
# postgresql-client against the compose loopback publish (127.0.0.1:5432)
# with the POSTGRES_* credentials from /etc/civic-lens.env. Encrypts with age
# if $BACKUP_AGE_RECIPIENT is set, falls back to raw if not (logged, but
# doesn't abort).
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set in /etc/civic-lens.env}"
: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD must be set in /etc/civic-lens.env}"
BACKUP_ROOT=/var/lib/civic-lens/backups
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
mkdir -p "$BACKUP_ROOT"

tmp=$(mktemp --tmpdir="$BACKUP_ROOT" civic-lens.dump.XXXXXXXX)
trap 'rm -f "$tmp"' EXIT

# -Fc: custom format — compressed, restorable table-by-table via pg_restore.
PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -h 127.0.0.1 -p 5432 \
    -U "$POSTGRES_USER" -d civic_lens -Fc -f "$tmp"

out="$BACKUP_ROOT/civic_lens-${STAMP}.dump"
if [[ -n "${BACKUP_AGE_RECIPIENT:-}" ]] && command -v age >/dev/null; then
    age -r "$BACKUP_AGE_RECIPIENT" -o "${out}.age" "$tmp"
    final="${out}.age"
else
    echo "warning: BACKUP_AGE_RECIPIENT unset or age(1) missing — storing plaintext" >&2
    mv "$tmp" "$out"
    trap - EXIT
    final="$out"
fi

# Optional remote push. rclone remote + creds come from /etc/civic-lens.env
# (RCLONE_CONFIG=/etc/civic-lens/rclone.conf, BACKUP_RCLONE_REMOTE=r2:civic-lens-backups).
if [[ -n "${BACKUP_RCLONE_REMOTE:-}" ]] && command -v rclone >/dev/null; then
    rclone copy "$final" "$BACKUP_RCLONE_REMOTE/"
fi

# Local retention: 14 days.
find "$BACKUP_ROOT" -maxdepth 1 -type f \( -name 'civic_lens-*.dump' -o -name 'civic_lens-*.dump.age' \) \
    -mtime +14 -delete
echo "backup ok: $final"
