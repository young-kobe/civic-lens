// Type definitions for the Civic Lens UI — mirrors the Phase 9 strictly-live
// API contract (analysis/src/api/models/*.py, all CamelModel -> camelCase
// JSON keys). Every shape here should map 1:1 onto a pydantic response
// model; when the backend changes the contract, this file changes with it.
//
// Two exceptions keep their pre-redesign snake_case shape because the
// backend routes them through plain dicts, not a CamelModel: the /review/*
// endpoints (see analysis/src/review/service.py) and /eval-accuracy's
// perTask/lowSample fields, which the service builds by hand already in
// camelCase for that one endpoint.

// --------------------------------------------------------------------------- //
//  Filters                                                                    //
// --------------------------------------------------------------------------- //

export type TimeWindow = '24h' | '7d' | '30d' | '90d' | 'all';

export interface Filters {
    timeRange: TimeWindow;
}

// --------------------------------------------------------------------------- //
//  Shared contract primitives (common.py)                                     //
// --------------------------------------------------------------------------- //

/**
 * The three-epistemic-kinds lean invariant (owner decision 2026-07-22):
 * a lean is a stated FACT (official party registration), a CURATED
 * editorial judgment (outlet/subreddit lean from the registry), or a
 * statistically DERIVED estimate — a derived lean never renders without
 * its evidence (leanShare, confidence, sampleCount). Always render this
 * shape through the shared <LeanLabel> component, never as a bare string.
 */
export interface LeanLabel {
    kind: 'fact' | 'curated' | 'derived';
    value: string;
    leanShare?: number | null;
    confidence?: number | null;
    sampleCount?: number | null;
}

/**
 * Honesty metadata every aggregate response carries: the resolved time
 * bounds (null = unbounded), the two admission bases counted separately,
 * and the distinct model_ids of the runs behind the aggregate.
 */
export interface RangeMeta {
    window: string | null;
    start: string | null;
    end: string | null;
    sampledDocCount: number;
    officialRecordDocCount: number;
    modelIds: string[];
}

export type AdmissionClass = 'sampled' | 'official_record';

/** One evidence sample backing a drill-down. `sourceUrl` is always
 *  present and non-empty (invariant C1). */
export interface SampleDoc {
    docId: number;
    sourceUrl: string;
    snippet?: string | null;
    confidence: number;
    admissionClass: AdmissionClass;
    publishedAt?: string | null;
}

// --------------------------------------------------------------------------- //
//  Sentiment panel (GET /sentiment)                                           //
// --------------------------------------------------------------------------- //

export interface SentimentOverview {
    netScore: number | null;
    volume: number;
    totalAnalyzed: number;
    trivialContentDocs: number;
    excludedBotDocs: number;
    meanConfidence: number | null;
}

export interface SentimentDistribution {
    strongPositive: number;
    mildPositive: number;
    neutral: number;
    mildNegative: number;
    strongNegative: number;
}

export interface BucketSentiment {
    positive: number;
    negative: number;
    neutral: number;
    volume: number;
    netScore: number | null;
}

export interface PlatformSentiment extends BucketSentiment { platform: string; }
export interface TopicSentiment extends BucketSentiment { topic: string; }
export interface TimeOfDaySentiment extends BucketSentiment { bucket: 'Morning' | 'Afternoon' | 'Evening' | 'Night'; }
export interface DayOfWeekSentiment extends BucketSentiment { day: string; }
export interface TierSplit extends BucketSentiment { tier: 'news' | 'officials' | 'general_public'; }

/** One calendar day's net tone/volume within the request window --
 *  restores the tone-trend chart line (docs/todos/ui-feature-restoration.md). */
export interface DailySentiment extends BucketSentiment { date: string; }

export interface StanceCounts {
    positive: number;
    negative: number;
    neutral: number;
    mixed: number;
    volume: number;
    netScore: number | null;
    lowSample: boolean;
}

/** One entity's stance breakdown for a single topic (analysis.target_mentions
 *  GROUP BY entity_id, topic) -- feeds the three-way grid's per-entity topic
 *  tabs. `topic` is 'General' when target_mentions.topic is NULL, matching
 *  the panel-level byTopic convention (never keyword-guessed). */
export interface TopicStanceCounts extends StanceCounts { topic: string; }

