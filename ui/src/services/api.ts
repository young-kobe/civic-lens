import { PublicSentimentData, BotData } from '../types';

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
