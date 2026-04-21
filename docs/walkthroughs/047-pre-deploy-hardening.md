# Walkthrough 047 — Pre-Deploy Hardening

Status: **landed + launched 2026-04-21**. See `049-launch.md` for the cutover log and the small fixes that came up during the real deploy. Consolidates remediation of the 04_20 consolidated security audit (`docs/audits/04_20_2026_pre_deploy_consolidated.md`). Goal: ship `civic-lens.info` with zero open HIGH/CRITICAL security items.

Everything here lands in five sequential PRs against branch `19-harden-for-v1-deployment`, then one merge to `main` that kicks off the first CI/CD deploy. The deploy-day dashboard work (Cloudflare Access, Authenticated Origin Pulls, Gemini key rotation) is not code but tracked here so the sequence is preserved in one place.

## Scope — what landed in this walkthrough

This single walkthrough covers all five PRs because they are one coordinated hardening pass with a shared goal (pre-deploy safety) and the audit doc they remediate is one file. Splitting into five walkthroughs would duplicate the narrative.

- **PR-A** — Python / API hardening (audit §§1.2-1.9): query+body validation, `/api/narratives` cap, `SnapshotCache._get_path` hardening, dep CVE upgrade + lockfile, `slowapi` rate limits, security-headers middleware, `/api/cache-status` redaction, `api_host` loopback binding.
- **PR-B** — Go ingest hardening (§§1.10-1.11): SSRF-via-redirect guard, RSS/Reddit body size cap.
- **PR-C** — UI hardening (§§1.12-1.13): strip `?admin=<token>` from URL after persist (Referer leak fix), `npm audit` gate in CI.
- **PR-D** — Infra-as-code (§§1.14-1.17): hardened systemd units, Caddyfile with security headers + body cap + structured logs, UFW pinned to Cloudflare IP ranges, SSH `AllowUsers` / `MaxAuthTries` / fail2ban.
- **PR-E** — CI/CD (§1.20): `.github/workflows/deploy.yml` runs tests, `pip-audit`, `npm audit`, then SSH-deploys to VPS via a restricted `deployment` user with a forced-command key.
- **Deploy-day runbook** (§§1.1, 1.18, 1.19): rotate Gemini key + billing cap, wire Cloudflare Access, enable Authenticated Origin Pulls.

## Changes — PR-A (Python / API)

**Input validation — review router (audit §1.2)**
`analysis/src/api/routers/review.py`. `task: str` and `source_type: Optional[str]` on `/review/queue` + `task: Optional[str]` on `/review/stats` were unvalidated strings that flowed into SQL via parameterized placeholders (safe) but without schema enforcement (not). Replaced with module-level `ReviewTask = Literal["sentiment","favorability","bot_detection","claims","propaganda"]` and `ReviewSourceType = Literal["news","reddit_post","reddit_comment","x_post"]`. `limit`, `offset`, `confidence_max` now use `fastapi.Query` with concrete bounds (`limit: 1-100`, `offset: ge=0`, `confidence_max: 0.0-1.0`). `ReviewSubmission` gains `pydantic.Field` caps: `reviewer_id` / `notes` / `human_label` capped at 255 / 2000 / 2000 chars to prevent DB bloat, `human_confidence` clamped to `[0,1]` to prevent accuracy-stat corruption, `is_correct` as `Literal[0, 1]` to reject anything else.

**`/api/narratives` limit cap (audit §1.3)**
`analysis/src/api/routers/data.py`. `limit: int = 20` → `limit: int = Query(default=20, ge=1, le=500)`. Above 100 still goes to the live aggregator; 500 is the hard ceiling so a pathological `?limit=1_000_000` can't run unbounded SQL.

