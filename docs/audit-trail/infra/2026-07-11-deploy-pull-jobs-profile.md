# 2026-07-11 — Pull jobs-profile images in deploy.sh

`deploy/deploy.sh` now runs `docker compose --profile jobs pull` instead of
a plain `compose pull`. Plain pull skips profile-gated services, so the
`ingest` image (which bakes the migrations at `/app/data/`) was never
refreshed on the box — migrate kept applying only the migrations the stale
image shipped, while the `api` service kept refreshing the shared analysis
image. Result: the 2026-07-11 incident where `civic-lens-analyze` aborted
with "schema is at version 21 but this build expects 25" immediately after
a "Migrations applied successfully" run.

## What shipped

- `deploy/deploy.sh` step 2: `docker compose --profile jobs pull --quiet`
  (pulls default-profile AND jobs-profile services: caddy, api, ingest,
  analyze).

## Why

- Migrations ride inside the ingest image; a deploy that doesn't refresh
  that image silently pins the schema to whatever the last-pulled build
  shipped. The schema guard (2026-07-10) caught the drift exactly as
  designed — the gap was upstream in the deploy's pull step.

## Recovery runbook (what fixed the box)

```
cd /opt/civic-lens
docker compose --profile jobs pull --quiet
docker compose run --rm ingest migrate --db /var/lib/civic-lens/data/civic_lens.db
sudo systemctl restart civic-lens-analyze.service
```

## Follow-ups

- None; the guard + fixed pull make the failure mode self-explaining.
