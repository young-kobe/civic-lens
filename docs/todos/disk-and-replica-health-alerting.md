# Disk + replica-health alerting

The 2026-07-15 disk-exhaustion incident (see
`docs/audit-trail/infra/2026-07-15-litestream-endpoint-disk-exhaustion.md`) ran
undetected for days because nothing watches disk usage or Litestream health. A
stalled replica grew its local shadow WAL to 23G and only surfaced when `/`
hit 100% and wedged the stack. This initiative adds a guardrail that pages
before the box is in trouble, reusing the existing alert plumbing
(`deploy/scripts/send-alert.py`, `civic-lens-alert@.service`, systemd
`OnFailure=`).

## Design constraints

- No new dependencies on the box beyond what `install.sh` already installs
  (bash, python3-stdlib, docker, systemd). The alerter is stdlib-only by design.
- Alert routing must go through the existing `civic-lens-alert@.service` so
  there is one notification path, not two.
- Thresholds live in `/etc/civic-lens.env` so they are tunable without a deploy.

## Checklist

- [ ] Health-check script (`deploy/scripts/health-check.sh`, run as civic-lens):
  - [ ] Alert if `/` usage crosses `DISK_ALERT_PERCENT` (default 80).
  - [ ] Alert if `data/.civic_lens.db-litestream/` exceeds
        `LITESTREAM_WAL_ALERT_BYTES` (default e.g. 3x the DB size) — the direct
        symptom of a stalled replica, caught before the disk fills.
  - [ ] Alert if `docker compose logs --since=15m litestream` contains
        `monitor error` / `NoSuchKey` — catches replica desync explicitly.
  - [ ] Fire via the same mechanism `send-alert.py` uses (confirm whether to
        invoke `civic-lens-alert@<unit>.service` or call the sender directly).
- [ ] `civic-lens-health.service` + `civic-lens-health.timer` (every 15m),
      added to `deploy/systemd/` and enabled in `install.sh` step [9/9].
- [ ] New env vars documented in `.env.example` + `deploy/README.md`
      (`DISK_ALERT_PERCENT`, `LITESTREAM_WAL_ALERT_BYTES`, alert recipient reuse).
- [ ] Test on the box: force a threshold breach (e.g. temporary low
      `DISK_ALERT_PERCENT`) and confirm an alert email arrives.
- [ ] Record in `docs/audit-trail/infra/` and delete this todo.

## Out of scope (separate tickets)

- Gemini credit-depletion alerting (429 RESOURCE_EXHAUSTED silently falls back
  to heuristics — same class of "silent degradation" gap, different subsystem).
- Verifying `backup.sh`/`civic-lens-backup.timer` actually produces artifacts;
  the local `backups/` dir was observed empty (4.0K) during the incident.
