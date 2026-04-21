# Walkthrough 049 — Launch

Status: **Live at https://civic-lens.info since 2026-04-21.**

Captures the last-mile fixes + operational changes made during the cutover and tails off with the live-ops posture.

## Cutover sequence

1. Hetzner CPX21 VPS provisioned (Ashburn, Ubuntu 24.04, 4 GB RAM). Hardening + system prep from `deploy/install.sh`.
2. DNS: `civic-lens.info` + `www.civic-lens.info` pointed at `87.99.141.180` via Cloudflare (proxied, Full-Strict TLS).
3. `/etc/civic-lens.env` populated (`CIVIC_ADMIN_TOKEN` generated, `CIVIC_GEMINI_API_KEY` rotated + billing cap $15, `X_BEARER_TOKEN` rotated, `CIVIC_API_HOST=127.0.0.1`, `CIVIC_RUN_ANALYSIS_ON=all`).
4. Cloudflare Origin CA cert + Authenticated Origin Pulls CA bundle installed in `/etc/caddy/`; APO toggled on globally.
5. GitHub Actions deploy key installed into `/home/deployment/.ssh/authorized_keys` with forced-command; `DEPLOY_HOST`, `DEPLOY_SSH_KEY`, `DEPLOY_KNOWN_HOSTS` saved as repo secrets.
6. First deploy via CI pipeline (`.github/workflows/deploy.yml`) — builds Go binary, Python venv, UI dist, runs migrations, reloads API.
7. Cloudflare Access application `Civic Lens Admin` created; paths `/api/v1/run/*`, `/api/v1/review/*`, `/api/v1/cache-status`; policy Allow Emails = `kobe.tyler.young@gmail.com`; default One-time PIN auth (Google SSO deferred).

## Bugs caught + fixed during cutover

These landed as small commits between walkthroughs 048 and this one. The audit and hardening work (047) was right; these were execution details we only saw in the real environment.

- **`/health` served SPA HTML** — Caddy routed everything non-`/api/*` to static files; `/health` is mounted at the FastAPI app root. Added `/health` to the reverse-proxy matcher.
- **Authenticated Origin Pulls syntax** — Caddy 2.6 (Ubuntu 24.04 package) uses `trusted_ca_cert_file`, not the 2.8+ `trust_pool file`. Fixed Caddyfile.
- **Deploy scripts not executable** — `git update-index --chmod=+x` applied to `deploy/*.sh` so `sudo` can invoke `deploy.sh` via the CI forced-command.
- **Migrations symlink** — `civic-ingest migrate` resolves migration path relative to the DB's directory. Added `ln -sfn /opt/civic-lens/data/migrations /var/lib/civic-lens/data/migrations` to both `install.sh` and `deploy.sh` so the link survives fresh installs and redeploys.
- **DB ownership drift** — `deploy.sh` now `chown`s `DB_DIR` + `DB_PATH` back to `civic-lens:civic-lens` after `civic-ingest migrate` runs as root.
- **systemd `ProtectSystem=strict` blocked `data/raw/` writes** — `/opt` is read-only under the hardened units. Changed `WorkingDirectory` on crawl/reddit/x/analyze units from `/opt/civic-lens` to `/var/lib/civic-lens` so relative paths in `seeds.yaml` resolve into the `ReadWritePaths` tree.
- **starlette CVE-2025-54121 + CVE-2025-62727** — surfaced by `pip-audit` in CI after the initial push. Bumped `fastapi>=0.120,<1` which pulls `starlette>=0.49.1`; added explicit `starlette>=0.49.1,<2` floor.
- **`test_cache.py` imported `pytest`** under unittest runner in CI → ImportError. Rewrote the file in plain `unittest.TestCase` style.
- **`test_llm_engines.py` case-sensitive assertion** — analyzer returns lowercased entity strings by design; test asserted title case. Fixed to lowercase.
- **`test_polling.py` ran real 9-second sleeps** between retry attempts — mocked `time.sleep`.
- **Noisy CI log output** — added `analysis/tests/__init__.py` that sets root logger to WARNING.
- **LLM schemas rejected by Gemini** — `minimum`/`maximum` keywords on number fields aren't in Gemini's JSON schema subset. Stripped them from `analysis/src/llm/schemas.py`; confidence bounds are documented in the prompts and clamped in the engine code instead.
- **`gemini-2.0-flash` deprecated** for new users. Bumped default to `gemini-2.5-flash` in `common/settings.py` and via `CIVIC_GEMINI_MODEL=gemini-2.5-flash` in `/etc/civic-lens.env`.
- **Reddit 403 from Hetzner IP** — datacenter IPs blocked by Reddit's anti-scraping. `civic-lens-reddit.timer` disabled pre-launch. Re-enabling needs OAuth-based API access (out of scope for v1; filed as follow-up).
- **Realclearpolling.com 403 from Hetzner** — same issue on a different host. `polling_gop` snapshot stays at 0. Non-blocking; polling data was a nice-to-have.
- **Crawl timer needed `ingest` first** — `civic-ingest crawl` drains the frontier but doesn't populate it. The service now runs `civic-ingest ingest` as a first `ExecStart` line so each scheduled cycle refreshes RSS seeds before crawling.

