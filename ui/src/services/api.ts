import {
    PublicSentimentData, BotData, NarrativeSummary,
    PropagandaOverview,
    ReviewQueueItem, ReviewSubmission, ReviewStats, ReviewTaskType,
} from '../types';

const API_BASE = '/api';

export type TimeWindow = '24h' | '7d' | '30d' | '90d' | 'all';

export async function fetchSentiment(window: TimeWindow = '24h'): Promise<PublicSentimentData> {
    const response = await fetch(`${API_BASE}/sentiment?window=${window}`);
    if (!response.ok) {
        throw new Error(`Failed to fetch sentiment: ${response.statusText}`);
    }
    return response.json();
}

export async function fetchBotActivity(): Promise<BotData> {
    const response = await fetch(`${API_BASE}/bot-activity`);
    if (!response.ok) {
        throw new Error(`Failed to fetch bot activity: ${response.statusText}`);
    }
    return response.json();
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
    const response = await fetch(`${API_BASE}/geo-sentiment?window=${window}`);
    if (!response.ok) {
        throw new Error(`Failed to fetch geo sentiment: ${response.statusText}`);
    }
    return response.json();
}

export async function fetchNarratives(window: TimeWindow = '7d', limit: number = 20): Promise<NarrativeSummary[]> {
    const response = await fetch(`${API_BASE}/narratives?window=${window}&limit=${limit}`);
    if (!response.ok) {
        throw new Error(`Failed to fetch narratives: ${response.statusText}`);
    }
    return response.json();
}

export async function fetchPropaganda(window: TimeWindow = '7d'): Promise<PropagandaOverview> {
    const response = await fetch(`${API_BASE}/propaganda?window=${window}`);
    if (!response.ok) {
        throw new Error(`Failed to fetch propaganda: ${response.statusText}`);
    }
    return response.json();
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
    const response = await fetch(`${API_BASE}/review/queue?${qs}`);
    if (!response.ok) {
        throw new Error(`Failed to fetch review queue: ${response.statusText}`);
    }
    return response.json();
}

export async function submitReview(submission: ReviewSubmission): Promise<{ ai_output_id: number; reviewed_at: number }> {
    const response = await fetch(`${API_BASE}/review/submit`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(submission),
    });
    if (!response.ok) {
        throw new Error(`Failed to submit review: ${response.statusText}`);
    }
    return response.json();
}

export async function fetchReviewStats(task?: ReviewTaskType): Promise<ReviewStats> {
    const url = task ? `${API_BASE}/review/stats?task=${task}` : `${API_BASE}/review/stats`;
    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(`Failed to fetch review stats: ${response.statusText}`);
    }
    return response.json();
}
