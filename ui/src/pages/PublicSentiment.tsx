import { useCallback, useEffect, useMemo, useState } from 'react';
import {
    CollapsibleInfo, EmptyState, EntityHeader, EntityHubLinks, EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, MethodPopover, Modal, PostCardList,
    ThreeWayColumn, ThreeWayGrid, TierRow, TopMetricsBlock,
    entityExternalUrl, entityLeanAccent,
    officialToneStats, parseEntityParam, sampleToPostCard, sentimentStats,
} from '../components/common';
import type { ColumnSorter, TickerItem } from '../components/common';
import type {
    ClassificationSample, EntitySentimentItem, Filters,
    PollingSocialComparison, PublicSentimentData, SentimentBreakdown,
    SentimentDistribution, SentimentSegmentKey,
} from '../types';
import {
    fetchEntityPosts, fetchEvalAccuracy, fetchSentiment, fetchSnapshotStatus,
    type SnapshotStatus, type TimeWindow,
} from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { formatRefreshedAgo, getSnapshotTimestamp } from '../services/freshness';
import { formatPct, formatPts } from '../services/format';
import { transformPublicSentiment } from '../services/transformers';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import {
    TOPICS, topicByKey, topicFromSlug,
    type Topic, type TopicKey,
} from '../services/topics';
import { readHashParam, useDeepLinkParam, writeHashParam } from '../services/deepLink';
import { OutletSignalsPanel } from './publicSentiment/OutletSignalsPanel';
import { TopicDivergencePanel } from './publicSentiment/TopicDivergencePanel';
import { TopicTabBar } from './publicSentiment/TopicTabBar';
import { ToneTrendPanel } from './publicSentiment/ToneTrendPanel';


// --------------------------------------------------------------------------- //
//  Top metrics block — Bloomberg-style dense header                           //
// --------------------------------------------------------------------------- //

interface TierAggregate {
    net: number | null;     // null = no data for this tier under the active filter
    volume: number;
}

function aggregateTier(items: EntitySentimentItem[] | undefined): TierAggregate {
    if (!items || items.length === 0) return { net: null, volume: 0 };
    let pos = 0, neg = 0, neu = 0;
    for (const it of items) {
        pos += it.positive;
        neg += it.negative;
        neu += it.neutral;
    }
    const total = pos + neg + neu;
    if (total === 0) return { net: null, volume: 0 };
    const net = ((pos - neg) / total) * 100;
    return { net: Math.round(net * 10) / 10, volume: total };
}

/** Tier aggregates pulled from a single per-topic byTopic row. Returns
 *  null-net for tiers the aggregator marked as having zero volume on the
 *  topic — the UI distinguishes that from a real zero-net reading. */
function aggregateTopicTier(
    topicRow: SentimentBreakdown | null,
    tier: 'news' | 'officials' | 'public',
): TierAggregate {
    if (!topicRow) return { net: null, volume: 0 };
    const netKey = tier === 'news' ? 'newsNet' : tier === 'officials' ? 'officialsNet' : 'publicNet';
    const volKey = tier === 'news' ? 'newsVolume' : tier === 'officials' ? 'officialsVolume' : 'publicVolume';
    const net = topicRow[netKey] as number | null | undefined;
    const volume = (topicRow[volKey] as number | undefined) ?? 0;
    if (net == null || volume === 0) return { net: null, volume };
    return { net: Math.round(net * 10) / 10, volume };
}

function toneVerb(net: number): string {
    if (net > 15) return 'clearly positive';
    if (net > 5)  return 'slightly positive';
    if (net < -15) return 'clearly negative';
    if (net < -5)  return 'slightly negative';
    return 'roughly neutral';
}

function toneColor(net: number): string {
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return 'var(--neutral-500)';
}

interface TopMetricsProps {
    data: PublicSentimentData;
    windowLabel: string;
    activeTopic: Topic;
    /** Per-topic row matching `activeTopic`, or null when activeTopic is
     *  'all'. Pre-resolved by the parent so TopMetrics doesn't repeat the
     *  lookup. */
    topicRow: SentimentBreakdown | null;
    /** Opens the per-intensity samples modal for a distribution segment. */
    onSegmentClick?: (segment: SentimentSegmentKey) => void;
}

function TopMetrics({ data, windowLabel, activeTopic, topicRow, onSegmentClick }: TopMetricsProps) {
    const isFiltered = activeTopic.key !== 'all';

    // When filtered: tier rows derive from the per-topic three-way row in
    // data.byTopic (already pre-computed by the aggregator). When not:
    // fall back to the global per-tier entity rollups.
    const news      = isFiltered
        ? aggregateTopicTier(topicRow, 'news')
        : aggregateTier(data.byNewsOutlet);
    const officials = isFiltered
        ? aggregateTopicTier(topicRow, 'officials')
        : aggregateTier(data.byOfficial);
    const pub       = isFiltered
        ? aggregateTopicTier(topicRow, 'public')
        : aggregateTier(data.byGeneralPublic);

    const eyebrow = isFiltered
        ? `${activeTopic.label} · As of ${windowLabel}`
        : `As of ${windowLabel}`;

    const filteredVolume = (news.volume) + (officials.volume) + (pub.volume);
    const meta = isFiltered
        ? `${filteredVolume.toLocaleString()} posts on ${activeTopic.label}`
        : `${data.overview.volume.toLocaleString()} posts`;

    return (
        <TopMetricsBlock
            eyebrow={eyebrow}
            meta={meta}
            aux={(
                <IntensityMini
                    distribution={data.distribution}
                    allTopics={isFiltered}
                    onSegmentClick={onSegmentClick}
                />
            )}
        >
            <ToneTierRow label="News articles are" agg={news} />
            <ToneTierRow label="Officials are" agg={officials} />
            <ToneTierRow label="The public is" agg={pub} />
        </TopMetricsBlock>
    );
}