**`SnapshotCache._get_path` hardening (audit §1.4)**
`analysis/src/common/cache.py`. Old sanitizer replaced `/` and `\` but passed `..` and `\x00`. New method rejects both upfront, resolves the candidate path, and asserts via `is_relative_to(cache_root)` that the final path stays under `cache_dir`. Three new tests in `test_cache.py` (`test_rejects_dot_dot_traversal`, `test_rejects_null_byte`, `test_path_stays_under_cache_dir`).

**Dependency CVE sweep (audit §1.5)**
`analysis/requirements.txt`. `requests==2.31.0` (CVE-2024-35195) → `>=2.32.2,<3`. `fastapi==0.109.0` → `>=0.115.0,<0.116`. `uvicorn==0.27.0` → `>=0.30.0,<0.35`. `pydantic==2.5.3` → `>=2.10.0,<3`. `pydantic-settings==2.1.0` → `>=2.5.0,<3`. `google-generativeai>=0.8.0` → `>=0.8.3`. `PyYAML>=6.0` → `>=6.0.1`. Added `slowapi>=0.1.9,<0.2` for PR-A §1.6. CI will run `pip-audit` to catch future transitive CVEs (wired in PR-E).

**slowapi rate limits (audit §1.6)**
New module `analysis/src/api/rate_limits.py` defines a singleton `Limiter` with a Cloudflare-aware `key_func` (prefers `CF-Connecting-IP`, falls back to first `X-Forwarded-For` entry, then socket peer). Default cap 120/min per IP via `SlowAPIMiddleware` in `server.py`. Per-route overrides: `/api/run/etl` and `/api/run/full-pipeline` at 1/hour (stacks on top of the existing 60s shared-state cooldown in `dependencies.py`), `/api/review/submit` at 30/hour, `/api/narratives` and `/api/geo-sentiment` at 10/minute. Each decorated handler now takes `request: Request` as required by slowapi. 429 responses via the built-in `_rate_limit_exceeded_handler`.

**Security-headers middleware (audit §1.7)**
New `SecurityHeadersMiddleware` in `server.py` emits `Content-Security-Policy` (self-only script/connect, Google Fonts allowed for style + font), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: camera=(), microphone=(), geolocation=(), interest-cohort=()`. HSTS intentionally omitted — Cloudflare sets it at the edge.

**`/api/cache-status` redaction (audit §1.8)**
`analysis/src/api/routers/admin.py`. Response previously returned `cache_dir: settings.cache_dir` (absolute path on the VPS). Now returns snapshot metadata only.

**`CIVIC_API_HOST` loopback binding (audit §1.9)**
`.env.example` now documents that production must set `CIVIC_API_HOST=127.0.0.1` so uvicorn is only reachable through Caddy on the same host. Default stays `0.0.0.0` for dev convenience.

## Changes — PR-B (Go ingest)

**SSRF-via-redirect guard (audit §1.10)**
New `ingest/internal/httpclient/safehost.go` defines `validateURL()`: rejects schemes other than http/https and literal IPs that are loopback, link-local, multicast, RFC1918, or unspecified. Also hard-rejects hostnames `localhost`, `*.localhost`, `*.local`. Called in two places in `client.go`: (a) at the top of `Fetch()` to reject a poisoned seeds.yaml URL before the first request, and (b) inside `CheckRedirect` on every hop so a trusted initial host can't 302 us into `169.254.169.254` or `127.0.0.1`. The validator does NOT resolve DNS — that would add a round-trip per fetch; the compensating control for DNS-only private names is the origin firewall + Hetzner egress (documented in the test file). New `safehost_test.go` with 18 positive/negative cases covers scheme blocklist, literal RFC1918/loopback v4 and v6, metadata IPs, and hostname suffixes.

**RSS / Reddit / X body size caps (audit §1.11)**
`ingest/internal/extract/reddit/reddit.go` + `ingest/internal/extract/x/x.go` — two `io.ReadAll(resp.Body)` calls each wrapped with `io.LimitReader(..., 10<<20)`. Matches the existing 10 MB cap in `httpclient.doFetch`. RSS extraction does not issue HTTP directly — it operates on already-capped bodies from the shared fetcher, so no change needed there.

Tests: `go test ./...` clean; new `TestValidateURL` adds 18 table cases.

## Changes — PR-C (UI)

**Strip `?admin=<token>` from URL after capture (audit §1.12)**
`ui/src/App.tsx`. The `ADMIN_MODE` IIFE now runs `history.replaceState({}, '', <cleanUrl>)` after persisting/removing the token. Without this, the token rides on every outbound Referer — font asset loads to `fonts.gstatic.com`, article links in review rows, any future analytics script. Scrub logic preserves other query params and the fragment. Runs for both `?admin=<token>` (persist path) and `?admin=0` (clear path); no-op when `admin` isn't present so the address bar isn't rewritten on every visit. Typecheck clean.

