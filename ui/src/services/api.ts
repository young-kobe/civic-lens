import {
    PublicSentimentData, BotData, NarrativeSummary,
    PropagandaOverview,
    ReviewQueueItem, ReviewSubmission, ReviewStats, ReviewTaskType,
} from '../types';

const API_BASE = '/api/v1';

export type TimeWindow = '24h' | '7d' | '30d' | '90d' | 'all';

/**
 * Dev-only mock toggle. Set VITE_USE_MOCKS=true in ui/.env.local (gitignored)
 * to render the UI against deterministic fixtures without a live backend.
 * Vite inlines import.meta.env at build time, so this branch dead-code-
 * eliminates in production builds when the flag is off.
 *
 * When you retire the fixtures, delete this constant, the three `if (USE_MOCKS)`
 * branches below, and `src/services/fixtures.ts`. No other code references them.
 */
const USE_MOCKS = import.meta.env.VITE_USE_MOCKS === 'true';

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

export async function fetchSentiment(window: TimeWindow = '24h'): Promise<PublicSentimentData> {
    if (USE_MOCKS) {
        const { mockSentiment } = await import('./fixtures');
        return mockSentiment();
    }
    return fetchJSON<PublicSentimentData>(`/sentiment?window=${window}`);
}

export async function fetchBotActivity(): Promise<BotData> {
    if (USE_MOCKS) {
        const { mockBotActivity } = await import('./fixtures');
        return mockBotActivity();
    }
    return fetchJSON<BotData>('/bot-activity');
}

export interface GeoSentimentData {
    countries: CountryStats[];
    total_posts: number;
    posts_with_geo: number;
    geo_coverage_pct: number;
    excluded_bots: number;
    country_count: number;
}

export interface CountryStats {
    country_code: string;
    country_name: string;
    post_count: number;
    avg_sentiment: number;
}

export async function fetchGeoSentiment(window: TimeWindow = '7d'): Promise<GeoSentimentData> {
    return fetchJSON<GeoSentimentData>(`/geo-sentiment?window=${window}`);
}

export async function fetchNarratives(window: TimeWindow = '7d', limit: number = 20): Promise<NarrativeSummary[]> {
    if (USE_MOCKS) {
        const { mockNarratives } = await import('./fixtures');
        return mockNarratives();
    }
    return fetchJSON<NarrativeSummary[]>(`/narratives?window=${window}&limit=${limit}`);
}

export async function fetchPropaganda(window: TimeWindow = '7d'): Promise<PropagandaOverview> {
    return fetchJSON<PropagandaOverview>(`/propaganda?window=${window}`);
}

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

export async function submitReview(submission: ReviewSubmission): Promise<{ ai_output_id: number; reviewed_at: number }> {
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