/** One entity's stance breakdown by the speaker tier (news/officials/
 *  general_public) of the mentioning doc's author -- the "received tone
 *  by speaker tier" divergence view. */
export interface TierStanceCounts extends StanceCounts { tier: 'news' | 'officials' | 'general_public'; }

/** Per-entity stance aggregate, sourced from analysis.target_mentions alone
 *  (the retired analysis.favorability_stances no longer contributes a
 *  separate `favorability` count — see docs/audit-trail/api/
 *  2026-07-25-favorability-retirement.md). `entityId` is null only for the
 *  unresolved-mentions catch-all (`catchAllKey` set instead). `kind` is
 *  corpus.entities.kind ('official' | 'collective' | 'outlet' |
 *  'subreddit'), null for the catch-all. `entityKey` is the stable slug
 *  counterpart to `entityId` (owner decision 2026-07-26: emit both so
 *  cross-page joins are exact, not a (kind, displayName) guess); also
 *  null for the catch-all. */
export interface EntityStanceAggregate {
    entityId: number | null;
    entityKey: string | null;
    catchAllKey: string | null;
    displayName: string;
    kind: string | null;
    lean: LeanLabel | null;
    targetStance: StanceCounts;
    byTopic: TopicStanceCounts[];
    receivedByTier: TierStanceCounts[];
    samples: SampleDoc[];
}

// --------------------------------------------------------------------------- //
//  Restored pre-Postgres entity-profile + editorial-card contract            //
//  (analysis/src/api/models/common.py EntityProfileModel /                   //
//  ClassificationSampleModel and friends — see docs/audit-trail/api/         //
//  2026-07-27-entity-profile-restoration.md). `kind` is a plain string on     //
//  the wire (the Python model doesn't constrain it to a Literal either).     //
// --------------------------------------------------------------------------- //

/** Editorial profile payload for one entity card. Shape is keyed by `kind`
 *  ('outlet' | 'official' | 'subreddit' | 'account' | 'collective' |
 *  'catch_all'); see EntityProfileModel's docstring for which PG columns
 *  feed which field per kind. */
export interface EntityProfile {
    kind: string;
    key: string;
    displayName: string;
    blurb: string;
    lean?: string | null;
    leanSource?: string | null;
    owner?: string | null;
    founded?: number | null;
    circulationNote?: string | null;
    office?: string | null;
    party?: string | null;
    termStart?: string | null;
    bioSource?: string | null;
    subscriberCountProxy?: string | null;
    accountType?: string | null;
    entityId?: number | null;
}

/** Engagement counts at collection time — a reach proxy, not verified
 *  reach. X posts carry retweets/replies/likes/quotes; Reddit carries
 *  score (+ numComments for posts). Absent = source stores none. */
export interface SampleEngagement {
    retweets?: number | null;
    replies?: number | null;
    likes?: number | null;
    quotes?: number | null;
    score?: number | null;
    numComments?: number | null;
}

/** X author metadata from corpus.authors. Absent for non-X docs — Reddit
 *  stores no author profile here, and we never fabricate one. */
export interface SampleAuthor {
    handle?: string | null;
    displayName?: string | null;
    avatarUrl?: string | null;
    verifiedType?: string | null;
    followersCount?: number | null;
    accountCreatedAt?: string | null;
    bio?: string | null;
}

/** One "about X — stance" chip on a ClassificationSample, sourced from
 *  analysis.target_mentions. */
export interface SampleTarget {
    label: string;
    stance: 'positive' | 'negative' | 'neutral' | 'mixed';
}

/** One evidence sample for the Source-signals drill-down (restores the
 *  pre-Postgres `ClassificationSample` shape). */
export interface ClassificationSample {
    docId: number;
    label: string;
    confidence: number;
    reasoning?: string | null;
    evidenceSpans: string[];
    sarcasmDetected?: boolean | null;
    title?: string | null;
    sourceType: string;
    sourceName?: string | null;
    date?: string | null;
    fullText?: string | null;
    url?: string | null;
    topic?: string | null;
    engagement?: SampleEngagement | null;
    author?: SampleAuthor | null;
    targets?: SampleTarget[] | null;
    narrative?: string | null;
}

