# Civic Lens deploy/

Infrastructure-as-code for the production deploy. Everything here is flat
config — no Ansible, no Terraform, no Docker. Matches the one-VPS shape
documented in `docs/deployment/plan.md`.

## Layout

- `systemd/` — all `.service` + `.timer` files, one per job. Hardened per
  audit §1.14.
- `caddy/Caddyfile` — reverse proxy + static serving + Cloudflare
  Authenticated Origin Pulls gate. Security headers split across this and
  the FastAPI middleware; see comments.
- `firewall.sh` — pins ingress on 80/443 to Cloudflare IP ranges. Run
  monthly by `civic-lens-firewall-refresh.timer`.
- `sshd_hardening.conf` — drop-in installed at
  `/etc/ssh/sshd_config.d/99-civic-lens.conf`.
- `fail2ban.local` — tighter SSH jail than the default.
- `install.sh` — first-time bootstrap. Run once as root.
- `deploy.sh` — idempotent code build + reload. Run by the `deployment`
  user on every CI deploy; also safe for manual re-runs.
- `backup.sh` — nightly SQLite backup to `/var/lib/civic-lens/backups/`,
  optionally encrypted via `age` and pushed to R2 via `rclone`.
- `authorized_keys.example` — forced-command template for the CI deploy key.

## First-time install (day 1)

1. Provision Ubuntu 24.04 VPS, SSH in as root with a key.
2. Clone: `git clone git@github.com:young-kobe/civic-lens.git /opt/civic-lens`.
3. Run `/opt/civic-lens/deploy/install.sh`. Installs packages, creates the
   `civic-lens` and `deployment` users, writes sshd drop-in, Caddyfile,
   systemd units, and refreshes the firewall.
4. Fill in `/etc/civic-lens.env`:
   - `CIVIC_ADMIN_TOKEN=` — generate with `python -c "import secrets; print(secrets.token_urlsafe(48))"`.
   - `CIVIC_GEMINI_API_KEY=` — freshly rotated key from Google AI Studio.
   - `CIVIC_API_HOST=127.0.0.1`.
   - `CIVIC_DB_PATH=/var/lib/civic-lens/data/civic_lens.db`.
   - `CIVIC_CACHE_DIR=/var/lib/civic-lens/data/cache`.
   - `X_BEARER_TOKEN=` — rotated X API token.
   - `BACKUP_AGE_RECIPIENT=` — optional. An `age` public key for backup
     encryption; generate a pair on a workstation with `age-keygen` and
     keep the private key off the VPS.
   - `BACKUP_RCLONE_REMOTE=` — optional. e.g. `r2:civic-lens-backups`.
5. Install the Cloudflare Origin CA cert + key in `/etc/caddy/`:
   - Cloudflare dashboard → SSL/TLS → Origin Server → Create Certificate.
     Download PEM + private key.
   - `install -m 0644 <path-to-origin.crt> /etc/caddy/origin.crt`
   - `install -m 0600 -o caddy -g caddy <path-to-origin.key> /etc/caddy/origin.key`
   - Download the Cloudflare Authenticated Origin Pull CA from
     https://developers.cloudflare.com/ssl/static/authenticated_origin_pull_ca.pem
     and install at `/etc/caddy/cloudflare-authenticated-origin-pull.pem`.
   - Enable the toggle at SSL/TLS → Origin Server → Authenticated Origin
     Pulls.
6. Install the deploy SSH key:
   - Generate a key pair locally (or use GitHub-managed one). Save the
     private key as the `DEPLOY_SSH_KEY` secret in the GitHub repo.
   - Append the public key with the forced-command prefix from
     `authorized_keys.example` to `/home/deployment/.ssh/authorized_keys`.
7. First build: `sudo -u deployment sudo /opt/civic-lens/deploy/deploy.sh`.
8. Start the API + Caddy: `systemctl enable --now civic-lens-api caddy`.
9. Smoke: `curl -I https://civic-lens.info` (expect 200 + security
   headers); `curl https://civic-lens.info/api/v1/sentiment` (expect JSON).
10. Wire Cloudflare Access: dashboard → Zero Trust → Access → Add
    self-hosted app for civic-lens.info, paths `/api/v1/run/*`,
    `/api/v1/review/*`, `/api/v1/cache-status`. Policy: Allow, emails =
    your email.

## Redeploy (every push to main)

GitHub Actions runs `.github/workflows/deploy.yml` (see PR-E). It SSHes
in as `deployment`, invokes `sudo /opt/civic-lens/deploy/deploy.sh`, and
exits. The forced-command restriction on the authorized_keys line means
that's the only thing the key can do — no shell, no port-forward.

## Recovery

- **API crashes**: `journalctl -fu civic-lens-api`. Fix code, push, let
  CI redeploy.
- **DB corruption**: stop the API, restore from most recent backup in
  `/var/lib/civic-lens/backups/` (or pull from R2), restart.
- **Cloudflare IP range rotation causes origin to block legit traffic**:
  `bash /opt/civic-lens/deploy/firewall.sh` — the timer picks this up
  monthly but you can re-run it manually.
- **Locked out of SSH**: Hetzner rescue mode, mount the disk, edit
  `/etc/ssh/sshd_config.d/99-civic-lens.conf` or reset
  `~/.ssh/authorized_keys`.
