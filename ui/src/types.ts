// Type definitions for Civic Lens UI

// Filter types
export interface Filters {
    timeRange: '24h' | '7d' | '30d' | '90d' | 'all';
    sourceType: 'all' | 'news' | 'reddit' | 'social';
    geography: string;
}

// Confidence levels
export type ConfidenceLevel = 'high' | 'medium' | 'low';
export type CoverageLevel = 'high' | 'medium' | 'low';

// Cluster types
export type ContentType = 'articles' | 'social' | 'mixed';

export interface Cluster {
    id: number;
    title: string;
    articleCount: number;
    contentType: ContentType;
    momentum: {
        delta24h: number;
        delta7d: number;
    };
    primarySources: string[];
    summary: string[];
    keyClaims: string[];
    entities: {
        people: string[];
        organizations: string[];
        locations: string[];
    };
    sourceMix: SourceMixItem[];
    timeline: TimelinePoint[];
    articles: Article[];
}

export interface SourceMixItem {
    name: string;
    value: number;
    type: 'news' | 'reddit' | 'social' | 'other';
}

export interface TimelinePoint extends ChartDataPoint {
    date: string;
    value: number;
}

export interface Article {
    id: number;
    title: string;
    source: string;
    snippet: string;
    reason: string;
}

// Sentiment types
export interface SocialVsNewsSentiment {
    social: {
        positive: number;
        negative: number;
        neutral: number;
        netScore: number;
        volume: number;
    };
    news: {
        positive: number;
        negative: number;
        neutral: number;
        netScore: number;
        volume: number;
    };
}

export interface PublicSentimentData {
    overview: SentimentOverview;
    byTopic: SentimentBreakdown[];
    byPlatform: SentimentBreakdown[];
    byTimeWindow: SentimentBreakdown[];
    distribution: SentimentDistribution;
    socialVsNews?: SocialVsNewsSentiment | null;
    // Merged GOP favorability data
    gopFavorability?: {
        favorable: number;
        unfavorable: number;
        neutral: number;
        netFavorability: number;
        sampleSize: number;
        sourceCount: number;
        lastUpdated: string;
    } | null;
    gopTrend?: TrendPoint[] | null;
    gopByPlatform?: DemographicBreakdown[] | null;
    pollingVsSocial?: PollingSocialComparison | null;
}

export interface SentimentOverview {
    netScore: number;
    volume: number;
    coverage: CoverageLevel;
    confidence: ConfidenceLevel;
}

export interface ClassificationSample {
    doc_id: number;
    label: string;
    confidence: number;
    reasoning: string;
    evidence_spans: string[];
    sarcasm_detected: boolean;
    title: string;
    source_type: string;
}

export interface SentimentBreakdown {
    topic?: string;
    platform?: string;
    window?: string;
    positive: number;
    negative: number;
    neutral: number;
    volume: number;
    sarcasm_rate?: number;
    classificationSamples?: ClassificationSample[];
}

export interface SentimentDistribution {
    strongPositive: number;
    mildPositive: number;
    neutral: number;
    mildNegative: number;
    strongNegative: number;
}

// Favorability types
export interface FavorabilityData {
    overall: FavorabilityOverall;
    trend: TrendPoint[];
    trendAnnotations: TrendAnnotation[];
    byAge: DemographicBreakdown[];
    byRegion: DemographicBreakdown[];
    byPartyId: DemographicBreakdown[];
    byPlatform: DemographicBreakdown[]; // Added field
    pollingVsSocial: PollingSocialComparison;
}

export interface FavorabilityOverall {
    favorable: number;
    unfavorable: number;
    neutral: number;
    netFavorability: number;
    sampleSize: number;
    sourceCount: number;
    lastUpdated: string;
    dateRange: string;
}

export interface TrendPoint extends ChartDataPoint {
    date: string;
    value: number;
}

export interface TrendAnnotation {
    x: string;
    label: string;
}

export interface DemographicBreakdown {
    group?: string;
    region?: string;
    party?: string;
    favorable: number;
    unfavorable: number;
    neutral: number;
}

export interface PollingSocialComparison {
    onlineSentiment: { favorable: number; unfavorable: number; neutral: number };
    pollingData: { favorable: number; unfavorable: number; neutral: number } | null;
}

// Bot Activity types
export interface BotData {
    overview: BotOverview;
    narrativeAmplification: NarrativeAmplification[];
    coordinationStats: CoordinationStats;
    behavioralSignals: BehavioralSignals;
}

export interface BotOverview {
    suspectedAutomationRate: number;
    coordinationIndex: number;
    topClusters: string[];
    totalFlaggedAccounts: number;
    confidence: ConfidenceLevel;
}

export interface NarrativeAmplification {
    id: number;
    narrative: string;
    confidence: ConfidenceLevel;
    examplePosts: string[];
    topHashtags: string[];
    topPhrases: string[];
    targets: string[];
    suspectedBotVolume: number;
    whyFlagged: string[];
}

export interface CoordinationStats {
    burstTimingSimilarity: number;
    accountReuse: number;
    identicalTextPairs: number;
    avgPostsPerSuspectedAccount: number;
}

export interface BehavioralSignals {
    accountAgeDistribution: AccountAgeItem[];
    postingCadence: HeatmapDataPoint[];
    copyPasteSimilarity: {
        high: number;
        medium: number;
        low: number;
    };
    linkDomainConcentration: DomainItem[];
}

export interface AccountAgeItem {
    range: string;
    count: number;
    percentage: number;
}

export interface HeatmapDataPoint {
    day: number;
    hour: number;
    value: number;
}

export interface DomainItem {
    domain: string;
    percentage: number;
}

// Chart data types
export interface ChartDataPoint {
    [key: string]: string | number;
}
