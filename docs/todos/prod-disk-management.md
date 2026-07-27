# Prod disk management

Motivated by the 2026-07-27 outage: the 38G root filesystem hit 100% and
postgres crash-looped (`could not write lock file "postmaster.pid"`) for
~15 minutes before anyone noticed, discovered only when a deploy failed.
The raw store (`/var/lib/civic-lens/data/raw/sha256`) was 23G after ~3
weeks of ingestion; SQLite-era artifacts accounted for another ~4G (since
moved off-box and deleted).

- [ ] Raw-store retention/offload decision: pick one of (a) rclone cold
      shards to object storage and delete locally past an age threshold,
      (b) compress blobs at rest (raw HTML compresses 5-10x; requires the
      Go store and Python readers to handle a compressed encoding), or
      (c) resize the volume and accept the cost. Growth is roughly 1G/day
      at current crawl cadence -- headroom after the 2026-07-27 cleanup is
      only a few weeks.
- [ ] Disk alert: a cron/systemd timer that checks `df` on the box and
      notifies (mail or a webhook) above ~80% usage, so the next fill-up
      announces itself before postgres falls over.
- [ ] Local backup retention: verify deploy/backup.sh's 14-day local
      retention actually matches available disk once dump size grows;
      consider keeping only the newest 1-2 dumps locally once off-box
      replication is confirmed working.
