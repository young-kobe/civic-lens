import { PublicSentimentData } from '../types';

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
        byDayOfWeek: apiData.byDayOfWeek || [],
        distribution: apiData.distribution || {
            strongPositive: 0,
            mildPositive: 0,
            neutral: 0,
            mildNegative: 0,
            strongNegative: 0
        },
        distributionSamples: apiData.distributionSamples || {},
        // Social vs News comparison data
        socialVsNews: apiData.socialVsNews || null,
        // Merged GOP favorability data
        gopFavorability: apiData.gopFavorability || null,
        gopTrend: apiData.gopTrend || null,
        gopByPlatform: apiData.gopByPlatform || null,
        pollingVsSocial: apiData.pollingVsSocial || null,
    };
}

