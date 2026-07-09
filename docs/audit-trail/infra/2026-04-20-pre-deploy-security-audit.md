# 2026-04-20 — Pre-deploy security audit (consolidated)

Moved from the retired `docs/audits/` directory (2026-07-09); "audit §N" comments in the Go, Python, and CI code cite this file's section numbers. Supersedes the 2026-04-19 general/security audits and the per-layer re-audits run on 2026-04-20 before the first `civic-lens.info` cutover — those files were deleted in the same cleanup; their remediations are recorded in walkthroughs 044-047. Severity legend identical: **CRITICAL** = deploy-blocker. **HIGH** = fix before any public traffic. **MEDIUM** = fix in the first iteration after launch.

## 0. What's already landed

- CORS middleware deleted (same-origin path-based routing — `server.py`).
- `require_admin_token` dependency gating `admin` and `review` routers (`api/dependencies.py`, `api/routers/admin.py`, `api/routers/review.py`).
- Per-endpoint pipeline-trigger cooldown (60s) on `/api/run/*` (`api/dependencies.py:42-56`).
- `WindowLiteral` applied to public data endpoints (`api/routers/data.py`).
- `/health` now touches DB + cache dir (`api/routers/health.py`).
- `admin_token` field in `Settings`; loud-503 when unset (`api/dependencies.py:27-31`).
- UI admin flow via `?admin=<token>` -> localStorage -> `X-Admin-Token` header (`ui/src/App.tsx`, `ui/src/services/api.ts`).
- Home-page verbiage sanitised, tech stack redacted.

## 1. Deploy blockers — CRITICAL / HIGH

Order = implementation order. Each item links file → what's broken → concrete fix.

### 1.1 [CRIT] Rotate Gemini key + set billing cap (user action)
Google AI Studio: rotate, set billing cap < $20/mo. Replace in `/etc/civic-lens.env` on the VPS; never in repo. Task #6.

### 1.2 [HIGH] `/api/review/*` input validation
`api/routers/review.py:38-52, 73-75` — `task: str`, `source_type: Optional[str]`, `limit: int = 20`, `offset: int = 0` all unvalidated. `ReviewSubmission` model has no field caps: `reviewer_id`, `notes`, `human_label` are unbounded strings; `human_confidence` accepts any float.

Fix:
- `task: Literal["sentiment","favorability","bot_detection","claims","propaganda"]`
- `source_type: Optional[Literal["news","reddit","x"]] = None`
- `limit: int = Query(20, ge=1, le=100)`, `offset: int = Query(0, ge=0)`
- `ReviewSubmission`: add `Field(None, max_length=255)` on `reviewer_id`; `Field(None, max_length=2000)` on `notes` and `human_label`; `Field(None, ge=0.0, le=1.0)` on `human_confidence`.

### 1.3 [HIGH] `/api/narratives` limit upper bound
`api/routers/data.py:58` — `limit: int = 20` with no cap allows live-compute of arbitrary rows when `limit > 100`. Add `Query(20, ge=1, le=500)`.

### 1.4 [HIGH] Harden `SnapshotCache._get_path`
`common/cache.py:51-55` only replaces `/` and `\`. Does not reject `..`, null bytes, URL-encoded separators. Fix:
```python
def _get_path(self, key: str) -> Path:
    if "\x00" in key or ".." in key:
        raise ValueError(f"Invalid cache key: {key!r}")
    safe = key.replace("/", "_").replace("\\", "_")
    resolved = (self.cache_dir / f"{safe}.json").resolve()
    if not str(resolved).startswith(str(self.cache_dir.resolve())):
        raise ValueError(f"Cache key escapes cache_dir: {key!r}")
    return resolved
