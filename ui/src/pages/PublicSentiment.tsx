import { useCallback, useEffect, useMemo, useState } from 'react';
import {
    AdmissionBadge, Card, CollapsibleInfo, EmptyState, EntityHeader, EntityHubLinks, EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, MethodPopover, Modal,
    ThreeWayColumn, ThreeWayGrid, ThreeWayToolbar, TierRow, TopMetricsBlock,
    entityExternalUrl, matchesLeanFilter,
    officialToneStats, parseEntityParam, sentimentStats,
} from '../components/common';
import type { ColumnSorter, LeanFilter, TickerItem } from '../components/common';
import { PostCardList, sampleToPostCard } from '../components/common/PostCard';
import type {
    ChartDataPoint, ClassificationSample, EntityPostRow, EntitySentimentItem, Filters,
    SentimentDistribution, SentimentPanelResponse, TopicSentiment,
} from '../types';
import Sparkline from '../components/charts/Sparkline';
import {
    fetchEntityPosts, fetchEvalAccuracy, fetchSentiment, fetchSnapshotStatus,
    type TimeWindow,
} from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPct, formatPts, formatRelativeDate } from '../services/format';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import {
    TOPICS, topicByKey, topicFromSlug,
    type Topic, type TopicKey,
} from '../services/topics';
import { readHashParam, useDeepLinkParam, writeHashParam } from '../services/deepLink';
import { OutletSignalsPanel } from './publicSentiment/OutletSignalsPanel';
import { TopicTabBar } from './publicSentiment/TopicTabBar';
import { ToneTrendPanel } from './publicSentiment/ToneTrendPanel';

// --------------------------------------------------------------------------- //
//  Restored verbatim from `pre-cutover-main` (docs/todos/                    //
//  ui-feature-restoration.md, "Full-fidelity restoration" -- Wave 3 UI). The  //
//  old JSX/behavior is the visual source of truth; `types.ts`/`services/api` //
//  are the data source of truth. Adaptations from the pure old file, and     //
//  why, are called out inline as they occur; the big ones:                  //
//                                                                             //
//   - `SentimentSegmentKey` (the distribution-bucket key union) no longer    //
//     lives in types.ts -- it's `keyof SentimentDistribution`, defined here. //
//   - `EntityHubLinks`/`parseEntityParam` moved from a (kind,key) pair to a  //
//     numeric `entityId` (the registry's stable identity token) -- deep      //
//     links and the entity modal's cross-page row read `entityProfile.       //
//     entityId` instead.                                                    //
//   - `fetchEntityPosts` now returns paginated `EntityPostRow` (SampleDoc +  //
//     relation), not rich `ClassificationSample` rows -- the "show all       //
//     posts" drill-down for entities without an initial-samples ceiling      //
//     therefore renders a thinner card (see `EntityPostRowList` below) than  //
//     the inline `classificationSamples`, which stay rich.                   //
//   - GOP favorability, its trend, and pollingVsSocial stay retired (owner   //
//     decision 2026-07-25/2026-07-27) -- no ticker item, no divergence row,  //
//     no data-gated polling card; PollingComparison keeps its old frame as a //
//     quiet, honest empty state.                                            //
//   - The old per-tier-per-topic `SentimentBreakdown.newsNet/officialsNet/   //
//     publicNet` field is gone; the topic-filtered tier read in `TopMetrics` //
//     is reconstructed from each entity's own `byTopic` cell (see            //
//     `aggregateTopicTier`'s docstring) -- real per-entity per-topic data,   //
//     not a fabricated number.                                              //
//   - The old per-time-window age-mix freshness strip                       //
//     (`SentimentPanelResponse.byTimeWindow`) has no contract successor;     //
//     `FreshnessStrip` below reports the latest pipeline run instead (real   //
//     data, a different but honest freshness signal) rather than an empty   //
//     shell.                                                                //
// --------------------------------------------------------------------------- //

/** The five intensity-bucket keys the tone-distribution bar and its
 *  segment-click drill-down key off of. Was a standalone type in the old
 *  contract; the current `SentimentDistribution` shape carries the same
 *  five fields, so this is just their key union. */
type SentimentSegmentKey = keyof SentimentDistribution;

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

/**
 * Topic-scoped tier aggregate, reconstructed from each entity's own
 * per-topic cell (`EntityTopicCell`: topic/net/volume/lowSample) rather than
 * a single backend-computed per-tier-per-topic field -- the old contract's
 * `SentimentBreakdown.newsNet/officialsNet/publicNet` has no successor (see
 * docs/audit-trail/api/2026-07-27-restoration-read-queries.md). A
 * volume-weighted average of the entities' nets is mathematically
 * equivalent to pooling the underlying positive/negative counts (each
 * `net_i = (pos_i - neg_i) / vol_i * 100`, so `sum(pos_i - neg_i) =
 * sum(net_i * vol_i) / 100`); cells the aggregator itself suppressed
 * (`net` null, below its own sample floor) are excluded from both the sum
 * and the reported volume -- there is no positive/negative split to pool
 * for them, and folding their volume in without a net would silently drag
 * the average toward zero.
 */
