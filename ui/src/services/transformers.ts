import type { EntitySentimentItem, PublicSentimentData, SentimentBreakdown } from '../types';

/**
 * Normalize the /api/v1/sentiment payload into `PublicSentimentData`.
 *
 * Historically this transformer whitelisted fields per shape, which meant
 * any new field the aggregator added (per-tier topic splits, three-way
 * entity rollups, etc.) would be silently dropped on the way through and
 * the consumer would render an empty state with no error. The fix is to
 * pass each row through as-is and only overlay defensive defaults on the
 * fields the UI's hot paths assume are present.
 */
export function transformPublicSentiment(apiData: any): PublicSentimentData {
    return {
        overview: apiData.overview,
        byTopic: (apiData.byTopic || []).map(normalizeTopicRow),
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
            strongNegative: 0,
        },
        distributionSamples: apiData.distributionSamples || {},
        // Three-way entity rollups — passed through verbatim. The aggregator
        // already shapes each row to match `EntitySentimentItem`; whitelisting
        // here previously silently dropped the entire field and emptied the
        // News / Officials / Public columns of the three-way grid.
        byNewsOutlet: (apiData.byNewsOutlet || []) as EntitySentimentItem[],
        byOfficial: (apiData.byOfficial || []) as EntitySentimentItem[],
        byGeneralPublic: (apiData.byGeneralPublic || []) as EntitySentimentItem[],
        // Social vs News comparison data
        socialVsNews: apiData.socialVsNews || null,
        // Merged GOP favorability data
        gopFavorability: apiData.gopFavorability || null,
        gopTrend: apiData.gopTrend || null,
        gopByPlatform: apiData.gopByPlatform || null,
        pollingVsSocial: apiData.pollingVsSocial || null,
    };
}

/**
 * Spread the source row, then defensively zero the count fields the UI's
 * hot paths read without a null check. Per-tier three-way fields
 * (`newsNet` / `officialsNet` / `publicNet`, ...) ride through verbatim —
 * they're optional in the type, and the aggregator emits `null` for
 * tiers with zero volume so the UI can distinguish "no data" from a
 * real zero. Coercing them to 0 here would erase that signal.
 */
function normalizeTopicRow(t: any): SentimentBreakdown {
    return {
        ...t,
        positive: t.positive || 0,
        negative: t.negative || 0,
        neutral: t.neutral || 0,
        volume: t.volume || 0,
        sarcasm_rate: t.sarcasm_rate ?? 0,
        classificationSamples: t.classificationSamples || [],
    };
}