```

### 1.5 [HIGH] Dependency CVE sweep + lockfile
`analysis/requirements.txt` pins are stale. Minimum upgrades:
- `requests>=2.32.2` (CVE-2024-35195, session verify bypass)
- `fastapi>=0.115.0` (path parsing + response-model fixes since 0.109)
- `uvicorn>=0.30.0`
- `pydantic>=2.10.0`
Add `pip-audit` run to build. Generate `requirements.lock` via `pip-compile`; commit it.

### 1.6 [HIGH] slowapi rate limits
Not installed. Per audit §4:
- `/api/run/*` — 1/hour per IP (stacks with the 60s cooldown as belt-and-suspenders).
- `/api/review/submit` — 30/hour per IP.
- `/api/geo-sentiment`, live-path `/api/narratives` — 10/min per IP.
- Cached GETs — 120/min per IP default.
Mirror `/api/run/*` at Cloudflare (10/hour rule).

### 1.7 [HIGH] Security-headers middleware
No headers set at origin. Add `@app.middleware("http")` that emits:
```
Content-Security-Policy: default-src 'self'; style-src 'self' https://fonts.googleapis.com;
                         font-src 'self' https://fonts.gstatic.com; img-src 'self' data:;
                         script-src 'self'; connect-src 'self'; frame-ancestors 'none';
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: camera=(), microphone=(), geolocation=()
```
HSTS is set by Cloudflare at the edge — don't duplicate.

### 1.8 [HIGH] Remove `cache_dir` from `/api/cache-status`
`api/routers/admin.py:31` returns the absolute path. Strip it; filenames only. Task #2.

### 1.9 [HIGH] Bind API to loopback in prod
`common/settings.py:17` defaults to `0.0.0.0`. Production env file must set `CIVIC_API_HOST=127.0.0.1`. Caddy reverse-proxies on same host. Task #11.

### 1.10 [HIGH] Go SSRF-via-redirect
`ingest/internal/httpclient/client.go:46-51` — `CheckRedirect` only counts hops. A hostile RSS feed can redirect to `http://169.254.169.254/` (cloud metadata), `http://127.0.0.1:8000/api/run/full-pipeline` (local admin API), `file://`, etc.

Fix: in `CheckRedirect`, parse `req.URL`, reject unless scheme is `http|https` AND `net.ParseIP(host).IsPrivate() == false && !IsLoopback && !IsLinkLocalUnicast`. Same guard at the top of `Fetch` for the initial URL (defense-in-depth for hostile seeds.yaml).

### 1.11 [HIGH] Go RSS/Reddit body size cap
`ingest/internal/extract/rss/rss.go`, `.../extract/reddit/reddit.go` call `io.ReadAll` without `io.LimitReader`. A single 10 GB response hangs the crawler. Wrap with `io.LimitReader(resp.Body, 10<<20)` (10 MB) — mirrors what `httpclient/client.go:142` already does for HTML fetches.

### 1.12 [HIGH] Strip `?admin=<token>` from URL after capture
`ui/src/App.tsx:10-25` — token persists to localStorage but the URL still carries it, so any subsequent outbound link leaks it via `Referer`. After the IIFE stores the token:
```ts
if (raw && raw !== '0') {
    params.delete('admin');
    const clean = params.toString() ? `?${params}` : '';
    window.history.replaceState({}, '', `${window.location.pathname}${clean}`);
}
```

### 1.13 [HIGH] UI `npm audit` in build
Run `npm audit --audit-level=high` after `npm ci` during build; fail on HIGH/CRITICAL. This catches transitive CVEs (d3-color ReDoS via react-simple-maps, any rollup RCEs) without guessing.

### 1.14 [HIGH] systemd unit hardening
Plan §6 has a minimal template. Every unit should inherit:
```ini
[Service]
User=civic-lens
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
PrivateDevices=yes
ProtectKernelTunables=yes
ProtectControlGroups=yes
ProtectClock=yes
RestrictNamespaces=yes
LockPersonality=yes
RestrictRealtime=yes
RestrictSUIDSGID=yes
CapabilityBoundingSet=
AmbientCapabilities=
SystemCallFilter=~@privileged @debug @module @mount @obsolete @reboot @swap @raw-io
SystemCallErrorNumber=EPERM
ReadWritePaths=/var/lib/civic-lens
MemoryMax=1G
CPUQuota=80%
TasksMax=100
```
API unit additionally: `Type=simple`, `Restart=on-failure`, `RestartSec=5s`.
Timer-triggered units: `Type=oneshot`, no restart.

### 1.15 [HIGH] Caddy hardening
Add to `/etc/caddy/Caddyfile`:
```
civic-lens.info, www.civic-lens.info {
    log {
        output file /var/log/caddy/civic-lens.log
        format json
        # suppress admin token from access logs
    }
    header {
        -Server
        X-Content-Type-Options "nosniff"
        X-Frame-Options "DENY"
        Referrer-Policy "strict-origin-when-cross-origin"
        Permissions-Policy "camera=(), microphone=(), geolocation=()"
    }
    encode gzip zstd
    request_body {
        max_size 1MB
    }
    handle /api/* {
        reverse_proxy 127.0.0.1:8000 {
            transport http {
                dial_timeout 5s
                response_header_timeout 15s
            }
        }
    }
    handle {
        root * /opt/civic-lens/ui/dist
        try_files {path} /index.html
        file_server
        header Cache-Control "public, max-age=3600"
    }
}
```
Content-Security-Policy is emitted by the FastAPI middleware (§1.7); don't set it twice.

### 1.16 [HIGH] Firewall — origin only to Cloudflare
`ufw allow 80,443` is wide open. Pin to Cloudflare IP ranges so the VPS IP becoming public doesn't bypass Access. Deploy script:
```bash
ufw default deny incoming
ufw allow 22/tcp
curl -sf https://www.cloudflare.com/ips-v4 | while read ip; do ufw allow from "$ip" to any port 80,443 proto tcp; done
curl -sf https://www.cloudflare.com/ips-v6 | while read ip; do ufw allow from "$ip" to any port 80,443 proto tcp; done
ufw --force enable
```
Re-run monthly via a timer — Cloudflare rotates ranges slowly.

### 1.17 [HIGH] SSH hardening beyond what's done
Append to `/etc/ssh/sshd_config`:
```
AllowUsers root deployment
MaxAuthTries 3
ClientAliveInterval 300
ClientAliveCountMax 2
AllowTcpForwarding no
AllowAgentForwarding no
X11Forwarding no
PermitTunnel no
AuthenticationMethods publickey
```
Install `fail2ban` with default sshd jail, `maxretry=3, bantime=3600`.

### 1.18 [CRIT] Cloudflare Access on admin paths
Plan §5a. Without this, the only admin gate is the localStorage token — XSS-visible. Cover: `/api/run/*`, `/api/review/*`, `/api/cache-status`. Task #14.

### 1.19 [HIGH] Cloudflare Authenticated Origin Pulls
Plan silent on this. Issue: VPS IP becoming public lets attackers hit origin directly (bypass CF Access + token gates — both terminate at origin so they still run, but rate limits don't). Enable Cloudflare's client cert requirement on the origin, verified by Caddy's `client_auth` block. Hard-lock origin to cloudflared.

### 1.20 [CRIT] CI/CD pipeline
No pipeline today. Minimum viable:
```yaml
# .github/workflows/deploy.yml
name: deploy
on:
  push:
    branches: [main]
jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with: { python-version: '3.12' }
      - uses: actions/setup-go@v5
        with: { go-version: '1.22' }
      - uses: actions/setup-node@v4
        with: { node-version: '20' }
      - name: Python tests
        run: |
          python -m venv .venv
          .venv/bin/pip install -r analysis/requirements.txt
          PYTHONPATH=$PWD .venv/bin/python -m unittest discover analysis/tests
      - name: Go tests
        run: cd ingest && go test ./...
      - name: Python security
        run: .venv/bin/pip install pip-audit && .venv/bin/pip-audit -r analysis/requirements.txt --strict
      - name: UI build + audit
        run: |
          cd ui
          npm ci
          npm audit --audit-level=high
          npm run typecheck
          npm run build
      - name: Deploy
        if: github.ref == 'refs/heads/main'
        env:
          SSH_KEY: ${{ secrets.DEPLOY_SSH_KEY }}
          SSH_HOST: ${{ secrets.DEPLOY_HOST }}
        run: |
          mkdir -p ~/.ssh && echo "$SSH_KEY" > ~/.ssh/k && chmod 600 ~/.ssh/k
          ssh-keyscan "$SSH_HOST" >> ~/.ssh/known_hosts
          ssh -i ~/.ssh/k deployment@$SSH_HOST '/opt/civic-lens/deploy.sh'
```
On the VPS: `deployment` user with a forced command (`authorized_keys`: `command="/opt/civic-lens/deploy.sh",no-pty,no-agent-forwarding,no-port-forwarding ssh-ed25519 ...`). Deploy script pulls, rebuilds, reloads.

## 2. First-iteration fixes — MEDIUM

Ship-without OK; do in week 1-2.

- **[MED] Pipeline cooldown race.** Single-worker uvicorn + GIL is safe today. If multi-worker is ever enabled, `_TRIGGER_LAST_FIRED` dict has no lock. Either document "do not run multi-worker" or move to `slowapi` exclusively.
- **[MED] Go frontier state updates via `map[string]any`.** Replace with typed struct to prevent accidental state downgrades.
- **[MED] Go `ArticleWriter` graceful shutdown.** Add `WaitGroup` around in-flight flushes.
- **[MED] UI classification sample link scheme validation.** `ClassificationSampleCard.tsx:175` renders `sample.url` as `href` without checking `new URL(...).protocol ∈ {http:,https:}`. API is trusted, but belt-and-suspenders.
- **[MED] Admin-token storage model** (task #15). Once Cloudflare Access is primary gate, the origin token becomes defense-in-depth — still worth moving to sessionStorage + prompt-per-session.
- **[MED] Key rotation cadence.** Document monthly rotation of Gemini key, X Bearer, admin token; quarterly rotation of deploy SSH key.
- **[MED] Backup encryption + retention.** `sqlite3 .backup` -> age-encrypt or openssl AES-GCM -> rclone to R2; 30-day retention, weekly restoration drill.

## 3. Nice-to-have / LOW

Skip for v1 unless trivial.

- Go X Bearer token exposed via public `BearerToken()` method. Make it package-private.
- Go `seeds.yaml` pre-commit check — reject non-empty `bearer_token:` strings.
- Reviewer ID localStorage key — move to sessionStorage; clears on close.
- UI `text_preview` hardened rendering (already auto-escaped via JSX text node).
- Caddy request-header redaction for `X-Admin-Token` in access logs (log format custom filter).
- `Server: Caddy` header strip (already `-Server` in the snippet above — verify it applies).

## 4. Implementation order

Stage these as three sequential PRs so reviews stay reasonable:

**PR-A — Python/API hardening** (1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9). One branch, one deploy. Breaks nothing — all backward-compatible at the HTTP layer.

**PR-B — Go ingest hardening** (1.10, 1.11). Independent of A; run Go tests after.

**PR-C — UI hardening** (1.12, 1.13). Trivial.

**PR-D — Infra as code** (1.14, 1.15, 1.16, 1.17). New files: `deploy/systemd/*.{service,timer}`, `deploy/caddy/Caddyfile`, `deploy/firewall.sh`, `deploy/sshd_config.patch`. No runtime code changes.

**PR-E — CI/CD** (1.20). `.github/workflows/deploy.yml` + `deploy/deploy.sh` on VPS.

**Deploy-day runbook** (1.1, 1.18, 1.19). Human-in-the-loop, not code.

Time estimate: ~6-10 hours of code work + 1-2 hours Cloudflare dashboard + 30 min to rotate secrets. Net: one long afternoon.