function ToneTierRow({ label, agg }: { label: string; agg: TierAggregate }) {
    const hasData = agg.net !== null;
    const color = hasData ? toneColor(agg.net!) : 'var(--neutral-500)';
    const axisPct = hasData ? ((agg.net! + 100) / 200) * 100 : undefined;

    return (
        <TierRow
            label={label}
            value={hasData ? formatPts(agg.net!) : '—'}
            valueColor={color}
            verb={hasData
                ? `${toneVerb(agg.net!)} · ${agg.volume.toLocaleString()} sampled posts`
                : 'no posts on this topic'}
            showZeroTick
            dotPct={axisPct}
            dotColor={hasData ? color : undefined}
        />
    );
}

function IntensityMini({
    distribution, allTopics = false, onSegmentClick,
}: {
    distribution: SentimentDistribution;
    allTopics?: boolean;
    onSegmentClick?: (segment: SentimentSegmentKey) => void;
}) {
    // The distribution is a site-wide rollup, not topic-scoped. Inside a
    // topic filter, mark the label "all topics" so it isn't misread as the
    // filtered topic's intensity (R-2).
    const label = allTopics ? 'Tone intensity · all topics' : 'Tone intensity';
    const total = distribution.strongPositive + distribution.mildPositive
        + distribution.neutral + distribution.mildNegative + distribution.strongNegative;

    if (total === 0) {
        return (
            <div className="mini-metric">
                <span className="mini-metric-label">{label}</span>
                <span className="mini-metric-value mini-metric-value-muted">—</span>
                <span className="mini-metric-visual">
                    <span
                        className="mini-metric-bar mini-intensity mini-metric-bar-empty"
                        aria-label="No tone distribution in this filter"
                        title="No distribution data for the current filter."
                    />
                </span>
            </div>
        );
    }

    const pct = (n: number) => (n / total) * 100;
    interface Bucket {
        key: SentimentSegmentKey;
        name: string;
        count: number;
        barClass: string;
    }
    const buckets: Bucket[] = [
        { key: 'strongPositive', name: 'strongly positive', count: distribution.strongPositive, barClass: 'mini-bar-strongpos' },
        { key: 'mildPositive',   name: 'mild positive',     count: distribution.mildPositive,   barClass: 'mini-bar-mildpos' },
        { key: 'neutral',        name: 'neutral',           count: distribution.neutral,        barClass: 'mini-bar-neu' },
        { key: 'mildNegative',   name: 'mild negative',     count: distribution.mildNegative,   barClass: 'mini-bar-mildneg' },
        { key: 'strongNegative', name: 'strongly negative', count: distribution.strongNegative, barClass: 'mini-bar-strongneg' },
    ];
    const biggest = buckets.reduce((a, b) => (a.count >= b.count ? a : b));
    const biggestPct = (biggest.count / total) * 100;

    const sampleSize = total.toLocaleString();
    const barTitle =
        `Tone intensity across ${sampleSize} sampled posts: ` +
        buckets.map((b) => `${pct(b.count).toFixed(0)}% ${b.name}`).join(' · ') +
        (onSegmentClick ? '. Click a segment to read its posts.' : '.');
    const hintTitle =
        `${formatPct(biggestPct, { decimals: 0 })} of ${sampleSize} posts fall in the "${biggest.name}" bucket.`;

    return (
        <div
            className="mini-metric"
            title={`Tone intensity distribution across ${sampleSize} sampled posts.`}
        >
            <span className="mini-metric-label">{label}</span>
            <span className="mini-metric-value">
                most {biggest.name}
            </span>
            <span className="mini-metric-visual">
                <span
                    className="mini-metric-bar mini-intensity"
                    aria-label={barTitle}
                    title={barTitle}
                >
                    {buckets.map((b) => onSegmentClick ? (
                        <button
                            key={b.key}
                            type="button"
                            className={`${b.barClass} mini-bar-segment-btn`}
                            style={{ width: `${pct(b.count)}%` }}
                            onClick={() => onSegmentClick(b.key)}
                            aria-label={`${b.name}: ${pct(b.count).toFixed(0)}% of posts. Read example posts.`}
                            title={`${b.name}: ${pct(b.count).toFixed(0)}% — click to read example posts`}
                        />
                    ) : (
                        <span key={b.key} className={b.barClass} style={{ width: `${pct(b.count)}%` }} />
                    ))}
                </span>
                <span className="mini-metric-hint" title={hintTitle}>
                    {formatPct(biggestPct, { decimals: 0 })} of posts
                </span>
            </span>
        </div>
    );
}

// --------------------------------------------------------------------------- //
//  Intensity-segment samples modal — reads the distributionSamples bucket     //
//  the aggregator has always written but the UI never rendered.               //
// --------------------------------------------------------------------------- //

const SEGMENT_TITLES: Record<SentimentSegmentKey, string> = {
    strongPositive: 'Strongly positive posts',
    mildPositive: 'Mildly positive posts',
    neutral: 'Neutral posts',
    mildNegative: 'Mildly negative posts',
    strongNegative: 'Strongly negative posts',
};

function SegmentSamplesModal({
    segment, samples, onClose,
}: {
    segment: SentimentSegmentKey;
    samples: ClassificationSample[];
    onClose: () => void;
}) {
    return (
        <Modal isOpen onClose={onClose} title={SEGMENT_TITLES[segment]}>
            {samples.length > 0 ? (
                <PostCardList
                    posts={samples.map(sampleToPostCard)}
                    sampleNote="The highest-confidence posts in this intensity bucket — a sample, not the full list. Highlighted text is the evidence the model quoted."
                />
            ) : (
                <p className="text-sm text-muted">
                    No example posts stored for this bucket in the current snapshot.
                    Buckets fill in on the next data refresh.
                </p>
            )}
        </Modal>
    );
}


// --------------------------------------------------------------------------- //
//  Three-way grid                                                             //
// --------------------------------------------------------------------------- //

