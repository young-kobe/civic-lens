# Civic Lens deploy/

Infrastructure-as-code for the production deploy. The runtime is a **Docker
Compose stack on a single VPS**: three images are built in GitHub Actions and
pushed to GHCR (`civic-lens-ingest`, `civic-lens-analysis`, `civic-lens-web`);
the box only ever pulls — no Go/Node/Python/Caddy packages on the host and no
compilation during deploys. Scheduling stays on host systemd timers, which
exec `docker compose run --rm <service>` so the existing `OnFailure=` alert
plumbing keeps watching real exit codes.

The full rationale and the shape of the stack live in
`docs/deployment/plan.md` and `docs/audit-trail/infra/2026-07-09-docker-compose-stack.md`.

## Stack shape

- **Always-on** (`docker compose up -d`): `caddy` (host network, TLS + static
  UI + `/api` reverse proxy + Cloudflare Authenticated Origin Pulls gate),
  `api` (uvicorn, publishes `127.0.0.1:8000` only), `litestream` (continuous
  SQLite replication to R2).
- **One-shots** (`jobs` profile, never started by `up`): `ingest` + `analyze`,
  invoked only as `docker compose run --rm <service> ...` by the systemd timers.
- **Data contract**: the host dir `/var/lib/civic-lens` is bind-mounted at the
  identical path in every container, so absolute paths in `/etc/civic-lens.env`
  and relative paths in `seeds.yaml` resolve the same in-container and on-host.
  Host tooling (`backup.sh`, `sqlite3` spot checks) keeps working. Containers
  run as UID 990, matching the `civic-lens` host user, so bind-mount writes
  need no chown.

## Layout

- `docker-compose.yml` (repo root) — the stack definition. `docker-compose.override.example.yml`
  documents local-dev overrides; production never has an override file.
- `docker/Dockerfile.web` — bakes `ui/dist` + the Caddyfile into a caddy image.
  (The `ingest` and `analysis` images build from `ingest/Dockerfile` and
  `analysis/Dockerfile`.)
- `caddy/Caddyfile` — reverse proxy + static serving + origin-pull gate. Shipped
  inside the web image; TLS material stays host-side, bind-mounted read-only.
- `litestream.yml` — continuous WAL replication of the SQLite DB to R2.
- `systemd/` — `civic-lens-stack.service` brings the compose stack up at boot;
  `civic-lens-{crawl,analyze,x}.{service,timer}` exec the compose one-shots;
  backup/firewall units. (There is no `civic-lens-api.service` — compose
  `restart: unless-stopped` owns the API.)
- `firewall.sh` — pins ingress on 80/443 to Cloudflare IP ranges. Run monthly
  by `civic-lens-firewall-refresh.timer`.
- `sshd_hardening.conf` — drop-in installed at `/etc/ssh/sshd_config.d/99-civic-lens.conf`.
- `fail2ban.local` — tighter SSH jail than the default.
- `install.sh` — first-time bootstrap (installs Docker, creates the
  `civic-lens`/`deployment` users, pins the app UID, writes units + firewall).
  Run once as root.
- `deploy.sh` — idempotent redeploy: `git fetch` (for the compose file +
  migrations only) → `docker compose pull` → `compose run --rm ingest migrate`
  → `compose up -d` → weekly image prune. Run by the `deployment` user on every
  CI deploy; safe for manual re-runs.
- `backup.sh` — nightly SQLite backup to `/var/lib/civic-lens/backups/`,
  optionally age-encrypted and pushed to R2 (second durability layer alongside
  Litestream).
- `authorized_keys.example` — forced-command template for the CI deploy key.

## First-time install (day 1)

1. Provision Ubuntu 24.04 VPS (Hetzner CPX21 or similar), SSH in as root with a key.
2. Clone: `git clone git@github.com:young-kobe/civic-lens.git /opt/civic-lens`.
   The checkout stays on the box for the compose file + migrations; code runs
   from the GHCR images, not this tree.
3. Run `/opt/civic-lens/deploy/install.sh`. Installs Docker, creates the
   `civic-lens` and `deployment` users, pins the app UID, writes the sshd
   drop-in, systemd units, and refreshes the firewall.
