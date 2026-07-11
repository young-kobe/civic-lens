// Type definitions for Civic Lens UI

// Filter types
export interface Filters {
    timeRange: '24h' | '7d' | '30d' | '90d' | 'all';
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
        // 'mixed' = both genuine praise and criticism of the GOP. Kept distinct
        // from 'neutral' (no stance): the four buckets sum to ~100. netFavorability
        // counts mixed in the denominator with zero net contribution. Optional so
        // pre-mixed cached snapshots keep parsing.
        mixed?: number;
        netFavorability: number;
        sampleSize: number;
        sourceCount: number;
        lastUpdated: string;
    } | null;
    gopTrend?: TrendPoint[] | null;
    /** Per-day per-tier tone series (Phase 2a). Dates ascend; a tier's
     *  net is null (suppressed) on low-sample days — render a gap, never
     *  a zero. Absent on pre-2a cached snapshots. */
    toneTrend?: ToneTrendPoint[] | null;
    gopByPlatform?: DemographicBreakdown[] | null;
    pollingVsSocial?: PollingSocialComparison | null;
    // Target-tone metadata from the target_sentiment fan-out: suppression
    // threshold, resolution coverage, party-collective rollups, alignment
    // baselines. Absent until the targets stage has produced rows.
    targetTone?: TargetToneMeta | null;
}

/** One per-topic received-tone cell. net is null (suppressed) when the
 *  cell's volume is below the aggregator's minSampleN. */
export interface TargetTopicCell {
    topic: string;
    net: number | null;
    volume: number;
    lowSample: boolean;
}

/** One by-speaker-tier received-tone cell: WHO is talking about the
 *  entity (news / officials / affiliated / public). */
export interface TargetSpeakerTierCell {
    tier: string;
    net: number | null;
    volume: number;
    lowSample: boolean;
}

/** One by-narrative received-tone cell: WHICH recurring claim drives
 *  the mentions of the entity. */
export interface TargetNarrativeCell {
    narrativeId: number;
    name: string;
    net: number | null;
    volume: number;
    lowSample: boolean;
}

/** How sampled posts talk ABOUT an entity (the reputational signal),
 *  as opposed to netScore, which scores the entity's own posts. */
export interface ReceivedTone {
    net: number | null;
    volume: number;
    lowSample: boolean;
    /** Reach-proxy variant: each sampled post weighted by
     *  1 + ln(1 + engagement counts). Null when suppressed (shares the
     *  same sample floor as `net`) or on older snapshots. */
    engagementWeightedNet?: number | null;
    byTopic: TargetTopicCell[];
    bySpeakerTier?: TargetSpeakerTierCell[];
    byNarrative?: TargetNarrativeCell[];
    samples?: ClassificationSample[];
}

/** How an official's own posts score toward same-party vs cross-party
 *  tracked targets. Nets are suppressed (null) below minSampleN. */
export interface ExpressedAlignment {
    samePartyNet: number | null;
    samePartyVolume: number;
    crossPartyNet: number | null;
    crossPartyVolume: number;
}

export interface TargetToneMeta {
    minSampleN: number;
    resolvedMentions: number;
    unresolvedMentions: number;
    /** Mentions withheld because the author's account-level bot rollup
     *  (author_bot_scores) is high. Absent on older snapshots. */
    botExcludedMentions?: number;
    /** Human-readable definition of the engagement weighting formula. */
    engagementWeight?: string;
    collectives: Record<string, ReceivedTone>;
    baselines: {
        samePartyNet: number | null;
        samePartyVolume: number;
        crossPartyNet: number | null;
        crossPartyVolume: number;
    };
}

/**
 * Editorial profile payload attached to every entity card. Shape is
 * keyed by `kind`: registry entities carry outlet/official/subreddit
 * metadata; unmatched docs get a `catch_all` placeholder with a minimal
 * blurb and no partisan-lean fields.
 */
export interface EntityProfile {
    kind: 'outlet' | 'official' | 'subreddit' | 'account' | 'catch_all';
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
    // Account-only (curated political-accounts list, kind='account').
    accountType?: string | null;
}

