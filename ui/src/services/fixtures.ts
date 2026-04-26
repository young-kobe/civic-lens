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
    AccountProfile,
    BotData,
    EntityProfile,
    EntitySentimentItem,
    HeatmapDataPoint,
    ClassificationSample,
    MoversResult,
    NarrativeSourceBreakdownItem,
    NarrativeSummary,
    SupportingDoc,
    PropagandaEntityItem,
    PropagandaOverview,
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
            { topic: 'Immigration', positive: 180, negative: 820, neutral: 410, volume: 1410, sarcasm_rate: 0.08, classificationSamples: [],
              newsNet: -24.1, officialsNet: -48.3, publicNet: -55.2, newsVolume: 540, officialsVolume: 210, publicVolume: 660 },
            { topic: 'Economy', positive: 290, negative: 540, neutral: 620, volume: 1450, sarcasm_rate: 0.05, classificationSamples: [],
              newsNet: -8.5, officialsNet: 12.4, publicNet: -22.0, newsVolume: 620, officialsVolume: 180, publicVolume: 650 },
            { topic: 'Foreign Policy', positive: 210, negative: 340, neutral: 500, volume: 1050, sarcasm_rate: 0.03, classificationSamples: [],
              newsNet: -5.2, officialsNet: 18.0, publicNet: -14.6, newsVolume: 480, officialsVolume: 150, publicVolume: 420 },
            { topic: 'Healthcare', positive: 360, negative: 280, neutral: 430, volume: 1070, sarcasm_rate: 0.04, classificationSamples: [],
              newsNet: 10.1, officialsNet: 4.2, publicNet: 15.8, newsVolume: 410, officialsVolume: 90, publicVolume: 570 },
            { topic: 'Climate', positive: 240, negative: 420, neutral: 380, volume: 1040, sarcasm_rate: 0.06, classificationSamples: [],
              newsNet: -15.4, officialsNet: null, publicNet: -22.8, newsVolume: 460, officialsVolume: 0, publicVolume: 580 },
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
        byNewsOutlet: mockOutletSentiment(),
        byOfficial: mockOfficialSentiment(),
        byGeneralPublic: mockGeneralPublicSentiment(),
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

/* ---------- Three-way entity rollups (walkthrough 058) ---------- */

function outletProfile(
    key: string, displayName: string, lean: string, leanSource: string, blurb: string,
): EntityProfile {
    return { kind: 'outlet', key, displayName, lean, leanSource, blurb };
}

function officialProfile(
    key: string, displayName: string, office: string, party: string, blurb: string,
): EntityProfile {
    return {
        kind: 'official', key, displayName, office, party, blurb,
        lean: null, leanSource: null,
    };
}

function subredditProfile(
    key: string, displayName: string, lean: string, leanSource: string, blurb: string,
): EntityProfile {
    return { kind: 'subreddit', key, displayName, lean, leanSource, blurb };
}

function catchAll(key: string, displayName: string, blurb: string): EntityProfile {
    return { kind: 'catch_all', key, displayName, blurb, lean: null, leanSource: null };
}

function entityItem(
    profile: EntityProfile,
    counts: { positive: number; negative: number; neutral: number },
): EntitySentimentItem {
    const volume = counts.positive + counts.negative + counts.neutral;
    const net = volume > 0 ? ((counts.positive - counts.negative) / volume) * 100 : 0;
    return {
        key: profile.key,
        kind: profile.kind,
        ...counts,
        volume,
        netScore: Math.round(net * 10) / 10,
        entityProfile: profile,
        classificationSamples: [],
    };
}

function mockOutletSentiment(): EntitySentimentItem[] {
    return [
        entityItem(
            outletProfile('nytimes.com', 'The New York Times', 'center-left',
                'AllSides 2024 (Lean Left)',
                'A general-interest daily of record and the largest US paper by paid digital subscriptions.'),
            { positive: 140, negative: 220, neutral: 320 },
        ),
        entityItem(
            outletProfile('foxnews.com', 'Fox News', 'right', 'AllSides 2024 (Right)',
                'The most-watched US cable news network and the dominant conservative TV voice.'),
            { positive: 95, negative: 310, neutral: 180 },
        ),
        entityItem(
            outletProfile('bbc.com', 'BBC', 'center', 'AllSides 2024 (Center)',
                'UK public broadcaster; one of the most-read non-US outlets covering American politics.'),
            { positive: 130, negative: 160, neutral: 310 },
        ),
        entityItem(
            catchAll('other-outlets', 'Other news outlets',
                'News docs whose domain is not in the tracked outlet registry.'),
            { positive: 115, negative: 180, neutral: 240 },
        ),
    ];
}

