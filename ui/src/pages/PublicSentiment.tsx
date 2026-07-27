import { useState } from 'react';
import {
    Card, CollapsibleInfo, EmptyState, EntityHeader, EntityHubLinks, EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, MethodPopover, Modal, RangeCaption,
    SampleCardList, ThreeWayColumn, ThreeWayGrid, ThreeWayToolbar, TierRow, TopMetricsBlock,
    matchesLeanFilter, toneStats,
} from '../components/common';
import type { ColumnSorter, LeanFilter, TickerItem } from '../components/common';
import { fetchEntityPosts, fetchEvalAccuracy, fetchSentiment, fetchSnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPct, formatPts } from '../services/format';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import type {
    EntityPostRow, EntityStanceAggregate, Filters, PipelineRun, SentimentDistribution,
    SentimentPanelResponse, SnapshotStatusResponse, TierSplit, TierStanceCounts,
    TopicSentiment, TopicStanceCounts,
} from '../types';
import { OutletSignalsPanel } from './publicSentiment/OutletSignalsPanel';
import { TopicTabBar, ALL_TOPICS_KEY } from './publicSentiment/TopicTabBar';
import { ToneTrendPanel } from './publicSentiment/ToneTrendPanel';

// --------------------------------------------------------------------------- //
//  Restoring the pre-cutover page tree (see docs/todos and the walkthrough    //
//  035 rationale) onto the strictly-live /sentiment contract                  //
//  (analysis/src/api/models/sentiment.py). ToneDivergenceCard and             //
//  TopicScopeStrip are wired onto the entity-level byTopic/receivedByTier     //
//  breakdowns added 2026-07-26 -- see their own docstrings for exactly what   //
//  they read. PollingComparison stays a quiet, honest empty frame: no         //
//  polling data is ingested in any data contract, so there's nothing to      //
//  wire it to. ToneTrendPanel and TopicTabBar are rebuilt onto data the      //
//  backend does publish — see their own module docstrings for exactly what   //
//  changed.                                                                   //
// --------------------------------------------------------------------------- //

function toneVerb(net: number): string {
    if (net > 15) return 'clearly positive';
    if (net > 5) return 'slightly positive';
    if (net < -15) return 'clearly negative';
    if (net < -5) return 'slightly negative';
    return 'roughly neutral';
}

function toneColor(net: number): string {
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return 'var(--neutral-500)';
}

const TIER_LABEL: Record<TierSplit['tier'], string> = {
    news: 'News articles are',
    officials: 'Officials are',
    general_public: 'The public is',
};

function TopMetrics({
    byTier, windowLabel, distribution,
}: {
    byTier: TierSplit[];
    windowLabel: string;
    distribution: SentimentDistribution;
}) {
    const order: TierSplit['tier'][] = ['news', 'officials', 'general_public'];
    return (
        <TopMetricsBlock
            eyebrow={`As of ${windowLabel}`}
            meta={null}
            aux={<IntensityMini distribution={distribution} />}
        >
            {order.map((tier) => {
                const row = byTier.find((t) => t.tier === tier);
                const hasData = !!row && row.volume > 0 && row.netScore != null;
                const color = hasData ? toneColor(row!.netScore!) : 'var(--neutral-500)';
                const axisPct = hasData ? ((row!.netScore! + 100) / 200) * 100 : undefined;
                return (
                    <TierRow
                        key={tier}
                        label={TIER_LABEL[tier]}
                        value={hasData ? formatPts(row!.netScore!) : '—'}
                        valueColor={color}
                        verb={hasData
                            ? `${toneVerb(row!.netScore!)} · ${row!.volume.toLocaleString()} sampled posts`
                            : 'no posts in this window'}
                        showZeroTick
                        dotPct={axisPct}
                        dotColor={hasData ? color : undefined}
                    />
                );
            })}
        </TopMetricsBlock>
    );
}

