# Infra timeline (pre-2026-07, consolidated)

Condensed record of deployment, scheduling, CI/CD, and secrets-handling history, consolidated from
the retired `docs/walkthroughs/` linear log (see `docs/todos/walkthrough-consolidation.md`). This
layer has fewer entries than the code layers because most infra decisions were one-shot (the launch
sequence) rather than iterative.

## 001 — Initial Infrastructure (undated)

Established `INVARIANTS.md` as the correctness constitution for the project — still the standing
reference (`docs/INVARIANTS.md`), maintained continuously since.

## 006 — Background Analysis Pipeline (undated, infra slice)

Scheduled the analysis pipeline via Windows Task Scheduler (`setup-scheduled-task.ps1`). Superseded
by cron for dev (`./setup-cron.sh`) and a systemd timer for production (`deploy/install.sh`), per the
Platform Notes in CLAUDE.md.

## 015 — GitIgnore Update & Security Scan (undated)

Point-in-time hygiene check: reorganized and expanded `.gitignore` (env files, DB files, executables,
build artifacts, OS files); confirmed no secrets or binaries were tracked in git at the time; flagged
the need for an `.env.example`, which exists in the repo today.

## 016 — Agent Rules & Workflows Update (undated)

Rewrote `.agent/` to match the (by-then-current) Go+Python+React architecture: added
`code-style.md` (DRY/SOLID + per-language style) and layer-specific workflow docs, deleted the
obsolete C++-era workflow doc entirely, confirming that layer was fully retired by this point.

## 037 — Dynamic Account Refresh (undated, infra slice)

Added `run.ps1 refresh-accounts [-DryRun]`, an operator-run CLI subcommand for refreshing the curated
Congress account roster — deliberately kept manual (not on the scheduled pipeline) so a scrape diff
is reviewed before it lands, not silently applied overnight.

## 047 — Pre-Deploy Hardening (2026-04-21 launch window)

Infra-as-code slice (PR-D) of a five-PR remediation of a 2026-04-20 consolidated security audit,
executed just before the first public cutover. New `deploy/` directory: hardened systemd units
(NoNewPrivileges, ProtectSystem=strict, syscall filtering, memory/CPU/task quotas), a Caddyfile using
Cloudflare Origin CA plus Authenticated Origin Pulls (client-cert-required for direct-IP access), UFW
pinned to Cloudflare IP ranges with a monthly auto-refresh, SSH hardening plus fail2ban, a backup
script (SQLite `.backup` + age-encrypt + optional rclone), and a restricted `deployment` user with a
forced-command SSH key limited to running exactly `deploy.sh`. CI/CD (PR-E): `ci.yml` (parallel
python/go/ui jobs with pip-audit/npm-audit gates) and `deploy.yml` (main-branch-only SSH deploys via
the restricted user). Deploy-day runbook: Gemini key + billing-cap rotation, X bearer-token rotation,
admin-token rotation, Cloudflare Access gating admin/review/cache-status routes to the owner's email,
smoke tests, timer enablement. Companion PR-A/B/C (Python/API, Go, UI) are recorded in their
respective layer timelines.

## 049 — Launch (live 2026-04-21)

Cutover sequence and the real-environment bugs only visible once deployed, distinct from 047's
audit-remediation plan (correct in design, incomplete in execution detail). Fixes: Caddy 2.6
(Ubuntu 24.04's packaged version) needed `trusted_ca_cert_file`, not the 2.8+ `trust_pool file`
syntax, for Authenticated Origin Pulls; deploy scripts weren't marked executable; DB ownership
drifted after migrations ran as root; `systemd ProtectSystem=strict` blocked writes to `data/raw/`
under `/opt` (read-only), fixed by relocating `WorkingDirectory` to `/var/lib/civic-lens`. Captured
the first production data snapshot (59 docs, 115 claims, 102 narratives, 11 propaganda-flagged, 13
citation edges) and the current timer schedule. Follow-ups filed: re-add Reddit OAuth (Reddit blocked
the Hetzner datacenter IP), fix a Cloudflare-Access redirect UX gap in the Review tab.