4. Fill in `/etc/civic-lens.env`:
   - `CIVIC_ADMIN_TOKEN=` — generate with `python -c "import secrets; print(secrets.token_urlsafe(48))"`.
   - `CIVIC_LLM_BACKEND=` — `gemini` | `ollama` | `openai_compat`.
   - `CIVIC_GEMINI_API_KEY=` — freshly rotated key from Google AI Studio (if using Gemini).
   - `CIVIC_LLM_BASE_URL=` / `CIVIC_LLM_API_KEY=` — for `openai_compat`.
   - `CIVIC_API_HOST=127.0.0.1`.
   - `CIVIC_DB_PATH=/var/lib/civic-lens/data/civic_lens.db`.
   - `CIVIC_CACHE_DIR=/var/lib/civic-lens/data/cache`.
   - `X_BEARER_TOKEN=` — rotated X API token.
   - `LITESTREAM_*` — R2 bucket + credentials for continuous replication.
   - `BACKUP_AGE_RECIPIENT=` — optional `age` public key for backup encryption;
     keep the private key off the VPS.
   - `BACKUP_RCLONE_REMOTE=` — optional, e.g. `r2:civic-lens-backups`.
5. Install the Cloudflare Origin CA cert + key in `/etc/caddy/` (bind-mounted
   read-only into the caddy container):
   - Cloudflare dashboard → SSL/TLS → Origin Server → Create Certificate.
   - `install -m 0644 <origin.crt> /etc/caddy/origin.crt`
   - `install -m 0600 <origin.key> /etc/caddy/origin.key`
   - Download the Authenticated Origin Pull CA from
     https://developers.cloudflare.com/ssl/static/authenticated_origin_pull_ca.pem
     to `/etc/caddy/cloudflare-authenticated-origin-pull.pem`, then enable the
     toggle at SSL/TLS → Origin Server → Authenticated Origin Pulls.
6. Install the deploy SSH key:
   - Generate a key pair; save the private key as the `DEPLOY_SSH_KEY` GitHub secret.
   - Append the public key with the forced-command prefix from
     `authorized_keys.example` to `/home/deployment/.ssh/authorized_keys`.
7. First deploy: `sudo -u deployment sudo /opt/civic-lens/deploy/deploy.sh`
   (pulls images, migrates, brings the stack up).
8. Enable boot bring-up: `systemctl enable --now civic-lens-stack`.
9. Smoke: `curl -I https://civic-lens.info` (expect 200 + security headers);
   `curl https://civic-lens.info/api/v1/sentiment` (expect JSON).
10. Wire Cloudflare Access: dashboard → Zero Trust → Access → Add self-hosted
    app for civic-lens.info, paths `/api/v1/run/*`, `/api/v1/review/*`,
    `/api/v1/cache-status`. Policy: Allow, emails = your email.

## Redeploy (every push to main)

GitHub Actions runs `.github/workflows/deploy.yml`: an `images` job builds and
pushes the three images to GHCR (`:latest` + `:sha` tags), gating a `deploy`
job that SSHes in as `deployment` and invokes `sudo deploy.sh` — which pulls
the new images and restarts the stack. The forced-command restriction on the
authorized_keys line means that's the only thing the key can do — no shell, no
port-forward. `:sha` tags give real rollback (`docker compose ... pull` a prior
tag) that on-box git builds never did.

## Recovery

- **A service crashes**: `docker compose logs -f api` (or `caddy`/`litestream`).
  Fix code, push, let CI rebuild + redeploy; or `docker compose up -d` to
  re-pull a known-good tag.
- **DB corruption**: stop the stack, restore from Litestream (`litestream
  restore`) or the most recent `backup.sh` archive in
  `/var/lib/civic-lens/backups/` (or R2), restart.
- **Cloudflare IP range rotation blocks legit traffic at the origin**:
  `bash /opt/civic-lens/deploy/firewall.sh` — the timer picks this up monthly,
  but you can re-run it manually.
- **Locked out of SSH**: Hetzner rescue mode, mount the disk, edit
  `/etc/ssh/sshd_config.d/99-civic-lens.conf` or reset `~/.ssh/authorized_keys`.