/** One topic-scoped EXPRESSED cell for an entity's own posts. */
export interface EntityTopicCell {
    topic: string;
    net: number | null;
    volume: number;
    lowSample: boolean;
}

/** One day of an entity's own-post net-tone series. */
export interface EntityDailyTonePoint {
    date: string;
    net: number | null;
    volume: number;
    lowSample: boolean;
}

/** One "who this bucket talks about" row. `kind` is the resolved target's
 *  corpus.entities.kind when resolved, 'collective' for a raw_target
 *  matched against a party alias set, 'raw' for a recurring unresolved
 *  free-text target, 'other' for the pooled overflow bucket. */
export interface OutboundTargetCell {
    label: string;
    entityKey: string | null;
    kind: string;
    net: number | null;
    volume: number;
    lowSample: boolean;
}

/** News/public-tier "who they're talking about" rollup — the inverse of
 *  received tone. Capped at MAX_OUTBOUND_TARGETS named rows plus an
 *  'Other targets' overflow row. */
export interface OutboundTargets {
    minSampleN: number;
    volume: number;
    targets: OutboundTargetCell[];
}

export interface ReceivedTopicCell {
    topic: string;
    net: number | null;
    volume: number;
    lowSample: boolean;
}

/** WHO is talking about the entity: news / officials / affiliated / public,
 *  from the mentioning doc's own tier. */
export interface ReceivedSpeakerTierCell {
    tier: 'news' | 'officials' | 'affiliated' | 'public';
    net: number | null;
    volume: number;
    lowSample: boolean;
}

/** Top narratives driving the mentions of an entity, by volume. */
export interface ReceivedNarrativeCell {
    narrativeId: number;
    name: string;
    net: number | null;
    volume: number;
    lowSample: boolean;
}

/** One provenance cell answering WHO a slice of received tone comes from --
 *  the inbound-edge counterpart to OutboundTargetCell. `receivedFromGroups`
 *  rolls cells up by (sourceClass, lean); `receivedFromTop` lists named
 *  individual sources (cap 8). `lean` is the flat corpus.political_lean
 *  enum value of the source (never leanSource), null for the 'other'
 *  catch-all and for unresolved sampled X users. `share` is this cell's
 *  volume over the received block's total volume -- all cells for one
 *  ReceivedTone sum to 1.0. `net`/`lowSample` share received tone's
 *  suppression floor. Build display labels UI-side from `sourceClass` +
 *  `lean` -- the backend emits raw values only. */
export interface ReceivedSourceCell {
    sourceClass: 'news_outlet' | 'official' | 'account' | 'subreddit' | 'x_user' | 'other';
    label: string;
    entityKey: string | null;
    lean: string | null;
    entityProfile: EntityProfile | null;
    share: number;
    volume: number;
    net: number | null;
    lowSample: boolean;
}

/** How sampled posts talk ABOUT an entity (the reputational signal), as
 *  opposed to an EntitySentimentItem's own positive/negative/neutral
 *  counts, which score the entity's OWN posts. */
export interface ReceivedTone {
    net: number | null;
    volume: number;
    lowSample: boolean;
    engagementWeightedNet: number | null;
    byTopic: ReceivedTopicCell[];
    bySpeakerTier: ReceivedSpeakerTierCell[];
    byNarrative: ReceivedNarrativeCell[];
    receivedFromGroups: ReceivedSourceCell[];
    receivedFromTop: ReceivedSourceCell[];
    samples: ClassificationSample[];
}

/** How an official's own posts score toward same-party vs cross-party
 *  tracked targets (self-mentions excluded). Nets withheld below the
 *  suppression floor. Also reused for the panel-wide alignment baseline. */
export interface ExpressedAlignment {
    samePartyNet: number | null;
    samePartyVolume: number;
    crossPartyNet: number | null;
    crossPartyVolume: number;
}

/** Received-tone meta: the suppression threshold, resolution coverage,
 *  bot-authored-mention exclusion count, party-collective rollups, and the
 *  global same/cross-party alignment baseline. */
export interface TargetToneMeta {
    minSampleN: number;
    resolvedMentions: number;
    unresolvedMentions: number;
    botExcludedMentions: number;
    collectives: Record<string, ReceivedTone>;
    baselines: ExpressedAlignment;
}