interface ThreeWayGridProps {
    newsOutlets: EntitySentimentItem[];
    officials: EntitySentimentItem[];
    generalPublic: EntitySentimentItem[];
    onOpen: (item: EntitySentimentItem) => void;
    activeTopic: Topic;
}

const SENTIMENT_SORTERS: ColumnSorter<EntitySentimentItem>[] = [
    { label: 'posts', compare: (a, b) => b.volume - a.volume },
    { label: 'net tone', compare: (a, b) => b.netScore - a.netScore },
    { label: 'name', compare: (a, b) => a.entityProfile.displayName.localeCompare(b.entityProfile.displayName) },
];

function SentimentThreeWayGrid({
    newsOutlets, officials, generalPublic, onOpen, activeTopic,
}: ThreeWayGridProps) {
    // Surface the modal's top received-tone topic on the card itself so
    // the most-read insight doesn't require a click to discover.
    const officialReadsAs = (item: EntitySentimentItem): string | undefined => {
        if (item.kind !== 'official') return undefined;
        const cells = (item.received?.byTopic ?? []).filter((c) => c.net != null);
        if (cells.length === 0) return undefined;
        const top = cells.reduce((a, b) => (b.volume > a.volume ? b : a));
        return `Mentioned mostly about ${top.topic} — ${toneVerb(top.net!)}.`;
    };
    const renderCard = (item: EntitySentimentItem) => (
        <EntityProfileCard
            key={item.key}
            profile={item.entityProfile}
            readsAs={officialReadsAs(item)}
            stats={item.kind === 'official'
                // Officials split the metric: received tone (posts about
                // them, the reputational signal) leads; expressed tone
                // (their own posts) stays, explicitly labeled.
                ? officialToneStats({
                    received: item.received,
                    netTone: item.netScore,
                    volume: item.volume,
                })
                : item.volume > 0
                    ? sentimentStats({ netTone: item.netScore, volume: item.volume })
                    : []}
            onClick={() => onOpen(item)}
        />
    );

    // When a topic is active we keep the same global entity cards (the
    // backend doesn't yet expose per-entity per-topic rollups — see PR
    // description). The bylines reflect that, and clicking a card opens
    // a modal that DOES filter its evidence to the active topic, so the
    // user-visible drill-down is honest even when the card-level score
    // remains global.
    const topicSuffix = activeTopic.key === 'all'
        ? ''
        : ` · scores cover all topics; click a card to see its ${activeTopic.label} posts`;

    return (
        <ThreeWayGrid>
            <ThreeWayColumn
                header="The News"
                byline={`Top outlets by coverage volume, with their editorial lean${topicSuffix}`}
                empty="No news articles in this window."
                items={newsOutlets}
                renderItem={renderCard}
                sorters={SENTIMENT_SORTERS}
            />
            <ThreeWayColumn
                header="Politicians & Officials"
                byline={`Tracked officeholders posting on X${topicSuffix}`}
                empty="No officials have posted in this window yet."
                items={officials}
                renderItem={renderCard}
                sorters={SENTIMENT_SORTERS}
            />
            <ThreeWayColumn
                header="The Public"
                byline={`Political subreddits, curated political accounts, and the most active X voices in our sample${topicSuffix}`}
                empty="No social posts in this window."
                items={generalPublic}
                renderItem={renderCard}
                sorters={SENTIMENT_SORTERS}
            />
        </ThreeWayGrid>
    );
}


// --------------------------------------------------------------------------- //
//  Entity detail modal (sentiment page)                                        //
// --------------------------------------------------------------------------- //

const SPEAKER_TIER_LABELS: Record<string, string> = {
    news: 'News outlets',
    officials: 'Officials',
    affiliated: 'Politically affiliated accounts',
    public: 'General public',
};

// --------------------------------------------------------------------------- //
//  Tone bar rows — the modal's received-tone breakdowns as dot-on-axis        //
//  rows instead of bare tables, matching the divergence panel's visual        //
//  language. Suppressed nets ("low sample") stay words, never numbers.        //
// --------------------------------------------------------------------------- //

interface ToneBarRow {
    key: string | number;
    label: string;
    net: number | null;
    volume: number;
}

function ToneBarRows({ rows }: { rows: ToneBarRow[] }) {
    return (
        <div className="tone-bar-rows">
            {rows.map((row) => (
                <div key={row.key} className="tone-bar-row" title={row.net != null
                    ? `${row.label}: ${formatPts(row.net)} across ${row.volume} posts`
                    : `${row.label}: only ${row.volume} post${row.volume === 1 ? '' : 's'} — too few to score reliably`}
                >
                    <span className="tone-bar-row-label">{row.label}</span>
                    <span className="tone-bar-row-axis" aria-hidden>
                        <span className="tone-bar-row-zero" />
                        {row.net != null && (
                            <span
                                className="tone-bar-row-dot"
                                style={{
                                    left: `${((Math.max(-100, Math.min(100, row.net)) + 100) / 200) * 100}%`,
                                    background: toneColor(row.net),
                                }}
                            />
                        )}
                    </span>
                    <span className="tone-bar-row-value" style={row.net != null ? { color: toneColor(row.net) } : undefined}>
                        {row.net != null ? formatPts(row.net) : 'low sample'}
                    </span>
                    <span className="tone-bar-row-n">n={row.volume}</span>
                </div>
            ))}
        </div>
    );
}