/** Per-entity sentiment card on the three-way dashboard frame. */
export interface EntitySentimentItem {
    key: string;
    kind: 'outlet' | 'official' | 'subreddit' | 'account' | 'catch_all';
    positive: number;
    negative: number;
    neutral: number;
    volume: number;
    netScore: number;
    entityProfile: EntityProfile;
    classificationSamples?: ClassificationSample[];
    // Officials only, and only once target_sentiment coverage exists.
    // netScore above is the EXPRESSED tone (this entity's own posts);
    // received is the tone of posts ABOUT the entity.
    received?: ReceivedTone | null;
    expressedAlignment?: ExpressedAlignment | null;
    /** Topic-scoped expressed cells for this entity's OWN posts, volume-
     *  sorted; net is null (lowSample) below the backend suppression floor.
     *  Missing on pre-topic cached snapshots. */
    byTopic?: EntityTopicCell[];
    /** Public tier only — WHO this bucket's posts talk about (the inverse
     *  of received). Missing on pre-outbound cached snapshots. */
    outbound?: OutboundTargets | null;
    /** Summed engagement (likes + reposts + replies + quotes) across this
     *  entity's posts in the window. Absent (treat as 0) for news outlets and
     *  on pre-engagement cached snapshots — powers the officials column's
     *  engagement-weighted default sort. */
    engagementTotal?: number;
}

/** Outbound-target rollup on a public-tier entity card. */
export interface OutboundTargets {
    minSampleN: number;
    volume: number;
    targets: OutboundTargetCell[];
}

/** One "who they're talking about" row. kind 'raw' = unresolved free-text
 *  target that recurred; 'other' = pooled one-offs and overflow. */
export interface OutboundTargetCell {
    label: string;
    entityKey: string | null;
    kind: 'official' | 'collective' | 'raw' | 'other';
    net: number | null;
    volume: number;
    lowSample: boolean;
}

