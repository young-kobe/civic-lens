#!/usr/bin/env bash
# Idempotent build + reload. Called by the deployment user over SSH with a
# forced-command key — never run directly by a human if you can help it, so
# that deploys are uniformly shaped.
#
# Invariants:
#   - Never touches /etc/civic-lens.env, /etc/caddy/*, sshd config.
#     Those are install-time concerns and live in install.sh.
#   - Builds Go binary, Python venv, UI into /opt/civic-lens.
#   - Runs DB migrations (idempotent by design — civic-ingest migrate).
#   - Reloads civic-lens-api via systemd.
#   - Does NOT enable/disable timers — the operator owns that.
set -euo pipefail

REPO=/opt/civic-lens
cd "$REPO"

echo "[1/6] pulling main"
git fetch --prune origin
git reset --hard origin/main

echo "[2/6] go binary"
(
    cd ingest
    go build -trimpath -ldflags "-s -w" -o "$REPO/civic-ingest" ./cmd/civic-ingest
)

echo "[3/6] python venv"
if [[ ! -d "$REPO/.venv" ]]; then
    python3.12 -m venv "$REPO/.venv"
fi
"$REPO/.venv/bin/pip" install --upgrade pip wheel
"$REPO/.venv/bin/pip" install --upgrade -r analysis/requirements.txt

echo "[4/6] ui build"
(
    cd ui
    npm ci
    npm run build
)

echo "[5/6] migrations"
# civic-ingest resolves migrations relative to the DB's parent directory,
# so make sure the symlink to the in-repo migrations/ exists. Idempotent:
# ln -sfn replaces any prior symlink or missing entry. Safe across git
# pulls — deploy.sh chowns the repo at the end, but never touches
# /var/lib/civic-lens, so this link survives each run.
DB_PATH="${CIVIC_DB_PATH:-/var/lib/civic-lens/data/civic_lens.db}"
DB_DIR="$(dirname "$DB_PATH")"
mkdir -p "$DB_DIR"
ln -sfn "$REPO/data/migrations" "$DB_DIR/migrations"

# civic-ingest migrate is a no-op when schema is already current.
"$REPO/civic-ingest" migrate --db "$DB_PATH"

# Disable the Reddit timer + service if a previous install enabled them.
# Reddit API access was withdrawn; keeping the timer running would just log
# failures every 2h. Idempotent: `disable --now` on a missing unit is a no-op;
# removing the files then `daemon-reload` purges anything still referencing
# them. If Reddit comes back, restore the unit files and rerun install.sh.
for unit in civic-lens-reddit.timer civic-lens-reddit.service; do
    systemctl disable --now "$unit" 2>/dev/null || true
    rm -f "/etc/systemd/system/$unit"
done
systemctl daemon-reload

echo "[6/6] chown + reload"
chown -R civic-lens:civic-lens "$REPO"
# civic-ingest runs as root inside deploy.sh and writes the DB file —
# hand ownership back to the service user so civic-lens-api.service can
# open it after the reload below. The /var/lib tree itself is left with
# install.sh's 0750 perms on dirs + 0600 on files.
chown civic-lens:civic-lens "$DB_DIR" "$DB_PATH" 2>/dev/null || true
systemctl reload-or-restart civic-lens-api.service || true
echo "deploy ok"