function EntitySentimentModal({
    item, onClose, activeTopic, timeWindow,
}: {
    item: EntitySentimentItem;
    onClose: () => void;
    activeTopic: Topic;
    timeWindow: TimeWindow;
}) {
    const { entityProfile: profile, netScore, volume, classificationSamples } = item;
    const sourceUrl = entityExternalUrl(profile);
    const isOfficial = profile.kind === 'official';
    const received = isOfficial ? item.received ?? null : null;
    const alignment = isOfficial ? item.expressedAlignment ?? null : null;

    // Live drill-down: the cached snapshot carries only ~10 highest-
    // confidence samples; "Show all posts" pages the full list from the
    // /entity-posts read path.
    const [loadedPosts, setLoadedPosts] = useState<ClassificationSample[] | null>(null);
    const [loadedTotal, setLoadedTotal] = useState<number>(0);
    const [postsLoading, setPostsLoading] = useState(false);
    const [postsError, setPostsError] = useState<string | null>(null);

    const loadMorePosts = useCallback(async () => {
        setPostsLoading(true);
        setPostsError(null);
        try {
            const page = await fetchEntityPosts(
                item.kind, item.key, timeWindow, 50, loadedPosts?.length ?? 0,
            );
            setLoadedPosts([...(loadedPosts ?? []), ...page.items]);
            setLoadedTotal(page.total);
        } catch (e) {
            setPostsError(e instanceof Error ? e.message : 'Failed to load posts');
        } finally {
            setPostsLoading(false);
        }
    }, [item.kind, item.key, timeWindow, loadedPosts]);

    // When a topic is active, filter the visible samples by the backend's
    // per-sample topic attribution (LLM mention topic with keyword
    // fallback) — an exact match, not client-side keyword guessing.
    // Samples from pre-topic cached snapshots have no `topic` and simply
    // don't match. Entity-scoped-by-topic SCORES still don't exist, so the
    // headline net score remains the entity's global score — the topic
    // strip is explicit about that.
    const allSamples: ClassificationSample[] = loadedPosts ?? classificationSamples ?? [];
    const filteredSamples = activeTopic.key === 'all'
        ? allSamples
        : allSamples.filter(s => s.topic === activeTopic.key);
    const samplesAreFiltered = activeTopic.key !== 'all';

    // Topic-scoped expressed score: exact per-topic cells from the backend,
    // net suppressed (null) below its small-n floor. An entirely missing
    // byTopic means a pre-topic cached snapshot — keep the old
    // "not yet available" copy for that case.
    const topicCell = samplesAreFiltered
        ? (item.byTopic ?? []).find(c => c.topic === activeTopic.key) ?? null
        : null;

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle={buildEntitySubtitle(profile)}
            accentColor={entityLeanAccent(profile)}
        >
            {samplesAreFiltered && (
                <TopicScopeStrip
                    activeTopic={activeTopic}
                    matched={filteredSamples.length}
                    total={allSamples.length}
                />
            )}

            <EntityHeader profile={profile} />

            <div className="entity-modal-stats">
                {received && received.volume > 0 && (
                    <div>
                        <div
                            className="eyebrow"
                            title="Average tone of sampled posts that talk ABOUT this person, from -100 (all negative) to +100 (all positive). Does not include their own posts about others."
                        >
                            Received tone
                        </div>
                        <div className="metric-value">
                            {received.net != null ? formatPts(received.net) : '—'}
                        </div>
                        <div className="text-xs text-muted">
                            {received.net != null
                                ? `across ${received.volume} sampled posts about them`
                                : `only ${received.volume} sampled post${received.volume === 1 ? '' : 's'} about them — too few to score reliably`}
                        </div>
                        {received.engagementWeightedNet != null && (
                            <div
                                className="text-xs text-muted"
                                title="Same posts, each weighted by 1 + ln(1 + retweets + replies + likes + quotes). Engagement counts are a reach proxy, not verified reach."
                            >
                                weighted by engagement: {formatPts(received.engagementWeightedNet)}
                            </div>
                        )}
                    </div>
                )}
                <div>
                    <div
                        className="eyebrow"
                        title={isOfficial
                            ? "Average tone of this person's OWN posts, from -100 to +100. A negative value means they post negatively (often about opponents) — not that others are negative about them."
                            : "Positive minus negative share of this source's posts, from -100 (all negative) to +100 (all positive)."}
                    >
                        {isOfficial ? 'Expressed tone' : 'Net tone'}
                    </div>
                    <div className="metric-value">
                        {samplesAreFiltered
                            ? (topicCell && topicCell.net != null ? formatPts(topicCell.net) : '—')
                            : (volume > 0 ? formatPts(netScore) : '—')}
                    </div>
                    {samplesAreFiltered && (
                        <div className="text-xs text-muted">
                            {topicCell
                                ? (topicCell.net != null
                                    ? `${activeTopic.label} only — across ${topicCell.volume} post${topicCell.volume === 1 ? '' : 's'}`
                                    : `only ${topicCell.volume} ${activeTopic.label} post${topicCell.volume === 1 ? '' : 's'} — too few to score reliably`)
                                : (item.byTopic
                                    ? `no ${activeTopic.label} posts in this window`
                                    : `across all topics — ${activeTopic.label}-only score not yet available`)}
                        </div>
                    )}
                    {samplesAreFiltered && volume > 0 && (
                        <div className="text-xs text-muted">
                            all topics: {formatPts(netScore)}
                        </div>
                    )}
                </div>
                <div>
                    <div className="eyebrow">Posts scored</div>
                    <div className="metric-value">{volume.toLocaleString()}</div>
                </div>
            </div>

            {received && received.byTopic.length > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">
                        Tone toward {profile.displayName} by topic
                    </h3>
                    <ToneBarRows
                        rows={received.byTopic.map((cell) => ({
                            key: cell.topic, label: cell.topic, net: cell.net, volume: cell.volume,
                        }))}
                    />
                </>
            )}

            {received && (received.bySpeakerTier?.length ?? 0) > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">
                        Who is talking about {profile.displayName}
                    </h3>
                    <ToneBarRows
                        rows={received.bySpeakerTier!.map((cell) => ({
                            key: cell.tier,
                            label: SPEAKER_TIER_LABELS[cell.tier] ?? cell.tier,
                            net: cell.net,
                            volume: cell.volume,
                        }))}
                    />
                </>
            )}

            {received && (received.byNarrative?.length ?? 0) > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">
                        Narratives driving these mentions
                    </h3>
                    <ToneBarRows
                        rows={received.byNarrative!.map((cell) => ({
                            key: cell.narrativeId, label: cell.name, net: cell.net, volume: cell.volume,
                        }))}
                    />
                    <p className="text-xs text-muted">
                        Narrative labels come from claim clustering over the posts we
                        sampled — an association between ingested documents, not a
                        claim about where the narrative originated.
                    </p>
                </>
            )}

            {alignment && (alignment.samePartyVolume > 0 || alignment.crossPartyVolume > 0) && (
                <p className="text-xs text-muted mt-2">
                    In their own posts about tracked figures: tone toward their own party{' '}
                    {alignment.samePartyNet != null
                        ? formatPts(alignment.samePartyNet)
                        : 'low sample'}{' '}
                    (n={alignment.samePartyVolume}), toward the other party{' '}
                    {alignment.crossPartyNet != null
                        ? formatPts(alignment.crossPartyNet)
                        : 'low sample'}{' '}
                    (n={alignment.crossPartyVolume}). Cross-party criticism is the
                    expected baseline — deviation from it is the signal.
                </p>
            )}

            {(sourceUrl || profile.leanSource || profile.bioSource) && (
                <div className="entity-modal-links">
                    {sourceUrl && (
                        <a href={sourceUrl} target="_blank" rel="noreferrer">
                            Visit {profile.displayName} ↗
                        </a>
                    )}
                    {profile.leanSource && (
                        <span className="text-xs text-muted">
                            Political lean rated by: {profile.leanSource}
                        </span>
                    )}
                    {profile.kind === 'official' && profile.bioSource && (
                        <a href={profile.bioSource} target="_blank" rel="noreferrer">Bio ↗</a>
                    )}
                </div>
            )}

            <EntityHubLinks profile={profile} currentTab="sentiment" />

            {filteredSamples.length > 0 ? (
                <>
                    <h3 className="card-title mt-4 mb-2">
                        {loadedPosts != null
                            ? (samplesAreFiltered
                                ? `Classified posts matching ${activeTopic.label} (newest first)`
                                : 'Classified posts (newest first)')
                            : (samplesAreFiltered
                                ? `Highest-confidence posts matching ${activeTopic.label}`
                                : 'Highest-confidence classified posts')}
                    </h3>
                    <PostCardList
                        posts={filteredSamples.map(sampleToPostCard)}
                        sampleNote={loadedPosts != null
                            ? 'Every classified post in this window, with the evidence each label rests on.'
                            : 'A sample of this window’s classified posts, not a complete feed. Highlighted text is the evidence the model quoted.'}
                    />
                </>
            ) : samplesAreFiltered ? (
                <p className="text-muted text-sm mt-4">
                    None of this entity's recent classified posts match {activeTopic.label}.
                </p>
            ) : null}

            {volume > (classificationSamples?.length ?? 0) && (
                <div className="mt-2">
                    {(loadedPosts == null || loadedPosts.length < loadedTotal) && (
                        <button
                            type="button"
                            className="btn btn-secondary"
                            onClick={loadMorePosts}
                            disabled={postsLoading}
                        >
                            {postsLoading
                                ? 'Loading…'
                                : loadedPosts == null
                                    ? `Show all ${volume.toLocaleString()} classified posts`
                                    : `Load more (${loadedPosts.length} of ${loadedTotal})`}
                        </button>
                    )}
                    {loadedPosts != null && loadedPosts.length >= loadedTotal && (
                        <p className="text-xs text-muted">
                            Showing all {loadedTotal.toLocaleString()} classified posts in this window.
                        </p>
                    )}
                    {postsError && (
                        <p className="text-xs text-muted">
                            Could not load posts: {postsError}
                        </p>
                    )}
                </div>
            )}
        </Modal>
    );
}

