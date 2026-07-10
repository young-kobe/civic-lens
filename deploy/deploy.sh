#!/usr/bin/env bash
# Idempotent deploy: pull images + restart the compose stack. Called by the
# deployment user over SSH with a forced-command key — never run directly by
# a human if you can help it, so that deploys are uniformly shaped.
#
# Invariants:
#   - Never touches /etc/civic-lens.env, /etc/caddy/*, sshd config.
#     Those are install-time concerns and live in install.sh.
#   - No compilation on the box — images are built in CI (deploy.yml `images`
#     job) and pulled from GHCR. The git checkout exists only for
#     docker-compose.yml, unit files, and deploy scripts.
#   - Runs DB migrations (idempotent by design — civic-ingest migrate; the
#     ingest image entrypoint materializes migrations beside the DB,
#     replacing the old symlink-into-/opt mechanism).
#   - Restarts the always-on stack; systemd timers keep owning one-shots.
#   - Does NOT enable/disable timers — the operator owns that.
set -euo pipefail

REPO=/opt/civic-lens
cd "$REPO"

echo "[1/5] pulling main"
git fetch --prune origin
git reset --hard origin/main

echo "[2/5] pulling images"
docker compose pull --quiet

echo "[3/5] migrations"
# Matches the containers' view: CIVIC_DB_PATH from /etc/civic-lens.env rides
# in via the compose env_file; this explicit --db only needs the same default
# the env file uses.
DB_PATH="${CIVIC_DB_PATH:-/var/lib/civic-lens/data/civic_lens.db}"
docker compose run --rm --quiet-pull ingest migrate --db "$DB_PATH"

echo "[4/5] systemd units"
# Retired units: reddit (API access withdrawn) and api (now a compose
# service with restart: unless-stopped). Idempotent: `disable --now` on a
# missing unit is a no-op; removing the files then `daemon-reload` purges
# anything still referencing them.
for unit in civic-lens-reddit.timer civic-lens-reddit.service civic-lens-api.service; do
    systemctl disable --now "$unit" 2>/dev/null || true
    rm -f "/etc/systemd/system/$unit"
done
install -m 0644 "$REPO"/deploy/systemd/*.service /etc/systemd/system/
install -m 0644 "$REPO"/deploy/systemd/*.timer   /etc/systemd/system/
systemctl daemon-reload

echo "[5/5] restart stack"
docker compose up -d --remove-orphans
# Keep /var/lib/docker bounded: drop dangling layers older than a week but
# retain recent :sha tags for quick rollback.
docker image prune -f --filter "until=168h" >/dev/null
echo "deploy ok"
