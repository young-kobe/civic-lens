/**
 * Freshness helpers — translate snapshot `generated_at` timestamps into
 * the editorial "refreshed 18 min ago" phrasing that the header and each
 * page's GlobalTicker render.
 *
 * Source of truth is `/api/v1/snapshot-status` (see services/api.ts). Every
 * page calls `useFetch(fetchSnapshotStatus, [], 'snapshot-status')` and the
 * module-level cache in useFetch dedupes the request across tab switches.
 */

import type { SnapshotStatus } from './api';

/**
 * Humanize an ISO timestamp into "N (unit) ago". Falls back to the absolute
 * date once the snapshot is older than a week, which means either the
 * pipeline has stopped running or the user is looking at an abandoned env —
 * either way the "days ago" phrasing would read as noise.
 */
export function formatRefreshedAgo(iso: string | null | undefined): string {
    if (!iso) return '—';
    const then = Date.parse(iso);
    if (Number.isNaN(then)) return '—';
    const ageMs = Date.now() - then;
    const ageSec = Math.max(0, Math.round(ageMs / 1000));

    if (ageSec < 60) return 'moments ago';
    const min = Math.round(ageSec / 60);
    if (min < 60) return `${min} min ago`;
    const hr = Math.round(ageSec / 3600);
    if (hr < 24) return `${hr}h ago`;
    const days = Math.round(ageSec / 86400);
    if (days < 7) return `${days}d ago`;
    // Older than a week: absolute ISO date (UTC, minute precision).
    return new Date(then).toISOString().slice(0, 16).replace('T', ' ') + ' UTC';
}

export function getSnapshotTimestamp(
    status: SnapshotStatus | null,
    key: string,
): string | null {
    if (!status) return null;
    const entry = status.snapshots.find((s) => s.key === key);
    return entry?.generated_at ?? null;
}

/**
 * Most recent `generated_at` across all cached snapshots. Serves as the
 * "when was the pipeline last refreshed" proxy shown in the page header.
 */
export function latestSnapshotTimestamp(status: SnapshotStatus | null): string | null {
    if (!status || status.snapshots.length === 0) return null;
    let latest = 0;
    let latestIso: string | null = null;
    for (const s of status.snapshots) {
        const t = Date.parse(s.generated_at);
        if (!Number.isNaN(t) && t > latest) {
            latest = t;
            latestIso = s.generated_at;
        }
    }
    return latestIso;
}