function TopicScopeStrip({
    activeTopic, matched, total,
}: {
    activeTopic: Topic;
    matched: number;
    total: number;
}) {
    return (
        <div
            className="modal-topic-strip"
            role="status"
            aria-label={`Filtered to topic: ${activeTopic.label}`}
        >
            <span className="modal-topic-strip-icon">
                <svg
                    viewBox="0 0 24 24"
                    width={14}
                    height={14}
                    fill="none"
                    stroke="currentColor"
                    strokeWidth={1.75}
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    aria-hidden
                >
                    {activeTopic.iconPaths.map((d, i) => <path key={i} d={d} />)}
                </svg>
            </span>
            <span className="modal-topic-strip-label">Showing: {activeTopic.label}</span>
            <span className="modal-topic-strip-detail">
                · {matched} of {total} recent posts match this topic
            </span>
        </div>
    );
}

function buildEntitySubtitle(profile: EntitySentimentItem['entityProfile']): string | undefined {
    if (profile.kind === 'outlet') {
        const parts = [profile.owner, profile.founded ? `est. ${profile.founded}` : null, profile.lean]
            .filter(Boolean);
        return parts.length > 0 ? parts.join(' · ') : undefined;
    }
    if (profile.kind === 'official') {
        return [profile.office, profile.party].filter(Boolean).join(' · ');
    }
    if (profile.kind === 'subreddit') {
        return [profile.subscriberCountProxy, profile.lean].filter(Boolean).join(' · ');
    }
    if (profile.kind === 'account') {
        return [profile.office, profile.party, profile.accountType]
            .filter(Boolean).join(' · ') || undefined;
    }
    return undefined;
}


// --------------------------------------------------------------------------- //
//  Polling-vs-online comparison — kept as a small collapsible below            //
// --------------------------------------------------------------------------- //