function aggregateTopicTier(items: EntitySentimentItem[] | undefined, topic: string): TierAggregate {
    if (!items) return { net: null, volume: 0 };
    let weightedNet = 0;
    let volume = 0;
    for (const item of items) {
        const cell = item.byTopic.find((c) => c.topic === topic);
        if (!cell || cell.net == null || cell.volume === 0) continue;
        weightedNet += cell.net * cell.volume;
        volume += cell.volume;
    }
    if (volume === 0) return { net: null, volume: 0 };
    return { net: Math.round((weightedNet / volume) * 10) / 10, volume };
}

function toneVerb(net: number): string {
    if (net > 15) return 'clearly positive';
    if (net > 5)  return 'slightly positive';
    if (net < -15) return 'clearly negative';
    if (net < -5)  return 'slightly negative';
    return 'roughly neutral';
}

/** Plain positive/negative/neutral for the "about X (party) · <stance>"
 *  expressed-target labels. */
function stanceWord(net: number): string {
    if (net > 5) return 'positive';
    if (net < -5) return 'negative';
    return 'neutral';
}

function toneColor(net: number): string {
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return 'var(--neutral-500)';
}

interface TopMetricsProps {
    data: SentimentPanelResponse;
    windowLabel: string;
    activeTopic: Topic;
    /** Opens the per-intensity samples modal for a distribution segment. */
    onSegmentClick?: (segment: SentimentSegmentKey) => void;
}

function TopMetrics({ data, windowLabel, activeTopic, onSegmentClick }: TopMetricsProps) {
    const isFiltered = activeTopic.key !== 'all';

    // When filtered: tier rows derive from each entity's own per-topic cell
    // (aggregateTopicTier). When not: the global per-tier entity rollups.
    const news      = isFiltered
        ? aggregateTopicTier(data.byNewsOutlet, activeTopic.key)
        : aggregateTier(data.byNewsOutlet);
    const officials = isFiltered
        ? aggregateTopicTier(data.byOfficial, activeTopic.key)
        : aggregateTier(data.byOfficial);
    const pub       = isFiltered
        ? aggregateTopicTier(data.byGeneralPublic, activeTopic.key)
        : aggregateTier(data.byGeneralPublic);

    const eyebrow = isFiltered
        ? `${activeTopic.label} · As of ${windowLabel}`
        : `As of ${windowLabel}`;

    const filteredVolume = (news.volume) + (officials.volume) + (pub.volume);
    const meta = isFiltered
        ? `${filteredVolume.toLocaleString()} posts on ${activeTopic.label}`
        : `${data.overview.volume.toLocaleString()} posts`;

    // Per-tier daily trend from the toneTrend series. Global, not
    // topic-scoped — so the trails render only in the unfiltered view (a
    // site-wide trend next to topic-scoped numbers would misread).
    // Suppressed days (net=null below the sample floor) draw as gaps.
    const tierTrend = (tier: 'news' | 'officials' | 'public') => {
        if (isFiltered) return undefined;
        const points = (data.toneTrend ?? []).map((p) => ({
            date: p.date,
            value: p[tier].net,
        }));
        return points.some((p) => p.value != null)
            ? (points as ChartDataPoint[])
            : undefined;
    };

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
            <ToneTierRow label="News articles are" agg={news} trend={tierTrend('news')} trendColor={COLORS.tierNews} />
            <ToneTierRow label="Officials are" agg={officials} trend={tierTrend('officials')} trendColor={COLORS.tierOfficials} />
            <ToneTierRow label="The public is" agg={pub} trend={tierTrend('public')} trendColor={COLORS.tierPublic} />
        </TopMetricsBlock>
    );
}

function ToneTierRow({
    label, agg, trend, trendColor,
}: {
    label: string;
    agg: TierAggregate;
    /** Daily net-tone series for this tier; undefined renders no trail. */
    trend?: ChartDataPoint[];
    trendColor?: string;
}) {
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
            trail={trend && (
                <Sparkline
                    data={trend}
                    color={trendColor}
                    height={26}
                    ariaLabel={`${label} daily net tone, last ${trend.length} days. Gaps are low-sample days.`}
                />
            )}
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
    // filtered topic's intensity.
    const label = allTopics ? 'Tone intensity · all topics' : 'Tone intensity';
    const total = distribution.strongPositive + distribution.mildPositive
        + distribution.neutral + distribution.mildNegative + distribution.strongNegative;

    if (total === 0) {
        return (
            <div className="mini-metric">
                <span className="mini-metric-label">{label}</span>
                <span className="mini-metric-value mini-metric-value-muted">—</span>
                <span className="mini-metric-visual is-intensity">
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
            <span className="mini-metric-visual is-intensity">
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
                <span className="mini-intensity-legend" title={hintTitle}>
                    {buckets.map((b) => (
                        <span
                            key={b.key}
                            className="mini-intensity-legend-item"
                            title={`${b.name}: ${b.count.toLocaleString()} of ${sampleSize} posts`}
                        >
                            <span className={`mini-intensity-legend-dot ${b.barClass}`} aria-hidden />
                            {formatPct(pct(b.count), { decimals: 0 })}
                        </span>
                    ))}
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
                    No example posts stored for this bucket in the current window.
                </p>
            )}
        </Modal>
    );
}

