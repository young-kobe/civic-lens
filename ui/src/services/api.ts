
import { Cluster, FavorabilityData, PublicSentimentData, BotData } from '../types';

const API_BASE = '/api';

export async function fetchStories(): Promise<Cluster[]> {
    const response = await fetch(`${API_BASE}/stories`);
    if (!response.ok) {
        throw new Error(`Failed to fetch stories: ${response.statusText}`);
    }
    return response.json();
}

export async function fetchFavorability(): Promise<FavorabilityData> {
    const response = await fetch(`${API_BASE}/favorability`);
    if (!response.ok) {
        throw new Error(`Failed to fetch favorability: ${response.statusText}`);
    }
    return response.json();
}

export async function fetchSentiment(): Promise<PublicSentimentData> {
    const response = await fetch(`${API_BASE}/sentiment`);
    if (!response.ok) {
        throw new Error(`Failed to fetch sentiment: ${response.statusText}`);
    }
    return response.json();
}

export async function fetchBotProfiles(): Promise<any[]> {
    // Note: The UI expects BotData (aggregated), but API returns List<OutletProfile>
    // Transformation happens in transformers.ts or the component
    const response = await fetch(`${API_BASE}/profiles`);
    if (!response.ok) {
        throw new Error(`Failed to fetch profiles: ${response.statusText}`);
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