function PollingComparison({ data }: { data: PollingSocialComparison }) {
    return (
        <CollapsibleInfo summary="Online stance vs. recent polling">
            <p className="text-xs text-muted">
                Our GOP-stance number is derived from sampled online discussion — it's not a
                scientific poll. For reference, here's {data.pollingData?.source || 'the latest polling'}:
            </p>
            <div className="polling-pair">
                <div>
                    <div className="eyebrow">Online stance (ours)</div>
                    <div className="text-sm">
                        favorable {formatPct(data.onlineSentiment?.favorable ?? 0, { decimals: 0 })} · unfavorable{' '}
                        {formatPct(data.onlineSentiment?.unfavorable ?? 0, { decimals: 0 })}
                    </div>
                </div>
                {data.pollingData && (
                    <div>
                        <div className="eyebrow">Recent polling</div>
                        <div className="text-sm">
                            favorable {formatPct(data.pollingData.favorable, { decimals: 0 })} · unfavorable {formatPct(data.pollingData.unfavorable, { decimals: 0 })}
                            {data.pollingData.date && (
                                <span className="text-muted"> · {data.pollingData.date}</span>
                            )}
                        </div>
                    </div>
                )}
            </div>
        </CollapsibleInfo>
    );
}


// --------------------------------------------------------------------------- //
//  How-this-works collapsible                                                  //
// --------------------------------------------------------------------------- //

// Age buckets ship oldest-last from the aggregator; the strip renders in
// that order with a light-to-dark ramp (darker = fresher).
const FRESHNESS_SHADE: Record<string, string> = {
    '24 hours': 'var(--chart-accent)',
    '7 days': 'var(--chart-accent-soft)',
    '30 days': 'var(--neutral-300)',
    '90+ days': 'var(--neutral-200)',
    'Unknown': 'var(--neutral-150)',
};

function FreshnessStrip({ byTimeWindow }: { byTimeWindow: SentimentBreakdown[] }) {
    const rows = byTimeWindow.filter((w) => w.window && w.volume > 0);
    const total = rows.reduce((s, w) => s + w.volume, 0);
    if (total === 0) return null;
    const summary = rows
        .map((w) => `${Math.round((w.volume / total) * 100)}% ${w.window} old`)
        .join(' · ');
    return (
        <div className="freshness-strip">
            <div className="eyebrow" style={{ marginBottom: 'var(--space-1)' }}>
                How old is what we scored
            </div>
            <div
                className="freshness-strip-bar"
                role="img"
                aria-label={`Age mix of the ${total.toLocaleString()} scored posts: ${summary}`}
                title={`Age of the ${total.toLocaleString()} scored posts at analysis time: ${summary}.`}
            >
                {rows.map((w) => (
                    <span
                        key={w.window}
                        style={{
                            width: `${(w.volume / total) * 100}%`,
                            background: FRESHNESS_SHADE[w.window!] ?? 'var(--neutral-200)',
                        }}
                        title={`${w.window}: ${w.volume.toLocaleString()} posts (${Math.round((w.volume / total) * 100)}%)`}
                    />
                ))}
            </div>
            <div className="freshness-strip-legend text-xs text-muted">{summary}</div>
        </div>
    );
}

function HowThisWorks({ byTimeWindow }: { byTimeWindow: SentimentBreakdown[] }) {
    return (
        <CollapsibleInfo>
            <p className="text-sm">
                We aggregate news articles, Reddit posts, and X posts about US politics from the
                last 30 days, then score each one for tone (positive / negative / neutral), and
                each score must point to the exact sentence that justifies it. Tracked outlets
                and officials each get their own profile card; everything else rolls up into the
                general-public column or into an "Other" bucket for sources we don't track
                individually.
            </p>
            <p className="text-sm">
                Tone is a classification of what the post says, not what the author feels. Don't
                read it as opinion polling. Sarcasm and irony are flagged when the model detects
                them but can still be misclassified.
            </p>
            <FreshnessStrip byTimeWindow={byTimeWindow} />
        </CollapsibleInfo>
    );
}


// --------------------------------------------------------------------------- //
//  Topic state — URL persistence + default selection                          //
// --------------------------------------------------------------------------- //

const TOPIC_QS_KEY = 'topic';

function readTopicFromUrl(): TopicKey {
    try {
        // Canonical location is the hash route ("#sentiment?topic=economy",
        // see services/deepLink). Old links used a real search param
        // ("?topic=economy#sentiment") — keep reading those so they still
        // land on the right topic; the next write migrates them.
        const slug = readHashParam(TOPIC_QS_KEY)
            ?? new URLSearchParams(window.location.search).get(TOPIC_QS_KEY);
        return topicFromSlug(slug).key;
    } catch {
        return 'all';
    }
}

function writeTopicToUrl(key: TopicKey): void {
    try {
        const topic = topicByKey(key);
        // Drop the legacy search param if a pre-migration link carried one —
        // otherwise the URL would hold two topic values that can disagree.
        const search = new URLSearchParams(window.location.search);
        if (search.has(TOPIC_QS_KEY)) {
            search.delete(TOPIC_QS_KEY);
            const qs = search.toString();
            window.history.replaceState(
                {}, '',
                window.location.pathname + (qs ? `?${qs}` : '') + window.location.hash,
            );
        }
        writeHashParam(TOPIC_QS_KEY, topic.key === 'all' ? null : topic.slug);
    } catch { /* noop — older browsers / sandboxed contexts */ }
}

function pickDefaultTopic(byTopic: SentimentBreakdown[] | undefined): TopicKey {
    // First load behavior: pick the topic with the most volume in the
    // current window so the page lands on something substantive instead
    // of an arbitrary alphabetical default. Falls back to 'all' if no
    // per-topic data is available.
    if (!byTopic || byTopic.length === 0) return 'all';
    // 'General' (the unclassified bucket) usually has the largest volume
    // but is never a substantive landing tab — exclude it from the default.
    const validTopicNames = new Set(
        TOPICS.filter(t => t.key !== 'all' && t.key !== 'General').map(t => t.key),
    );
    let best: { key: TopicKey; volume: number } | null = null;
    for (const row of byTopic) {
        if (!row.topic || !validTopicNames.has(row.topic as TopicKey)) continue;
        const volume = row.volume ?? 0;
        if (!best || volume > best.volume) {
            best = { key: row.topic as TopicKey, volume };
        }
    }
    return best && best.volume > 0 ? best.key : 'all';
}


