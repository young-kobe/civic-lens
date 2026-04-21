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
    // Per-weekday breakdown (Mon..Sun). Populated by the sentiment aggregator
    // from each doc's published_at weekday; orthogonal to byTimeWindow's age
    // buckets. Optional for backwards-compat with older cached snapshots.
    byDayOfWeek?: SentimentBreakdown[];
    distribution: SentimentDistribution;
    // Per-intensity drill-down samples. Keys match the five fields on
    // SentimentDistribution. Each list is confidence-sorted desc and capped
    // server-side (~15 per bucket). Absent on older snapshots.
    distributionSamples?: Partial<Record<SentimentSegmentKey, ClassificationSample[]>>;
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

/** Keys of SentimentDistribution. Used as lookup keys for distributionSamples. */
export type SentimentSegmentKey =
    | 'strongPositive'
    | 'mildPositive'
    | 'neutral'
    | 'mildNegative'
    | 'strongNegative';

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
    source_name?: string;
    date?: string;
    full_text?: string;
    url?: string;
}

export interface SentimentBreakdown {
    topic?: string;
    platform?: string;
    window?: string;
    /** Weekday short label (Mon..Sun) when used for byDayOfWeek rows. */
    day?: string;
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
    pollingData: { favorable: number; unfavorable: number; neutral: number; source?: string; date?: string } | null;
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

// Narrative types
export interface NarrativeSourceBreakdownItem {
    source_type: string;
    label: string;
    count: number;
}

export interface NarrativeTimelinePoint {
    date: string;
    count: number;
}

// Account tier for X-origin narratives. See walkthrough 036.
export type AccountTier = 'elected_official' | 'affiliated' | 'general_public';

// Faction context for X-origin narratives. Populated from account_profiles
// via curated known_political_x_accounts.yaml. Null for news/reddit source
// types and for unclassified X accounts.
export interface AccountProfile {
    handle: string | null;
    full_name: string | null;
    party: string | null;                 // 'D' | 'R' | 'I' | 'L' | 'G' | null
    branch: string | null;                // 'executive' | 'legislative' | 'judicial' | 'party_org' | 'pac' | 'think_tank' | ...
    chamber: string | null;               // 'senate' | 'house' | null
    state_or_district: string | null;     // 'NY', 'CA33', null
    office_title: string | null;          // 'President', 'Senator', 'Representative', etc.
    account_type: string | null;          // 'official' | 'personal' | 'institutional' | ...
}

export interface NarrativeSummary {
    narrative_id: number;
    name: string;
    first_seen_at: number;
    // Earliest doc WE ingested carrying this claim — not world-origin.
    first_seen_doc_id: number | null;
    first_seen_source_type: string | null;
    first_seen_domain: string | null;
    // Only set for x_post source types; null for news/reddit.
    first_seen_tier: AccountTier | null;
    // Faction context for x_post first-seen docs; null otherwise.
    first_seen_author: AccountProfile | null;
    supporting_doc_count: number;
    source_breakdown: NarrativeSourceBreakdownItem[];
    timeline: NarrativeTimelinePoint[];
    net_sentiment: number;
    inbound_citation_count: number;
    // Walkthrough 043 overlays — null when no data.
    propaganda_score: number | null;       // 0..1 mean across supporting docs
    bot_pushed_fraction: number | null;    // 0..1 over unique X supporting authors
}

// =============================================================================
// Propaganda Detection (walkthrough 043)
// =============================================================================

export type PropagandaTechniqueName =
    | 'loaded_language'
    | 'name_calling'
    | 'ad_hominem'
    | 'appeal_to_fear'
    | 'whataboutism'
    | 'doubt_casting';

export interface PropagandaTechniqueSpan {
    technique: PropagandaTechniqueName;
    confidence: number;
    evidence_span: string;
}

export interface PropagandaTechniqueCount {
    technique: PropagandaTechniqueName;
    count: number;
    pct_of_flagged_docs: number;
}

export interface PropagandaSourceSplit {
    label: 'News' | 'Social Media';
    total_docs: number;
    flagged_docs: number;
    flagged_rate_pct: number;
    mean_score: number;
}

export interface PropagandaExample {
    doc_id: number;
    source_type: string;
    domain: string | null;
    title: string | null;
    overall_score: number;
    techniques: PropagandaTechniqueSpan[];
    text_preview: string;
}

export interface PropagandaOverview {
    window: string;
    total_eligible_docs: number;
    flagged_docs: number;
    propaganda_rate_pct: number;
    mean_score: number;
    by_technique: PropagandaTechniqueCount[];
    by_source: PropagandaSourceSplit[];
    examples: PropagandaExample[];
    disclaimer: string;
}

// Review types
export type ReviewTaskType = 'sentiment' | 'favorability' | 'bot_detection' | 'claims' | 'propaganda';

export interface ReviewQueueItem {
    ai_output_id: number;
    doc_id: number;
    task_type: ReviewTaskType;
    model_id: string;
    prompt_version: string;
    model_confidence: number | null;
    model_output: Record<string, any>;
    created_at: number;
    doc: {
        source_type: string;
        domain: string | null;
        title: string | null;
        ident: string;
        text_preview: string;
        text_truncated: boolean;
    };
}

export interface ReviewSubmission {
    ai_output_id: number;
    is_correct: number | null;
    human_label: string | null;
    human_confidence: number | null;
    is_golden: boolean;
    reviewer_id: string | null;
    notes: string | null;
}

export interface ReviewTaskStats {
    task_type: string;
    total_outputs: number;
    reviewed: number;
    correct: number;
    incorrect: number;
    golden: number;
    accuracy_pct: number | null;
}

export interface ReviewStats {
    per_task: ReviewTaskStats[];
}