/** One topic-scoped expressed-tone cell on an entity card. */
export interface EntityTopicCell {
    topic: string;
    net: number | null;
    volume: number;
    lowSample: boolean;
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

/** Engagement counts at collection time — a reach proxy, not verified
 *  reach. X posts carry retweets/replies/likes/quotes; reddit carries
 *  score (+ num_comments for posts). Null/absent = source stores none. */
export interface SampleEngagement {
    retweets?: number;
    replies?: number;
    likes?: number;
    quotes?: number;
    score?: number;
    num_comments?: number;
}

/** X author metadata from the ingest store. Null/absent for non-X docs —
 *  Reddit stores no author at ingest, and we never fabricate one. */
export interface SampleAuthor {
    handle: string | null;
    display_name: string | null;
    avatar_url: string | null;
    verified_type: string | null;
    followers_count: number | null;
    account_created_at: number | null;
    /** The account's own X bio, verbatim. Absent on pre-bio snapshots. */
    bio?: string | null;
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
    /** Doc topic attribution stamped by the backend (LLM mention topic
     *  with title-keyword fallback). Missing on pre-topic cached
     *  snapshots; the modal filter treats those as non-matching. */
    topic?: string | null;
    // Phase 2b/2c enrichment. Absent on pre-enrichment cached snapshots.
    engagement?: SampleEngagement | null;
    author?: SampleAuthor | null;
    /** Who this post's sentiment is directed at (frozen target_mentions):
     *  resolved targets first, capped small. Absent on older snapshots. */
    targets?: SampleTarget[] | null;
}

/** One "about X — negative" chip on a sampled post. */
export interface SampleTarget {
    label: string;
    stance: 'positive' | 'negative' | 'neutral' | 'mixed';
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

/** One tier cell of the toneTrend daily series. */
export interface ToneTrendCell {
    net: number | null;
    volume: number;
}

export interface ToneTrendPoint {
    date: string;
    news: ToneTrendCell;
    officials: ToneTrendCell;
    public: ToneTrendCell;
}

/** One row of GET /outlet-profiles — per-domain net tone x bot rate.
 *  Includes bot-flagged content on purpose (the bot rate is the signal);
 *  net tone therefore differs from the bot-excluded Overall Tone rollups. */
export interface OutletProfileItem {
    outlet: string;
    source_type: string;
    net_tone: number | null;
    bot_rate_pct: number;
    volume: number;
    total_scanned: number;
}

export interface OutletProfilesResult {
    window: string;
    disclaimer: string;
    outlets: OutletProfileItem[];
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
    // 'mixed' (both praise and criticism) — distinct from neutral. Optional so
    // pre-mixed cached snapshots keep parsing.
    mixed?: number;
}

export interface PollingSocialComparison {
    onlineSentiment: { favorable: number; unfavorable: number; neutral: number; mixed?: number };
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
    totalFlaggedPosts: number;
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
    kind: 'outlet' | 'official' | 'subreddit' | 'account' | 'catch_all';
    total_docs: number;
    bot_docs: number;
    bot_rate_pct: number;
    entity_profile: EntityProfile;
    /** Up to a handful of this entity's bot-flagged posts, confidence-
     *  ranked — evidence for the card's drill-down modal. Missing on
     *  pre-2026-07-10 cached snapshots. */
    samples?: FlaggedExample[];
}

/** One bot-flagged post displayed as evidence inside the Bot Detector's
 *  amplification modal. Every row carries a URL back to the original when
 *  the backend was able to synthesize one — invariant C1. */
export interface FlaggedExample {
    doc_id: number;
    text: string;
    source_label: string;   // "News · foo.com", "X · @handle", "Reddit · r/politics"
    url: string | null;
    // Per-example bot evidence (Phase 2d): flag confidence (0..1), the
    // humanized behavioral indicators that triggered it, and the model's
    // reasoning. Absent on pre-2d cached snapshots.
    confidence?: number | null;
    indicators?: string[];
    reasoning?: string | null;
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
    // Text preview shown in the Headline column when `title` is null (social
    // posts have no headline). Falls back to "(untitled)" when both are empty.
    snippet?: string | null;
    source_type: string;
    source_label: string;   // "News · nytimes.com", "X · @Schumer", "Reddit · r/politics"
    url: string | null;
    published_at: number | null;  // unix seconds
    sentiment_label: 'positive' | 'negative' | 'neutral' | 'mixed' | null;
    confidence: number | null;
    reasoning: string | null;
    // Phase 2b/2c enrichment. Absent on pre-enrichment cached snapshots.
    engagement?: SampleEngagement | null;
    author?: SampleAuthor | null;
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

/** One cross-narrative citation edge rollup: this narrative's docs cite
 *  into (or are cited by) another narrative's docs. Edges between sampled
 *  documents only — never an origin/propagation claim. */
export interface CrossNarrativeCitation {
    narrative_id: number;
    name: string;
    direction: 'cites' | 'cited_by';
    edge_count: number;
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
    // Citation-edge semantics (2026-07-10). Optional on older snapshots.
    inbound_by_link_type?: Record<string, number>;
    external_citation_count?: number;
    cross_narrative_citations?: CrossNarrativeCitation[];
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
    kind: 'outlet' | 'official' | 'subreddit' | 'account' | 'catch_all';
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
    // Per-entity flagged-example bucket. Keyed by the same key used in
    // PropagandaEntityItem.key (outlet domain, official handle, subreddit
    // name, or catch-all sentinel). The drill-down modal reads from this
    // — the global ``examples`` array is too small to filter by entity.
    examples_by_entity?: Record<string, PropagandaExample[]>;
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

/** Public human-agreement overlay (GET /eval-accuracy). Aggregate-only:
 *  per-task counts and percentages, with accuracy suppressed server-side
 *  below `minReviewN` scored reviews. */
export interface EvalAccuracyTask {
    taskType: string;
    reviewed: number;
    scored: number;
    golden: number;
    accuracyPct: number | null;
    lowSample: boolean;
}

export interface EvalAccuracy {
    perTask: EvalAccuracyTask[];
    minReviewN: number;
}