function mockOfficialSentiment(): EntitySentimentItem[] {
    return [
        entityItem(
            officialProfile('potus', 'Donald J. Trump', 'President of the United States', 'R',
                '47th President; high-volume X poster driving news cycles.'),
            { positive: 58, negative: 22, neutral: 30 },
        ),
        entityItem(
            officialProfile('senschumer', 'Chuck Schumer', 'Senate Minority Leader', 'D',
                'Senior Democratic senator from New York; Senate minority leader since Jan 2025.'),
            { positive: 18, negative: 44, neutral: 12 },
        ),
        entityItem(
            officialProfile('speakerjohnson', 'Mike Johnson', 'Speaker of the House', 'R',
                'Speaker of the US House since October 2023.'),
            { positive: 30, negative: 15, neutral: 18 },
        ),
    ];
}

function mockGeneralPublicSentiment(): EntitySentimentItem[] {
    return [
        entityItem(
            subredditProfile('politics', 'r/politics', 'left',
                'Community sidebar + widely documented liberal skew',
                'The largest general US-politics subreddit; ~8M subscribers with a strong liberal skew.'),
            { positive: 110, negative: 380, neutral: 210 },
        ),
        entityItem(
            subredditProfile('conservative', 'r/Conservative', 'right',
                'Community sidebar + flair-gated participation',
                '~1.3M-subscriber subreddit; sidebar positions it as a "safe space for conservatives".'),
            { positive: 180, negative: 60, neutral: 95 },
        ),
        entityItem(
            catchAll('other-x-users', 'Other X users',
                'X posts whose author is not in the tracked officials registry.'),
            { positive: 220, negative: 360, neutral: 140 },
        ),
    ];
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

// Real entity profiles the registry would emit — reused across narratives
// so the officials column actually populates with recognisable names.
const NYT_PROFILE = outletProfile(
    'nytimes.com', 'The New York Times', 'center-left', 'AllSides 2024 (Lean Left)',
    'General-interest daily of record; largest US paper by paid digital subs.',
);
const FOX_PROFILE = outletProfile(
    'foxnews.com', 'Fox News', 'right', 'AllSides 2024 (Right)',
    'Most-watched US cable network; dominant conservative TV voice.',
);
const BBC_PROFILE = outletProfile(
    'bbc.com', 'BBC', 'center', 'AllSides 2024 (Center)',
    'UK public broadcaster; one of the most-read non-US outlets on US politics.',
);
const POTUS_PROFILE = officialProfile(
    'potus', 'Donald J. Trump', 'President of the United States', 'R',
    '47th President; high-volume X poster driving daily news cycles.',
);
const SCHUMER_PROFILE = officialProfile(
    'senschumer', 'Chuck Schumer', 'Senate Minority Leader', 'D',
    'Senior NY Democrat; Senate minority leader since Jan 2025.',
);
const JOHNSON_PROFILE = officialProfile(
    'speakerjohnson', 'Mike Johnson', 'Speaker of the House', 'R',
    'Speaker of the US House since October 2023.',
);
const R_POLITICS_PROFILE = subredditProfile(
    'politics', 'r/politics', 'left', 'Community sidebar + widely-documented liberal skew',
    'The largest general US-politics subreddit; ~8M subscribers.',
);

// Short helper to build one supporting-doc row for narrative drill-down mocks.
function supDoc(n: {
    id: number; title: string;
    sourceType: 'news' | 'x_post' | 'reddit_post' | 'reddit_comment';
    sourceLabel: string;
    url: string | null;
    daysAgo: number;
    sentiment: 'positive' | 'negative' | 'neutral';
    confidence: number;
    reasoning: string;
}): SupportingDoc {
    return {
        doc_id: n.id,
        title: n.title,
        source_type: n.sourceType,
        source_label: n.sourceLabel,
        url: n.url,
        published_at: daysAgo(n.daysAgo),
        sentiment_label: n.sentiment,
        confidence: n.confidence,
        reasoning: n.reasoning,
    };
}

// Helper that builds a narrative dict — less repetition per entry below.
function narrative(n: {
    id: number; name: string;
    seenAgoDays: number;
    profile: EntityProfile;
    tierGroup: 'news' | 'officials' | 'public';
    sourceType: string;
    domain: string | null;
    author?: AccountProfile | null;
    docs: number;
    mix: { news: number; reddit: number; x: number };
    net: number;
    citations: number;
    propaganda: number | null;
    botPushed: number | null;
    cross: boolean;
    trendBase?: number;
    supporting?: SupportingDoc[];
}): NarrativeSummary {
    const mix: NarrativeSourceBreakdownItem[] = [];
    if (n.mix.news)   mix.push({ source_type: 'news',        label: 'News',   count: n.mix.news });
    if (n.mix.reddit) mix.push({ source_type: 'reddit_post', label: 'Reddit', count: n.mix.reddit });
    if (n.mix.x)      mix.push({ source_type: 'x_post',      label: 'X',      count: n.mix.x });
    return {
        narrative_id: n.id,
        name: n.name,
        first_seen_at: daysAgo(n.seenAgoDays),
        first_seen_doc_id: 90000 + n.id,
        first_seen_source_type: n.sourceType,
        first_seen_domain: n.domain,
        first_seen_tier: n.author?.office_title ? 'elected_official' : null,
        first_seen_author: n.author ?? null,
        supporting_doc_count: n.docs,
        source_breakdown: mix,
        timeline: mockTimeline(14, n.trendBase ?? Math.max(3, Math.round(n.docs / 14)), 4),
        net_sentiment: n.net,
        inbound_citation_count: n.citations,
        propaganda_score: n.propaganda,
        bot_pushed_fraction: n.botPushed,
        first_seen_entity_profile: n.profile,
        first_seen_tier_group: n.tierGroup,
        cross_tier: n.cross,
        top_supporting_docs: n.supporting ?? [],
    };
}

export function mockNarratives(): NarrativeSummary[] {
    return [
        // --- News-tier ------------------------------------------------
        narrative({
            id: 1001,
            name: 'Border crossings hit record high; federal response insufficient',
            seenAgoDays: 12, sourceType: 'news', domain: 'nytimes.com',
            profile: NYT_PROFILE, tierGroup: 'news',
            docs: 142, mix: { news: 58, reddit: 34, x: 50 },
            net: -38.4, citations: 23, propaganda: 0.44, botPushed: 0.18, cross: true,
            trendBase: 10,
            supporting: [
                supDoc({
                    id: 910011, title: 'Border Crossings Surge to Record Levels, Straining Federal Response',
                    sourceType: 'news', sourceLabel: 'News · nytimes.com', daysAgo: 12,
                    url: 'https://www.nytimes.com/2026/04/10/us/politics/border-crossings-record.html',
                    sentiment: 'negative', confidence: 0.92,
                    reasoning: 'Reports unprecedented crossing numbers and quotes officials calling response "inadequate".',
                }),
                supDoc({
                    id: 910012, title: 'DHS Official Admits Agency Overwhelmed by April Surge',
                    sourceType: 'news', sourceLabel: 'News · washingtonpost.com', daysAgo: 11,
                    url: 'https://www.washingtonpost.com/politics/2026/04/11/dhs-april-surge',
                    sentiment: 'negative', confidence: 0.88,
                    reasoning: 'Direct admission of agency incapacity; tone critical of executive handling.',
                }),
                supDoc({
                    id: 910013, title: 'Fox Panel: Administration Has "No Plan" for Border',
                    sourceType: 'news', sourceLabel: 'News · foxnews.com', daysAgo: 10,
                    url: 'https://www.foxnews.com/politics/administration-no-plan-border',
                    sentiment: 'negative', confidence: 0.79,
                    reasoning: 'Opinion panel harshly critical; loaded language flagged but not propaganda-level.',
                }),
                supDoc({
                    id: 910014, title: 'r/politics thread: "Why nothing is being done about the border"',
                    sourceType: 'reddit_post', sourceLabel: 'Reddit · r/politics', daysAgo: 9,
                    url: 'https://reddit.com/r/politics/comments/xyz910014',
                    sentiment: 'negative', confidence: 0.71,
                    reasoning: 'Community thread echoing the "federal response insufficient" framing; mostly critical.',
                }),
                supDoc({
                    id: 910015, title: 'POTUS: "Our southern border is more secure than ever before."',
                    sourceType: 'x_post', sourceLabel: 'X · @POTUS', daysAgo: 8,
                    url: 'https://x.com/POTUS/status/910015',
                    sentiment: 'positive', confidence: 0.83,
                    reasoning: 'Official counter-framing; categorized under the same narrative cluster by contrast.',
                }),
                supDoc({
                    id: 910016, title: 'Border sheriffs say resource shortfalls continuing into Q2',
                    sourceType: 'news', sourceLabel: 'News · apnews.com', daysAgo: 6,
                    url: 'https://apnews.com/article/border-sheriffs-resources-910016',
                    sentiment: 'negative', confidence: 0.86,
                    reasoning: 'Straight reporting of local-official concerns; reinforces the "insufficient response" framing.',
                }),
            ],
        }),
        narrative({
            id: 1002,
            name: 'Prescription-drug pricing reform gains bipartisan traction',
            seenAgoDays: 9, sourceType: 'news', domain: 'bbc.com',
            profile: BBC_PROFILE, tierGroup: 'news',
            docs: 74, mix: { news: 46, reddit: 18, x: 10 },
            net: 12.7, citations: 9, propaganda: 0.08, botPushed: 0.02, cross: true,
            trendBase: 5,
        }),
        narrative({
            id: 1003,
            name: 'New immigration bill stalls in committee amid partisan divide',
            seenAgoDays: 6, sourceType: 'news', domain: 'foxnews.com',
            profile: FOX_PROFILE, tierGroup: 'news',
            docs: 61, mix: { news: 39, reddit: 12, x: 10 },
            net: -21.4, citations: 6, propaganda: 0.28, botPushed: 0.06, cross: false,
            trendBase: 4,
        }),

        // --- Officials-tier ------------------------------------------
        narrative({
            id: 2001,
            name: 'Trump: "We will impose reciprocal tariffs on every country that has tariffed us"',
            seenAgoDays: 5, sourceType: 'x_post', domain: null,
            profile: POTUS_PROFILE, tierGroup: 'officials',
            author: {
                handle: 'POTUS', full_name: 'Donald J. Trump', party: 'R', branch: 'executive',
                chamber: null, state_or_district: null,
                office_title: 'President', account_type: 'official',
            },
            docs: 188, mix: { news: 42, reddit: 56, x: 90 },
            net: -12.3, citations: 14, propaganda: 0.32, botPushed: 0.24, cross: true,
            trendBase: 13,
            supporting: [
                supDoc({
                    id: 920011, title: '"Reciprocal tariffs on every country that has tariffed us."',
                    sourceType: 'x_post', sourceLabel: 'X · @POTUS', daysAgo: 5,
                    url: 'https://x.com/POTUS/status/920011',
                    sentiment: 'neutral', confidence: 0.74,
                    reasoning: 'Policy-declaration tweet; reads as directive rather than opinion.',
                }),
                supDoc({
                    id: 920012, title: 'White House: Reciprocal Tariff Framework Details Released',
                    sourceType: 'news', sourceLabel: 'News · reuters.com', daysAgo: 5,
                    url: 'https://www.reuters.com/markets/us/reciprocal-tariff-framework-920012',
                    sentiment: 'neutral', confidence: 0.90,
                    reasoning: 'Straight policy reporting without editorialization.',
                }),
                supDoc({
                    id: 920013, title: 'WSJ editorial: "Reciprocal Tariffs Risk Inflation Spike"',
                    sourceType: 'news', sourceLabel: 'News · wsj.com', daysAgo: 4,
                    url: 'https://www.wsj.com/articles/reciprocal-tariffs-inflation-920013',
                    sentiment: 'negative', confidence: 0.85,
                    reasoning: 'Opinion piece critical of tariff approach on economic grounds.',
                }),
                supDoc({
                    id: 920014, title: 'r/Conservative: "Finally, reciprocal tariffs — about time"',
                    sourceType: 'reddit_post', sourceLabel: 'Reddit · r/Conservative', daysAgo: 4,
                    url: 'https://reddit.com/r/Conservative/comments/xyz920014',
                    sentiment: 'positive', confidence: 0.78,
                    reasoning: 'Supportive community thread; clearly favorable framing.',
                }),
                supDoc({
                    id: 920015, title: 'Schumer: "Reciprocal tariffs are a tax on American families."',
                    sourceType: 'x_post', sourceLabel: 'X · @SenSchumer', daysAgo: 3,
                    url: 'https://x.com/SenSchumer/status/920015',
                    sentiment: 'negative', confidence: 0.91,
                    reasoning: 'Opposition framing from Senate minority leader; rhetorical attack.',
                }),
            ],
        }),
        narrative({
            id: 2002,
            name: 'Schumer demands hearing on campus-speech enforcement order',
            seenAgoDays: 4, sourceType: 'x_post', domain: null,
            profile: SCHUMER_PROFILE, tierGroup: 'officials',
            author: {
                handle: 'SenSchumer', full_name: 'Chuck Schumer', party: 'D', branch: 'legislative',
                chamber: 'senate', state_or_district: 'NY',
                office_title: 'Senator', account_type: 'official',
            },
            docs: 64, mix: { news: 18, reddit: 9, x: 37 },
            net: -8.1, citations: 4, propaganda: 0.14, botPushed: 0.04, cross: true,
            trendBase: 5,
        }),
        narrative({
            id: 2003,
            name: 'Speaker Johnson: House will advance continuing resolution this week',
            seenAgoDays: 2, sourceType: 'x_post', domain: null,
            profile: JOHNSON_PROFILE, tierGroup: 'officials',
            author: {
                handle: 'SpeakerJohnson', full_name: 'Mike Johnson', party: 'R', branch: 'legislative',
                chamber: 'house', state_or_district: 'LA04',
                office_title: 'Representative', account_type: 'official',
            },
            docs: 48, mix: { news: 15, reddit: 6, x: 27 },
            net: 4.8, citations: 3, propaganda: 0.11, botPushed: 0.03, cross: false,
            trendBase: 4,
        }),

        // --- Public-tier ---------------------------------------------
        narrative({
            id: 3001,
            name: 'Tech giants colluding to suppress conservative voices',
            seenAgoDays: 7, sourceType: 'x_post', domain: null,
            profile: catchAll('other-x-users', 'Other X users',
                'X post whose author is not in the tracked officials registry.'),
            tierGroup: 'public',
            docs: 203, mix: { news: 13, reddit: 42, x: 148 },
            net: -44.1, citations: 6, propaganda: 0.71, botPushed: 0.38, cross: true,
            trendBase: 14,
            supporting: [
                supDoc({
                    id: 930011, title: '"They are BURYING every story about X — wake up people."',
                    sourceType: 'x_post', sourceLabel: 'X · unverified account', daysAgo: 7,
                    url: 'https://x.com/_anon/status/930011',
                    sentiment: 'negative', confidence: 0.82,
                    reasoning: 'Flagged for loaded language + near-identical phrasing across a cluster of accounts.',
                }),
                supDoc({
                    id: 930012, title: 'r/Conservative: "Shadowban evidence thread (megathread)"',
                    sourceType: 'reddit_post', sourceLabel: 'Reddit · r/Conservative', daysAgo: 6,
                    url: 'https://reddit.com/r/Conservative/comments/xyz930012',
                    sentiment: 'negative', confidence: 0.80,
                    reasoning: 'Community megathread aggregating suppression claims; some evidence-based, some speculative.',
                }),
                supDoc({
                    id: 930013, title: 'Platform engagement metrics dispute suppression claims',
                    sourceType: 'news', sourceLabel: 'News · theatlantic.com', daysAgo: 5,
                    url: 'https://www.theatlantic.com/technology/archive/2026/04/930013',
                    sentiment: 'neutral', confidence: 0.87,
                    reasoning: 'Evidence-based piece weighing claims; reaches no-consensus conclusion.',
                }),
                supDoc({
                    id: 930014, title: '"Same script, same timing, same accounts — coincidence?"',
                    sourceType: 'x_post', sourceLabel: 'X · unverified account', daysAgo: 4,
                    url: 'https://x.com/_anon/status/930014',
                    sentiment: 'negative', confidence: 0.76,
                    reasoning: 'High-confidence bot-pushed classification — phrasing matches 37 other accounts.',
                }),
            ],
        }),
        narrative({
            id: 3002,
            name: 'Young voters abandoning both parties over inflation',
            seenAgoDays: 5, sourceType: 'reddit_post', domain: 'politics',
            profile: R_POLITICS_PROFILE, tierGroup: 'public',
            docs: 96, mix: { news: 7, reddit: 71, x: 18 },
            net: -17.2, citations: 2, propaganda: 0.10, botPushed: 0.05, cross: false,
            trendBase: 6,
        }),
        narrative({
            id: 3003,
            name: 'SCOTUS ruling seen as victory for religious-liberty advocates',
            seenAgoDays: 8, sourceType: 'reddit_post', domain: 'Conservative',
            profile: subredditProfile(
                'conservative', 'r/Conservative', 'right',
                'Community sidebar + flair-gated participation',
                '~1.3M-subscriber subreddit; sidebar positions it as a "safe space for conservatives".',
            ),
            tierGroup: 'public',
            docs: 54, mix: { news: 8, reddit: 39, x: 7 },
            net: 22.6, citations: 1, propaganda: 0.06, botPushed: 0.01, cross: false,
            trendBase: 4,
        }),
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
            by_news_outlet: [
                { key: 'nytimes.com', kind: 'outlet',  total_docs: 162, bot_docs: 1, bot_rate_pct: 0.6, entity_profile: NYT_PROFILE },
                { key: 'foxnews.com', kind: 'outlet',  total_docs: 128, bot_docs: 3, bot_rate_pct: 2.3, entity_profile: FOX_PROFILE },
                { key: 'bbc.com',     kind: 'outlet',  total_docs:  94, bot_docs: 0, bot_rate_pct: 0.0, entity_profile: BBC_PROFILE },
                {
                    key: 'other-outlets', kind: 'catch_all',
                    total_docs: 240, bot_docs: 4, bot_rate_pct: 1.7,
                    entity_profile: catchAll('other-outlets', 'Other news outlets',
                        'News docs whose domain is not in the tracked outlet registry.'),
                },
            ],
            by_official: [
                { key: 'potus',          kind: 'official', total_docs: 82, bot_docs: 0, bot_rate_pct: 0.0, entity_profile: POTUS_PROFILE },
                { key: 'senschumer',     kind: 'official', total_docs: 31, bot_docs: 0, bot_rate_pct: 0.0, entity_profile: SCHUMER_PROFILE },
                { key: 'speakerjohnson', kind: 'official', total_docs: 20, bot_docs: 0, bot_rate_pct: 0.0, entity_profile: JOHNSON_PROFILE },
            ],
            by_general_public: [
                {
                    key: 'other-x-users', kind: 'catch_all',
                    total_docs: 412, bot_docs: 94, bot_rate_pct: 22.8,
                    entity_profile: catchAll('other-x-users', 'Other X users',
                        'X posts whose author is not in the tracked officials registry.'),
                },
                { key: 'politics',     kind: 'subreddit', total_docs: 186, bot_docs: 18, bot_rate_pct:  9.7, entity_profile: R_POLITICS_PROFILE },
                {
                    key: 'conservative', kind: 'subreddit',
                    total_docs: 102, bot_docs: 12, bot_rate_pct: 11.8,
                    entity_profile: subredditProfile(
                        'conservative', 'r/Conservative', 'right',
                        'Community sidebar + flair-gated participation',
                        '~1.3M-subscriber subreddit; sidebar positions it as a "safe space for conservatives".',
                    ),
                },
            ],
        },
        narrativeAmplification: [
            {
                id: 2001,
                narrative: 'Tech giants colluding to suppress conservative voices',
                confidence: 'high',
                examplePosts: [
                    {
                        doc_id: 91001,
                        text: 'They are BURYING every story about X, wake up people.',
                        source_label: 'X · @liberty_patriot_88',
                        url: 'https://x.com/liberty_patriot_88/status/1781234500001',
                    },
                    {
                        doc_id: 91002,
                        text: 'Same script, same timing, same accounts — coincidence?',
                        source_label: 'X · @real_truth_seeker',
                        url: 'https://x.com/real_truth_seeker/status/1781234500002',
                    },
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
                    {
                        doc_id: 91010,
                        text: 'Another day, another broken border. Act now.',
                        source_label: 'X · @border_watch_now',
                        url: 'https://x.com/border_watch_now/status/1781234500010',
                    },
                    {
                        doc_id: 91011,
                        text: 'These numbers are UNPRECEDENTED. Why is nobody talking about this?',
                        source_label: 'Reddit · r/Conservative',
                        url: 'https://reddit.com/r/Conservative/comments/1abc011',
                    },
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


/* ---------- Propaganda ---------- */

function propagandaItem(
    profile: EntityProfile,
    stats: { total: number; flagged: number; mean: number },
): PropagandaEntityItem {
    return {
        key: profile.key,
        kind: profile.kind,
        total_docs: stats.total,
        flagged_docs: stats.flagged,
        flagged_rate_pct: Math.round((stats.flagged / stats.total) * 1000) / 10,
        mean_score: Math.round(stats.mean * 1000) / 1000,
        entity_profile: profile,
    };
}

export function mockPropaganda(): PropagandaOverview {
    return {
        window: '7d',
        total_eligible_docs: 1210,
        flagged_docs: 187,
        propaganda_rate_pct: 15.5,
        mean_score: 0.22,
        by_technique: [
            { technique: 'loaded_language', count: 88, pct_of_flagged_docs: 47.1 },
            { technique: 'name_calling', count: 61, pct_of_flagged_docs: 32.6 },
            { technique: 'appeal_to_fear', count: 46, pct_of_flagged_docs: 24.6 },
            { technique: 'whataboutism', count: 34, pct_of_flagged_docs: 18.2 },
            { technique: 'ad_hominem', count: 28, pct_of_flagged_docs: 15.0 },
            { technique: 'doubt_casting', count: 21, pct_of_flagged_docs: 11.2 },
        ],
        by_source: [
            { label: 'News', total_docs: 640, flagged_docs: 82, flagged_rate_pct: 12.8, mean_score: 0.19 },
            { label: 'Social Media', total_docs: 570, flagged_docs: 105, flagged_rate_pct: 18.4, mean_score: 0.26 },
        ],
        examples: [
            {
                doc_id: 71001, source_type: 'x_post', domain: null, author_handle: 'POTUS',
                title: 'The radical left are BETRAYING everything America stands for. WAKE UP.',
                overall_score: 0.82,
                techniques: [
                    { technique: 'loaded_language', confidence: 0.88, evidence_span: 'BETRAYING everything America stands for' },
                    { technique: 'name_calling', confidence: 0.74, evidence_span: 'radical left' },
                    { technique: 'appeal_to_fear', confidence: 0.66, evidence_span: 'WAKE UP' },
                ],
                text_preview: 'The radical left are BETRAYING everything America stands for. WAKE UP before it\'s too late for this country.',
                url: 'https://x.com/POTUS/status/71001',
            },
            {
                doc_id: 71002, source_type: 'news', domain: 'foxnews.com', author_handle: null,
                title: 'Op-ed: Border policy "catastrophic failure" of leadership',
                overall_score: 0.68,
                techniques: [
                    { technique: 'loaded_language', confidence: 0.79, evidence_span: 'catastrophic failure' },
                    { technique: 'appeal_to_fear', confidence: 0.58, evidence_span: 'open invitation to criminals' },
                ],
                text_preview: 'The administration\'s handling of the border is a catastrophic failure of leadership. It is an open invitation to criminals...',
                url: 'https://www.foxnews.com/opinion/border-policy-catastrophic-failure',
            },
            {
                doc_id: 71003, source_type: 'reddit_post', domain: 'politics', author_handle: null,
                title: 'Why do conservatives never answer this question?',
                overall_score: 0.55,
                techniques: [
                    { technique: 'whataboutism', confidence: 0.72, evidence_span: 'never answer this question' },
                ],
                text_preview: 'Every time the topic comes up, they pivot to something Biden did. Why do conservatives never answer this question directly?',
                url: 'https://reddit.com/r/politics/comments/71003',
            },
            {
                doc_id: 71004, source_type: 'news', domain: 'foxnews.com', author_handle: null,
                title: 'Commentary: the administration\'s "disastrous" economic record',
                overall_score: 0.62,
                techniques: [
                    { technique: 'loaded_language', confidence: 0.81, evidence_span: 'disastrous economic record' },
                    { technique: 'doubt_casting', confidence: 0.54, evidence_span: 'no one should trust their numbers' },
                ],
                text_preview: 'After four years of this administration\'s disastrous economic record, no one should trust their numbers on inflation or growth...',
                url: 'https://www.foxnews.com/opinion/administration-economic-record',
            },
            {
                doc_id: 71005, source_type: 'x_post', domain: null, author_handle: 'POTUS',
                title: 'Reciprocal tariffs coming for every country that cheated us!',
                overall_score: 0.58,
                techniques: [
                    { technique: 'loaded_language', confidence: 0.72, evidence_span: 'cheated us' },
                    { technique: 'appeal_to_fear', confidence: 0.48, evidence_span: 'they will pay the price' },
                ],
                text_preview: 'Reciprocal tariffs coming for every country that cheated us! They will pay the price for decades of unfair trade.',
                url: 'https://x.com/POTUS/status/71005',
            },
            {
                doc_id: 71006, source_type: 'reddit_post', domain: 'Conservative', author_handle: null,
                title: 'Megathread: why the media ignores stories that hurt their narrative',
                overall_score: 0.61,
                techniques: [
                    { technique: 'doubt_casting', confidence: 0.68, evidence_span: 'their narrative' },
                    { technique: 'whataboutism', confidence: 0.55, evidence_span: 'but nothing when a Democrat does the same' },
                ],
                text_preview: 'The mainstream media buries every story that hurts their narrative but nothing when a Democrat does the same thing. Pattern is obvious at this point.',
                url: 'https://reddit.com/r/Conservative/comments/71006',
            },
        ],
        disclaimer:
            'Sampled political discourse. Propaganda scoring is a per-doc LLM classification with verbatim evidence spans; a high rate means the sample leaned on these techniques, not that every flagged doc is propaganda-by-intent.',
        by_news_outlet: [
            propagandaItem(FOX_PROFILE,  { total: 122, flagged: 31, mean: 0.34 }),
            propagandaItem(NYT_PROFILE,  { total: 168, flagged: 19, mean: 0.17 }),
            propagandaItem(BBC_PROFILE,  { total: 96,  flagged:  7, mean: 0.11 }),
            propagandaItem(
                catchAll('other-outlets', 'Other news outlets',
                    'News docs whose domain is not in the tracked outlet registry.'),
                { total: 254, flagged: 25, mean: 0.18 },
            ),
        ],
        by_official: [
            propagandaItem(POTUS_PROFILE,   { total: 88, flagged: 34, mean: 0.41 }),
            propagandaItem(SCHUMER_PROFILE, { total: 34, flagged:  6, mean: 0.19 }),
            propagandaItem(JOHNSON_PROFILE, { total: 22, flagged:  3, mean: 0.15 }),
        ],
        by_general_public: [
            propagandaItem(
                subredditProfile(
                    'conservative', 'r/Conservative', 'right',
                    'Community sidebar + flair-gated participation',
                    '~1.3M-subscriber subreddit; sidebar positions it as a "safe space for conservatives".',
                ),
                { total: 118, flagged: 29, mean: 0.31 },
            ),
            propagandaItem(R_POLITICS_PROFILE, { total: 204, flagged: 32, mean: 0.22 }),
            propagandaItem(
                catchAll('other-x-users', 'Other X users',
                    'X posts whose author is not in the tracked officials registry.'),
                { total: 204, flagged: 44, mean: 0.29 },
            ),
        ],
    };
}

/**
 * Mock snapshot-status payload — one row per cached aggregation with a
 * timestamp 18 minutes in the past, mimicking a recent pipeline run.
 */
export function mockSnapshotStatus() {
    const recent = new Date(Date.now() - 18 * 60 * 1000).toISOString();
    const keys = [
        'sentiment_24h', 'sentiment_7d', 'sentiment_30d', 'sentiment_90d',
        'bot_activity',
        'narratives_24h', 'narratives_7d', 'narratives_30d', 'narratives_90d',
        'propaganda_24h', 'propaganda_7d', 'propaganda_30d', 'propaganda_90d',
        'polling_gop',
    ];
    return {
        snapshots: keys.map((key) => ({ key, generated_at: recent, doc_count: 100 })),
    };
}

/**
 * Mock movers payload — exercises the MoversTicker with a handful of
 * plausibly-shaped entity deltas plus one GOP-favorability row.
 */
export function mockMovers(): MoversResult {
    return {
        window: '7d',
        favorability_mover: {
            label: 'GOP party stance',
            current_net: -14.2,
            prev_net: -8.7,
            delta_pts: -5.5,
            current_volume: 4318,
            prev_volume: 4102,
        },
        entity_movers: [
            {
                key: 'foxnews.com', kind: 'outlet', displayName: 'Fox News',
                current_net: -12.4, prev_net: -3.2, delta_pts: -9.2,
                current_volume: 221, prev_volume: 208,
                entity_profile: FOX_PROFILE,
            },
            {
                key: 'senschumer', kind: 'official', displayName: 'Chuck Schumer',
                current_net: 18.1, prev_net: 11.6, delta_pts: 6.5,
                current_volume: 34, prev_volume: 31,
                entity_profile: SCHUMER_PROFILE,
            },
            {
                key: 'r/politics', kind: 'subreddit', displayName: 'r/politics',
                current_net: -7.9, prev_net: -12.4, delta_pts: 4.5,
                current_volume: 612, prev_volume: 580,
                entity_profile: R_POLITICS_PROFILE,
            },
            {
                key: 'nytimes.com', kind: 'outlet', displayName: 'The New York Times',
                current_net: 3.7, prev_net: -0.8, delta_pts: 4.5,
                current_volume: 168, prev_volume: 162,
                entity_profile: NYT_PROFILE,
            },
            {
                key: 'bbc.com', kind: 'outlet', displayName: 'BBC',
                current_net: 1.2, prev_net: 5.0, delta_pts: -3.8,
                current_volume: 96, prev_volume: 102,
                entity_profile: BBC_PROFILE,
            },
        ],
    };
}
