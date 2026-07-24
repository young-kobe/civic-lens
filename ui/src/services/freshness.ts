/**
 * Freshness helpers — translate the latest `ops.pipeline_runs` row (GET
 * /snapshot-status) into the editorial "refreshed 18 min ago" phrasing the
 * header and each page's GlobalTicker render.
 *
 * Phase 9 (strictly-live) retired the cache-metadata endpoint this used to
 * read: there is no longer one freshness timestamp per panel, just the
 * pipeline's last recorded run. Every page reads the same single value.
 */

import type { SnapshotStatusResponse } from '../types';

/**
 * Humanize an ISO timestamp into "N (unit) ago". Falls back to the absolute
 * date once the run is older than a week, which means either the pipeline
 * has stopped running or the user is looking at an abandoned env — either
 * way the "days ago" phrasing would read as noise.
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

/** The pipeline run's completion time if it finished, else its start time
 *  (a still-running or failed run has no completed_at). Null on a fresh
 *  database with no recorded runs yet. */
export function pipelineRunTimestamp(status: SnapshotStatusResponse | null): string | null {
    const run = status?.pipelineRun;
    if (!run) return null;
    return run.completedAt ?? run.startedAt;
}
