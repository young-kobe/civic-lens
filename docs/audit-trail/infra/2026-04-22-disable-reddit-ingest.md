# 2026-04-22 — Disable Reddit ingest job

Civic Lens no longer collects Reddit posts or comments. The job is removed from the scheduled pipeline on prod and from the repository's systemd unit catalogue. Existing Reddit documents in the database are untouched — historical analysis surfaces that already joined against them still work.

## What shipped

- Deleted `deploy/systemd/civic-lens-reddit.service` and `civic-lens-reddit.timer`.
- Removed `civic-lens-reddit.timer` from the enable list in `deploy/install.sh` (step `[9/9] timer enable`).
- Added an idempotent cleanup block to `deploy/deploy.sh` — on the next deploy to prod, the running Reddit timer is disabled and masked, the unit files are removed from `/etc/systemd/system/`, and systemd is reloaded. Safe to run repeatedly.

## Why

Reddit API access was withdrawn. Leaving the 12×/day job in place would have filled the logs with auth failures without producing any new documents.

## Budget impact

Reddit ingest was free (OAuth-authenticated script-type app). No spend change. Removing 12 job runs/day drops a small amount of IO and CPU load from the VPS but not enough to retune any other cadence.

## Follow-ups

- If Reddit access is restored, restore `civic-lens-reddit.{service,timer}` from git history and add back to `install.sh` — both files are ~10 lines. Rerun `install.sh` on the VPS (or hand-install and enable via `systemctl enable --now`).
- Downstream: the UI's Reddit filter pill and prose references were removed in the sibling entry under `../ui/2026-04-22-hide-reddit-from-ui.md`. Underlying `SourceFilter = 'reddit'` union + source-label rendering are retained so historical Reddit docs in the DB still render correctly.
