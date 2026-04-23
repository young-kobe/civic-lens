// Type definitions for Civic Lens UI

// Filter types
export interface Filters {
    timeRange: '24h' | '7d' | '30d' | '90d' | 'all';
    sourceType: 'all' | 'news' | 'reddit' | 'social';
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
    // Three-way entity rollups (walkthrough 057/058). Lists the dashboard
    // renders as the News Outlets / Verified Officials / General Public
    // columns. Optional for pre-Phase-3b cached snapshots.
    byNewsOutlet?: EntitySentimentItem[];
    byOfficial?: EntitySentimentItem[];
    byGeneralPublic?: EntitySentimentItem[];
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

/**
 * Editorial profile payload attached to every entity card. Shape is
 * keyed by `kind`: registry entities carry outlet/official/subreddit
 * metadata; unmatched docs get a `catch_all` placeholder with a minimal
 * blurb and no partisan-lean fields.
 */
export interface EntityProfile {
    kind: 'outlet' | 'official' | 'subreddit' | 'catch_all';
    key: string;
    displayName: string;
    blurb: string;
    // Shared lean/leanSource axis across outlets + subreddits (null on
    // officials + catch_all). Outlets use the 6-bucket partisan_lean;
    // subreddits the 4-bucket tilt.
    lean?: string | null;
    leanSource?: string | null;
    // Outlet-only.
    owner?: string | null;
    founded?: number | null;
    circulationNote?: string | null;
    // Official-only.
    office?: string;
    party?: string;
    termStart?: string;
    bioSource?: string;
    // Subreddit-only.
    subscriberCountProxy?: string | null;
}

/** Per-entity sentiment card on the three-way dashboard frame. */
export interface EntitySentimentItem {
    key: string;
    kind: 'outlet' | 'official' | 'subreddit' | 'catch_all';
    positive: number;
    negative: number;
    neutral: number;
    volume: number;
    netScore: number;
    entityProfile: EntityProfile;
    classificationSamples?: ClassificationSample[];
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
    // Per-topic three-way split (walkthrough 057). Only present on byTopic
    // rows — null when a tier had zero volume on that topic so the UI can
    // distinguish "no data" from a real zero.
    newsNet?: number | null;
    officialsNet?: number | null;
    publicNet?: number | null;
    newsVolume?: number;
    officialsVolume?: number;
    publicVolume?: number;
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
    // Three-way entity rollups — registry-matched outlets / officials /
    // subreddits (+ a catch-all per tier) with their per-entity bot rate.
    // Empty lists on older snapshots.
    by_news_outlet?: BotEntityItem[];
    by_official?: BotEntityItem[];
    by_general_public?: BotEntityItem[];
}

// =============================================================================
// Movers (window-over-window deltas)
// =============================================================================

/** One row of the political-tone movers ticker — an outlet, official, or
 *  subreddit whose net tone shifted meaningfully between the previous
 *  equivalent window and the current one. */
export interface EntityToneMover {
    key: string;
    kind: 'outlet' | 'official' | 'subreddit';
    displayName: string;
    current_net: number;
    prev_net: number;
    delta_pts: number;     // signed: positive = climbing, negative = falling
    current_volume: number;
    prev_volume: number;
    entity_profile: EntityProfile;
}

/** Window-over-window shift in overall GOP favorability — one summary row
 *  that lives alongside the entity movers in the same ticker. */
export interface FavorabilityMover {
    label: string;          // "GOP party stance"
    current_net: number;
    prev_net: number;
    delta_pts: number;
    current_volume: number;
    prev_volume: number;
}

export interface MoversResult {
    window: string;
    entity_movers: EntityToneMover[];
    favorability_mover: FavorabilityMover | null;
}


/** Per-entity bot-amplification rollup for the Bot Detector's three-way
 *  grid. Sort by `bot_rate_pct` desc. */
export interface BotEntityItem {
    key: string;
    kind: 'outlet' | 'official' | 'subreddit' | 'catch_all';
    total_docs: number;
    bot_docs: number;
    bot_rate_pct: number;
    entity_profile: EntityProfile;
}

/** One bot-flagged post displayed as evidence inside the Bot Detector's
 *  amplification modal. Every row carries a URL back to the original when
 *  the backend was able to synthesize one — invariant C1. */
export interface FlaggedExample {
    doc_id: number;
    text: string;
    source_label: string;   // "News · foo.com", "X · @handle", "Reddit · r/politics"
    url: string | null;
}

export interface NarrativeAmplification {
    id: number;
    narrative: string;
    confidence: ConfidenceLevel;
    examplePosts: FlaggedExample[];
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

/** One row of the "Supporting documents" drill-down table used on the
 *  Political Narratives and Overall Tone pages. Mirror of
 *  `NarrativeAggregator._top_supporting_docs` / `_build_source_label`
 *  output — headline / source / publish date / sentiment + confidence /
 *  LLM reasoning / link. Different shape than ClassificationSample: the
 *  narrative side synthesizes a pre-formatted `source_label`, emits unix
 *  `published_at`, and uses lowercase sentiment enum. */
export interface SupportingDoc {
    doc_id: number;
    title: string | null;
    source_type: string;
    source_label: string;   // "News · nytimes.com", "X · @Schumer", "Reddit · r/politics"
    url: string | null;
    published_at: number | null;  // unix seconds
    sentiment_label: 'positive' | 'negative' | 'neutral' | null;
    confidence: number | null;
    reasoning: string | null;
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
    // Walkthrough 058 — three-way entity framing. Optional for
    // backwards-compat with pre-Phase-3b cached snapshots.
    first_seen_entity_profile?: EntityProfile | null;
    first_seen_tier_group?: 'news' | 'officials' | 'public' | null;
    cross_tier?: boolean;
    top_supporting_docs?: SupportingDoc[];
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
    // X handle for x_post examples; null for news/reddit. Used to filter the
    // Examples list down to one entity inside PropagandaEntityModal.
    author_handle?: string | null;
    // External source URL — news story, X permalink, or Reddit post link.
    url?: string | null;
}

/**
 * Per-entity propaganda rollup for the three-way dashboard frame
 * (walkthrough 058). Sort the "Top flagged entities" card by
 * ``mean_score`` desc; catch-all buckets sort to the end.
 */
export interface PropagandaEntityItem {
    key: string;
    kind: 'outlet' | 'official' | 'subreddit' | 'catch_all';
    total_docs: number;
    flagged_docs: number;
    flagged_rate_pct: number;
    mean_score: number;
    entity_profile: EntityProfile;
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
    // Walkthrough 058 — three-way entity rollups. Empty on older snapshots.
    by_news_outlet?: PropagandaEntityItem[];
    by_official?: PropagandaEntityItem[];
    by_general_public?: PropagandaEntityItem[];
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
        /** External permalink to the original source. Null when the ingest
         *  layer didn't have enough metadata to synthesize one (rare).
         *  Rendered as a clickable title in the reviewer UI — invariant C1. */
        url: string | null;
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
