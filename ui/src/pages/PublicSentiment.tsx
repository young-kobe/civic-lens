import { useCallback, useState } from 'react';
import {
    Card, CollapsibleInfo, EmptyState, EntityHeader, EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, MethodPopover, Modal, RangeCaption,
    SampleCardList, ThreeWayColumn, ThreeWayGrid, TierRow, TopMetricsBlock, toneStats,
} from '../components/common';
import type { ColumnSorter, TickerItem } from '../components/common';
import { fetchEntityPosts, fetchEvalAccuracy, fetchSentiment, fetchSnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPct, formatPts } from '../services/format';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import type {
    EntityPostRow, EntityStanceAggregate, Filters, SentimentDistribution,
    SentimentPanelResponse, SnapshotStatusResponse, TierSplit,
} from '../types';
import { OutletSignalsPanel } from './publicSentiment/OutletSignalsPanel';

// --------------------------------------------------------------------------- //
//  Phase 10 adaptation note: the pre-redesign News/Officials/Public entity    //
//  rollups, GOP favorability, target-tone-by-topic, engagement weighting,    //
//  per-day tone trend, and polling-vs-online comparison have no equivalent   //
//  in the strictly-live /sentiment response (analysis/src/api/models/        //
//  sentiment.py) and are removed rather than faked. The three-way frame is   //
//  rebuilt from `entityStances` grouped by corpus.entities.kind (outlet ->   //
//  News, official/collective -> Officials, subreddit -> Communities); the    //
//  by-tier doc-level tone split (news/officials/general_public) replaces     //
//  the old top-metrics tier rows.                                            //
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

function TopMetrics({ byTier, windowLabel }: { byTier: TierSplit[]; windowLabel: string }) {
    const order: TierSplit['tier'][] = ['news', 'officials', 'general_public'];
    return (
        <TopMetricsBlock eyebrow={`As of ${windowLabel}`} meta={null}>
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
        <Card title="Tone intensity">
            <div className="mini-metric" title={`Across ${total.toLocaleString()} sampled posts`}>
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
        </Card>
    );
}

function BreakdownCard({ data }: { data: SentimentPanelResponse }) {
    return (
        <Card title="By platform, topic, and time" subtitle="Volume and net tone across the window's dimensions.">
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
                <div>
                    <div className="eyebrow mb-2">By time of day</div>
                    {data.byTimeOfDay.map((t) => (
                        <div key={t.bucket} className="flex justify-between text-sm" style={{ padding: '4px 0' }}>
                            <span>{t.bucket}</span>
                            <span className="num">{t.volume.toLocaleString()}</span>
                        </div>
                    ))}
                </div>
                <div>
                    <div className="eyebrow mb-2">By day of week</div>
                    {data.byDayOfWeek.map((d) => (
                        <div key={d.day} className="flex justify-between text-sm" style={{ padding: '4px 0' }}>
                            <span>{d.day}</span>
                            <span className="num">{d.volume.toLocaleString()}</span>
                        </div>
                    ))}
                </div>
            </div>
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Entity three-way grid (grouped by corpus.entities.kind)                    //
// --------------------------------------------------------------------------- //

const ENTITY_SORTERS: ColumnSorter<EntityStanceAggregate>[] = [
    { label: 'posts', compare: (a, b) => b.targetStance.volume - a.targetStance.volume },
    { label: 'net tone', compare: (a, b) => (b.targetStance.netScore ?? 0) - (a.targetStance.netScore ?? 0) },
    { label: 'name', compare: (a, b) => a.displayName.localeCompare(b.displayName) },
];

function EntityThreeWayGrid({
    entityStances, onOpen,
}: {
    entityStances: EntityStanceAggregate[];
    onOpen: (item: EntityStanceAggregate) => void;
}) {
    const outlets = entityStances.filter((e) => e.kind === 'outlet');
    const officials = entityStances.filter((e) => e.kind === 'official' || e.kind === 'collective');
    const communities = entityStances.filter((e) => e.kind === 'subreddit');
    const unresolved = entityStances.filter((e) => e.entityId === null);

    const renderCard = (item: EntityStanceAggregate) => (
        <EntityProfileCard
            key={item.entityId ?? item.catchAllKey}
            entity={{ kind: item.kind, displayName: item.displayName, lean: item.lean }}
            stats={toneStats({ netTone: item.targetStance.netScore, volume: item.targetStance.volume })}
            onClick={() => onOpen(item)}
        />
    );

    return (
        <>
            <ThreeWayGrid>
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

function EntitySentimentModal({
    item, window, onClose,
}: {
    item: EntityStanceAggregate;
    window: Filters['timeRange'];
    onClose: () => void;
}) {
    const [loadedPosts, setLoadedPosts] = useState<EntityPostRow[] | null>(null);
    const [loadedTotal, setLoadedTotal] = useState(0);
    const [nextPage, setNextPage] = useState(1);
    const [loadingPosts, setLoadingPosts] = useState(false);
    const [postsError, setPostsError] = useState<string | null>(null);

    const loadMore = useCallback(async () => {
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
    }, [item.entityId, window, loadedPosts, nextPage]);

    const samples = loadedPosts ?? item.samples;

    return (
        <Modal isOpen onClose={onClose} title={item.displayName}>
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

interface PublicSentimentProps {
    filters: Filters;
}

function PublicSentiment({ filters }: PublicSentimentProps) {
    const [activeEntity, setActiveEntity] = useState<EntityStanceAggregate | null>(null);

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
                <div className="reads-as-today">
                    <span className="eyebrow reads-as-today-eyebrow">{asOfTodayEyebrow(filters.timeRange)}</span>
                    <p className="lead" style={{ margin: 0 }}>
                        How news outlets, public officials, and everyday people are reading American politics.
                    </p>
                </div>
            </div>

            <div className="col-span-8">
                <TopMetrics byTier={data.byTier} windowLabel={formatTimeWindow(filters.timeRange)} />
            </div>
            <div className="col-span-4">
                <IntensityMini distribution={data.distribution} />
            </div>

            <div className="col-span-12"><BreakdownCard data={data} /></div>

            <div className="col-span-12">
                <OutletSignalsPanel window={filters.timeRange} />
            </div>

            <div className="col-span-12">
                <EntityThreeWayGrid entityStances={data.entityStances} onOpen={setActiveEntity} />
            </div>

            {activeEntity && (
                <EntitySentimentModal
                    item={activeEntity}
                    window={filters.timeRange}
                    onClose={() => setActiveEntity(null)}
                />
            )}

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
                <CollapsibleInfo>
                    <p className="text-sm">
                        We aggregate news articles, Reddit posts, and X posts about US politics, then
                        score each one for tone (positive / negative / neutral). Tone is a classification
                        of what the post says, not what the author feels — don't read it as opinion
                        polling.
                    </p>
                </CollapsibleInfo>
            </div>
        </div>
    );
}

export default PublicSentiment;
