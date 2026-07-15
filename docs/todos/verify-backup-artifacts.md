# Verify backup.sh actually produces artifacts

During the 2026-07-15 disk incident, `/var/lib/civic-lens/backups/` was empty
(4.0K) on the production box. That directory should hold up to 14 days of local
SQLite snapshots written by `deploy/backup.sh` on the `civic-lens-backup.timer`
schedule. An empty dir means the nightly backup is either not running, failing
silently, or deleting its output — a second, independent durability gap beyond
Litestream. This needs confirming and fixing in its own PR.

## Investigate (on the box)

- [ ] `systemctl status civic-lens-backup.timer civic-lens-backup.service` —
      is the timer enabled and firing? When did it last run?
- [ ] `journalctl -u civic-lens-backup.service -n 100` — is it erroring
      (permissions, `sqlite3` missing, rclone/age failure) or exiting 0 with no
      file?
- [ ] Confirm whether the R2 push (`BACKUP_RCLONE_REMOTE`) is succeeding even if
      the local copy is absent — the remote may be fine while local retention or
      a failing branch removes the local artifact.
- [ ] Check `BACKUP_AGE_RECIPIENT` — if unset, backup.sh stores plaintext and
      logs a warning; confirm that path isn't silently aborting.

## Likely fix areas

- `deploy/backup.sh` — the `set -euo pipefail` + `trap 'rm -f "$tmp"' EXIT`
  interaction: if any step after `.backup` fails, the trap removes the temp file
  and the run leaves nothing behind. Verify the success path actually persists
  `$final` locally before the trap fires.
- `civic-lens-backup.timer` schedule / enablement in `install.sh`.

## Definition of done

- [ ] A fresh `civic-lens-*.db(.age)` artifact appears in
      `/var/lib/civic-lens/backups/` after a manual
      `systemctl start civic-lens-backup.service`.
- [ ] Confirmed the same artifact lands in the R2 backup bucket.
- [ ] Record in `docs/audit-trail/infra/` and delete this todo.

## Relationship to other work

- The disk + replica-health alerting initiative
  (`docs/todos/disk-and-replica-health-alerting.md`) should also alert if no
  backup artifact has been produced in >24h — fold that check in there rather
  than duplicating it.