function IntensityMini({ distribution }: { distribution: SentimentDistribution }) {
    const total = distribution.strongPositive + distribution.mildPositive
        + distribution.neutral + distribution.mildNegative + distribution.strongNegative;
    if (total === 0) return null;
    const pct = (n: number) => (n / total) * 100;
    const buckets = [
        { name: 'strongly positive', count: distribution.strongPositive, barClass: 'mini-bar-strongpos' },
        { name: 'mild positive', count: distribution.mildPositive, barClass: 'mini-bar-mildpos' },
        { name: 'neutral', count: distribution.neutral, barClass: 'mini-bar-neu' },
        { name: 'mild negative', count: distribution.mildNegative, barClass: 'mini-bar-mildneg' },
        { name: 'strongly negative', count: distribution.strongNegative, barClass: 'mini-bar-strongneg' },
    ];
    const biggest = buckets.reduce((a, b) => (a.count >= b.count ? a : b));
    return (
        <div className="mini-metric" title={`Across ${total.toLocaleString()} sampled posts`}>
            <span className="mini-metric-label">Tone intensity</span>
            <span className="mini-metric-value">most {biggest.name}</span>
            <span className="mini-metric-visual is-intensity">
                <span className="mini-metric-bar mini-intensity" aria-hidden>
                    {buckets.map((b) => (
                        <span key={b.name} className={b.barClass} style={{ width: `${pct(b.count)}%` }} />
                    ))}
                </span>
                <span className="mini-intensity-legend">
                    {buckets.map((b) => (
                        <span key={b.name} className="mini-intensity-legend-item">
                            <span className={`mini-intensity-legend-dot ${b.barClass}`} aria-hidden />
                            {formatPct(pct(b.count), { decimals: 0 })}
                        </span>
                    ))}
                </span>
            </span>
        </div>
    );
}

