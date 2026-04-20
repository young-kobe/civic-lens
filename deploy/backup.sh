#!/usr/bin/env bash
# Nightly SQLite backup + rclone-to-R2 with client-side encryption (audit
# §6 MEDIUM). Runs as civic-lens; reads the DB via sqlite3's live-safe
# .backup command, encrypts with age if $BACKUP_AGE_RECIPIENT is set,
# falls back to raw if not (logged, but doesn't abort).
set -euo pipefail

DB_PATH=${CIVIC_DB_PATH:-/var/lib/civic-lens/data/civic_lens.db}
BACKUP_ROOT=/var/lib/civic-lens/backups
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
mkdir -p "$BACKUP_ROOT"

tmp=$(mktemp --tmpdir="$BACKUP_ROOT" civic-lens.db.XXXXXXXX)
trap 'rm -f "$tmp"' EXIT

sqlite3 "$DB_PATH" ".backup '$tmp'"

out="$BACKUP_ROOT/civic_lens-${STAMP}.db"
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
find "$BACKUP_ROOT" -maxdepth 1 -type f \( -name 'civic_lens-*.db' -o -name 'civic_lens-*.db.age' \) \
    -mtime +14 -delete
echo "backup ok: $final"
