#!/usr/bin/env bash
# First-time VPS bootstrap. Idempotent — running it twice does nothing on a
# clean state beyond re-asserting config. Run as root from a freshly SSH'd
# box after Ubuntu 24.04 is up.
#
# Prereqs: /opt/civic-lens already contains a clone of the repo (the deploy
# script handles that during CI deploys; for the very first install you
# clone manually, then run this).
#
# Split of concern vs deploy.sh:
#   install.sh runs ONCE — creates users, installs packages, writes system
#              config (sshd drop-in, Caddyfile, systemd units, firewall).
#   deploy.sh  runs every push — rebuilds code artifacts, `systemctl reload`
#              the API, leaves everything install.sh did alone.
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
    echo "install.sh must run as root" >&2
    exit 1
fi

REPO=/opt/civic-lens
if [[ ! -d "$REPO/.git" ]]; then
    echo "expected a git checkout at $REPO before running install.sh" >&2
    exit 1
fi

echo "[1/9] system packages"
apt-get update
apt-get install -y --no-install-recommends \
    git caddy python3.12 python3.12-venv python3-pip golang-go \
    nodejs npm sqlite3 ufw fail2ban rclone age curl ca-certificates

echo "[2/9] users + directories"
if ! id civic-lens &>/dev/null; then
    adduser --system --group --home "$REPO" --shell /usr/sbin/nologin civic-lens
fi
if ! id deployment &>/dev/null; then
    adduser --disabled-password --gecos "" deployment
    install -d -m 0700 -o deployment -g deployment /home/deployment/.ssh
fi
install -d -m 0750 -o civic-lens -g civic-lens /var/lib/civic-lens /var/lib/civic-lens/data /var/lib/civic-lens/backups
install -d -m 0700 -o civic-lens -g civic-lens /var/lib/civic-lens/data/cache
install -d -m 0755 /etc/civic-lens /var/log/caddy
# civic-ingest resolves migrations relative to the DB's directory; symlink
# the in-repo migrations dir so `civic-ingest migrate` finds them and
# `git pull` picks up new SQL files automatically.
ln -sfn "$REPO/data/migrations" /var/lib/civic-lens/data/migrations

echo "[3/9] env file"
if [[ ! -f /etc/civic-lens.env ]]; then
    umask 0077
    cp "$REPO/.env.example" /etc/civic-lens.env
    chown civic-lens:civic-lens /etc/civic-lens.env
    chmod 0600 /etc/civic-lens.env
    echo "  wrote /etc/civic-lens.env skeleton — EDIT before starting services"
fi

echo "[4/9] ssh hardening"
install -m 0644 "$REPO/deploy/sshd_hardening.conf" /etc/ssh/sshd_config.d/99-civic-lens.conf
install -m 0644 "$REPO/deploy/fail2ban.local" /etc/fail2ban/jail.d/civic-lens.local
systemctl restart ssh.socket
systemctl enable --now fail2ban

echo "[5/9] caddy"
install -m 0644 "$REPO/deploy/caddy/Caddyfile" /etc/caddy/Caddyfile
echo "  NOTE: /etc/caddy/origin.crt + origin.key + cloudflare-authenticated-origin-pull.pem must exist"
echo "  before caddy will start. See deploy/README.md Cloudflare APO section."

echo "[6/9] systemd units"
install -m 0644 "$REPO"/deploy/systemd/*.service /etc/systemd/system/
install -m 0644 "$REPO"/deploy/systemd/*.timer   /etc/systemd/system/
systemctl daemon-reload

echo "[7/9] firewall (CF IP pin)"
bash "$REPO/deploy/firewall.sh"

echo "[8/9] deployment user hardening"
# Only allow the deployment user to run deploy.sh — the SSH public key will
# be installed by the operator with a forced-command restriction. See
# deploy/README.md for the authorized_keys template.
install -d -m 0700 -o deployment -g deployment /home/deployment/.ssh
touch /home/deployment/.ssh/authorized_keys
chown deployment:deployment /home/deployment/.ssh/authorized_keys
chmod 0600 /home/deployment/.ssh/authorized_keys
# Give the deployment user permission to run deploy.sh (which itself is a
# civic-lens-owned script — deployment triggers it via sudo NOPASSWD).
cat > /etc/sudoers.d/civic-lens-deploy <<'EOF'
deployment ALL=(root) NOPASSWD: /opt/civic-lens/deploy/deploy.sh
EOF
chmod 0440 /etc/sudoers.d/civic-lens-deploy

echo "[9/9] timer enable"
systemctl enable civic-lens-crawl.timer civic-lens-reddit.timer \
    civic-lens-x.timer civic-lens-analyze.timer \
    civic-lens-backup.timer civic-lens-firewall-refresh.timer
# API service stays disabled until /etc/civic-lens.env is filled in.

echo
echo "install.sh complete. NEXT STEPS (see deploy/README.md):"
echo "  1. fill in /etc/civic-lens.env (especially CIVIC_ADMIN_TOKEN + CIVIC_GEMINI_API_KEY)"
echo "  2. install Cloudflare origin cert + key + CF origin-pull CA cert in /etc/caddy/"
echo "  3. add the deployment SSH key to /home/deployment/.ssh/authorized_keys with the forced command"
echo "  4. bash /opt/civic-lens/deploy/deploy.sh   # first code build + migration"
echo "  5. systemctl enable --now civic-lens-api caddy"
