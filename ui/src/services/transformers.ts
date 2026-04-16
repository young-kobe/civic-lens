
import { Cluster, FavorabilityData, BotData, PublicSentimentData } from '../types';

export function transformStories(apiData: any[]): Cluster[] {
    return apiData.map(item => ({
        id: item.id,
        title: item.title,
        articleCount: item.articleCount,
        contentType: item.contentType || 'mixed',
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
    // API byPlatform: [{ group: 'News', favorable, unfavorable, neutral }, ...]
    const byPlatform = (apiData.byPlatform || []).map((p: any) => ({
        group: p.group || p.platform,
        favorable: p.favorable || 0,
        unfavorable: p.unfavorable || 0,
        neutral: p.neutral || 0
    }));

    // Map API pollingVsSocial (onlineSentiment/pollingData) to frontend (polling/social)
    const pollingVsSocial = apiData.pollingVsSocial || {};

    // Handle null/undefined pollingData - use safe defaults
    const pollingData = pollingVsSocial.pollingData || {};

    return {
        overall: apiData.overall,
        trend: apiData.trend || [],
        trendAnnotations: apiData.trendAnnotations || [],
        byAge: [],
        byRegion: [],
        byPartyId: [],
        byPlatform: byPlatform,
        pollingVsSocial: {
            onlineSentiment: pollingVsSocial.onlineSentiment || { favorable: 0, unfavorable: 0, neutral: 0 },
            pollingData: pollingData.favorable !== undefined ? {
                favorable: pollingData.favorable ?? 0,
                unfavorable: pollingData.unfavorable ?? 0,
                neutral: pollingData.neutral ?? 0,
                source: pollingData.source,
                date: pollingData.date,
            } : null,
        },
    };
}

export function transformPublicSentiment(apiData: any): PublicSentimentData {
    return {
        overview: apiData.overview,
        byTopic: (apiData.byTopic || []).map((t: any) => ({
            topic: t.topic,
            positive: t.positive || 0,
            negative: t.negative || 0,
            neutral: t.neutral || 0,
            volume: t.volume || 0,
            sarcasm_rate: t.sarcasm_rate || 0,
            classificationSamples: t.classificationSamples || [],
        })),
        byPlatform: (apiData.byPlatform || []).map((p: any) => ({
            platform: p.platform,
            positive: p.positive || 0,
            negative: p.negative || 0,
            neutral: p.neutral || 0,
            volume: p.volume || 0,
        })),
        byTimeWindow: apiData.byTimeWindow || [],
        distribution: apiData.distribution || {
            strongPositive: 0,
            mildPositive: 0,
            neutral: 0,
            mildNegative: 0,
            strongNegative: 0
        },
        // Social vs News comparison data
        socialVsNews: apiData.socialVsNews || null,
        // Merged GOP favorability data
        gopFavorability: apiData.gopFavorability || null,
        gopTrend: apiData.gopTrend || null,
        gopByPlatform: apiData.gopByPlatform || null,
        pollingVsSocial: apiData.pollingVsSocial || null,
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
