import type {
    BotActivityResponse, DocumentDetail, EntityPostsResponse, EntityProfileResponse,
    EvalAccuracy, MoversResponse, NarrativesResponse, OutletProfilesResponse,
    PropagandaOverview, ReviewQueueItem, ReviewStats, ReviewSubmission, ReviewTaskType,
    SentimentPanelResponse, SnapshotStatusResponse, TimeWindow,
} from '../types';

export type { TimeWindow } from '../types';

const API_BASE = '/api/v1';

function adminHeaders(): HeadersInit {
    try {
        const token = localStorage.getItem('civic_admin_token');
        return token ? { 'X-Admin-Token': token } : {};
    } catch {
        return {};
    }
}

// Flag flipped when we trigger a CF Access bounce so concurrent admin fetches
// (Review tab fires queue + stats in parallel) don't each race to set
// sessionStorage + call location.href.
let redirectingToCfAccess = false;

const CF_ACCESS_BOOTSTRAP_PATH = '/api/v1/review/bootstrap';

function triggerCfAccessLogin(): void {
    if (redirectingToCfAccess) return;
    redirectingToCfAccess = true;
    try {
        // Same-origin path only (pathname+search+hash) — the bootstrap HTML
        // validates it starts with "/" and not "//" before navigating, so a
        // tampered value can't be coerced into an open redirect.
        const returnTo =
            window.location.pathname + window.location.search + window.location.hash;
        sessionStorage.setItem('civic_post_auth_return', returnTo);
    } catch { /* sessionStorage may be blocked; bootstrap falls back to "/" */ }
    window.location.href = CF_ACCESS_BOOTSTRAP_PATH;
}

async function fetchJSON<T>(
    path: string,
    init: RequestInit & { admin?: boolean } = {},
): Promise<T> {
    const { admin, headers, ...rest } = init;
    const mergedHeaders: HeadersInit = {
        ...(rest.body ? { 'Content-Type': 'application/json' } : {}),
        ...(admin ? adminHeaders() : {}),
        ...(headers || {}),
    };
    // For admin endpoints, opt out of auto-following redirects: CF Access's
    // 302 to cloudflareaccess.com is cross-origin and would surface as an
    // opaque CORS TypeError. `redirect: 'manual'` lets us detect the 302 as
    // `resp.type === 'opaqueredirect'` and trigger a top-level navigation
    // instead, so the browser can actually complete the CF login flow.
    const fetchInit: RequestInit = admin
        ? { ...rest, headers: mergedHeaders, redirect: 'manual' }
        : { ...rest, headers: mergedHeaders };

    let resp: Response;
    try {
        resp = await fetch(`${API_BASE}${path}`, fetchInit);
    } catch (err) {
        // Network-level failure on an admin endpoint usually means CF Access
        // intercepted with a cross-origin redirect fetch() couldn't handle.
        // Bounce to the bootstrap endpoint so the browser can follow it.
        if (admin && err instanceof TypeError) {
            triggerCfAccessLogin();
            throw new Error('Redirecting to sign in...');
        }
        throw err;
    }

    if (admin && resp.type === 'opaqueredirect') {
        triggerCfAccessLogin();
        throw new Error('Redirecting to sign in...');
    }

    if (!resp.ok) {
        throw new Error(`API ${resp.status} ${resp.statusText} on ${path}`);
    }
    return resp.json();
}

/** Every aggregate endpoint (except movers) takes `window=24h|7d|30d|90d|all`. */
function windowParams(window: TimeWindow): URLSearchParams {
    return new URLSearchParams({ window });
}

export async function fetchSentiment(window: TimeWindow = '30d'): Promise<SentimentPanelResponse> {
    return fetchJSON<SentimentPanelResponse>(`/sentiment?${windowParams(window)}`);
}

export async function fetchBotActivity(window: TimeWindow = '30d'): Promise<BotActivityResponse> {
    return fetchJSON<BotActivityResponse>(`/bot-activity?${windowParams(window)}`);
}

