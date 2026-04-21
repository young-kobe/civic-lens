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
    ClassificationSample,
    HeatmapDataPoint,
    NarrativeSummary,
    PublicSentimentData,
    SentimentSegmentKey,
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
            { window: '24 hours', positive: 310, negative: 420, neutral: 520, volume: 1250 },
            { window: '7 days', positive: 620, negative: 850, neutral: 1180, volume: 2650 },
            { window: '30 days', positive: 480, negative: 720, neutral: 890, volume: 2090 },
        ],
        byDayOfWeek: [
            { day: 'Mon', positive: 170, negative: 280, neutral: 410, volume: 860 },
            { day: 'Tue', positive: 195, negative: 310, neutral: 445, volume: 950 },
            { day: 'Wed', positive: 210, negative: 335, neutral: 430, volume: 975 },
            { day: 'Thu', positive: 225, negative: 360, neutral: 420, volume: 1005 },
            { day: 'Fri', positive: 240, negative: 305, neutral: 400, volume: 945 },
            { day: 'Sat', positive: 210, negative: 245, neutral: 360, volume: 815 },
            { day: 'Sun', positive: 160, negative: 305, neutral: 320, volume: 785 },
        ],
        distribution,
        distributionSamples: mockDistributionSamples(),
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

/* ---------- Distribution drill-down samples ---------- */

function sample(
    base: Omit<ClassificationSample, 'doc_id' | 'evidence_spans' | 'sarcasm_detected' | 'reasoning'> & {
        reasoning: string;
        evidence: string[];
        sarcasm?: boolean;
    },
    docId: number,
): ClassificationSample {
    const { reasoning, evidence, sarcasm, ...rest } = base;
    return {
        doc_id: docId,
        reasoning,
        evidence_spans: evidence,
        sarcasm_detected: !!sarcasm,
        ...rest,
    };
}

function mockDistributionSamples(): Partial<Record<SentimentSegmentKey, ClassificationSample[]>> {
    return {
        strongNegative: [
            sample({
                label: 'NEGATIVE', confidence: 0.94,
                title: '"Catastrophic failure of leadership" — op-ed slams border response',
                source_type: 'news', source_name: 'nypost.com', date: isoDay(1),
                url: 'https://example.com/op-ed-border',
                full_text: 'The administration has failed on every metric that matters...',
                reasoning: 'Multiple intensifiers ("catastrophic", "every metric"), zero qualification, explicit blame.',
                evidence: ['catastrophic failure of leadership', 'failed on every metric that matters'],
            }, 88421),
            sample({
                label: 'NEGATIVE', confidence: 0.91,
                title: 'They are literally destroying this country in real time',
                source_type: 'x_post', source_name: '@politics_pundit', date: isoDay(2),
                url: 'https://twitter.com/politics_pundit/status/123',
                full_text: 'They are literally destroying this country in real time. Nothing subtle about it.',
                reasoning: 'Absolute terms ("literally destroying"), present-tense accusation.',
                evidence: ['literally destroying this country', 'Nothing subtle about it'],
            }, 90884),
            sample({
                label: 'NEGATIVE', confidence: 0.89,
                title: 'Comment: this is the worst administration in my lifetime',
                source_type: 'reddit_post', source_name: 'r/politics', date: isoDay(3),
                url: 'https://reddit.com/r/politics/comments/xyz',
                full_text: 'Nothing good has come from this. Worst admin in my lifetime, full stop.',
                reasoning: 'Superlative + emphatic closure ("full stop").',
                evidence: ['worst administration in my lifetime', 'full stop'],
            }, 91002),
        ],
        mildNegative: [
            sample({
                label: 'NEGATIVE', confidence: 0.72,
                title: 'Critics say drug pricing plan falls short of promises',
                source_type: 'news', source_name: 'reuters.com', date: isoDay(1),
                url: 'https://example.com/drug-pricing-critics',
                full_text: 'The plan has merits but critics note it exempts several major classes...',
                reasoning: 'Criticism acknowledged but softened by "has merits". Net-negative but qualified.',
                evidence: ['falls short of promises', 'exempts several major classes'],
            }, 90114),
            sample({
                label: 'NEGATIVE', confidence: 0.68,
                title: 'Skeptical take on the Ukraine aid package from House Republicans',
                source_type: 'news', source_name: 'apnews.com', date: isoDay(4),
                url: 'https://example.com/skeptical-ukraine',
                full_text: 'Representatives expressed unease, though several left room for compromise.',
                reasoning: 'Concerns voiced but explicit openness to compromise.',
                evidence: ['expressed unease', 'left room for compromise'],
            }, 91640),
        ],
        neutral: [
            sample({
                label: 'NEUTRAL', confidence: 0.88,
                title: 'House schedules vote on Ukraine aid package',
                source_type: 'news', source_name: 'reuters.com', date: isoDay(2),
                url: 'https://example.com/ukraine-vote-schedule',
                full_text: 'The House will vote Tuesday on the $61 billion package, sources confirmed.',
                reasoning: 'Straight reportage. Dates, figures, attribution; no evaluative language.',
                evidence: ['House will vote Tuesday', 'sources confirmed'],
            }, 91641),
            sample({
                label: 'NEUTRAL', confidence: 0.84,
                title: 'Polling data: 41% approve of handling of economy',
                source_type: 'news', source_name: 'pewresearch.org', date: isoDay(3),
                reasoning: 'Reported number, cited source, no commentary.',
                evidence: ['41% approve', 'according to Pew'],
            }, 90230),
        ],
        mildPositive: [
            sample({
                label: 'POSITIVE', confidence: 0.71,
                title: 'Bipartisan push on prescription drug pricing gains momentum',
                source_type: 'news', source_name: 'reuters.com', date: isoDay(4),
                url: 'https://example.com/bipartisan-drug',
                full_text: 'Members from both parties signaled willingness, though details remain.',
                reasoning: 'Positive framing ("gains momentum"), tempered by "details remain".',
                evidence: ['gains momentum', 'members from both parties signaled willingness'],
            }, 90115),
        ],
        strongPositive: [
            sample({
                label: 'POSITIVE', confidence: 0.93,
                title: 'Landmark drug pricing deal praised as "historic victory"',
                source_type: 'news', source_name: 'politico.com', date: isoDay(5),
                url: 'https://example.com/historic-drug-deal',
                full_text: 'Advocates called it "the biggest win in a decade" for consumers.',
                reasoning: 'Unqualified endorsement language ("historic victory", "biggest win").',
                evidence: ['historic victory', 'biggest win in a decade'],
            }, 90116),
            sample({
                label: 'POSITIVE', confidence: 0.87,
                title: 'Local healthcare rollout exceeds enrollment targets by 40%',
                source_type: 'news', source_name: 'apnews.com', date: isoDay(6),
                url: 'https://example.com/healthcare-rollout',
                full_text: 'Officials described the numbers as "a remarkable success".',
                reasoning: 'Concrete positive outcome + explicit endorsement quote.',
                evidence: ['exceeds enrollment targets by 40%', 'a remarkable success'],
            }, 90117),
        ],
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
