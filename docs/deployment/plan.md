# Civic Lens Deployment Plan — civic-lens.info

Status: draft 2026-04-19. Domain purchased via Cloudflare. Target: public internet.

> **2026-07-09 addendum:** the Docker Compose shape this plan recommends has
> shipped — see `docker-compose.yml` at the repo root and
> `docs/audit-trail/infra/2026-07-09-docker-compose-stack.md` for the system
> as it is now (GHCR-built images, systemd timers exec `docker compose run
> --rm`, Litestream replication). Sections below describing on-box builds and
> a host Caddy package are superseded.

## Recommendation

**Single small VPS (Hetzner CX22 or DigitalOcean Basic) running everything via Docker Compose + systemd timers, Caddy for TLS, Cloudflare in front.** One box, one SSH login, one `git pull && deploy.sh` to redeploy. ~$10-17/mo all-in.

Why not the alternatives:
- **Split PaaS (Option B)** adds moving parts (persistent volumes on Fly, scheduled cron on Railway or GitHub Actions, separate build pipeline for the UI) with no offsetting capability gain.
- **Cloudflare-only (Option C)** is a non-starter: Workers can't run the Python pipeline, D1 doesn't hold the raw-content filesystem, R2 doesn't speak SQL. The only piece that fits is the static UI — do that as a later optimization, not an architecture.

Shape of the app is "a scheduled pipeline writing to disk, plus a read-only JSON API and a static SPA." That shape wants a box, not a platform.

## 1. What You're Deploying

Honest characterization: **a scheduled data-analysis pipeline with a thin read-only API and a static SPA on top.** Not a web app in the interactive-CRUD sense. At request time the critical path is "read a small JSON file from `data/cache/` and return it." Everything heavy happens out-of-band in `job_runner.py`, invoked by the scheduler.

| Component | Runtime | When it runs | Resource profile |
|---|---|---|---|
| `civic-ingest` | Go binary (static) | Scheduled (crawl, reddit, x) | CPU-light, network-heavy, ~20 MB binary |
| `job_runner.py` | Python 3 | Scheduled (every 6h) | Small — 10 packages, ~80 MB install |
| FastAPI `server.py` | uvicorn | Continuous, port 8000 | <200 MB RAM — serves JSON files |
| UI `ui/dist` | Static HTML/JS/CSS | Always on | Nothing |

No torch, no transformers, no faiss, no chromadb. Embedding is outsourced to Ollama; Gemini is a plain HTTP call. This is a small Python service.

Data on disk (current dev box):
- `data/raw/` (content-addressed) — ~1 GB, ~3k files
- `data/civic_lens.db` — 42 MB
- `data/cache/*.json` — 44 KB

Projected annual growth: 5-10 GB total. SQLite on a persistent volume is sufficient. Don't migrate to Postgres at this scale.

## 2. Architecture Options

### Option A — Single VPS (RECOMMENDED)

```
civic-lens.info (Cloudflare DNS, orange cloud, Full Strict TLS)
    |
    v
VPS (Ubuntu 24.04 LTS, Hetzner CX22 ~$5/mo or DO Basic $6-12/mo)
    |--- Caddy :443 (auto Let's Encrypt)
    |     |--- /        -> /var/www/civic-lens/ui/dist (static)
    |     '--- /api/*   -> 127.0.0.1:8000
    |--- systemd: civic-lens-api.service (uvicorn, always-on)
    |--- systemd timer: civic-lens-crawl.timer (every 4h)
    |--- systemd timer: civic-lens-reddit.timer (every 2h)
    |--- systemd timer: civic-lens-x.timer (daily, budget control)
    '--- systemd timer: civic-lens-analyze.timer (every 6h)
Data: /var/lib/civic-lens/data/ (db + raw + cache)
```

- **Cost:** $5-12 VPS + ~$5 Gemini + $0 domain (already bought) + $0 Cloudflare = **$10-17/mo all-in.**
- **Ops complexity:** 2/5. SSH, git pull, deploy script. `journalctl -u civic-lens-api`.
- **Ceiling:** thousands of unique visitors/day before CPU matters (serves static JSON). Ingestion throughput bottlenecks long before the box does.
- **CI/CD:** GitHub Actions on `push: main` -> build, rsync to VPS, `systemctl reload`. ~50 lines of YAML.

**Docker Compose** recommended as the single-file version of the deployment: 2 services (api always-on, ingest one-shot). Systemd timers exec `docker compose run --rm ingest crawl`. Easier box migration if needed.

### Option B — Split PaaS