export async function fetchNarratives(
    window: TimeWindow = '30d', limit: number = 20,
): Promise<NarrativesResponse> {
    const params = windowParams(window);
    params.set('limit', String(limit));
    return fetchJSON<NarrativesResponse>(`/narratives?${params}`);
}

export async function fetchPropaganda(window: TimeWindow = '30d'): Promise<PropagandaOverview> {
    return fetchJSON<PropagandaOverview>(`/propaganda?${windowParams(window)}`);
}

/** Per-domain cross-signal profiles (net tone x bot rate). Includes
 *  bot-flagged content on purpose — the payload carries the disclaimer. */
export async function fetchOutletProfiles(window: TimeWindow = '30d'): Promise<OutletProfilesResponse> {
    return fetchJSON<OutletProfilesResponse>(`/outlet-profiles?${windowParams(window)}`);
}

/** GET /movers rejects window='all' — no previous period to compare against. */
export type MoversWindow = Exclude<TimeWindow, 'all'>;

export async function fetchMovers(window: MoversWindow = '30d'): Promise<MoversResponse> {
    return fetchJSON<MoversResponse>(`/movers?${windowParams(window)}`);
}

/** Paginated docs mentioning/authored-by entity_id. Omitting `window`
 *  defaults server-side to all-time. */
export async function fetchEntityPosts(
    entityId: number, window?: TimeWindow, page: number = 1,
): Promise<EntityPostsResponse> {
    const params = new URLSearchParams({ page: String(page) });
    if (window) params.set('window', window);
    return fetchJSON<EntityPostsResponse>(`/entity-posts?entity_id=${entityId}&${params}`);
}

/** ALL-TIME entity profile — no window param, see routers/entities.py. */
export async function fetchEntityProfile(entityId: number): Promise<EntityProfileResponse> {
    return fetchJSON<EntityProfileResponse>(`/entity-profile/${entityId}`);
}

/** Universal document drill-down — resolves regardless of age. */
export async function fetchDocument(docId: number): Promise<DocumentDetail> {
    return fetchJSON<DocumentDetail>(`/docs/${docId}`);
}

/** Per-task human-review agreement for the public "human agreement" chips. */
export async function fetchEvalAccuracy(): Promise<EvalAccuracy> {
    return fetchJSON<EvalAccuracy>('/eval-accuracy');
}

/** Freshness signal: the latest ops.pipeline_runs row, replacing the retired
 *  cache-metadata endpoint now that the API is strictly-live. */
export async function fetchSnapshotStatus(): Promise<SnapshotStatusResponse> {
    return fetchJSON<SnapshotStatusResponse>('/snapshot-status');
}

// --------------------------------------------------------------------------- //
//  Review (admin-gated)                                                       //
// --------------------------------------------------------------------------- //

export interface ReviewQueueParams {
    task: ReviewTaskType;
    sourceType?: string;
    confidenceMax?: number;
    limit?: number;
    offset?: number;
}

export async function fetchReviewQueue(params: ReviewQueueParams): Promise<ReviewQueueItem[]> {
    const qs = new URLSearchParams({ task: params.task });
    if (params.sourceType) qs.set('source_type', params.sourceType);
    if (params.confidenceMax !== undefined) qs.set('confidence_max', String(params.confidenceMax));
    if (params.limit !== undefined) qs.set('limit', String(params.limit));
    if (params.offset !== undefined) qs.set('offset', String(params.offset));
    return fetchJSON<ReviewQueueItem[]>(`/review/queue?${qs}`, { admin: true });
}

export async function submitReview(
    submission: ReviewSubmission,
): Promise<{ eval_id: number; run_id: number; reviewed_at: string }> {
    return fetchJSON(`/review/submit`, {
        method: 'POST',
        body: JSON.stringify(submission),
        admin: true,
    });
}

export async function fetchReviewStats(task?: ReviewTaskType): Promise<ReviewStats> {
    const path = task ? `/review/stats?task=${task}` : `/review/stats`;
    return fetchJSON<ReviewStats>(path, { admin: true });
}