**npm audit gate (audit §1.13)**
Implemented in PR-E's `.github/workflows/deploy.yml` (not locally in `package.json`): the CI job runs `npm audit --audit-level=high` after `npm ci`, failing the build on any HIGH or CRITICAL advisory. Keeping it CI-only means local `npm run build` isn't blocked by a freshly-announced transitive CVE the maintainer hasn't patched yet — but merges to `main` are.

## Changes — PR-D (Infra-as-code)

New directory `deploy/` holds everything the VPS needs. Flat config — no Ansible, no Terraform — matches the one-box shape the plan commits to.

**systemd units (audit §1.14)** — `deploy/systemd/`:
- `civic-lens-api.service` — always-on uvicorn.
- `civic-lens-{crawl,reddit,x,analyze}.{service,timer}` — scheduled ingest + analysis.
- `civic-lens-backup.{service,timer}` — nightly SQLite .backup + optional age-encrypt + rclone.
- `civic-lens-firewall-refresh.{service,timer}` — monthly Cloudflare IP range re-pin.

Every unit inherits the same hardening set: `NoNewPrivileges`, `ProtectSystem=strict`, `ProtectHome`, `PrivateTmp`, `PrivateDevices`, `ProtectKernelTunables/Modules/ControlGroups/Clock/Hostname/Logs`, `RestrictNamespaces`, `RestrictRealtime`, `RestrictSUIDSGID`, `LockPersonality`, `MemoryDenyWriteExecute`, cap bounding set cleared, `SystemCallFilter` dropping @privileged/@debug/@module/@mount/@obsolete/@reboot/@swap/@raw-io/@cpu-emulation, `ReadWritePaths=/var/lib/civic-lens` only, per-unit `MemoryMax` + `CPUQuota` + `TasksMax`.

The firewall-refresh unit runs as root (needs to touch `ufw`) — it still gets `NoNewPrivileges`, `ProtectHome`, `ProtectKernelTunables/Modules`, `PrivateTmp`. Can't drop root because that's the minimum capability needed for the job; blast radius mitigated by the tight `ExecStart`.

**Caddyfile (audit §1.15)** — `deploy/caddy/Caddyfile`:
- TLS via Cloudflare Origin CA cert + `client_auth mode require_and_verify` backed by Cloudflare's Authenticated Origin Pull CA → direct curl to the VPS IP fails with `tls: client certificate required`.
- JSON access log with rotation (50 MB × 7 files × 30 days), explicit body size cap (1 MB), `reverse_proxy` with dial / header / read / write timeouts.
- `header_up -X-Admin-Token -Cf-Access-Jwt-Assertion` strips both from the proxied request's headers-to-log so `/var/log/caddy/` never contains admin tokens or Access JWTs. `header_up X-Forwarded-For {…Cf-Connecting-Ip…}` forwards CF's client IP header so slowapi can bucket limits correctly.
- Long-cache immutable assets, short-cache index.html, bare `:80` returns 403 (CF handles the HTTPS redirect).
- HSTS + X-Frame-Options + X-Content-Type-Options set at Caddy; CSP + Referrer-Policy + Permissions-Policy come from the FastAPI middleware so `/api/*` responses get them identically to static pages. Deliberately split so the origin enforces the policy even if Caddy is misconfigured.

**Firewall pinned to Cloudflare (audit §1.16)** — `deploy/firewall.sh`:
Idempotent bash script. Fetches `cloudflare.com/ips-v4` + `ips-v6`, strips any prior `# civic-lens-cf` rules (identified by comment), then replays the fresh allowlist via `ufw allow from <cf-cidr>`. Bails if CF returns empty (refuses to lock the admin out). Called monthly by `civic-lens-firewall-refresh.timer`; safe to run manually any time.

**SSH hardening (audit §1.17)** — `deploy/sshd_hardening.conf`:
Drop-in at `/etc/ssh/sshd_config.d/99-civic-lens.conf` on Ubuntu 24.04. Re-asserts key-only auth, adds `AllowUsers root deployment`, `MaxAuthTries 3`, `LoginGraceTime 30`, `ClientAliveInterval 300`, and disables every forwarding mechanism + unused auth methods (PAM-only Kerberos/GSSAPI/ChallengeResponse off). `deploy/fail2ban.local` lands at `/etc/fail2ban/jail.d/civic-lens.local` with `maxretry=3, findtime=10m, bantime=1h`.