/** "2026-07-04" → "Friday, Jul 4, 2026" for the day modal title. */
function formatDayTitle(iso: string): string {
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
    if (!m) return iso;
    const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
    return d.toLocaleDateString(undefined, {
        weekday: 'long', month: 'short', day: 'numeric', year: 'numeric',
    });
}

/** Net tone of an officials subset filtered by party ('D' | 'R'), summed from
 *  the per-official rollups (which carry each official's party). */
function aggregateOfficialsByParty(
    items: EntitySentimentItem[] | undefined, party: string,
): TierAggregate {
    if (!items) return { net: null, volume: 0 };
    let pos = 0, neg = 0, neu = 0;
    for (const it of items) {
        if (it.entityProfile.party !== party) continue;
        pos += it.positive; neg += it.negative; neu += it.neutral;
    }
    const total = pos + neg + neu;
    if (total === 0) return { net: null, volume: 0 };
    return { net: Math.round(((pos - neg) / total) * 1000) / 10, volume: total };
}

/** Divergence graphic — how far News and each party's officials sit from the
 *  PUBLIC's net tone (the baseline). Bars diverge from a center zero: right =
 *  warmer than the public, left = harsher. */
function ToneDivergenceCard({ data }: { data: SentimentPanelResponse }) {
    const publicAgg = aggregateTier(data.byGeneralPublic);
    if (publicAgg.net === null) return null; // no baseline to compare against
    const base = publicAgg.net;

    const rows = [
        { key: 'news', label: 'News', color: COLORS.tierNews, agg: aggregateTier(data.byNewsOutlet) },
        { key: 'dem', label: 'Dem officials', color: COLORS.leanLeft, agg: aggregateOfficialsByParty(data.byOfficial, 'D') },
        { key: 'gop', label: 'GOP officials', color: COLORS.leanRight, agg: aggregateOfficialsByParty(data.byOfficial, 'R') },
    ]
        .filter((r) => r.agg.net !== null)
        .map((r) => ({ ...r, div: Math.round((r.agg.net! - base) * 10) / 10 }));
    if (rows.length === 0) return null;
    const maxAbs = Math.max(1, ...rows.map((r) => Math.abs(r.div)));

    return (
        <Card
            title="Divergence from the public"
            subtitle={`How far each group's net tone sits from the public's ${formatPts(base)} baseline. Right = warmer than the public, left = harsher. Officials split by party.`}
            headerActions={
                <MethodPopover
                    description={
                        'The public tier (subreddits + untracked social accounts) is the baseline. '
                        + "Each bar is a group's net tone minus the public's, in points, so you can "
                        + 'see whether news and each party\'s officials talk warmer or harsher than the '
                        + 'crowd. Officials are grouped by their registry party. Scores cover all '
                        + 'topics; a sample of collected posts, not a poll.'
                    }
                />
            }
        >
            <div className="tone-divergence">
                {rows.map((r) => {
                    const half = (Math.abs(r.div) / maxAbs) * 50;
                    const left = r.div >= 0 ? 50 : 50 - half;
                    return (
                        <div
                            key={r.key}
                            className="tone-divergence-row"
                            title={`${r.label}: ${formatPts(r.agg.net)} net vs the public's ${formatPts(base)} — ${r.div >= 0 ? '+' : ''}${r.div.toFixed(1)} pts`}
                        >
                            <span className="tone-divergence-label">{r.label}</span>
                            <span className="tone-divergence-track" aria-hidden>
                                <span className="tone-divergence-zero" />
                                <span
                                    className="tone-divergence-fill"
                                    style={{ left: `${left}%`, width: `${half}%`, background: r.color }}
                                />
                            </span>
                            <span className="tone-divergence-value">
                                {r.div >= 0 ? '+' : ''}{r.div.toFixed(1)}
                            </span>
                        </div>
                    );
                })}
            </div>
            <p className="card-note" style={{ marginTop: 'var(--space-2)' }}>
                Baseline: the public at {formatPts(base)} net tone.
            </p>
        </Card>
    );
}

