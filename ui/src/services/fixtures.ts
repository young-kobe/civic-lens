/**
 * Dev-only mock fixtures for the API client. Activated by setting
 * `VITE_USE_MOCKS=true` in `ui/.env.local` (gitignored), or
 * `VITE_USE_MOCKS=true npm run dev` on the CLI.
 *
 * Nothing in this file is used when the flag is off. Vite's compile-time
 * replacement of `import.meta.env.VITE_USE_MOCKS` + dynamic import in
 * `api.ts` keeps the fixtures out of the production bundle.
 *
 * To retire: delete this file and the dev-mock branches in `api.ts`.
 * No prod code touches it.
 */

import type {
    BotData,
    HeatmapDataPoint,
    NarrativeSummary,
    PublicSentimentData,
} from '../types';

const NOW = Math.floor(Date.now() / 1000);
const DAY = 24 * 60 * 60;

function daysAgo(n: number): number {
    return NOW - n * DAY;
}

function isoDay(n: number): string {
    return new Date((NOW - n * DAY) * 1000).toISOString().slice(0, 10);
}

/* ---------- Public Sentiment ---------- */

export function mockSentiment(): PublicSentimentData {
    const distribution = {
        strongPositive: 612,
        mildPositive: 798,
        neutral: 1489,
        mildNegative: 1102,
        strongNegative: 1238,
    };
    const total = Object.values(distribution).reduce((a, b) => a + b, 0);

    return {
        overview: {
            netScore: -14.2,
            volume: total,
            coverage: 'high',
            confidence: 'high',
        },
        byTopic: [
            { topic: 'Border & immigration', positive: 180, negative: 820, neutral: 410, volume: 1410, sarcasm_rate: 0.08, classificationSamples: [] },
            { topic: 'Economy & inflation', positive: 290, negative: 540, neutral: 620, volume: 1450, sarcasm_rate: 0.05, classificationSamples: [] },
            { topic: 'Foreign policy', positive: 210, negative: 340, neutral: 500, volume: 1050, sarcasm_rate: 0.03, classificationSamples: [] },
            { topic: 'Healthcare', positive: 360, negative: 280, neutral: 430, volume: 1070, sarcasm_rate: 0.04, classificationSamples: [] },
            { topic: 'Climate & energy', positive: 240, negative: 420, neutral: 380, volume: 1040, sarcasm_rate: 0.06, classificationSamples: [] },
        ],
        byPlatform: [
            { platform: 'News', positive: 480, negative: 520, neutral: 1120, volume: 2120 },
            { platform: 'Reddit', positive: 220, negative: 640, neutral: 310, volume: 1170 },
            { platform: 'X', positive: 410, negative: 428, neutral: 59, volume: 897 },
        ],
        byTimeWindow: [
            { window: 'Mon', positive: 120, negative: 210, neutral: 380, volume: 710 },
            { window: 'Tue', positive: 140, negative: 260, neutral: 410, volume: 810 },
            { window: 'Wed', positive: 155, negative: 290, neutral: 395, volume: 840 },
            { window: 'Thu', positive: 165, negative: 310, neutral: 410, volume: 885 },
            { window: 'Fri', positive: 180, negative: 270, neutral: 420, volume: 870 },
            { window: 'Sat', positive: 175, negative: 230, neutral: 400, volume: 805 },
            { window: 'Sun', positive: 175, negative: 238, neutral: 380, volume: 793 },
        ],
        distribution,
        socialVsNews: {
            social: { positive: 630, negative: 1068, neutral: 369, netScore: -21.2, volume: 2067 },
            news: { positive: 480, negative: 520, neutral: 1120, netScore: -1.9, volume: 2120 },
        },
        gopFavorability: {
            favorable: 780,
            unfavorable: 1210,
            neutral: 950,
            netFavorability: -14.7,
            sampleSize: 2940,
            sourceCount: 3,
            lastUpdated: isoDay(0),
        },
        gopTrend: [
            { date: isoDay(13), value: -8.2 },
            { date: isoDay(12), value: -9.1 },
            { date: isoDay(11), value: -8.4 },
            { date: isoDay(10), value: -10.2 },
            { date: isoDay(9), value: -11.6 },
            { date: isoDay(8), value: -12.3 },
            { date: isoDay(7), value: -11.0 },
            { date: isoDay(6), value: -13.4 },
            { date: isoDay(5), value: -14.9 },
            { date: isoDay(4), value: -14.1 },
            { date: isoDay(3), value: -15.6 },
            { date: isoDay(2), value: -16.2 },
            { date: isoDay(1), value: -15.1 },
            { date: isoDay(0), value: -14.7 },
        ],
        gopByPlatform: null,
        pollingVsSocial: {
            onlineSentiment: { favorable: 780, unfavorable: 1210, neutral: 950 },
            pollingData: {
                favorable: 41,
                unfavorable: 52,
                neutral: 7,
                source: 'Pew Research',
                date: isoDay(3),
            },
        },
    };
}

