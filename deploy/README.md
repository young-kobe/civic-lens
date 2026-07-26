# Civic Lens deploy/

Infrastructure-as-code for the production deploy. The runtime is a **Docker
Compose stack on a single VPS**: three images are built in GitHub Actions and
pushed to GHCR (`civic-lens-ingest`, `civic-lens-analysis`, `civic-lens-web`);
the box only ever pulls — no Go/Node/Python/Caddy packages on the host and no
compilation during deploys. Scheduling stays on host systemd timers, which
exec `docker compose run --rm <service>` so the existing `OnFailure=` alert
plumbing keeps watching real exit codes.

## Stack shape

- **Always-on** (`docker compose up -d`): `caddy` (host network, TLS + static
  UI + `/api` reverse proxy + Cloudflare Authenticated Origin Pulls gate),
  `api` (uvicorn, publishes `127.0.0.1:8000` only), `postgres` (the primary
  datastore, publishes `127.0.0.1:5432` for host tooling).
- **One-shots** (`jobs` profile, never started by `up`): `ingest` + `analyze`,
  invoked only as `docker compose run --rm <service> ...` by the systemd timers.
- **Data contract**: the host dir `/var/lib/civic-lens` is bind-mounted at the
  identical path in every container, so absolute paths in `/etc/civic-lens.env`
  (e.g. `CIVIC_RAW_STORE_DIR`) resolve the same in-container and on-host.
  Postgres data lives in the `pgdata` named volume. Containers run as UID
  10001, matching the `civic-lens` host user, so bind-mount writes need no
  chown.
- **DSN vantage points**: containers reach the DB at `@postgres:5432` (compose
  service DNS); host tooling (`backup.sh`, `psql` spot checks) uses the
  loopback publish `@127.0.0.1:5432`. `/etc/civic-lens.env` must carry the
  container form.

## Layout

- `docker-compose.yml` (repo root) — the stack definition. `docker-compose.override.example.yml`
  documents local-dev overrides; production never has an override file.
- `docker/Dockerfile.web` — bakes `ui/dist` + the Caddyfile into a caddy image.
  (The `ingest` and `analysis` images build from `ingest/Dockerfile` and
  `analysis/Dockerfile`.)
- `caddy/Caddyfile` — reverse proxy + static serving + origin-pull gate. Shipped
  inside the web image; TLS material stays host-side, bind-mounted read-only.
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
  unit files only) → `docker compose pull` → `compose run --rm ingest migrate`
  (applies `data/pg-migrations/`, tracked in `ops.schema_migrations`) →
  `compose up -d` → weekly image prune. Run by the `deployment` user on every
  CI deploy; safe for manual re-runs.
- `backup.sh` — nightly `pg_dump -Fc` to `/var/lib/civic-lens/backups/`,
  age-encrypted when `BACKUP_AGE_RECIPIENT` is set, optionally pushed to R2
  via rclone. 14-day local retention.
- `authorized_keys.example` — forced-command template for the CI deploy key.

## First-time install (day 1)

1. Provision Ubuntu 24.04 VPS (Hetzner CPX21 or similar), SSH in as root with a key.
2. Clone: `git clone git@github.com:young-kobe/civic-lens.git /opt/civic-lens`.
   The checkout stays on the box for the compose file + unit files; code runs
   from the GHCR images, not this tree.
3. Run `/opt/civic-lens/deploy/install.sh`. Installs Docker + postgresql-client,
   creates the `civic-lens` and `deployment` users, pins the app UID, writes
   the sshd drop-in, systemd units, and refreshes the firewall.
4. Fill in `/etc/civic-lens.env`:
   - `POSTGRES_USER=` / `POSTGRES_PASSWORD=` — the compose file hard-fails
     every operation if these are unset (`:?` interpolation).
   - `CIVIC_DATABASE_URL=postgresql://civic:<password>@postgres:5432/civic_lens`
     — the CONTAINER form; see DSN vantage points above.
   - `CIVIC_RAW_STORE_DIR=/var/lib/civic-lens/data/raw/sha256` — ABSOLUTE.
     A relative value silently admits zero news documents.
   - `CIVIC_NARRATIVE_EMBEDDING_MODEL=` — REQUIRED (e.g.
     `gemini-embedding-001`); narrative clustering refuses to start without it.
   - `CIVIC_ANALYZE_CONCURRENCY=` / `CIVIC_PG_POOL_MAX=` — pipeline
     concurrency; keep pool above concurrency.
   - `CIVIC_ADMIN_TOKEN=` — generate with `python -c "import secrets; print(secrets.token_urlsafe(48))"`.
   - `CIVIC_LLM_BACKEND=` — `gemini` | `ollama` | `openai_compat`, plus its
     key/URL settings.
   - `CIVIC_API_HOST=127.0.0.1`.
   - `X_BEARER_TOKEN=` — rotated X API token.
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
    app for civic-lens.info, paths `/api/v1/run/*`, `/api/v1/review/*`.

## Redeploy (every push to main)

GitHub Actions runs `.github/workflows/deploy.yml`: an `images` job builds and
pushes the three images to GHCR (`:latest` + `:sha` tags), gating a `deploy`
job that SSHes in as `deployment` and invokes `sudo deploy.sh` — which pulls
the new images and restarts the stack. The forced-command restriction on the
authorized_keys line means that's the only thing the key can do — no shell, no
port-forward. `:sha` tags give real rollback (`docker compose ... pull` a prior
tag) that on-box git builds never did.

## Recovery

- **A service crashes**: `docker compose logs -f api` (or `caddy`/`postgres`).
  Fix code, push, let CI rebuild + redeploy; or `docker compose up -d` to
  re-pull a known-good tag.
- **DB loss**: stop the stack, restore the most recent `backup.sh` dump from
  `/var/lib/civic-lens/backups/` (or R2):
  `age -d -i <key> civic_lens-<stamp>.dump.age | pg_restore -h 127.0.0.1 -U <user> -d civic_lens --clean --if-exists`,
  restart.
- **Cloudflare IP range rotation blocks legit traffic at the origin**:
  `bash /opt/civic-lens/deploy/firewall.sh` — the timer picks this up monthly,
  but you can re-run it manually.
- **Locked out of SSH**: Hetzner rescue mode, mount the disk, edit
  `/etc/ssh/sshd_config.d/99-civic-lens.conf` or reset `~/.ssh/authorized_keys`.