/** One per-entity card on the three-way (news/officials/public) sentiment
 *  dashboard. positive/negative/neutral/volume/netScore are the entity's
 *  OWN posts (MIXED folded into neutral); `received` is the tone directed
 *  AT the entity (officials only; null elsewhere). */
export interface EntitySentimentItem {
    key: string;
    kind: string;
    entityProfile: EntityProfile;
    positive: number;
    negative: number;
    neutral: number;
    volume: number;
    netScore: number | null;
    classificationSamples: ClassificationSample[];
    received?: ReceivedTone | null;
    expressedAlignment?: ExpressedAlignment | null;
    byTopic: EntityTopicCell[];
    outbound?: OutboundTargets | null;
    engagementTotal: number;
    dailyTone: EntityDailyTonePoint[];
}

export interface ToneTrendCell {
    net: number | null;
    volume: number;
}

/** One day of the day-by-tier expressed net-tone series overlaying the
 *  news/officials/public columns on one chart. */
export interface ToneTrendPoint {
    date: string;
    news: ToneTrendCell;
    officials: ToneTrendCell;
    public: ToneTrendCell;
}

export interface SentimentPanelResponse {
    range: RangeMeta;
    overview: SentimentOverview;
    distribution: SentimentDistribution;
    byPlatform: PlatformSentiment[];
    byTopic: TopicSentiment[];
    byTimeOfDay: TimeOfDaySentiment[];
    byDayOfWeek: DayOfWeekSentiment[];
    byTier: TierSplit[];
    daily: DailySentiment[];
    entityStances: EntityStanceAggregate[];
    samples: SampleDoc[];
    disclaimer: string;
    // Three-way (news/officials/public) per-entity rollups, restoring the
    // pre-Postgres PublicSentimentData.byNewsOutlet/byOfficial/byGeneralPublic
    // contract (docs/todos/ui-feature-restoration.md).
    byNewsOutlet: EntitySentimentItem[];
    byOfficial: EntitySentimentItem[];
    byGeneralPublic: EntitySentimentItem[];
    targetTone: TargetToneMeta | null;
    toneTrend: ToneTrendPoint[];
    // Keyed by SentimentDistribution's own field names (e.g. 'strongPositive').
    distributionSamples: Record<string, ClassificationSample[]>;
    daySamples: Record<string, ClassificationSample[]>;
}

// --------------------------------------------------------------------------- //
//  Bot activity panel (GET /bot-activity)                                     //
// --------------------------------------------------------------------------- //

export interface BehavioralSignalBucket {
    label: string;
    docCount: number;
    avgLlmTextLikelihood: number | null;
    avgBurstiness: number | null;
    avgTypeTokenRatio: number | null;
    avgTemplateScore: number | null;
}

export interface AccountAgeBucket {
    ageRange: string;
    accountCount: number;
    // Share of all bot-flagged authors this bucket represents (0.0 when
    // there are no bot-flagged authors at all, never divide-by-zero).
    percentage: number;
}

export interface EntityBotRate {
    entityId: number;
    entityKey: string;
    kind: string;
    displayName: string;
    totalDocs: number;
    botDocs: number;
    botRatePct: number;
}

/** Count of bot-flagged docs published in this UTC hour-of-day (0-23) across
 *  the whole window -- the histogram coordinationIndex is computed over. All
 *  24 hours are present even at count 0. */
export interface PostingCadenceBucket {
    hour: number;
    docCount: number;
}

export interface BotPushedNarrative {
    narrativeId: number;
    name: string;
    memberDocCount: number;
    botAuthoredDocCount: number;
    botFractionPct: number;
    samples: SampleDoc[];
}

export interface FlaggedAccount {
    authorId: number;
    platform: string;
    handle: string | null;
    displayName: string | null;
    flaggedPostShare: number;
    sampleCount: number;
    followersCount: number | null;
    lean: LeanLabel | null;
    samples: SampleDoc[];
}

/** One bot-flagged doc shown as evidence on a BotEntityItem card. Restores
 *  the pre-Postgres `FlaggedExample` shape. `url` is always present
 *  (invariant C1), never synthesized. */
export interface FlaggedExample {
    docId: number;
    text: string;
    url: string;
    sourceLabel: string;
    confidence?: number | null;
    reasoning?: string | null;
    indicators: string[];
}