/* ---------- Narratives ---------- */

function mockTimeline(points: number, base: number, swing: number): { date: string; count: number }[] {
    const out: { date: string; count: number }[] = [];
    for (let i = points - 1; i >= 0; i--) {
        const wave = Math.sin((points - i) * 0.8) * swing;
        const noise = ((i * 7) % 5) - 2;
        out.push({
            date: isoDay(i),
            count: Math.max(0, Math.round(base + wave + noise)),
        });
    }
    return out;
}

export function mockNarratives(): NarrativeSummary[] {
    return [
        {
            narrative_id: 1001,
            name: 'Border crossings hit record high, federal response insufficient',
            first_seen_at: daysAgo(12),
            first_seen_doc_id: 88421,
            first_seen_source_type: 'news',
            first_seen_domain: 'nypost.com',
            first_seen_tier: null,
            first_seen_author: null,
            supporting_doc_count: 142,
            source_breakdown: [
                { source_type: 'news', label: 'News', count: 58 },
                { source_type: 'reddit_post', label: 'Reddit', count: 34 },
                { source_type: 'x_post', label: 'X', count: 50 },
            ],
            timeline: mockTimeline(14, 10, 6),
            net_sentiment: -38.4,
            inbound_citation_count: 23,
            propaganda_score: 0.44,
            bot_pushed_fraction: 0.18,
        },
        {
            narrative_id: 1002,
            name: 'Prescription drug pricing reform gaining bipartisan traction',
            first_seen_at: daysAgo(9),
            first_seen_doc_id: 90114,
            first_seen_source_type: 'news',
            first_seen_domain: 'reuters.com',
            first_seen_tier: null,
            first_seen_author: null,
            supporting_doc_count: 74,
            source_breakdown: [
                { source_type: 'news', label: 'News', count: 46 },
                { source_type: 'reddit_post', label: 'Reddit', count: 18 },
                { source_type: 'x_post', label: 'X', count: 10 },
            ],
            timeline: mockTimeline(14, 5, 3),
            net_sentiment: 12.7,
            inbound_citation_count: 9,
            propaganda_score: 0.08,
            bot_pushed_fraction: 0.02,
        },
        {
            narrative_id: 1003,
            name: 'Tech giants colluding to suppress conservative voices',
            first_seen_at: daysAgo(7),
            first_seen_doc_id: 90884,
            first_seen_source_type: 'x_post',
            first_seen_domain: null,
            first_seen_tier: 'affiliated',
            first_seen_author: {
                handle: 'politics_pundit',
                full_name: 'Jordan Parker',
                party: 'R',
                branch: null,
                chamber: null,
                state_or_district: null,
                office_title: null,
                account_type: 'personal',
            },
            supporting_doc_count: 203,
            source_breakdown: [
                { source_type: 'x_post', label: 'X', count: 148 },
                { source_type: 'reddit_post', label: 'Reddit', count: 42 },
                { source_type: 'news', label: 'News', count: 13 },
            ],
            timeline: mockTimeline(14, 14, 10),
            net_sentiment: -44.1,
            inbound_citation_count: 6,
            propaganda_score: 0.71,
            bot_pushed_fraction: 0.38,
        },
        {
            narrative_id: 1004,
            name: 'Ukraine aid package vote scheduled for next week',
            first_seen_at: daysAgo(3),
            first_seen_doc_id: 91640,
            first_seen_source_type: 'x_post',
            first_seen_domain: null,
            first_seen_tier: 'elected_official',
            first_seen_author: {
                handle: 'SenSmith',
                full_name: 'Jane Smith',
                party: 'D',
                branch: 'legislative',
                chamber: 'senate',
                state_or_district: 'OH',
                office_title: 'Senator',
                account_type: 'official',
            },
            supporting_doc_count: 58,
            source_breakdown: [
                { source_type: 'x_post', label: 'X', count: 31 },
                { source_type: 'news', label: 'News', count: 22 },
                { source_type: 'reddit_post', label: 'Reddit', count: 5 },
            ],
            timeline: mockTimeline(14, 3, 4),
            net_sentiment: -4.2,
            inbound_citation_count: 14,
            propaganda_score: 0.21,
            bot_pushed_fraction: 0.05,
        },
    ];
}

