# 2026-04-23 — Analyze pipeline resilience + failure-alert email

The analysis pipeline's systemd unit has been hitting `TimeoutStartSec=45m` in prod. Each fire completes ETL → bot → text → propaganda → citations, gets partway through claims, then gets SIGTERMed — every time right before `snapshots` (stage 10/10). Because `snapshots` is what the UI reads, **the cache stayed 2 days stale** even though 4 cron fires had "partially" succeeded in the meantime. This change turns the pipeline into a system that degrades gracefully and emails the operator when it has to.

## What shipped

### Per-stage failure isolation + soft time budget (`analysis/src/scheduler/job_runner.py`)

`run_full_pipeline()` was a single try/except that re-raised on any stage failure. Rewritten with three guarantees:

1. **Per-stage failure isolation.** Each LLM-heavy stage runs inside its own inner `try/except`. A Gemini 429, a transient SQLite lock, a malformed upstream row — none of these abort stages N+1..10. Per-stage outcomes land in `summary["stage_status"]` and `summary["stage_errors"]`.
2. **Soft wall-clock budget.** A new `--budget-seconds` CLI arg (also reads `CIVIC_BUDGET_SECONDS` from env). Before each LLM stage, the runner checks remaining time. If less than 120 seconds remain (`_SNAPSHOTS_RESERVE_SECONDS`), the stage skips with status `skipped_budget`. The reserve guarantees snapshots always gets its turn.
3. **Snapshots run in a `finally`.** Regardless of prior successes, skips, or exceptions, `save_snapshots()` runs on exit. Aggregators are pure SQL → JSON; they produce a consistent view of whatever data the DB currently holds. Two cron fires making partial LLM progress plus a fresh UI cache beats one cron fire that chewed the full budget and left the UI 2 days stale.

Aggregate status becomes `success` | `partial` | `failed`:
- `success` — every run stage ok
- `partial` — at least one LLM stage failed or was budget-skipped, but snapshots ran
- `failed` — snapshots themselves failed (UI cache will stay stale)

Exit codes: `success` and `partial` both return 0. `failed` returns 1. Systemd sees a failed unit only when the UI is actually stale.

### Failure-alert email wiring

Two complementary paths — one for "systemd killed the unit" (crash/timeout/exit 1), one for "runner finished with degraded status" (partial). Both converge on the same SMTP contract via `/etc/civic-lens.env`.

**Systemd path.** New `deploy/systemd/civic-lens-alert@.service` (templated). Existing `civic-lens-analyze.service`, `civic-lens-crawl.service`, `civic-lens-x.service` gain `OnFailure=civic-lens-alert@%n.service`. On any non-zero exit, kill, or timeout, systemd starts the alerter instance with the failing unit's name as `%i`. The alerter runs `deploy/scripts/send-alert.py <service>`, which pulls 50 lines of journal + a systemctl status header and emails them.

**Runner path.** New `analysis/src/common/alerts.py` exposes `send_alert(subject, body)` — stdlib smtplib + STARTTLS + app-password auth. `job_runner.main()` calls it when status is `partial`, since systemd exits 0 and won't fire OnFailure in that case. The email includes the stage-by-stage status map so the operator can tell which stage skipped and why.

Both paths share `CIVIC_ALERT_SMTP_*` env vars:

```
CIVIC_ALERT_SMTP_HOST  default smtp.gmail.com
CIVIC_ALERT_SMTP_PORT  default 587
CIVIC_ALERT_SMTP_USER  required (full sender address)
CIVIC_ALERT_SMTP_PASS  required (Gmail App Password, not login password)
CIVIC_ALERT_TO         required (recipient)
CIVIC_ALERT_FROM       optional (defaults to SMTP_USER)
```

When any required var is missing, both paths exit silently — no spam on misconfigured dev boxes.

### Systemd unit updates

`deploy/systemd/civic-lens-analyze.service`:
- `TimeoutStartSec` 45m → 2h (hard kill ceiling)
- `Environment=CIVIC_BUDGET_SECONDS=6900` (soft budget = 1h55m, leaves 5m of headroom before the hard kill so the runner exits voluntarily with a clean summary)
- `OnFailure=civic-lens-alert@%n.service`

`deploy/systemd/civic-lens-crawl.service` + `civic-lens-x.service`: `OnFailure=civic-lens-alert@%n.service` added for free — the crawl and X units are long-running enough to occasionally fail on upstream flakiness.

`deploy/deploy.sh` now syncs `/etc/systemd/system/civic-lens-*.{service,timer}` from the repo on every deploy + runs `systemctl daemon-reload`. Previously only `install.sh` copied unit files, so `TimeoutStartSec` and `OnFailure=` changes wouldn't land on prod without a fresh install. Idempotent — `install -m 0644` replaces with same mode.

### `.env.example` + docs

New "Failure alerting (email)" section documenting the SMTP contract, the Gmail App Password requirement, and the silent-skip behavior when unset.

## Why

Prod systemd status showed:
```
× civic-lens-analyze.service - Civic Lens analysis pipeline (one-shot)
  Active: failed (Result: timeout) since Thu 2026-04-23 13:05:09 UTC
  Main PID: 48226 (code=killed, signal=TERM)
```
Every fire for ~2 days hit `start operation timed out` and got killed mid-claims. Snapshots never ran. UI served stale data with the pre-fix 197% propaganda %s the operator was seeing.

User: *"can we just ensure that it doesnt time out?"* — then: *"nevermind re-implement these changes, you were right the more robust our system the better. still have it alert if it fails and has to serve early though."*

The simple answer (bump the timeout) papers over the class of bug where ONE slow stage can silently delay every downstream stage + the cache for hours. The robust answer builds in graceful degradation so the UI is always fresh, with email signalling when the system is operating in degraded mode.

## Validation

- `PYTHONPATH=. python -m unittest analysis.tests.test_engines` — 6/6 pass.
- Import + smoke test for `_build_partial_alert_body` produces a readable email with the correct stage status embedded.
- `send_alert()` returns `False` without side effects when required env vars aren't set (covers dev-box default).
- `job_runner.py` imports clean under the project venv.

## Operator setup required (one-time, prod only)

1. Generate a Gmail App Password: https://myaccount.google.com/apppasswords (2FA must be enabled on the Google account first).
2. Append to `/etc/civic-lens.env` on the Hetzner box:
   ```
   CIVIC_ALERT_SMTP_USER=kobe.tyler.young@gmail.com
   CIVIC_ALERT_SMTP_PASS=<the app password>
   CIVIC_ALERT_TO=kobe.tyler.young@gmail.com
   ```
3. `chmod 600 /etc/civic-lens.env` (already set by install.sh; confirm).
4. Next deploy will pick up the new systemd units + daemon-reload automatically. No install.sh rerun needed.
5. Test the email path: `systemctl start civic-lens-alert@civic-lens-analyze.service` — should send a test email with whatever the analyze unit's last journal lines are.

## Follow-ups

- Consider adding `CIVIC_BUDGET_SECONDS` to `.env.example` with a comment pointing to this file. Currently it's only set in the systemd unit, which is the right place for the default — but an operator running `run.ps1 analyze` locally doesn't get the guard.
- If partial alerts become noisy, the natural escalation is a distinct "every run is partial" alarm keyed on successive failures (e.g., only email when 3 runs in a row are partial). Not needed until noise is observed.