## Current timer schedule

| Timer | Schedule (UTC) | What it does |
|---|---|---|
| `civic-lens-crawl.timer` | every 4h + 5min jitter | `ingest` then `crawl` for 10 min |
| `civic-lens-reddit.timer` | **disabled** | Hetzner IP blocked by Reddit |
| `civic-lens-x.timer` | daily at 04:00 + 15min jitter | Pull ~2,400 tweets/mo capped by `x_api_budget` |
| `civic-lens-analyze.timer` | every 6h at :15 | Full LLM pipeline |
| `civic-lens-backup.timer` | daily at 03:30 | SQLite `.backup` + optional rclone to R2 |
| `civic-lens-firewall-refresh.timer` | monthly, 1st at 04:00 | Re-pin UFW to current Cloudflare IP ranges |

## First real data (snapshot at launch)

From the first successful pipeline run after the Gemini billing+model fix:
- **docs loaded**: 59 (after 10 "too old" and 77 "non-political" filtered)
- **sentiment/favorability**: 59 each, all LLM-backed
- **bot detection**: 59 (news articles; social-media scoring kicks in once X ingests)
- **claims**: 59, producing **115 claim rows**
- **propaganda**: 59, with **11 docs flagged** under at least one technique
- **narrative clustering (jaccard)**: **102 narratives created from 115 claims, 111 assignments**
- **citations (deterministic)**: 13 edges between docs

Cache sizes: `sentiment_24h=32 docs-aggregated`, `narratives_24h=84 narrative-items`, `propaganda_24h=11`, `bot_activity=1` (social-media-only aggregator had no X data yet), `geo_sentiment_*=0` (same).

## Follow-ups (not deploy-blockers)

Tracked in issues / tasks for later walkthroughs:

- **Re-add Reddit OAuth** so datacenter IP ingest works. Removed as dead code in walkthrough 044 because no account was connected; we now have the operational need. Needs: Reddit developer app, OAuth token flow in Go, refresh token handling.
- **Replace `google.generativeai` with `google.genai`** — the SDK we use is EOL and emits a `FutureWarning`. Mechanical rename + API shape adjustment; not urgent while 2.5-flash still works via the legacy client.
- **UI `/api/v1/review/queue` 403 path** — when Cloudflare Access session cookie is missing, `fetch()` can't follow CF's cross-origin redirect to the login page. Workaround today: user visits any admin URL in a top-level nav first to seed the session cookie, then returns to the SPA. Proper fix: detect 401/network-error on admin endpoints and top-level navigate to the endpoint, letting CF do its login flow.
- **Bot detection on news articles** is a scope mismatch — `HybridBotDetector` was designed for social-media signals (follower ratios, post cadence). News-doc rows get heuristic fallback labels. The aggregator already filters news out of the bot metrics; the DB rows are harmless but wasteful. Eventually: skip bot task for news `source_type` in job_runner.
- **Replace deprecated SDK warning + retry backoff** for Gemini rate-limit cases.

## Verification

- `curl https://civic-lens.info/health` → 200 JSON with `db_reachable: true, cache_dir_exists: true`.
- `curl https://civic-lens.info/api/v1/sentiment?window=24h` → populated structure, non-zero `volume`.
- Cloudflare Access gate test: direct curl to `/api/v1/cache-status` → 302 redirect to `civic-lens-admin.cloudflareaccess.com/cdn-cgi/access/login/...` → one-time PIN via email → after login, origin returns 401 (no `X-Admin-Token`), proving both gates fire.
- Cloudflare Authenticated Origin Pulls bypass test: `curl --resolve civic-lens.info:443:87.99.141.180 https://civic-lens.info` from outside → TLS handshake fails with client-certificate-required.
- CI deploy pipeline: last successful run `2026-04-21 13:44:43 UTC` (schemas + starlette + model bumps).
- Scheduled timers enabled; first unattended cycle expected 2026-04-21 16:15 UTC (analyze).