// --------------------------------------------------------------------------- //
//  Page                                                                       //
// --------------------------------------------------------------------------- //

function netToneColor(net: number): TickerItem['tone'] {
    // Green for positive so it matches the tier-row tone color (toneColor);
    // a blue "accent" here previously made the same positive reading render
    // in two different hues on the same page.
    if (net > 10) return 'positive';
    if (net < -10) return 'negative';
    return 'neutral';
}

function buildSentimentTickerItems(data: PublicSentimentData, activeTopic: Topic): TickerItem[] {
    const overall = data.overview;
    // The ticker always reads the site-wide sentiment rollup; the backend
    // doesn't expose these figures scoped to a single topic. When a topic
    // filter is active we mark each item "all topics" so a reader inside a
    // filtered view doesn't read a global number as a topic number (R-2).
    const scopeHint = activeTopic.key === 'all' ? undefined : 'all topics';
    const items: TickerItem[] = [
        {
            label: 'Net tone',
            value: formatPts(overall.netScore),
            hint: scopeHint,
            tone: netToneColor(overall.netScore),
            emphasis: true,
            ariaLabel: `Net tone ${formatPts(overall.netScore)}`,
        },
    ];
    if (data.gopFavorability) {
        const gopNet = data.gopFavorability.netFavorability;
        items.push({
            label: 'Tone toward GOP',
            value: formatPts(gopNet),
            hint: scopeHint,
            tone: netToneColor(gopNet),
            emphasis: true,
            ariaLabel: `Tone toward GOP ${formatPts(gopNet)}`,
        });
    }
    items.push(
        { label: 'Posts scored', value: overall.volume.toLocaleString(), hint: scopeHint },
    );
    return items;
}

/**
 * Static framing sentence for the Overall Tone page. When a topic is
 * active, the spec calls for "How news outlets, public officials, and
 * everyday people are feeling and talking about [TOPIC]." The unfiltered
 * default keeps the original sentence.
 */
function readsAsToday(activeTopic: Topic): string {
    if (activeTopic.key === 'all') {
        return 'How news outlets, public officials, and everyday people are reading American politics.';
    }
    return `How news outlets, public officials, and everyday people are feeling and talking about ${activeTopic.label}.`;
}


interface PublicSentimentProps {
    filters: Filters;
}