- **UI:** Cloudflare Pages (free, git-connected, `wrangler pages deploy ui/dist`).
- **API:** Fly.io shared-cpu-1x (256 MB) + Fly Volume (1-10 GB, $0.15/GB/mo). ~$5/mo machine + $1-2/mo volume.
- **Pipeline:** Fly Machines scheduled via cron (preferred), or GitHub Actions cron doing `flyctl ssh console -C "python -m analysis.src.scheduler.job_runner"`.
- **Cost:** ~$12/mo.
- **Ops complexity:** 4/5. Three deployment pipelines, two log surfaces, volume-pinned region.
- **When it wins:** you want the UI on a global CDN today. But with Cloudflare proxying a Hetzner Ashburn box you get 90% of the edge benefit anyway.

### Option C — Fully on Cloudflare

Stretch. Viable slice is UI on Pages, R2 for raw-content backups, but the Python analysis has nowhere to run (Workers is V8; Python Workers beta doesn't run `google-generativeai` the way we use it). A real Cloudflare-only build = 6-12 week rewrite. A realistic **Cloudflare-hybrid** = Option A with UI later moved to Pages.

Skip for v1.

## 3. Data Persistence and Backups

SQLite on the VPS's persistent disk is sufficient. WAL pragma is already set (by `narrative_clusterer.py`). Readers are read-only (FastAPI); writer is `job_runner.py`. Crash-resume is handled by `frontier.RecoverStale` on startup (per CLAUDE.md).

**Backup plan:** nightly `sqlite3 civic_lens.db ".backup /backups/civic_lens-$(date +%F).db"` + rclone to Backblaze B2 or Cloudflare R2 ($0.015/GB/mo for R2; <$1/mo for this dataset). Raw content is sha256-addressed so technically rebuildable, but back it up anyway — crawling is the expensive part.

## 4. LLM Backend in Production

Current `.env` sets `CIVIC_LLM_BACKEND=ollama` pointing at a LAN address (Orin Nano). **That won't work from a VPS.**

- **Option L1 — Gemini (RECOMMENDED).** `gemini-2.0-flash` at $0.10/M input, $0.40/M output. Per-doc bot/sentiment/favorability = ~1k input + 100 output tokens = **<$0.0002 per doc**. At 500 docs/day = ~$3/mo. Flip `CIVIC_LLM_BACKEND=gemini`.
- Embeddings: either wrap Gemini `text-embedding-004` (small client change) or fall back to Jaccard (`CIVIC_NARRATIVE_SIMILARITY_MODE=jaccard`, already supported and tested).
- **Option L2 — Keep Orin Nano as LLM worker.** Cloudflare Tunnel (`cloudflared`) from the Orin gives a stable `https://ollama.civic-lens.info`. No open ports, no dyndns. Free. Caveats: single point of failure, home upload bandwidth ceiling.

**Recommendation:** Gemini + Jaccard clustering for v1. Keep the Ollama tunnel as a zero-marginal-cost fallback architecture for later.

## 5. DNS and TLS for civic-lens.info

Domain on Cloudflare makes this trivial.

```
A     civic-lens.info        <VPS_IP>    Proxied (orange cloud)
A     www.civic-lens.info    <VPS_IP>    Proxied
```

**Use path-based routing, not an `api.` subdomain.** `ui/src/services/api.ts` already hardcodes `const API_BASE = '/api'`. Zero client changes. Same-origin means we can **delete** the CORS middleware entirely (see security audit §3). One TLS cert, one Caddy block.

Cloudflare settings:
- SSL/TLS: **Full (Strict)**.
- **Always Use HTTPS**, **Automatic HTTPS Rewrites**, **HSTS** (max-age 6mo) in Edge Certificates.
- **WAF free tier** + Rate Limiting Rule on `/api/run/*` (10/hour) and `/api/review/*` (30/min per IP) as belt-and-suspenders to the in-app `slowapi` limits.
- **Cloudflare Access** (Zero Trust, free up to 50 users): see §5a below — this is the **primary** admin gate. The origin `X-Admin-Token` check is defense-in-depth.

## 5a. Cloudflare Access — Primary Admin Gate

Why this and not just the `X-Admin-Token` header: a token stored in browser localStorage is visible to any JS running on `civic-lens.info` (XSS, compromised npm dep, hostile browser extension). Cloudflare Access puts Google SSO *in front of origin* so the secret never reaches the browser — Cloudflare issues a short-lived session cookie only it can mint/verify. Keep the origin `X-Admin-Token` gate too, so a Cloudflare bypass (origin IP leak, misconfig) still fails closed.

**Setup** (once, ~10 minutes, Cloudflare Zero Trust dashboard):

1. Cloudflare Zero Trust -> **Access -> Applications -> Add application -> Self-hosted**.
2. Application name: `Civic Lens Admin`. Session duration: 24 hours. Application domain:
   - `civic-lens.info` with path `/api/run/*`
   - `civic-lens.info` with path `/api/review/*`
   - `civic-lens.info` with path `/api/cache-status`
   - (Optional) any URL containing `?admin=` — Access policies can't match query strings directly, so gate the paths only; the localStorage `?admin=<token>` bootstrap is harmless if the admin endpoints themselves are protected.
3. **Policy**: Action = Allow, Include = Emails = `kobe.tyler.young@gmail.com`. (Add more identities later by extending the Include list.)
4. **Identity provider**: enable Google as a login method under **Settings -> Authentication -> Login methods**. Cloudflare provides a default One-time PIN option out of the box — add Google SSO for a better UX.
5. Save. First visit to any protected path redirects to a Cloudflare-hosted login page; after SSO, Cloudflare sets the `CF_Authorization` cookie and forwards to origin.

**Origin-side verification** (optional hardening):

Cloudflare Access adds a `Cf-Access-Jwt-Assertion` header on every forwarded request. You can verify it in FastAPI by fetching Cloudflare's JWKS at `https://<team>.cloudflareaccess.com/cdn-cgi/access/certs` and checking the JWT's `aud` matches your Access Application's AUD tag. Skip this for v1 — the origin `X-Admin-Token` check is already enough defense-in-depth as long as the origin firewall only accepts Cloudflare IP ranges.

**How this affects the UI flow:**

- Admin visits `civic-lens.info/?admin=<token>` -> Cloudflare does not intercept the bare homepage (no Access rule on `/`), so the token persists to localStorage as before.
- First admin API call (e.g. `/api/review/queue`) -> Cloudflare intercepts, prompts Google SSO, sets session cookie, forwards to origin. Origin also verifies `X-Admin-Token`. Both must pass.
- Non-admin users never hit an Access rule because they never call admin endpoints.

## 6. Replacing `run.ps1` on Linux

`run.ps1` is a dev convenience. In production:
- **Systemd units** for always-on API (`.service`) and scheduled pipeline jobs (`.service` + `.timer`).
- **`scripts/run.sh`** as the Linux analogue of `run.ps1` — a bash case-statement that dispatches to `civic-ingest` subcommands or the Python job runner. Don't run PowerShell Core on Linux; no upside.

Representative unit:
```ini
# /etc/systemd/system/civic-lens-analyze.service
[Service]
Type=oneshot
WorkingDirectory=/opt/civic-lens
Environment=PYTHONPATH=/opt/civic-lens
EnvironmentFile=/etc/civic-lens.env
ExecStart=/opt/civic-lens/.venv/bin/python -m analysis.src.scheduler.job_runner
User=civic-lens

# /etc/systemd/system/civic-lens-analyze.timer
[Timer]
OnCalendar=*-*-* 00,06,12,18:15:00
Persistent=true

[Install]
WantedBy=timers.target
```

## 7. Required Code Changes Before Cutover

Short list. None are big.

**Required (deploy will break without these):**
1. **Rotate secrets.** Gemini + X Bearer. Assume compromised. Store in `/etc/civic-lens.env` (0600, owned by `civic-lens` user).
2. **Lock CORS** (`server.py:38`) — delete or allowlist. Recommended: delete (same-origin).
3. **`db_path` portability.** Currently relative to cwd. Set `CIVIC_DB_PATH` absolute in prod env file; or resolve absolute in `settings.py`.
4. **Switch `CIVIC_LLM_BACKEND=gemini`** or set up the Ollama tunnel.

**Strongly recommended before public launch (see security audit for full list):**
5. **Auth on `/api/run/*` and `/api/review/submit`** — shared-secret header or Cloudflare Access.
6. **`slowapi` rate limits** on the same endpoints.
7. **Validate every query param** with `Literal[...]` / `Query(..., le=100)`.
8. **Harden `SnapshotCache._get_path`** — reject `..`, assert containment.
9. **Gate or redact `/api/review/queue`** (leaks 1200-char raw text).
10. **Upgrade `requests==2.31.0`** (CVE-2024-35195); commit a lockfile.
11. **Security-headers middleware.**
12. **Real `/health`** — touch cache + DB.

**Not required to deploy:** the general-audit LOC-reduction findings, the Ollama 0-100 confidence bug, schema refactors.

## 8. Day-1 Deploy Checklist

Assumes: Hetzner/DO Ubuntu 24.04 VPS provisioned, IP known, Cloudflare DNS already owns the domain, SSH'd in as root, created `civic-lens` user.

**Step 1 — Prepare box (15 min):**
```bash
apt update && apt install -y git caddy python3.12-venv golang-go nodejs npm sqlite3 ufw
ufw allow 22 && ufw allow 80 && ufw allow 443 && ufw enable
mkdir -p /opt/civic-lens /var/lib/civic-lens/data /etc/civic-lens
chown -R civic-lens:civic-lens /opt/civic-lens /var/lib/civic-lens
```

**Step 2 — DNS (5 min, Cloudflare dashboard):**
- `A civic-lens.info -> <VPS_IP>` proxied
- `A www.civic-lens.info -> <VPS_IP>` proxied
- SSL/TLS: Full (Strict). Always Use HTTPS: on. HSTS: on.

**Step 3 — Secrets (5 min):**
- Rotate Gemini key in Google AI Studio.
- Rotate X Bearer token.
- Write `/etc/civic-lens.env` (chmod 600, `civic-lens:civic-lens`):
```
CIVIC_ENVIRONMENT=production
CIVIC_DB_PATH=/var/lib/civic-lens/data/civic_lens.db
CIVIC_CACHE_DIR=/var/lib/civic-lens/data/cache
CIVIC_LLM_BACKEND=gemini
CIVIC_LLM_ENABLED=true
CIVIC_GEMINI_API_KEY=<new_key>
CIVIC_NARRATIVE_SIMILARITY_MODE=jaccard
# Without this, the setting defaults to "social_media" and news docs are
# never analyzed -> the dashboard's news column stays empty.
CIVIC_RUN_ANALYSIS_ON=all
X_BEARER_TOKEN=<new_token>
CIVIC_API_HOST=127.0.0.1
CIVIC_API_PORT=8000
CIVIC_ADMIN_TOKEN=<long_random>
```

**Step 4 — Clone + build (10 min, as civic-lens user):**
```bash
git clone https://github.com/young-kobe/civic-lens /opt/civic-lens
cd /opt/civic-lens
cd ingest && go build -o /opt/civic-lens/civic-ingest ./cmd/civic-ingest && cd ..
python3 -m venv .venv && .venv/bin/pip install -r analysis/requirements.txt
cd ui && npm ci && npm run build && cd ..
./civic-ingest migrate --db /var/lib/civic-lens/data/civic_lens.db
```

**Step 5 — systemd units (15 min, as root):**
Create `civic-lens-api.service`, `civic-lens-{crawl,reddit,x,analyze}.{service,timer}`.
```bash
systemctl daemon-reload
systemctl enable --now civic-lens-api civic-lens-*.timer
```

**Step 6 — Caddy (5 min):**
`/etc/caddy/Caddyfile`:
```
civic-lens.info, www.civic-lens.info {
    encode gzip zstd
    handle /api/* {
        reverse_proxy 127.0.0.1:8000
    }
    handle {
        root * /opt/civic-lens/ui/dist
        try_files {path} /index.html
        file_server
    }
}
```
`systemctl reload caddy`. Let's Encrypt cert issues on first hit.

**Step 7 — First pipeline run + smoke (10 min):**
```bash
systemctl start civic-lens-crawl    # tail: journalctl -fu civic-lens-crawl
systemctl start civic-lens-analyze  # tail: journalctl -fu civic-lens-analyze
curl https://civic-lens.info/api/cache-status
```
Open `https://civic-lens.info` — UI should populate.

**Step 8 — Wire Cloudflare Access before sharing the URL (10 min):**
See §5a for full steps. Short version:
1. Cloudflare Zero Trust -> Access -> Applications -> Add self-hosted.
2. Domain `civic-lens.info`, paths `/api/run/*`, `/api/review/*`, `/api/cache-status`.
3. Policy: Allow, emails = `kobe.tyler.young@gmail.com`.
4. Add Google as a login method under Settings -> Authentication.
5. Verify: `curl https://civic-lens.info/api/cache-status` from a fresh browser profile should now redirect to a Cloudflare login page instead of returning 401/503 directly.

**Step 9 — Post-deploy hardening (30 min):**
- Nightly `sqlite3 .backup` to a second disk or R2.
- Write `/opt/civic-lens/deploy.sh`: `git pull && go build ... && npm ci && npm run build && systemctl reload civic-lens-api`. Wire GitHub Actions on `push: main` to SSH in and run it.
- Confirm origin firewall accepts only Cloudflare IP ranges (`https://www.cloudflare.com/ips/`). Without this, anyone who finds the VPS IP can hit `/api/*` directly and bypass Access.

**Total time to first deploy: ~100 minutes.** Redeploys after = `git push` -> ~60s.

## Operational Reality

~$10-17/mo. One box. One compose file or four systemd units. One Caddyfile. The remaining security items ship incrementally after the site is live — but items 1-4 in §7 are hard blockers and items 5-9 should land before the URL is shared publicly.
