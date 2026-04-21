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
    const resp = await fetch(`${API_BASE}${path}`, { ...rest, headers: mergedHeaders });
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
