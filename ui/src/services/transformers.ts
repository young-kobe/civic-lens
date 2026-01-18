
import { Cluster, FavorabilityData, BotData, PublicSentimentData } from '../types';

export function transformStories(apiData: any[]): Cluster[] {
    return apiData.map(item => ({
        id: item.id,
        title: item.title,
        articleCount: item.articleCount,
        momentum: item.momentum || { delta24h: 0, delta7d: 0 },
        primarySources: item.primarySources || [],
        summary: item.summary || [],
        keyClaims: item.keyClaims || [],
        entities: item.entities || { people: [], organizations: [], locations: [] },
        sourceMix: item.sourceMix || [],
        timeline: (item.timeline || []).map((t: any) => ({
            date: t.date,
            value: t.value
        })),
        articles: item.articles || []
    }));
}

export function transformFavorability(apiData: any): FavorabilityData {
    // Map API byPlatform to DemographicBreakdown if possible
    // API byPlatform: [{ platform: 'news_article', ... }, ...]
    // DemographicBreakdown: { group/region/party?, favorable, unfavorable, neutral }
    const byPlatform = (apiData.byPlatform || []).map((p: any) => ({
        group: p.platform, // use group field for platform name
        favorable: p.positive || 0,
        unfavorable: p.negative || 0,
        neutral: p.neutral || 0
    }));

    return {
        overall: apiData.overall,
        trend: apiData.trend || [],
        trendAnnotations: apiData.trendAnnotations || [],
        byAge: [], // Default empty
        byRegion: [], // Default empty
        byPartyId: [], // Default empty
        byPlatform: byPlatform,
        pollingVsSocial: apiData.pollingVsSocial || {
            polling: { favorable: 0, unfavorable: 0, neutral: 0 },
            social: { favorable: 0, unfavorable: 0, neutral: 0 }
        },
    } as FavorabilityData & { byPlatform: any[] };
}

export function transformPublicSentiment(apiData: any): PublicSentimentData {
    return {
        overview: apiData.overview,
        byTopic: apiData.byTopic || [],
        byPlatform: apiData.byPlatform || [],
        byTimeWindow: apiData.byTimeWindow || [],
        distribution: apiData.distribution || {
            strongPositive: 0,
            mildPositive: 0,
            neutral: 0,
            mildNegative: 0,
            strongNegative: 0
        }
    };
}

export function transformBotData(profiles: any[]): BotData {
    // Transform outlet profiles into a summary BotData object
    const totalScanned = profiles.reduce((acc, p) => acc + p.total_scanned, 0);
    const totalBot = profiles.reduce((acc, p) => acc + p.bot_flags, 0);
    const avgBotRate = totalScanned > 0 ? (totalBot / totalScanned) * 100 : 0;

    return {
        overview: {
            suspectedAutomationRate: Math.round(avgBotRate * 10) / 10,
            narrativeAmplification: "Low", // Placeholder
            coordinatedNetworks: 0 // Placeholder
        },
        narrativeAmplification: [
            // Placeholder for now, could be derived if we had more data
        ],
        coordinationStats: {
            // Simplify to showing top bot-heavy outlets
        },
        behavioralSignals: profiles.slice(0, 5).map(p => ({
            signal: `High Bot Activity: ${p.outlet}`,
            impact: "High",
            confidence: 0.9,
            description: `${Math.round(p.bot_rate * 100)}% of content flagged as bot or suspicious.`
        }))
    } as unknown as BotData;
}