/** Sampled posts for one calendar day — opened by clicking a point on the
 *  Tone-over-time chart, mirroring the intensity-segment drill-down. */
function DaySamplesModal({
    date, samples, onClose,
}: {
    date: string;
    samples: ClassificationSample[];
    onClose: () => void;
}) {
    return (
        <Modal isOpen onClose={onClose} kicker="Tone over time" title={`Posts sampled on ${formatDayTitle(date)}`}>
            {samples.length > 0 ? (
                <PostCardList
                    posts={samples.map(sampleToPostCard)}
                    sampleNote="The highest-confidence posts published on this day — a sample, not the full list. Highlighted text is the evidence the model quoted."
                />
            ) : (
                <p className="text-sm text-muted">
                    No example posts stored for this day in the current window.
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
    { label: 'net tone', compare: (a, b) => (b.netScore ?? 0) - (a.netScore ?? 0) },
    { label: 'name', compare: (a, b) => a.entityProfile.displayName.localeCompare(b.entityProfile.displayName) },
];

// Officials lead with engagement-weighted volume so the highest-reach voices
// surface first; the remaining orders match the other columns.
const OFFICIAL_SORTERS: ColumnSorter<EntitySentimentItem>[] = [
    { label: 'engagement', compare: (a, b) => (b.engagementTotal ?? 0) - (a.engagementTotal ?? 0) },
    ...SENTIMENT_SORTERS,
];

/** Top targets of the highest-volume public bucket ("who the public is
 *  talking about") — used to fill the public column, which otherwise often
 *  shows a single pooled "Other X users" card. */
function publicOutboundTargets(items: EntitySentimentItem[]): EntitySentimentItem['outbound'] | null {
    const withTargets = items.filter((it) => it.outbound && it.outbound.targets.length > 0);
    if (withTargets.length === 0) return null;
    return withTargets.reduce((a, b) => ((b.outbound!.volume) > (a.outbound!.volume) ? b : a)).outbound ?? null;
}

function SentimentThreeWayGrid({
    newsOutlets, officials, generalPublic, onOpen, activeTopic,
}: ThreeWayGridProps) {
    // Lean/party filter (owned here; ThreeWayColumn receives filtered items).
    const [leanFilter, setLeanFilter] = useState<LeanFilter>('all');

    const byLean = (it: EntitySentimentItem) => matchesLeanFilter(it.entityProfile, leanFilter);
    const filteredNews = newsOutlets.filter(byLean);
    const filteredOfficials = officials.filter(byLean);
    const filteredPublic = generalPublic.filter(byLean);
    // Targets are computed from the unfiltered public tier — they describe who
    // the public talks ABOUT, independent of the entities' own lean.
    const publicTargets = publicOutboundTargets(generalPublic);
    // Surface the modal's top received-tone topic on the card itself so
    // the most-read insight doesn't require a click to discover.
    const officialReadsAs = (item: EntitySentimentItem): string | undefined => {
        if (item.kind !== 'official') return undefined;
        const cells = (item.received?.byTopic ?? []).filter((c) => c.net != null);
        if (cells.length === 0) return undefined;
        const top = cells.reduce((a, b) => (b.volume > a.volume ? b : a));
        return `Mentioned mostly about ${top.topic} — ${toneVerb(top.net!)}.`;
    };
    // News outlets carry their EXPRESSED stance toward each party collective,
    // e.g. "about Democrats (party) · negative · about Republicans (party) ·
    // positive" — the outbound-target read the public cards already show.
    const newsReadsAs = (item: EntitySentimentItem): string | undefined => {
        if (item.kind !== 'outlet') return undefined;
        const parties = (item.outbound?.targets ?? [])
            .filter((t) => t.kind === 'collective' && t.net != null);
        if (parties.length === 0) return undefined;
        return parties
            .map((t) => `about ${t.label} · ${stanceWord(t.net!)}`)
            .join(' · ');
    };
    const readsAsFor = (item: EntitySentimentItem): string | undefined =>
        item.kind === 'official' ? officialReadsAs(item)
            : item.kind === 'outlet' ? newsReadsAs(item)
                : undefined;
    const renderCard = (item: EntitySentimentItem) => (
        <EntityProfileCard
            key={item.key}
            profile={item.entityProfile}
            readsAs={readsAsFor(item)}
            stats={item.kind === 'official'
                // Officials split the metric: received tone (posts about
                // them, the reputational signal) leads; expressed tone
                // (their own posts) stays, explicitly labeled.
                ? officialToneStats({
                    received: item.received,
                    netTone: item.netScore ?? 0,
                    volume: item.volume,
                })
                : item.volume > 0
                    ? sentimentStats({ netTone: item.netScore ?? 0, volume: item.volume })
                    : []}
            onClick={() => onOpen(item)}
        />
    );

    // When a topic is active we keep the same global entity cards (per-entity
    // per-topic rollups exist on the card's data via byTopic, but the card
    // itself still surfaces the all-topic score — clicking it opens a modal
    // that DOES filter its evidence and its headline score to the active
    // topic, so the user-visible drill-down is honest even when the
    // card-level score stays global).
    const topicSuffix = activeTopic.key === 'all'
        ? ''
        : ` · scores cover all topics; click a card to see its ${activeTopic.label} posts`;

    const publicTargetsFooter = publicTargets && publicTargets.targets.length > 0 ? (
        <div className="three-way-column-targets">
            <div className="eyebrow three-way-column-targets-title">
                Who the public is talking about
            </div>
            <ToneBarRows
                rows={publicTargets.targets.slice(0, 8).map((cell, i) => ({
                    key: cell.entityKey ?? `${cell.kind}-${i}`,
                    label: cell.label,
                    net: cell.net,
                    volume: cell.volume,
                }))}
            />
            <p className="text-xs text-muted" style={{ margin: 'var(--space-1) 0 0' }}>
                Targets extracted per post from this tier's sampled posts. Net tone
                is toward each target; one-off mentions pool into "Other targets".
            </p>
        </div>
    ) : null;

    return (
        <ThreeWayGrid
            toolbar={
                <ThreeWayToolbar
                    leanFilter={leanFilter}
                    onLeanFilterChange={setLeanFilter}
                />
            }
        >
                <ThreeWayColumn
                    header="The News"
                    byline={`Top outlets by coverage volume, with their editorial lean${topicSuffix}`}
                    empty="No news articles match this filter in this window."
                    items={filteredNews}
                    renderItem={renderCard}
                    sorters={SENTIMENT_SORTERS}
                />
                <ThreeWayColumn
                    header="Politicians & Officials"
                    byline={`Tracked officeholders posting on X${topicSuffix}`}
                    empty="No officials match this filter in this window."
                    items={filteredOfficials}
                    renderItem={renderCard}
                    sorters={OFFICIAL_SORTERS}
                />
                <ThreeWayColumn
                    header="The Public"
                    byline={`Political subreddits, curated political accounts, and the most active X voices in our sample${topicSuffix}`}
                    empty="No social posts match this filter in this window."
                    items={filteredPublic}
                    renderItem={renderCard}
                    sorters={SENTIMENT_SORTERS}
                    footer={publicTargetsFooter}
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
                    {/* title repeats the label so an ellipsized narrative/topic
                        name is still readable on hover. */}
                    <span className="tone-bar-row-label" title={row.label}>{row.label}</span>
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

/** "1234567890" (unix seconds) → "3 days ago". Entity-posts pagination rows
 *  carry an ISO `publishedAt`, not the unix-seconds `formatRelativeDate`
 *  expects. */
function unixSecondsFromIso(iso: string | null | undefined): number | null {
    if (!iso) return null;
    const t = Date.parse(iso);
    return Number.isNaN(t) ? null : Math.floor(t / 1000);
}

/**
 * Thin post row for the entity modal's "show all posts" pagination.
 * `GET /entity-posts` returns `EntityPostRow` (a `SampleDoc` plus which
 * relation matched — mentions/authored_by/both), not a rich
 * `ClassificationSample` — there is no per-doc label/evidence/engagement
 * here to render, so this stays a plain link+snippet+confidence card
 * rather than dressing up fields the endpoint doesn't carry.
 */
function EntityPostRowCard({ row }: { row: EntityPostRow }) {
    const relationNote = row.relation === 'authored_by'
        ? 'authored by this entity'
        : row.relation === 'both'
            ? 'authored by & mentions this entity'
            : 'mentions this entity';
    return (
        <article className="post-card">
            <header className="post-card-head">
                <div className="post-card-head-text">
                    <span className="post-card-source">{relationNote}</span>
                </div>
                <span className="post-card-when">{formatRelativeDate(unixSecondsFromIso(row.publishedAt))}</span>
                <a
                    className="post-card-permalink"
                    href={row.sourceUrl}
                    target="_blank"
                    rel="noreferrer"
                    aria-label="Open the original post in a new tab"
                    title="Open the original in a new tab"
                >
                    View original
                </a>
            </header>
            {row.snippet && <p className="post-card-body">{row.snippet}</p>}
            <div className="post-card-analysis">
                <AdmissionBadge admissionClass={row.admissionClass} />
                <span className="post-card-confidence" title="Model confidence in this run's label">
                    {formatPct(row.confidence * 100, { decimals: 0 })} confidence
                </span>
            </div>
        </article>
    );
}

function EntityPostRowList({ rows }: { rows: EntityPostRow[] }) {
    if (rows.length === 0) return null;
    return (
        <div className="post-card-list">
            <p className="post-card-list-note">
                All-time posts mentioning or authored by this entity.
            </p>
            {rows.map((r) => <EntityPostRowCard key={r.docId} row={r} />)}
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

    // Live drill-down: the entity carries only a handful of highest-
    // confidence classificationSamples; "Show all posts" pages the full
    // list from the /entity-posts read path (thin EntityPostRow, not the
    // rich ClassificationSample the inline samples use — see
    // EntityPostRowList's docstring).
    const [loadedPosts, setLoadedPosts] = useState<EntityPostRow[] | null>(null);
    const [loadedTotal, setLoadedTotal] = useState<number>(0);
    const [nextPage, setNextPage] = useState(1);
    const [postsLoading, setPostsLoading] = useState(false);
    const [postsError, setPostsError] = useState<string | null>(null);

    const loadMorePosts = useCallback(async () => {
        if (profile.entityId == null) return;
        setPostsLoading(true);
        setPostsError(null);
        try {
            const page = await fetchEntityPosts(profile.entityId, timeWindow, nextPage);
            setLoadedPosts([...(loadedPosts ?? []), ...page.items]);
            setLoadedTotal(page.total);
            setNextPage((p) => p + 1);
        } catch (e) {
            setPostsError(e instanceof Error ? e.message : 'Failed to load posts');
        } finally {
            setPostsLoading(false);
        }
    }, [profile.entityId, timeWindow, nextPage, loadedPosts]);

    // When a topic is active, filter the visible inline samples by the
    // backend's per-sample topic attribution — an exact match, not
    // client-side keyword guessing. Once "show all" has loaded the paged
    // EntityPostRow list, that list is all-time and untagged by topic, so
    // the topic filter no longer applies to it (the strip below says so).
    const allSamples: ClassificationSample[] = classificationSamples ?? [];
    const filteredSamples = activeTopic.key === 'all'
        ? allSamples
        : allSamples.filter(s => s.topic === activeTopic.key);
    const samplesAreFiltered = activeTopic.key !== 'all';

    // Topic-scoped expressed score: exact per-topic cells from the backend,
    // net suppressed (null) below its small-n floor.
    const topicCell = samplesAreFiltered
        ? (item.byTopic ?? []).find(c => c.topic === activeTopic.key) ?? null
        : null;

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle={buildEntitySubtitle(profile)}
        >
            {samplesAreFiltered && loadedPosts == null && (
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
                        <div
                            className="text-xs text-muted"
                            title={received.engagementWeightedNet != null
                                ? 'The weighted figure re-scores the same posts by 1 + ln(1 + retweets + replies + likes + quotes). Engagement counts are a reach proxy, not verified reach.'
                                : undefined}
                        >
                            {received.net != null
                                ? `across ${received.volume} posts about them`
                                    + (received.engagementWeightedNet != null
                                        ? ` · engagement-weighted ${formatPts(received.engagementWeightedNet)}`
                                        : '')
                                : `only ${received.volume} sampled post${received.volume === 1 ? '' : 's'} about them — too few to score reliably`}
                        </div>
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
                            : (volume > 0 && netScore != null ? formatPts(netScore) : '—')}
                    </div>
                    {samplesAreFiltered && (
                        <div className="text-xs text-muted">
                            {topicCell
                                ? (topicCell.net != null
                                    ? `${activeTopic.label} only — across ${topicCell.volume} post${topicCell.volume === 1 ? '' : 's'}`
                                    : `only ${topicCell.volume} ${activeTopic.label} post${topicCell.volume === 1 ? '' : 's'} — too few to score reliably`)
                                : `no ${activeTopic.label} posts in this window`}
                        </div>
                    )}
                    {samplesAreFiltered && volume > 0 && netScore != null && (
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

            {item.outbound && item.outbound.targets.length > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">
                        Who they're talking about
                    </h3>
                    <ToneBarRows
                        rows={item.outbound.targets.map((cell, i) => ({
                            key: cell.entityKey ?? `${cell.kind}-${i}`,
                            label: cell.label,
                            net: cell.net,
                            volume: cell.volume,
                        }))}
                    />
                    <p className="text-xs text-muted">
                        Targets extracted per post by the model from this group's
                        sampled posts. Free-text targets that don't match a tracked
                        figure appear verbatim; one-off mentions pool into "Other
                        targets".
                    </p>
                </>
            )}

            {received && received.byTopic.length > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">
                        Tone toward {profile.displayName} by topic
                    </h3>
                    <p className="modal-section-lede">
                        WHAT the mentions are about — each row is one topic.
                    </p>
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
                    <p className="modal-section-lede">
                        WHO the mentions come from — news, officials, or the public.
                    </p>
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
                    <p className="modal-section-lede">
                        WHICH recurring claims the mentions ride on — an association
                        within our sample, not a claim about origin.
                    </p>
                    <ToneBarRows
                        rows={received.byNarrative!.map((cell) => ({
                            key: cell.narrativeId, label: cell.name, net: cell.net, volume: cell.volume,
                        }))}
                    />
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
                            Visit {profile.displayName}
                        </a>
                    )}
                    {profile.leanSource && (
                        <span className="text-xs text-muted">
                            Political lean rated by: {profile.leanSource}
                        </span>
                    )}
                    {profile.kind === 'official' && profile.bioSource && (
                        <a href={profile.bioSource} target="_blank" rel="noreferrer">Bio</a>
                    )}
                </div>
            )}

            <EntityHubLinks entityId={profile.entityId ?? null} currentTab="sentiment" />

            {loadedPosts != null ? (
                <>
                    <h3 className="card-title mt-4 mb-2">
                        Posts mentioning or authored by this entity
                        {' '}
                        <span className="modal-section-count">
                            {loadedPosts.length.toLocaleString()} of {loadedTotal.toLocaleString()}
                        </span>
                    </h3>
                    <EntityPostRowList rows={loadedPosts} />
                </>
            ) : filteredSamples.length > 0 ? (
                <>
                    <h3 className="card-title mt-4 mb-2">
                        {samplesAreFiltered
                            ? `Highest-confidence posts matching ${activeTopic.label}`
                            : 'Highest-confidence classified posts'}
                        {' '}
                        <span className="modal-section-count">
                            {filteredSamples.length.toLocaleString()} shown
                        </span>
                    </h3>
                    <PostCardList
                        posts={filteredSamples.map(sampleToPostCard)}
                        sampleNote="A sample of this window's classified posts, not a complete feed. Highlighted text is the evidence the model quoted."
                    />
                </>
            ) : samplesAreFiltered ? (
                <p className="text-muted text-sm mt-4">
                    None of this entity's recent classified posts match {activeTopic.label}.
                </p>
            ) : null}

            {profile.entityId != null && (loadedPosts == null || loadedPosts.length < loadedTotal) && (
                <div className="mt-2">
                    <button
                        type="button"
                        className="btn btn-secondary"
                        onClick={loadMorePosts}
                        disabled={postsLoading}
                    >
                        {postsLoading
                            ? 'Loading…'
                            : loadedPosts == null
                                ? 'Show all posts mentioning this entity'
                                : `Load more (${loadedPosts.length} of ${loadedTotal})`}
                    </button>
                    {postsError && (
                        <p className="text-xs text-muted">
                            Could not load posts: {postsError}
                        </p>
                    )}
                </div>
            )}
            {loadedPosts != null && loadedPosts.length >= loadedTotal && loadedTotal > 0 && (
                <p className="text-xs text-muted">
                    Showing all {loadedTotal.toLocaleString()} posts in this window.
                </p>
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
//  Polling-vs-online comparison — stays retired (owner decision 2026-07-25/   //
//  2026-07-27): no polling data is ingested in any data contract, so this     //
//  keeps its old frame as a quiet, honest empty state rather than a          //
//  data-gated card that would never show.                                    //
// --------------------------------------------------------------------------- //

function PollingComparison() {
    return (
        <CollapsibleInfo summary="Online stance vs. recent polling">
            <p className="text-xs text-muted" style={{ fontStyle: 'italic' }}>
                Not available: no polling data is ingested in this data contract, so there's
                nothing to compare our sampled online stance against.
            </p>
        </CollapsibleInfo>
    );
}


// --------------------------------------------------------------------------- //
//  How-this-works collapsible                                                  //
// --------------------------------------------------------------------------- //

function FreshnessStrip({ refreshed }: { refreshed: string }) {
    return (
        <div className="freshness-strip">
            <div className="eyebrow" style={{ marginBottom: 'var(--space-1)' }}>
                How fresh is this data
            </div>
            <div
                className="freshness-strip-bar"
                role="img"
                aria-label={`Most recent pipeline run: ${refreshed}`}
                title={`Most recent pipeline run: ${refreshed}`}
            >
                <span style={{ width: '100%', background: 'var(--chart-accent)' }} />
            </div>
            <div className="freshness-strip-legend text-xs text-muted">
                Last analysis run: {refreshed}
            </div>
        </div>
    );
}

function HowThisWorks({ refreshed }: { refreshed: string }) {
    return (
        <CollapsibleInfo>
            <p className="text-sm">
                We aggregate news articles, Reddit posts, and X posts about US politics, then
                score each one for tone (positive / negative / neutral), and each score must
                point to the exact sentence that justifies it. Tracked outlets and officials
                each get their own profile card; everything else rolls up into the
                general-public column or into an "Other" bucket for sources we don't track
                individually.
            </p>
            <p className="text-sm">
                Tone is a classification of what the post says, not what the author feels. Don't
                read it as opinion polling. Sarcasm and irony are flagged when the model detects
                them but can still be misclassified.
            </p>
            <FreshnessStrip refreshed={refreshed} />
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

function pickDefaultTopic(byTopic: TopicSentiment[] | undefined): TopicKey {
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

function buildSentimentTickerItems(
    data: SentimentPanelResponse, activeTopic: Topic, topicRow: TopicSentiment | null,
): TickerItem[] {
    const overall = data.overview;
    // Net tone + posts are scoped to the selected topic (from the per-topic
    // byTopic row, which already carries its own backend-computed netScore)
    // when one is active.
    const filtered = activeTopic.key !== 'all' && topicRow != null;
    const netScore = filtered ? topicRow!.netScore : overall.netScore;
    const volume = filtered ? topicRow!.volume : overall.volume;
    const topicHint = filtered ? activeTopic.label : undefined;

    return [
        {
            label: 'Net tone',
            value: formatPts(netScore),
            hint: topicHint,
            tone: netToneColor(netScore ?? 0),
            emphasis: true,
            ariaLabel: `Net tone ${formatPts(netScore)}`,
        },
        { label: 'Posts scored', value: volume.toLocaleString(), hint: topicHint },
    ];
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
    const [activeDate, setActiveDate] = useState<string | null>(null);
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

    // Cross-page entity deep link ("#sentiment?entity=<entityId>").
    const [entityParam, setEntityParam] = useDeepLinkParam('entity');

    const { data, loading, error, refetch } = useFetch<SentimentPanelResponse>(
        () => fetchSentiment(filters.timeRange),
        [filters.timeRange],
        `sentiment:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );
    // Human-agreement chip: how often reviewers marked our tone
    // classifications correct. Renders only when the server publishes a
    // percentage (enough scored reviews); silent otherwise. The task is
    // named 'text' in the current engine (engine/text.py, sentiment-only —
    // favorability retired 2026-07-25), not the old 'sentiment' task name.
    const { data: evalAccuracy } = useFetch(
        () => fetchEvalAccuracy(),
        [],
        'eval-accuracy',
    );
    const sentimentAgreement = evalAccuracy?.perTask.find(
        (t) => t.taskType === 'text' && !t.lowSample && t.accuracyPct != null,
    ) ?? null;

    // Resolve the entity= param once data lands: search all three tier
    // lists for the matching numeric entityId. Unknown entities (or ones
    // with no data in this window) clear the param instead of erroring.
    useEffect(() => {
        if (!data || !entityParam) return;
        const targetId = parseEntityParam(entityParam);
        if (targetId != null) {
            const lists = [data.byNewsOutlet, data.byOfficial, data.byGeneralPublic];
            for (const list of lists) {
                const item = list.find((it) => it.entityProfile.entityId === targetId);
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
    const topicRow = useMemo<TopicSentiment | null>(() => {
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

    const tickerItems = buildSentimentTickerItems(data, activeTopic, topicRow);
    const refreshed = formatRefreshedAgo(pipelineRunTimestamp(snapshotStatus ?? null));

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
                                + 'balance out). Summarizes the posts we collected — a sample, not a poll of '
                                + 'the public.'
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

            {/* Topic tab bar — visual anchor of the page. */}
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
                + intensity. */}
            <div className="col-span-12">
                <TopMetrics
                    data={data}
                    windowLabel={formatTimeWindow(filters.timeRange)}
                    activeTopic={activeTopic}
                    onSegmentClick={setActiveSegment}
                />
            </div>

            {/* Tone over time (left) + Source signals (right) share a row —
                two wide-but-bounded reads side by side instead of stacked
                full-bleed. Balanced 6/6 so neither the trend chart nor the
                5-column table is cramped. */}
            <div className="col-span-6">
                <ToneTrendPanel
                    toneTrend={data.toneTrend}
                    entitiesByTier={{
                        news: data.byNewsOutlet ?? [],
                        officials: data.byOfficial ?? [],
                        public: data.byGeneralPublic ?? [],
                    }}
                    onOpenEntity={setActiveEntity}
                    onDateClick={setActiveDate}
                />
                <ToneDivergenceCard data={data} />
            </div>
            <div className="col-span-6">
                <OutletSignalsPanel window={filters.timeRange} />
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

            {activeDate && (
                <DaySamplesModal
                    date={activeDate}
                    samples={data.daySamples?.[activeDate] ?? []}
                    onClose={() => setActiveDate(null)}
                />
            )}

            {/* Polling-vs-online collapsible — stays retired, quiet empty state. */}
            <div className="col-span-12">
                <PollingComparison />
            </div>

            {/* How this page works — self-documenting content + collapsible backup. */}
            <div className="col-span-12">
                <HowThisWorks refreshed={refreshed} />
            </div>
        </div>
    );
}

export default PublicSentiment;