/** One entity's bot-classification rollup for the officials / general-
 *  public grid (restores the pre-Postgres `BotEntityItem`). `samples` are
 *  confidence-ranked, capped at 3 -- an evidence preview, not a full
 *  drill-down. */
export interface BotEntityItem {
    key: string;
    kind: string;
    entityProfile: EntityProfile;
    totalDocs: number;
    botDocs: number;
    botRatePct: number;
    samples: FlaggedExample[];
}

/** Restores the pre-Postgres `CoordinationStats`. `identicalTextPairs` has
 *  no Postgres source (the old O(n^2) shingle-similarity scan was never
 *  ported) -- always null, never fabricated. */
export interface CoordinationStats {
    accountReuse: number;
    avgPostsPerSuspectedAccount: number;
    identicalTextPairs: number | null;
}

/** One cell of the day-of-week x hour-of-day bot-flagged posting-cadence
 *  grid (restores the pre-Postgres `HeatmapDataPoint`). All 7 x 24 = 168
 *  cells are present even at count 0. `dayOfWeek` follows Postgres
 *  EXTRACT(dow) (and JS Date.getDay()): Sunday=0 .. Saturday=6. */
export interface PostingCadenceCell {
    dayOfWeek: number;
    hour: number;
    docCount: number;
}

export interface BotActivityResponse {
    range: RangeMeta;
    analyzedDocCount: number;
    botScoredDocCount: number;
    automationRatePct: number;
    // Docs labelled 'bot' OR 'suspicious' -- restores the pre-Postgres
    // BotOverview.totalFlaggedPosts, wider than botScoredDocCount.
    totalFlaggedPosts: number;
    behavioralSignals: BehavioralSignalBucket[];
    accountAgeBuckets: AccountAgeBucket[];
    // Max single-hour share of the bot-flagged posting-cadence histogram
    // (postingCadence) -- 1.0 means every bot-flagged doc in range posted
    // in the same UTC hour, 0.0 when there is no bot-flagged activity at all.
    coordinationIndex: number;
    postingCadence: PostingCadenceBucket[];
    // Day-of-week x hour-of-day grid over the same bot-flagged docs --
    // restores the pre-Postgres BehavioralSignals.postingCadence heatmap.
    postingCadenceGrid: PostingCadenceCell[];
    byEntity: EntityBotRate[];
    // Officials / general-public entity rollups -- restores the
    // pre-Postgres BotOverview.by_official / by_general_public three-way
    // grid (news is out of scope here; there is no byNewsOutlet).
    byOfficial: BotEntityItem[];
    byGeneralPublic: BotEntityItem[];
    coordinationStats: CoordinationStats;
    botPushedNarratives: BotPushedNarrative[];
    flaggedAccounts: FlaggedAccount[];
    flaggedDocs: SampleDoc[];
}

// --------------------------------------------------------------------------- //
//  Narratives panel (GET /narratives)                                         //
// --------------------------------------------------------------------------- //

export interface SourceBreakdownItem {
    source: 'news' | 'reddit' | 'x';
    docCount: number;
}

export interface TimelinePoint {
    day: string;
    docCount: number;
}

/** Faction context for an X-origin narrative's first-seen doc (old
 *  pre-Postgres `AccountProfile` subset carried by `firstSeenAuthor`).
 *  `branch`/`chamber`/`stateOrDistrict` have no PG equivalent (that data
 *  lived in the retired SQLite `account_profiles` table) -- always null
 *  here, never guessed. */
export interface AccountProfile {
    handle: string | null;
    fullName: string | null;
    party: string | null;
    branch: string | null;
    chamber: string | null;
    stateOrDistrict: string | null;
    officeTitle: string | null;
}