function PublicSentiment({ filters }: PublicSentimentProps) {
    const [activeEntity, setActiveEntity] = useState<EntitySentimentItem | null>(null);
    const [activeSegment, setActiveSegment] = useState<SentimentSegmentKey | null>(null);
    const [activeTopicKey, setActiveTopicKeyState] = useState<TopicKey>(() => readTopicFromUrl());
    // Tracks whether the current activeTopicKey came from the URL (or
    // explicit user click) vs. the implicit default. We only auto-pick a
    // most-discussed default on the first render where no URL value was
    // present — re-fetches on filter changes shouldn't keep flipping the
    // user's selection.
    const [pickedDefault, setPickedDefault] = useState<boolean>(() => readTopicFromUrl() !== 'all');

    const setActiveTopicKey = (key: TopicKey) => {
        setActiveTopicKeyState(key);
        setPickedDefault(true);
        writeTopicToUrl(key);
    };

    // Cross-page entity deep link ("#sentiment?entity=official:SenSchumer").
    const [entityParam, setEntityParam] = useDeepLinkParam('entity');

    const { data, loading, error, refetch } = useFetch<PublicSentimentData>(
        async () => transformPublicSentiment(await fetchSentiment(filters.timeRange)),
        [filters.timeRange],
        `sentiment:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatus>(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );
    // Human-agreement chip: how often reviewers marked our tone
    // classifications correct. Renders only when the server publishes a
    // percentage (enough scored reviews); silent otherwise.
    const { data: evalAccuracy } = useFetch(
        () => fetchEvalAccuracy(),
        [],
        'eval-accuracy',
    );
    const sentimentAgreement = evalAccuracy?.perTask.find(
        (t) => t.taskType === 'sentiment' && !t.lowSample && t.accuracyPct != null,
    ) ?? null;

    // Resolve the entity= param once data lands: search all three tier
    // lists for the kind:key. Unknown entities (or ones with no data in
    // this window) clear the param instead of erroring.
    useEffect(() => {
        if (!data || !entityParam) return;
        const target = parseEntityParam(entityParam);
        if (target) {
            const lists = [data.byNewsOutlet, data.byOfficial, data.byGeneralPublic];
            for (const list of lists) {
                const item = (list ?? []).find(
                    (it) => it.kind === target.kind && it.key === target.key,
                );
                if (item) {
                    setActiveEntity(item);
                    return;
                }
            }
        }
        setEntityParam(null);
    }, [data, entityParam, setEntityParam]);

    // Pick the most-discussed topic as the default once data lands, but
    // only if the user hasn't already chosen something (URL or click).
    useEffect(() => {
        if (pickedDefault) return;
        if (!data) return;
        const def = pickDefaultTopic(data.byTopic);
        if (def !== 'all') {
            setActiveTopicKeyState(def);
            writeTopicToUrl(def);
            setPickedDefault(true);
        }
    }, [data, pickedDefault]);

    // Sync state from back/forward navigation or an incoming deep link
    // ("#sentiment?topic=economy") that mutates the topic param while the
    // page stays mounted. hashchange covers deep links + back/forward on
    // the hash; popstate covers legacy search-param history entries.
    useEffect(() => {
        const onUrlChange = () => {
            const next = readTopicFromUrl();
            setActiveTopicKeyState(next);
            setPickedDefault(next !== 'all');
        };
        window.addEventListener('popstate', onUrlChange);
        window.addEventListener('hashchange', onUrlChange);
        return () => {
            window.removeEventListener('popstate', onUrlChange);
            window.removeEventListener('hashchange', onUrlChange);
        };
    }, []);

    const activeTopic = topicByKey(activeTopicKey);
    const topicRow = useMemo<SentimentBreakdown | null>(() => {
        if (!data || activeTopic.key === 'all') return null;
        return data.byTopic.find(t => t.topic === activeTopic.key) ?? null;
    }, [data, activeTopic.key]);

    if (error) return <ErrorState message={error.message} onRetry={refetch} />;

    if (loading) {
        return (
            <div className="flex flex-col gap-4">
                <LoadingCard />
                <div className="grid-2">
                    <LoadingCard />
                    <LoadingCard />
                </div>
            </div>
        );
    }

    if (!data) return <EmptyState title="No tone data available" />;

    const tickerItems = buildSentimentTickerItems(data, activeTopic);
    const refreshed = formatRefreshedAgo(
        getSnapshotTimestamp(snapshotStatus, `sentiment_${filters.timeRange}`),
    );

    return (
        <div className="dashboard-grid">
            <div className="col-span-12">
                <GlobalTicker
                    items={tickerItems}
                    refreshed={refreshed}
                    ariaLabel="Overall tone overview"
                    legend={
                        <MethodPopover
                            title="How to read these numbers"
                            description={
                                'Net tone = the share of sampled posts scored positive minus the share scored '
                                + 'negative, in points on a -100 to +100 scale (0 means positive and negative '
                                + 'balance out). Tone toward GOP = the net stance of sampled posts toward '
                                + 'Republican-party entities, on the same scale. Both summarize the posts we '
                                + 'collected — they are samples, not polls of the public.'
                            }
                        />
                    }
                />
                {sentimentAgreement && (
                    <p
                        className="text-xs text-muted"
                        title="Share of human-reviewed tone classifications marked correct in our review queue. Reviews cover a sample of outputs, not all of them."
                    >
                        Human review agreement on tone classifications:{' '}
                        {sentimentAgreement.accuracyPct}% across {sentimentAgreement.scored} reviewed outputs.
                    </p>
                )}
            </div>

            {/* Topic tab bar — visual anchor of the page (spec A2). */}
            <div className="col-span-12">
                <TopicTabBar
                    activeKey={activeTopicKey}
                    onChange={setActiveTopicKey}
                    byTopic={data.byTopic}
                />
            </div>

            <div className="col-span-12">
                <div className="reads-as-today">
                    <span className="eyebrow reads-as-today-eyebrow">
                        {asOfTodayEyebrow(filters.timeRange)}
                    </span>
                    <p className="lead" style={{ margin: 0 }}>{readsAsToday(activeTopic)}</p>
                </div>
            </div>

            {/* Compact top-metrics block — tier tones (topic-scoped when filtered)
                + GOP + intensity. */}
            <div className="col-span-12">
                <TopMetrics
                    data={data}
                    windowLabel={formatTimeWindow(filters.timeRange)}
                    activeTopic={activeTopic}
                    topicRow={topicRow}
                    onSegmentClick={setActiveSegment}
                />
            </div>

            {/* Topic divergence — the page's signature read: where the three
                groups disagree. Promoted above the entity grid; it was
                previously last on the page where few readers reached it. */}
            <div className="col-span-12">
                <TopicDivergencePanel
                    topics={data.byTopic}
                    onFilterTopic={(topic) => {
                        // Row topics and tab-bar keys share the backend's
                        // topic vocabulary; unknown ones (e.g. a retired
                        // topic in an old snapshot) just no-op.
                        const match = TOPICS.find((t) => t.key === topic);
                        if (match) setActiveTopicKey(match.key);
                    }}
                />
            </div>

            {/* Tone over time — per-group daily series (GOP series behind
                the toggle) + weekday rhythm. */}
            <div className="col-span-12">
                <ToneTrendPanel
                    toneTrend={data.toneTrend}
                    gopTrend={data.gopTrend}
                    byDayOfWeek={data.byDayOfWeek}
                />
            </div>

            {/* Three-way grid: News / Officials / Public. */}
            <div className="col-span-12">
                <SentimentThreeWayGrid
                    newsOutlets={data.byNewsOutlet ?? []}
                    officials={data.byOfficial ?? []}
                    generalPublic={data.byGeneralPublic ?? []}
                    onOpen={setActiveEntity}
                    activeTopic={activeTopic}
                />
            </div>

            {activeEntity && (
                <EntitySentimentModal
                    item={activeEntity}
                    onClose={() => {
                        setActiveEntity(null);
                        if (entityParam) setEntityParam(null);
                    }}
                    activeTopic={activeTopic}
                    timeWindow={filters.timeRange}
                />
            )}

            {activeSegment && (
                <SegmentSamplesModal
                    segment={activeSegment}
                    samples={data.distributionSamples?.[activeSegment] ?? []}
                    onClose={() => setActiveSegment(null)}
                />
            )}

            {/* Per-domain tone x bot-rate cross-signal table (bots included
                by design — see the panel's method note). */}
            <div className="col-span-12">
                <OutletSignalsPanel window={filters.timeRange} />
            </div>

            {/* Polling-vs-online collapsible (optional). */}
            {data.pollingVsSocial && (
                <div className="col-span-12">
                    <PollingComparison data={data.pollingVsSocial} />
                </div>
            )}

            {/* How this page works — self-documenting content + collapsible backup. */}
            <div className="col-span-12">
                <HowThisWorks byTimeWindow={data.byTimeWindow ?? []} />
            </div>
        </div>
    );
}

export default PublicSentiment;
