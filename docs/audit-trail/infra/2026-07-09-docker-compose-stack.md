# 2026-07-09 — Containerize the stack: GHCR images + Docker Compose + Litestream

The production runtime is now a Docker Compose stack. Three images are built
in GitHub Actions and pushed to GHCR (`civic-lens-ingest`,
`civic-lens-analysis`, `civic-lens-web`); the box only ever pulls — no Go,
Node, Python-venv, or Caddy packages on the host, and no compilation during
deploys. This replaces the systemd-native layout where `deploy.sh` rebuilt
the binary/venv/UI in `/opt/civic-lens` and a host Caddy served traffic.
Scheduling stays on host systemd timers, which now exec
`docker compose run --rm <service>` so the existing `OnFailure=` alert
plumbing keeps watching real exit codes.

## What shipped

- `docker-compose.yml` (repo root) — always-on `caddy` (host network),
  `api` (uvicorn, publishes `127.0.0.1:8000` only), `litestream`; one-shot
  `ingest` + `analyze` under the `jobs` profile, only ever invoked via
  `compose run --rm`. Resource caps (`mem_limit`/`cpus`) mirror the old
  systemd `MemoryMax`/`CPUQuota` values.
- Data contract: the host dir `/var/lib/civic-lens` is bind-mounted at the
  identical path in every container with `working_dir` set to it, so the
  absolute paths in `/etc/civic-lens.env` and relative paths in `seeds.yaml`
  resolve exactly as they did under systemd. Host tooling (backup.sh,
  sqlite3 spot checks) is unaffected. WAL over a same-host bind mount is
  safe; the Windows-mount case is the documented exception
  (`docker-compose.override.example.yml`).
- `ingest/Dockerfile` + `ingest/docker-entrypoint.sh` — static Go build
  (CGO_ENABLED=0, alpine, ~20 MB). civic-ingest resolves migrations as
  `dir(dbPath)/migrations`; the entrypoint materializes the image's copy
  beside the DB on every run, replacing the old symlink into `/opt`.
- `analysis/Dockerfile` — one python:3.12-slim image shared by `api` and
  `analyze` (identical deps; only the compose `command` differs). Code at
  `/app/analysis` with committed `data/` beside it so `project_root` joins
  and `context_seeds` `parents[3]` resolution work unchanged.
- `deploy/docker/Dockerfile.web` — `ui/dist` baked into caddy:2.10 at
  `/srv/ui`; the Caddyfile ships in the image, TLS material stays on the
  host bind-mounted read-only. Caddyfile updated to the Caddy 2.8+
  `trust_pool file` spelling (was 2.6 `trusted_ca_cert_file`).
- Networking/firewall: caddy runs with `network_mode: host` because
  Docker-published ports bypass ufw and the firewall pins :80/:443 to
  Cloudflare ranges — host networking preserves those semantics; the API is
  loopback-published only.
- Containers run as UID/GID 10001 (`APP_UID`/`APP_GID` build args) matching the
  `civic-lens` host user (`install.sh` pins it via `groupadd`/`useradd`) so
  bind-mount writes need no chown. 10001 is deliberately above the host
  system-UID range: the initial 990 collided with `systemd-resolve` on stock
  Ubuntu, and `adduser --system` rejects out-of-range UIDs, hence low-level
  `groupadd`/`useradd`.
- Litestream sidecar (`deploy/litestream.yml`): continuous WAL replication
  of the SQLite DB to a dedicated R2 bucket (10s sync, 24h snapshots, 168h
  retention), creds via `LITESTREAM_*` in `/etc/civic-lens.env`. This is the
  standing SQLite-durability decision landing; the nightly `backup.sh`
  (separate mechanism, separate bucket, age-encrypted) stays as the second
  layer until restore drills are verified for a quarter.
- systemd: `civic-lens-{crawl,analyze,x}.service` exec compose one-shots
  (root for the docker socket; the sandboxing directives moved into
  container isolation + compose caps); `civic-lens-api.service` deleted
  (compose `restart: unless-stopped` owns it); new
  `civic-lens-stack.service` brings the stack up at boot. Timers, alerting,
  backup, and firewall units unchanged.
- CI/CD: go 1.24 everywhere (was pinned 1.22 against a 1.24 go.mod);
  `ci.yml` gains a build-only image job on PRs; `deploy.yml` gains an
  `images` job (buildx, GHCR via GITHUB_TOKEN, `:latest` + `:sha` tags, gha
  layer cache) that gates `deploy`; `deploy.sh` is now
  pull → migrate → unit sync → `up -d` + weekly image prune.

## Why

- Deployment prep: the box should be reproducible from images + env + data
  volume, and on-box npm/pip/go builds contended with live traffic on a
  3-vCPU/4-GB CPX21. `:sha` image tags give rollback that git-reset builds
  never did.
- The Litestream decision (decoupled data volume + continuous replication)
  was standing since the SQLite-vs-Postgres call; containerizing the stack
  was the natural moment to land it.

## Follow-ups

- Cutover runbook + smoke checklist: `docs/todos/containerization.md`.
- Companion entry for the swappable LLM client:
  `../analysis/2026-07-09-openai-compat-llm-client.md`.