export interface NarrativeSummary {
    narrativeId: number;
    name: string;
    anchorClaimText: string | null;
    docCount: number;
    sourceBreakdown: SourceBreakdownItem[];
    timeline: TimelinePoint[];
    netSentiment: number | null;
    citationCount: number;
    citedDocs: SampleDoc[];
    propagandaFlaggedFraction: number | null;
    botPushedFraction: number | null;
    lean: LeanLabel | null;
    memberDocSamples: SampleDoc[];
    // First-ingested-by-us, not claim origin in the world. Null whenever the
    // narrative predates the first_seen columns existing.
    firstSeenAt: string | null;
    firstSeenDocId: number | null;
    // First-seen-doc provenance (old contract restoration) -- all null
    // together whenever firstSeenDocId is null.
    firstSeenSourceType: string | null;
    firstSeenDomain: string | null;
    // corpus.author_profiles.tier of the first-seen doc's author; null when
    // the doc has no author or the author has no profile row.
    firstSeenTier: string | null;
    firstSeenAuthor: AccountProfile | null;
    firstSeenEntityProfile: EntityProfile | null;
    // Coarse three-way frame: 'news' for a news first-seen doc, 'officials'
    // when firstSeenTier is elected_official/affiliated, else 'public'.
    firstSeenTierGroup: 'news' | 'officials' | 'public' | null;
    // True when the narrative's in-window member docs span more than one
    // of the three tier groups above.
    crossTier: boolean;
    // analysis.citations grouped by link_type, targeting this narrative's
    // in-window member docs.
    inboundByLinkType: Record<string, number>;
    // Up to MAX_EVIDENCE_PER_SAMPLE rich evidence samples for the modal
    // drill-down -- restores the old ClassificationSample-backed table.
    topSupportingDocs: ClassificationSample[];
    // Mean claim-match confidence over ALL in-window member docs -- not just
    // the ranked subset memberDocSamples carries. Null when no member doc in
    // range has a non-null confidence.
    meanConfidence: number | null;
}

export interface NarrativesResponse {
    range: RangeMeta;
    narratives: NarrativeSummary[];
}

// --------------------------------------------------------------------------- //
//  Propaganda panel (GET /propaganda)                                        //
// --------------------------------------------------------------------------- //

export type PropagandaTechniqueName =
    | 'loaded_language' | 'name_calling' | 'ad_hominem'
    | 'appeal_to_fear' | 'whataboutism' | 'doubt_casting';

export interface TechniqueCount {
    technique: string;
    count: number;
    pctOfFlaggedDocs: number;
    sampleEvidence: string[];
}

export interface SourceSplit {
    source: 'news' | 'social';
    totalDocs: number;
    flaggedDocs: number;
    flaggedRatePct: number;
    meanScore: number;
}

export interface PartySplit {
    party: string;
    totalDocs: number;
    flaggedDocs: number;
    flaggedRatePct: number;
    meanScore: number;
}

/** One registry entity's propaganda footprint (top 20, ranked). `group` is
 *  'news'/'subreddit' for an outlet/subreddit-resolved entity, or the
 *  author's tier ('elected_official' etc.) for an author-resolved entity. */
export interface PropagandaEntityRow {
    entityId: number;
    entityKey: string;
    displayName: string;
    group: string;
    docCount: number;
    meanDensity: number;
    flaggedShare: number;
}

/** News/Officials/Public split -- the ThreeWayGrid tiers. 'news' is
 *  source_type='news'; 'officials' is an elected-official author; every
 *  other doc (affiliated/general_public authors, unresolved authors)
 *  buckets as 'public'. */
export interface PropagandaTierSplit {
    group: 'news' | 'officials' | 'public';
    totalDocs: number;
    flaggedDocs: number;
    flaggedRatePct: number;
    meanScore: number;
}

/** One evidence span backing a PropagandaExample, from
 *  analysis.propaganda_techniques for the doc's current propaganda run. */
export interface PropagandaTechniqueSpan {
    technique: string;
    evidenceSpan: string | null;
    confidence: number;
}

/** One flagged doc backing a per-entity drill-down (restores the
 *  pre-Postgres `PropagandaExample`). `authorHandle` is null for news/reddit
 *  docs. `party` is populated only when the author resolves to the
 *  editorial 'official' bucket. */
export interface PropagandaExample {
    docId: number;
    sourceType: string;
    domain: string | null;
    title: string | null;
    overallScore: number;
    textPreview: string;
    url: string | null;
    techniques: PropagandaTechniqueSpan[];
    authorHandle: string | null;
    party: string | null;
}

/** One registry entity's (or catch-all bucket's) propaganda footprint
 *  within a single tier (news outlets / officials / general public). */