function BreakdownCard({ data }: { data: SentimentPanelResponse }) {
    return (
        <Card title="By platform and topic" subtitle="Volume and net tone across the window's dimensions.">
            <div className="grid-2 gap-3">
                <div>
                    <div className="eyebrow mb-2">By platform</div>
                    {data.byPlatform.map((p) => (
                        <div key={p.platform} className="flex justify-between text-sm" style={{ padding: '4px 0' }}>
                            <span>{p.platform}</span>
                            <span className="num" style={p.netScore != null ? { color: toneColor(p.netScore) } : undefined}>
                                {p.netScore != null ? formatPts(p.netScore) : '—'} · {p.volume.toLocaleString()}
                            </span>
                        </div>
                    ))}
                </div>
                <div>
                    <div className="eyebrow mb-2">By topic</div>
                    {[...data.byTopic].sort((a, b) => b.volume - a.volume).slice(0, 8).map((t) => (
                        <div key={t.topic} className="flex justify-between text-sm" style={{ padding: '4px 0' }}>
                            <span>{t.topic}</span>
                            <span className="num" style={t.netScore != null ? { color: toneColor(t.netScore) } : undefined}>
                                {t.netScore != null ? formatPts(t.netScore) : '—'} · {t.volume.toLocaleString()}
                            </span>
                        </div>
                    ))}
                </div>
            </div>
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Topic snapshot — shown under the reads-as line when a topic tab is active. //
//  Restores the old TopMetrics' topic-scoped read WITHOUT fabricating a       //
//  per-tier split: SentimentPanelResponse.byTopic carries one pooled net      //
//  tone + volume per topic (no news/officials/public breakout), so this is    //
//  a single reading rather than three tier rows.                              //
// --------------------------------------------------------------------------- //

function TopicSnapshot({ topic }: { topic: TopicSentiment }) {
    const hasData = topic.volume > 0 && topic.netScore != null;
    const color = hasData ? toneColor(topic.netScore!) : 'var(--neutral-500)';
    const axisPct = hasData ? ((topic.netScore! + 100) / 200) * 100 : undefined;
    return (
        <Card
            title={`${topic.topic}, at a glance`}
            subtitle="One pooled reading across news, officials, and the public — this data contract doesn't break topics out by tier."
        >
            <div className="top-metrics-rows">
                <TierRow
                    label="Net tone on this topic"
                    value={hasData ? formatPts(topic.netScore!) : '—'}
                    valueColor={color}
                    verb={hasData
                        ? `${toneVerb(topic.netScore!)} · ${topic.volume.toLocaleString()} sampled posts`
                        : 'no posts on this topic in this window'}
                    showZeroTick
                    dotPct={axisPct}
                    dotColor={hasData ? color : undefined}
                />
            </div>
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Tone divergence — how differently each speaker tier talks ABOUT tracked    //
//  entities, aggregated from EntityStanceAggregate.receivedByTier across all  //
//  entities (analysis.target_mentions, grouped by the mentioning doc's        //
//  author tier). This isn't the old per-party officials split (that needs a   //
//  party breakout the current contract doesn't carry) -- it's the tier a     //
//  mention came from, compared against the public's baseline net tone.        //
// --------------------------------------------------------------------------- //

interface TierAgg { positive: number; negative: number; neutral: number; mixed: number; volume: number; }

function zeroTierAgg(): TierAgg { return { positive: 0, negative: 0, neutral: 0, mixed: 0, volume: 0 }; }

function aggregateReceivedByTier(entityStances: EntityStanceAggregate[]): Record<TierStanceCounts['tier'], TierAgg> {
    const acc: Record<TierStanceCounts['tier'], TierAgg> = {
        news: zeroTierAgg(), officials: zeroTierAgg(), general_public: zeroTierAgg(),
    };
    for (const entity of entityStances) {
        for (const row of entity.receivedByTier) {
            const bucket = acc[row.tier];
            bucket.positive += row.positive;
            bucket.negative += row.negative;
            bucket.neutral += row.neutral;
            bucket.mixed += row.mixed;
            bucket.volume += row.volume;
        }
    }
    return acc;
}

function tierAggNet(agg: TierAgg): number | null {
    return agg.volume > 0 ? Math.round(((agg.positive - agg.negative) / agg.volume) * 1000) / 10 : null;
}

function ToneDivergenceCard({ entityStances }: { entityStances: EntityStanceAggregate[] }) {
    const byTier = aggregateReceivedByTier(entityStances);
    const baseline = tierAggNet(byTier.general_public);

    if (baseline === null) {
        return (
            <Card
                title="Divergence from the public"
                subtitle="How differently news and officials talk about tracked entities, versus the public's baseline."
            >
                <p className="text-sm text-muted" style={{ fontStyle: 'italic' }}>
                    Not available: no general-public mentions of tracked entities carry a stance
                    reading in this window.
                </p>
            </Card>
        );
    }

    const rows = ([
        { key: 'news', label: 'News', color: COLORS.tierNews, agg: byTier.news },
        { key: 'officials', label: 'Officials', color: COLORS.tierOfficials, agg: byTier.officials },
    ] as const)
        .map((r) => ({ ...r, net: tierAggNet(r.agg) }))
        .filter((r): r is typeof r & { net: number } => r.net !== null)
        .map((r) => ({ ...r, div: Math.round((r.net - baseline) * 10) / 10 }));

    if (rows.length === 0) {
        return (
            <Card
                title="Divergence from the public"
                subtitle="How differently news and officials talk about tracked entities, versus the public's baseline."
            >
                <p className="text-sm text-muted" style={{ fontStyle: 'italic' }}>
                    Not available: neither news nor officials have a stance reading toward
                    tracked entities in this window.
                </p>
            </Card>
        );
    }

    const maxAbs = Math.max(1, ...rows.map((r) => Math.abs(r.div)));

    return (
        <Card
            title="Divergence from the public"
            subtitle={`How far news and officials' tone toward tracked entities sits from the public's ${formatPts(baseline)} baseline. Right = warmer than the public, left = harsher.`}
            headerActions={
                <MethodPopover
                    description={
                        'The public tier (subreddits + untracked social accounts) mentioning tracked '
                        + "entities is the baseline. Each bar is a group's net tone toward those "
                        + 'entities minus the public\'s, in points -- so you can see whether news and '
                        + 'officials talk warmer or harsher about the same entities than the crowd. '
                        + 'A sample of collected posts, not a poll.'
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
                            title={`${r.label}: ${formatPts(r.net)} net vs the public's ${formatPts(baseline)} -- ${r.div >= 0 ? '+' : ''}${r.div.toFixed(1)} pts`}
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
                Baseline: the public at {formatPts(baseline)} net tone toward tracked entities.
            </p>
        </Card>
    );
}

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
//  Freshness strip — degraded to the single most-recent pipeline run.        //
// --------------------------------------------------------------------------- //

function FreshnessStrip({ pipelineRun }: { pipelineRun: PipelineRun | null }) {
    if (!pipelineRun) return null;
    const when = pipelineRun.completedAt ?? pipelineRun.startedAt;
    const label = `${pipelineRun.status} · ${formatRefreshedAgo(when)}`;
    return (
        <div className="freshness-strip">
            <div className="eyebrow" style={{ marginBottom: 'var(--space-1)' }}>
                How fresh is this data
            </div>
            <div
                className="freshness-strip-bar"
                role="img"
                aria-label={`Most recent pipeline run: ${label}`}
                title={`Most recent pipeline run: ${label}`}
            >
                <span style={{ width: '100%', background: 'var(--chart-accent)' }} />
            </div>
            <div className="freshness-strip-legend text-xs text-muted">
                Last analysis run: {label}
            </div>
        </div>
    );
}

function HowThisWorks({ pipelineRun }: { pipelineRun: PipelineRun | null }) {
    return (
        <CollapsibleInfo>
            <p className="text-sm">
                We aggregate news articles, Reddit posts, and X posts about US politics, then
                score each one for tone (positive / negative / neutral). Tone is a classification
                of what the post says, not what the author feels — don't read it as opinion
                polling.
            </p>
            <FreshnessStrip pipelineRun={pipelineRun} />
        </CollapsibleInfo>
    );
}

// --------------------------------------------------------------------------- //
//  Entity three-way grid (grouped by corpus.entities.kind)                    //
// --------------------------------------------------------------------------- //

/** Looks up an entity's per-topic stance row for the active topic tab
 *  (analysis.target_mentions GROUP BY entity_id, topic). Returns null for
 *  the all-topics tab, or when this entity has no mentions on that topic --
 *  callers fall back to the pooled targetStance in that case, so the grid
 *  re-scopes "where possible" rather than blanking an entity out. */
function topicStanceFor(item: EntityStanceAggregate, activeTopic: string): TopicStanceCounts | null {
    if (activeTopic === ALL_TOPICS_KEY) return null;
    return item.byTopic.find((t) => t.topic === activeTopic) ?? null;
}

const ENTITY_SORTERS: ColumnSorter<EntityStanceAggregate>[] = [
    { label: 'posts', compare: (a, b) => b.targetStance.volume - a.targetStance.volume },
    { label: 'net tone', compare: (a, b) => (b.targetStance.netScore ?? 0) - (a.targetStance.netScore ?? 0) },
    { label: 'name', compare: (a, b) => a.displayName.localeCompare(b.displayName) },
];

function EntityThreeWayGrid({
    entityStances, activeTopic, onOpen,
}: {
    entityStances: EntityStanceAggregate[];
    activeTopic: string;
    onOpen: (item: EntityStanceAggregate) => void;
}) {
    const [leanFilter, setLeanFilter] = useState<LeanFilter>('all');
    const byLean = (e: EntityStanceAggregate) => matchesLeanFilter(e.lean, leanFilter);

    const outlets = entityStances.filter((e) => e.kind === 'outlet').filter(byLean);
    const officials = entityStances.filter((e) => e.kind === 'official' || e.kind === 'collective').filter(byLean);
    const communities = entityStances.filter((e) => e.kind === 'subreddit').filter(byLean);
    const unresolved = entityStances.filter((e) => e.entityId === null);

    const renderCard = (item: EntityStanceAggregate) => {
        const topicStance = topicStanceFor(item, activeTopic);
        const stats = topicStance
            ? toneStats({ netTone: topicStance.netScore, volume: topicStance.volume })
            : toneStats({ netTone: item.targetStance.netScore, volume: item.targetStance.volume });
        return (
            <EntityProfileCard
                key={item.entityId ?? item.catchAllKey}
                entity={{ kind: item.kind, displayName: item.displayName, lean: item.lean }}
                stats={stats}
                onClick={() => onOpen(item)}
            />
        );
    };

    return (
        <>
            <ThreeWayGrid
                toolbar={<ThreeWayToolbar leanFilter={leanFilter} onLeanFilterChange={setLeanFilter} />}
            >
                <ThreeWayColumn
                    header="The News"
                    byline="Outlets with a stance reading in this window"
                    empty="No news outlets have a stance reading in this window."
                    items={outlets}
                    renderItem={renderCard}
                    sorters={ENTITY_SORTERS}
                />
                <ThreeWayColumn
                    header="Politicians, Officials & Collectives"
                    byline="Tracked officeholders and party collectives"
                    empty="No officials have a stance reading in this window."
                    items={officials}
                    renderItem={renderCard}
                    sorters={ENTITY_SORTERS}
                />
                <ThreeWayColumn
                    header="Communities"
                    byline="Tracked subreddits"
                    empty="No communities have a stance reading in this window."
                    items={communities}
                    renderItem={renderCard}
                    sorters={ENTITY_SORTERS}
                />
            </ThreeWayGrid>
            {unresolved.length > 0 && (
                <p className="text-xs text-muted mt-2">
                    Plus {unresolved[0].targetStance.volume} mentions
                    of entities outside our tracked registry ("{unresolved[0].displayName}").
                </p>
            )}
        </>
    );
}

// --------------------------------------------------------------------------- //
//  Entity detail modal                                                        //
// --------------------------------------------------------------------------- //

function TopicScopeStrip({ topic, stance }: { topic: string; stance: TopicStanceCounts | null }) {
    const detail = stance
        ? stance.netScore != null
            ? `· net tone on ${topic}: ${formatPts(stance.netScore)} across ${stance.volume.toLocaleString()} mentions`
            : `· ${stance.volume.toLocaleString()} mentions on ${topic}, too few for a net-tone reading`
        : `· no mentions of this entity are tagged to ${topic} -- showing its all-topic samples below`;
    return (
        <div className="modal-topic-strip" role="status" aria-label={`Page filtered to topic: ${topic}`}>
            <span className="modal-topic-strip-label">Page filtered to: {topic}</span>
            <span className="modal-topic-strip-detail">{detail}</span>
        </div>
    );
}

function EntitySentimentModal({
    item, window, activeTopic, onClose,
}: {
    item: EntityStanceAggregate;
    window: Filters['timeRange'];
    activeTopic: string;
    onClose: () => void;
}) {
    const [loadedPosts, setLoadedPosts] = useState<EntityPostRow[] | null>(null);
    const [loadedTotal, setLoadedTotal] = useState(0);
    const [nextPage, setNextPage] = useState(1);
    const [loadingPosts, setLoadingPosts] = useState(false);
    const [postsError, setPostsError] = useState<string | null>(null);

    const loadMore = async () => {
        if (item.entityId == null) return;
        setLoadingPosts(true);
        setPostsError(null);
        try {
            const page = await fetchEntityPosts(item.entityId, window, nextPage);
            setLoadedPosts([...(loadedPosts ?? []), ...page.items]);
            setLoadedTotal(page.total);
            setNextPage((p) => p + 1);
        } catch (e) {
            setPostsError(e instanceof Error ? e.message : 'Failed to load posts');
        } finally {
            setLoadingPosts(false);
        }
    };

    const samples = loadedPosts ?? item.samples;
    const topicStance = topicStanceFor(item, activeTopic);

    return (
        <Modal isOpen onClose={onClose} title={item.displayName}>
            {activeTopic !== ALL_TOPICS_KEY && <TopicScopeStrip topic={activeTopic} stance={topicStance} />}

            <EntityHeader entity={{ kind: item.kind, displayName: item.displayName, lean: item.lean }} />
            <div className="entity-modal-stats">
                <div>
                    <div className="eyebrow" title="Tone of posts mentioning this entity (target_mentions), -100..+100">
                        Net tone
                    </div>
                    <div className="metric-value">
                        {item.targetStance.netScore != null ? formatPts(item.targetStance.netScore) : '—'}
                    </div>
                    <div className="text-xs text-muted">{item.targetStance.volume.toLocaleString()} mentions</div>
                </div>
            </div>

            {item.entityId != null && (
                <div className="mt-2">
                    {(loadedPosts == null || loadedPosts.length < loadedTotal) && (
                        <button type="button" className="btn btn-secondary" onClick={loadMore} disabled={loadingPosts}>
                            {loadingPosts ? 'Loading…' : loadedPosts == null ? 'Show all posts mentioning this entity' : `Load more (${loadedPosts.length} of ${loadedTotal})`}
                        </button>
                    )}
                    {postsError && <p className="text-xs text-muted">Could not load posts: {postsError}</p>}
                </div>
            )}

            <EntityHubLinks entityId={item.entityId} currentTab="sentiment" />

            <h3 className="card-title mt-4 mb-2">Sample posts</h3>
            <SampleCardList
                samples={samples}
                sampleNote={loadedPosts != null
                    ? 'All-time posts mentioning or authored by this entity.'
                    : 'A sample of this window\'s scored posts, not a complete feed.'}
                emptyNote="No sample posts stored for this entity in this window."
            />
        </Modal>
    );
}

// --------------------------------------------------------------------------- //
//  Page                                                                       //
// --------------------------------------------------------------------------- //

function buildTickerItems(data: SentimentPanelResponse): TickerItem[] {
    const netScore = data.overview.netScore;
    const netTone: TickerItem['tone'] = netScore == null ? 'neutral' : netScore > 10 ? 'positive' : netScore < -10 ? 'negative' : 'neutral';
    return [
        {
            label: 'Net tone', value: netScore != null ? formatPts(netScore) : '—',
            tone: netTone, emphasis: true,
        },
        { label: 'Posts scored', value: data.overview.volume.toLocaleString() },
        {
            label: 'Mean confidence',
            value: data.overview.meanConfidence != null ? formatPct(data.overview.meanConfidence * 100, { decimals: 0 }) : '—',
        },
    ];
}

function readsAsToday(activeTopic: string): string {
    if (activeTopic === ALL_TOPICS_KEY) {
        return 'How news outlets, public officials, and everyday people are reading American politics.';
    }
    return `How news outlets, public officials, and everyday people are feeling and talking about ${activeTopic}.`;
}

interface PublicSentimentProps {
    filters: Filters;
}

function PublicSentiment({ filters }: PublicSentimentProps) {
    const [activeEntity, setActiveEntity] = useState<EntityStanceAggregate | null>(null);
    const [activeTopic, setActiveTopic] = useState<string>(ALL_TOPICS_KEY);

    const { data, loading, error, refetch } = useFetch<SentimentPanelResponse>(
        () => fetchSentiment(filters.timeRange),
        [filters.timeRange],
        `sentiment:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatusResponse>(
        () => fetchSnapshotStatus(), [], 'snapshot-status',
    );
    const { data: evalAccuracy } = useFetch(() => fetchEvalAccuracy(), [], 'eval-accuracy');
    const textAgreement = evalAccuracy?.perTask.find(
        (t) => t.taskType === 'text' && !t.lowSample && t.accuracyPct != null,
    ) ?? null;

    if (error) return <ErrorState message={error.message} onRetry={refetch} />;
    if (loading) {
        return (
            <div className="flex flex-col gap-4">
                <LoadingCard />
                <div className="grid-2"><LoadingCard /><LoadingCard /></div>
            </div>
        );
    }
    if (!data) return <EmptyState title="No tone data available" />;

    const refreshed = formatRefreshedAgo(pipelineRunTimestamp(snapshotStatus));
    const topicRow = activeTopic === ALL_TOPICS_KEY
        ? null
        : data.byTopic.find((t) => t.topic === activeTopic) ?? null;

    return (
        <div className="dashboard-grid">
            <div className="col-span-12">
                <GlobalTicker
                    items={buildTickerItems(data)}
                    refreshed={refreshed}
                    ariaLabel="Overall tone overview"
                    legend={
                        <MethodPopover
                            title="How to read these numbers"
                            description={
                                'Net tone = the share of sampled posts scored positive minus the share scored '
                                + 'negative, in points on a -100 to +100 scale. Summarizes the posts we '
                                + 'collected — a sample, not a poll of the public.'
                            }
                        />
                    }
                />
                <RangeCaption range={data.range} />
                {textAgreement && (
                    <p className="text-xs text-muted">
                        Human review agreement on tone classifications: {textAgreement.accuracyPct}% across {textAgreement.scored} reviewed outputs.
                    </p>
                )}
            </div>

            <div className="col-span-12">
                <TopicTabBar activeKey={activeTopic} onChange={setActiveTopic} byTopic={data.byTopic} />
            </div>

            <div className="col-span-12">
                <div className="reads-as-today">
                    <span className="eyebrow reads-as-today-eyebrow">{asOfTodayEyebrow(filters.timeRange)}</span>
                    <p className="lead" style={{ margin: 0 }}>{readsAsToday(activeTopic)}</p>
                </div>
            </div>

            {topicRow && (
                <div className="col-span-12">
                    <TopicSnapshot topic={topicRow} />
                </div>
            )}

            <div className="col-span-12">
                <TopMetrics byTier={data.byTier} windowLabel={formatTimeWindow(filters.timeRange)} distribution={data.distribution} />
            </div>

            <div className="col-span-6">
                <ToneTrendPanel daily={data.daily} byDayOfWeek={data.byDayOfWeek} byTimeOfDay={data.byTimeOfDay} />
                <ToneDivergenceCard entityStances={data.entityStances} />
            </div>
            <div className="col-span-6">
                <OutletSignalsPanel window={filters.timeRange} />
            </div>

            <div className="col-span-12">
                <EntityThreeWayGrid entityStances={data.entityStances} activeTopic={activeTopic} onOpen={setActiveEntity} />
            </div>

            {activeEntity && (
                <EntitySentimentModal
                    item={activeEntity}
                    window={filters.timeRange}
                    activeTopic={activeTopic}
                    onClose={() => setActiveEntity(null)}
                />
            )}

            <div className="col-span-12"><BreakdownCard data={data} /></div>

            <div className="col-span-12">
                <Card title="Sample posts" subtitle={data.disclaimer}>
                    <SampleCardList
                        samples={data.samples}
                        sampleNote="The highest-confidence scored posts in this window — a sample, not a complete feed."
                        emptyNote="No sample posts stored for this window."
                    />
                </Card>
            </div>

            <div className="col-span-12">
                <PollingComparison />
            </div>

            <div className="col-span-12">
                <HowThisWorks pipelineRun={snapshotStatus?.pipelineRun ?? null} />
            </div>
        </div>
    );
}

export default PublicSentiment;