/* ---------- Bot Activity ---------- */

// Deterministic posting-cadence heatmap: evenings on weekdays, lighter on weekends.
function mockPostingCadence(): HeatmapDataPoint[] {
    const out: HeatmapDataPoint[] = [];
    for (let day = 0; day < 7; day++) {
        const isWeekend = day === 0 || day === 6;
        for (let hour = 0; hour < 24; hour++) {
            // base shape: rising through day, peak at 20:00, tail overnight
            const hourShape = Math.max(0, Math.sin(((hour - 2) / 24) * Math.PI)) * 100;
            const eveningBoost = hour >= 18 && hour <= 22 ? 60 : 0;
            const weekendFactor = isWeekend ? 0.55 : 1;
            const seeded = ((day * 31 + hour * 7) % 11) - 5;
            const value = Math.max(0, Math.round((hourShape + eveningBoost + seeded) * weekendFactor));
            out.push({ day, hour, value });
        }
    }
    return out;
}

export function mockBotActivity(): BotData {
    return {
        overview: {
            suspectedAutomationRate: 8.4,
            coordinationIndex: 0.42,
            topClusters: ['anti-immigration amplifiers', 'pro-candidate X ring', 'climate-denial chorus'],
            totalFlaggedAccounts: 247,
            confidence: 'medium',
        },
        narrativeAmplification: [
            {
                id: 2001,
                narrative: 'Tech giants colluding to suppress conservative voices',
                confidence: 'high',
                examplePosts: [
                    'They are BURYING every story about X, wake up people.',
                    'Same script, same timing, same accounts — coincidence?',
                ],
                topHashtags: ['#shadowban', '#censorship', '#bigtech'],
                topPhrases: ['wake up', 'same script', 'coincidence'],
                targets: ['Twitter', 'Meta'],
                suspectedBotVolume: 148,
                whyFlagged: [
                    'Near-identical phrasing across 37 accounts within a 20-minute window',
                    'Majority of accounts created within the last 90 days',
                    'High link-domain concentration (3 domains = 82% of shared URLs)',
                ],
            },
            {
                id: 2002,
                narrative: 'Border crossings hit record high, federal response insufficient',
                confidence: 'medium',
                examplePosts: [
                    'Another day, another broken border. Act now.',
                    'These numbers are UNPRECEDENTED. Why is nobody talking about this?',
                ],
                topHashtags: ['#borderCrisis', '#secureTheBorder'],
                topPhrases: ['unprecedented', 'act now', 'broken border'],
                targets: ['DHS', 'Administration'],
                suspectedBotVolume: 62,
                whyFlagged: [
                    'Bursty posting aligned to off-hours for claimed US timezones',
                    'Reused stock imagery across unrelated accounts',
                ],
            },
        ],
        coordinationStats: {
            burstTimingSimilarity: 0.67,
            accountReuse: 0.24,
            identicalTextPairs: 412,
            avgPostsPerSuspectedAccount: 38.4,
        },
        behavioralSignals: {
            accountAgeDistribution: [
                { range: '<30 days', count: 94, percentage: 38 },
                { range: '30-90 days', count: 61, percentage: 25 },
                { range: '90-365 days', count: 48, percentage: 19 },
                { range: '1-3 years', count: 29, percentage: 12 },
                { range: '>3 years', count: 15, percentage: 6 },
            ],
            postingCadence: mockPostingCadence(),
            copyPasteSimilarity: { high: 89, medium: 110, low: 48 },
            linkDomainConcentration: [
                { domain: 'substack.com', percentage: 31 },
                { domain: 'youtube.com', percentage: 22 },
                { domain: 't.me', percentage: 14 },
                { domain: 'rumble.com', percentage: 9 },
                { domain: 'breitbart.com', percentage: 7 },
            ],
        },
    };
}