export interface PropagandaEntityItem {
    key: string;
    kind: string;
    entityProfile: EntityProfile;
    totalDocs: number;
    flaggedDocs: number;
    flaggedRatePct: number;
    meanScore: number;
}

export interface PropagandaOverview {
    range: RangeMeta;
    totalEligibleDocs: number;
    flaggedDocs: number;
    flaggedRatePct: number;
    meanScore: number;
    byTechnique: TechniqueCount[];
    bySource: SourceSplit[];
    byParty: PartySplit[];
    byTier: PropagandaTierSplit[];
    byEntity: PropagandaEntityRow[];
    examples: SampleDoc[];
    // Per-tier ranked entity leaderboards with full editorial profiles,
    // restoring the old PropagandaOverview.byNewsOutlet/byOfficial/
    // byGeneralPublic contract (docs/todos/ui-feature-restoration.md).
    byNewsOutlet: PropagandaEntityItem[];
    byOfficial: PropagandaEntityItem[];
    byGeneralPublic: PropagandaEntityItem[];
    // Per-entity flagged-example bucket, keyed by the same key used in
    // PropagandaEntityItem.key (entity_key or catch-all sentinel).
    examplesByEntity: Record<string, PropagandaExample[]>;
}

// --------------------------------------------------------------------------- //
//  Outlet profiles (GET /outlet-profiles)                                     //
// --------------------------------------------------------------------------- //

export interface OutletProfile {
    outletKey: string;
    sourceType: string;
    netTone: number | null;
    botRatePct: number;
    volume: number;
    totalScanned: number;
    samples: SampleDoc[];
}

export interface OutletProfilesResponse {
    range: RangeMeta;
    disclaimer: string;
    outlets: OutletProfile[];
}

// --------------------------------------------------------------------------- //
//  Movers (GET /movers)                                                       //
// --------------------------------------------------------------------------- //

export interface ToneMover {
    entityId: number;
    entityKey: string;
    kind: string;
    displayName: string;
    currentNet: number;
    prevNet: number;
    deltaPts: number;
    currentVolume: number;
    prevVolume: number;
}

export interface FavorabilityMover {
    entityId: number;
    entityKey: string;
    kind: string;
    displayName: string;
    currentNet: number;
    prevNet: number;
    deltaPts: number;
    currentVolume: number;
    prevVolume: number;
}

export interface MoversResponse {
    currentRange: RangeMeta;
    previousRange: RangeMeta;
    toneMovers: ToneMover[];
    topFavorabilityMover: FavorabilityMover | null;
}

// --------------------------------------------------------------------------- //
//  Entities (GET /entity-posts, GET /entity-profile/{id})                     //
// --------------------------------------------------------------------------- //

export interface EntityPostRow extends SampleDoc {
    relation: 'mentions' | 'authored_by' | 'both';
}

export interface EntityPostsResponse {
    entityId: number;
    entityKey: string;
    window: string | null;
    page: number;
    pageSize: number;
    total: number;
    items: EntityPostRow[];
}

// --------------------------------------------------------------------------- //
//  Public post feed (GET /public-posts)                                       //
// --------------------------------------------------------------------------- //

/** Paginated corpus-wide public feed: non-official Reddit/X posts, ordered
 *  by engagement. `topic` echoes the active filter; null = all topics. */
export interface PublicPostsResponse {
    window: string | null;
    topic: string | null;
    page: number;
    pageSize: number;
    total: number;
    items: ClassificationSample[];
}

export interface StanceDistribution {
    positive: number;
    negative: number;
    neutral: number;
    mixed: number;
    volume: number;
    netScore: number | null;
}

export interface AdmissionSplit {
    sampled: number;
    officialRecord: number;
}

export interface MonthlyActivity {
    month: string;
    docCount: number;
    sampledCount: number;
    officialRecordCount: number;
}

export interface EntityProfileResponse {
    entityId: number;
    entityKey: string;
    displayName: string;
    kind: string;
    lean: LeanLabel | null;
    range: RangeMeta;
    analyzedDocCounts: AdmissionSplit;
    stanceReceived: StanceDistribution;
    stanceExpressed: StanceDistribution;
    propagandaRate: number | null;
    propagandaAnalyzedDocs: number;
    firstActivity: string | null;
    lastActivity: string | null;
    monthlyActivity: MonthlyActivity[];
}

