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
# civic-ingest migrate is a no-op when schema is already current.
"$REPO/civic-ingest" migrate --db "${CIVIC_DB_PATH:-/var/lib/civic-lens/data/civic_lens.db}"

echo "[6/6] chown + reload"
chown -R civic-lens:civic-lens "$REPO"
systemctl reload-or-restart civic-lens-api.service || true
echo "deploy ok"
