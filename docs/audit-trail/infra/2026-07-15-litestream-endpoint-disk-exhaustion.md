# 2026-07-15 — Fix Litestream R2 endpoint misconfiguration that exhausted the disk

The production VPS filled to 100% (`/` at 38G, 0 bytes free), wedging the
stack. Root cause was a misconfigured `LITESTREAM_R2_ENDPOINT`: it included the
bucket name as a path suffix while `LITESTREAM_R2_BUCKET` also named the bucket.
Litestream builds each request as `endpoint + "/" + bucket + "/" + key`, so the
bucket was doubled (`.../civic-lens-litestream/civic-lens-litestream/...`) and
every R2 operation returned `NoSuchKey 404`. A replica that can never confirm a
write can never truncate its local shadow WAL, so
`data/.civic_lens.db-litestream/` grew to 23G (for a 1.4G database) until the
disk was full. The DB itself was never at risk — it stayed intact at 1.4G and
remained the source of truth throughout.

## What shipped

- `.env.example` — `LITESTREAM_R2_ENDPOINT` now carries an explicit warning
  that it is the account host ONLY (no bucket, no path, no trailing slash),
  with correct/WRONG examples and a note that a doubled bucket 404s and grows
  the shadow WAL until the disk fills.
- `deploy/README.md` — the env-setup step documents the endpoint-vs-bucket
  split and how to verify a healthy replica in `docker compose logs litestream`
  (`replicating to` prints no bucket suffix; `snapshot written` with no
  follow-up `monitor error`).

## Remediation applied on the box (not code)

1. `docker compose stop litestream` then `rm -rf .civic_lens.db-litestream` —
   reclaimed the 23G. Safe because the live DB (`civic_lens.db`/`-wal`/`-shm`)
   is authoritative and Litestream re-seeds a fresh generation on restart.
2. Corrected `LITESTREAM_R2_ENDPOINT` in `/etc/civic-lens.env` to the account
   host only.
3. `docker compose up -d --force-recreate litestream` — verified `write
   snapshot` / `snapshot written` (130MB) / `wal segment written` with zero
   404s. Shadow WAL now truncates normally.

## Why

- The repo gave operators no guidance: `.env.example` shipped
  `LITESTREAM_R2_ENDPOINT=` blank, `deploy/README.md` said nothing, and the
  only hint (endpoint is host-only) was buried in a comment inside
  `deploy/litestream.yml`. Pasting the full R2 bucket URL was an easy and
  silent mistake.
- Nothing monitored disk usage or replica health, so a slow-motion failure ran
  for days and only surfaced when the box wedged at 100%. That gap is the real
  hazard; the config note above only prevents this specific trigger.

## Follow-ups

- Disk + Litestream-health guardrail so a stalled replica pages before it fills
  the disk, using the existing `deploy/scripts/send-alert.py` +
  `civic-lens-alert@.service`. Tracked in
  `docs/todos/disk-and-replica-health-alerting.md`.