**Backup + rotation (audit §6 MEDIUM → pulled forward)** — `deploy/backup.sh`:
`sqlite3 .backup` to a tempfile then age-encrypt to `$BACKUP_AGE_RECIPIENT` (public key stays on VPS; private key lives off-box). Optional `rclone copy` to `$BACKUP_RCLONE_REMOTE`. 14-day local retention via `find -mtime +14 -delete`. Falls back to plaintext with a loud warning if `age(1)` isn't installed or the recipient isn't set.

**Install + deploy split**:
- `deploy/install.sh` — runs once on day-one. Installs packages, creates `civic-lens` + `deployment` users, writes sshd drop-in + fail2ban + Caddyfile + systemd units, runs `firewall.sh`. Writes `/etc/civic-lens.env` skeleton from `.env.example` with 0600 perms, but leaves it for the operator to fill in.
- `deploy/deploy.sh` — runs every push. `git fetch && git reset --hard origin/main`, build Go binary, upsert Python venv + reinstall requirements, `npm ci && npm run build`, run migrations, chown, `systemctl reload-or-restart civic-lens-api`. Never touches env file, Caddyfile, sshd config.

**Deployment user isolation**:
- `deployment` is a regular user with an empty shell usage — its only SSH authorized_keys line has a forced command (template in `deploy/authorized_keys.example`) that runs `sudo /opt/civic-lens/deploy/deploy.sh` and nothing else, with `no-pty,no-agent-forwarding,no-port-forwarding,no-X11-forwarding,no-user-rc`.
- A sudoers drop-in at `/etc/sudoers.d/civic-lens-deploy` grants `deployment` NOPASSWD on exactly `/opt/civic-lens/deploy/deploy.sh` — no escalation elsewhere.
- GitHub Actions holds only the deploy key (key compromise → attacker can redeploy the repo but can't pivot).

**Docs**: `deploy/README.md` contains the day-one checklist + operating notes, pointing back to the plan doc for rationale.

## Changes — PR-E (CI/CD)

Two GitHub Actions workflows under `.github/workflows/`.

**`ci.yml`** — runs on every push and PR. Three parallel jobs:
- `python` — installs `analysis/requirements.txt` with pip cache; runs `pip-audit --strict` to block on any known HIGH/CRITICAL advisory in the pinned deps; runs the unittest suite with `PYTHONPATH` set to the repo root (matches the dev invocation in CLAUDE.md).
- `go` — `go vet`, `go test`, and a `go build -trimpath -ldflags "-s -w"` of `civic-ingest` to catch compile failures that `go test` doesn't reach.
- `ui` — `npm ci`, `npm audit --audit-level=high` (gates on HIGH / CRITICAL — the two remaining MODERATE esbuild dev-server advisories are accepted because they are dev-only and upgrading past vite 5 is a breaking change out of scope for v1), `npm run typecheck`, `npm run build`.

**`deploy.yml`** — runs only on push to `main`, serialized via concurrency group. Two jobs:
- `gate` — mirrors the CI checks. Redundant with the PR gate but prevents a direct-to-main merge from shipping broken code (belt-and-suspenders).
- `deploy` — SSHs to `deployment@$DEPLOY_HOST` using `$DEPLOY_SSH_KEY` (GH environment secrets, scoped to the `production` environment for manual approval workflows later) and `$DEPLOY_KNOWN_HOSTS` for host-key pinning. The forced-command on the VPS authorized_keys runs `sudo /opt/civic-lens/deploy/deploy.sh` and exits — no arguments, no shell access.

**UI dependency overrides** — `ui/package.json`:
Added `overrides` block pinning `d3-color>=3.1.0`, `rollup>=4.59.0`, `lodash>=4.17.24`. The d3-color override resolves the HIGH ReDoS CVE transitively pulled in via `react-simple-maps@3.0.0` without requiring a breaking-change downgrade to `react-simple-maps@1.0.0`. npm's `overrides` patches the nested copies inside `d3-transition` and `d3-zoom`.

**Lockfile** — `ui/package-lock.json` committed for the first time. `npm ci` in CI needs it; previously the UI had no lockfile, which meant every build re-resolved deps against whatever floated on npm at build time — unacceptable for reproducible releases.

**CI-enforced audit results**:
- `pip-audit`: clean against current pinned Python deps (requests 2.32+, fastapi 0.115+, pydantic 2.10+, uvicorn 0.30+, slowapi 0.1.9+).
- `npm audit --audit-level=high`: clean (2 MODERATE esbuild dev-server findings remain, below threshold).
- `go vet`: clean.

## Deploy-day runbook

Followed at cutover, in order. Each step is human-in-the-loop — not code — which is why it's in the walkthrough rather than the repo.

1. **Rotate secrets** (audit §1.1). Google AI Studio: regenerate Gemini key, set a billing cap ≤$20/month on the GCP project. X developer portal: rotate Bearer token. Store both plus a fresh `CIVIC_ADMIN_TOKEN` (from `python -c "import secrets; print(secrets.token_urlsafe(48))"`) in `/etc/civic-lens.env` on the VPS, chmod 600.

2. **Cloudflare Authenticated Origin Pulls** (audit §1.19). SSL/TLS → Origin Server → "Create Certificate" (15-year cert, hostnames `*.civic-lens.info` + `civic-lens.info`). Download both PEMs. Install on VPS:
   - `/etc/caddy/origin.crt` — cert, 0644 root:root
   - `/etc/caddy/origin.key` — key, 0600 caddy:caddy
   - `/etc/caddy/cloudflare-authenticated-origin-pull.pem` — download the pinned CA bundle from Cloudflare docs.
   Toggle on "Authenticated Origin Pulls" (same page). `systemctl reload caddy`. Verify: `curl -I https://civic-lens.info` succeeds (proxied), `curl -I --resolve civic-lens.info:443:87.99.141.180 https://civic-lens.info` fails with client-cert-required.

3. **Cloudflare Access** (audit §1.18). Zero Trust → Access → Applications → Add Self-hosted → app name "Civic Lens Admin". Domain `civic-lens.info`, paths `/api/v1/run/*`, `/api/v1/review/*`, `/api/v1/cache-status`. Policy: Action Allow, Include Emails = `kobe.tyler.young@gmail.com`. Session 24h. Under Settings → Authentication enable Google as a login method.

4. **Smoke tests from fresh browser profile**:
   - `https://civic-lens.info` — public UI loads, no auth prompt.
   - `https://civic-lens.info/api/v1/sentiment` — 200, JSON, has the security headers (`X-Frame-Options`, `Content-Security-Policy`, etc.).
   - `https://civic-lens.info/api/v1/cache-status` — redirects to Cloudflare login (Access kicks in). After SSO: 401 without `X-Admin-Token`, 200 with the token.
   - `https://civic-lens.info?admin=<token>` — visit, check localStorage has `civic_admin_token`, check URL is scrubbed to `/`. Navigate to Review tab — loads.
   - Slam `/api/v1/run/full-pipeline` with `for i in 1..5; do curl -X POST ...; done` — expect 1× 200, 4× 429 (slowapi caps at 1/hour).

5. **Enable scheduled jobs**: `systemctl enable --now civic-lens-{crawl,reddit,x,analyze,backup,firewall-refresh}.timer`.

6. **Post-deploy lockdown review** (one week after): confirm `journalctl -u civic-lens-api` contains no admin tokens or tracebacks. Check `/var/log/caddy/civic-lens.log` — zero `X-Admin-Token` or `Cf-Access-Jwt-Assertion` leakage (should have been stripped in the `header_up` directives).

## Verification

**PR-A**:
- `py_compile` clean on all 5 modified Python files.
- `test_cache.py` — 13/13 pass, including 3 new traversal-rejection tests.
- Other suites blocked locally by a partially-upgraded venv from the pydantic mid-install hiccup; CI (PR-E workflow) runs on a fresh venv and is the authoritative gate.

**PR-B**:
- `go test ./...` clean. `TestValidateURL` adds 18 cases covering schemes, RFC1918 v4/v6, loopback v4/v6, link-local, multicast, unspecified, and `*.localhost`/`*.local` hostnames.
- `go vet` clean.

**PR-C**:
- `npm run typecheck` clean.
- URL-scrub visual check will happen during the deploy-day smoke (§4 above).

**PR-D**:
- No runtime artifact; review focused on configuration correctness. Each unit file lints via `systemd-analyze verify` during install.sh (NOT currently, would be a follow-up enhancement). The drop-in paths match Ubuntu 24.04 conventions.

**PR-E**:
- `ci.yml` and `deploy.yml` not dry-runnable locally. First push to `19-harden-for-v1-deployment` triggers them; their output is the first real test.
- `npm audit --audit-level=high` exits 0 locally after `overrides` applied; confirmed the 5 HIGH CVEs (d3-color × 3, rollup, lodash) are resolved transitively.