// --------------------------------------------------------------------------- //
//  Universal doc drill-down (GET /docs/{doc_id})                              //
// --------------------------------------------------------------------------- //

export interface NewsExtras {
    domain: string | null;
    extractionVersion: string | null;
    outletEntityId: number | null;
}

export interface RedditExtras {
    subreddit: string | null;
    score: number | null;
    numComments: number | null;
    subredditEntityId: number | null;
}

export interface XPostExtras {
    tweetId: string;
    conversationId: string | null;
    lang: string | null;
    placeCountryCode: string | null;
    retweetCount: number | null;
    replyCount: number | null;
    likeCount: number | null;
    quoteCount: number | null;
    isOfficialTier: boolean | null;
    referencedTweetId: string | null;
    referencedTweetType: string | null;
}

/** One current run against this doc. `fields` carries task-specific
 *  result columns — deliberately untyped since the shape varies by task. */
export interface AnalysisResult {
    task: string;
    runId: number;
    confidence: number | null;
    modelId: string;
    promptVersion: string | null;
    fields: Record<string, unknown>;
}

export interface CitationEdge {
    linkType: string;
    docId: number | null;
    sourceUrl: string | null;
    publishedAt: string | null;
    admissionClass: AdmissionClass | null;
    targetUrl: string | null;
}

export interface DocumentDetail {
    docId: number;
    sourceType: 'news' | 'reddit_post' | 'x_post';
    domainOrSubreddit: string | null;
    authorId: number | null;
    authorHandle: string | null;
    authorDisplayName: string | null;
    authorProfileImageUrl: string | null;
    authorVerified: boolean | null;
    publishedAt: string;
    title: string | null;
    body: string;
    sourceUrl: string;
    admissionClass: AdmissionClass;
    newsExtras: NewsExtras | null;
    redditExtras: RedditExtras | null;
    xPostExtras: XPostExtras | null;
    analysisResults: AnalysisResult[];
    citationsOut: CitationEdge[];
    citationsIn: CitationEdge[];
}

// --------------------------------------------------------------------------- //
//  Ops (GET /snapshot-status, GET /pipeline-runs)                             //
// --------------------------------------------------------------------------- //

export interface PipelineRun {
    pipelineRunId: number;
    status: string;
    startedAt: string;
    completedAt: string | null;
    stageSummary: Record<string, unknown> | null;
}

export interface SnapshotStatusResponse {
    pipelineRun: PipelineRun | null;
}

export interface PipelineRunsResponse {
    pipelineRuns: PipelineRun[];
}

// --------------------------------------------------------------------------- //
//  Review endpoints — plain dicts, NOT a CamelModel (snake_case on the wire;  //
//  see analysis/src/review/service.py and the Phase 9 review contract test). //
// --------------------------------------------------------------------------- //

export type ReviewTaskType =
    | 'bot' | 'text' | 'targets' | 'propaganda' | 'claims' | 'citations' | 'account_tier';

export interface ReviewQueueItem {
    run_id: number;
    doc_id: number;
    confidence: number | null;
    model_id: string;
    prompt_version: string | null;
    raw_response: Record<string, any>;
    completed_at: string | null;
    doc: {
        source_type: string;
        admission_class: AdmissionClass;
        source_url: string;
        title: string | null;
        text_preview: string;
        text_truncated: boolean;
    };
}

export interface ReviewSubmission {
    run_id: number;
    verdict: 'correct' | 'incorrect' | 'uncertain';
    reviewer_id?: string | null;
    notes?: string | null;
    is_golden?: boolean;
    expected_label?: string | null;
}

export interface ReviewTaskStats {
    task: string;
    total_runs: number;
    reviewed: number;
    correct: number;
    incorrect: number;
    uncertain: number;
    golden: number;
    accuracy_pct: number | null;
}

export interface ReviewStats {
    per_task: ReviewTaskStats[];
}

/** Public human-agreement overlay (GET /eval-accuracy) — the one review
 *  surface the backend hand-builds as camelCase already (see
 *  review/service.py::get_public_accuracy). */
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

// --------------------------------------------------------------------------- //
//  Chart helper                                                               //
// --------------------------------------------------------------------------- //

export interface ChartDataPoint {
    [key: string]: string | number | null;
}
