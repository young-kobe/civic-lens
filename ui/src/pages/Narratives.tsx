import { useEffect, useMemo, useState } from 'react';
import {
    Card, CollapsibleInfo, EmptyState, ErrorState, GlobalTicker, LeanLabel as LeanLabelView,
    LoadingCard, MethodPopover, Modal, RangeCaption, SampleCardList,
} from '../components/common';
import type { TickerItem } from '../components/common';
import { Sparkline } from '../components/charts';
import { fetchNarratives, fetchSnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPts } from '../services/format';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import { useDeepLinkParam } from '../services/deepLink';
import type { Filters, NarrativeSummary, NarrativesResponse, SnapshotStatusResponse } from '../types';

// --------------------------------------------------------------------------- //
//  Phase 10 adaptation note: the pre-redesign "first seen at <entity/tier>"    //
//  tracking, cross-tier spreading panel, and account-profile author labels    //
//  have no equivalent in the strictly-live /narratives response               //
//  (analysis/src/api/models/narratives.py) -- NarrativeSummaryModel carries    //
//  no first_seen_* field at all. This page is rebuilt as a flat ranked list    //
//  of narratives (by member doc count), each opening a detail modal with the  //
//  timeline, source mix, propaganda/bot-pushed fractions, lean, cited docs,   //
//  and member-doc samples the new contract actually provides.                 //
// --------------------------------------------------------------------------- //

const NARRATIVE_TOP_N = 20;

function netSentimentColor(net: number | null): string {
    if (net == null) return COLORS.neutral;
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return COLORS.neutral;
}

const SOURCE_DOT_COLOR: Record<string, string> = {
    news: COLORS.sourceNews, reddit: COLORS.sourceReddit, x: COLORS.sourceX,
};

function SourceBar({ narrative }: { narrative: NarrativeSummary }) {
    const total = narrative.sourceBreakdown.reduce((s, it) => s + it.docCount, 0);
    if (total === 0) return null;
    return (
        <div className="narrative-source-bar" aria-hidden>
            {narrative.sourceBreakdown.map((item) => (
                <div
                    key={item.source}
                    title={`${item.source}: ${item.docCount} of ${total} posts`}
                    style={{ width: `${(item.docCount / total) * 100}%`, background: SOURCE_DOT_COLOR[item.source] || 'var(--neutral-400)' }}
                />
            ))}
        </div>
    );
}

function NarrativeRow({ narrative, onOpen }: { narrative: NarrativeSummary; onOpen: () => void }) {
    const timelineData = narrative.timeline.map((t) => ({ date: t.day, value: t.docCount }));
    return (
        <button type="button" className="lifecycle-row" onClick={onOpen} title={narrative.anchorClaimText ?? '(unnamed)'}>
            <span className="lifecycle-row-name-wrap">
                <span className="lifecycle-row-name">{narrative.anchorClaimText || '(unnamed narrative)'}</span>
                <SourceBar narrative={narrative} />
            </span>
            <span className="lifecycle-row-spark">
                {timelineData.length >= 2 ? (
                    <Sparkline data={timelineData} height={36} color={netSentimentColor(narrative.netSentiment)} showTooltip={false} />
                ) : <span className="lifecycle-row-flat" aria-hidden />}
            </span>
            <span className="lifecycle-row-count">
                {narrative.docCount.toLocaleString()}
                <span className="lifecycle-row-count-label">posts</span>
            </span>
        </button>
    );
}

function NarrativeDetailModal({ narrative, onClose }: { narrative: NarrativeSummary; onClose: () => void }) {
    const timelineData = useMemo(
        () => narrative.timeline.map((t) => ({ date: t.day, value: t.docCount })),
        [narrative.timeline],
    );
    const sentColor = netSentimentColor(narrative.netSentiment);

    return (
        <Modal
            isOpen
            onClose={onClose}
            kicker="Narrative"
            title={narrative.anchorClaimText || '(unnamed narrative)'}
            maxWidth={1040}
        >
            <div className="narrative-modal-stats">
                <div>
                    <div className="eyebrow">Member posts</div>
                    <div className="metric-value">{narrative.docCount.toLocaleString()}</div>
                </div>
                <div>
                    <div className="eyebrow">Net tone</div>
                    <div className="metric-value" style={{ color: sentColor }}>
                        {narrative.netSentiment != null ? formatPts(narrative.netSentiment) : '—'}
                    </div>
                </div>
                <div>
                    <div className="eyebrow">Citations</div>
                    <div className="metric-value">{narrative.citationCount.toLocaleString()}</div>
                </div>
                {narrative.propagandaFlaggedFraction != null && (
                    <div>
                        <div className="eyebrow">Propaganda-flagged</div>
                        <div className="metric-value">{Math.round(narrative.propagandaFlaggedFraction * 100)}%</div>
                    </div>
                )}
                {narrative.botPushedFraction != null && (
                    <div>
                        <div className="eyebrow">Bot-pushed</div>
                        <div className="metric-value">{Math.round(narrative.botPushedFraction * 100)}%</div>
                    </div>
                )}
            </div>

            {narrative.lean && (
                <p className="mt-2"><LeanLabelView lean={narrative.lean} /></p>
            )}

            <h3 className="card-title mt-4 mb-2">Daily volume</h3>
            {timelineData.length >= 2 ? (
                <Sparkline data={timelineData} dataKey="value" height={200} showXAxis color="var(--neutral-700)" />
            ) : (
                <p className="text-sm text-muted">Not enough days of data yet to draw a trend.</p>
            )}

            <h3 className="card-title mt-4 mb-2">Source mix</h3>
            <SourceBar narrative={narrative} />

            {narrative.citedDocs.length > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">Cited documents</h3>
                    <SampleCardList
                        samples={narrative.citedDocs}
                        sampleNote="Documents cited by this narrative's member posts — resolves regardless of age."
                    />
                </>
            )}

            <h3 className="card-title mt-4 mb-2">Member posts</h3>
            <SampleCardList
                samples={narrative.memberDocSamples}
                sampleNote="The strongest posts carrying this story in our sample — not every post that repeats it."
                emptyNote="No sample posts stored for this narrative."
            />
        </Modal>
    );
}

function buildTickerItems(data: NarrativesResponse, window: Filters['timeRange']): TickerItem[] {
    const total = data.narratives.length;
    let top: NarrativeSummary | null = null;
    for (const n of data.narratives) {
        if (!top || n.docCount > top.docCount) top = n;
    }
    const items: TickerItem[] = [
        { label: 'Top stories', value: total.toLocaleString(), hint: 'in window', emphasis: true },
        { label: 'Window', value: formatTimeWindow(window) },
    ];
    if (top) {
        const short = (top.anchorClaimText ?? '(unnamed)').length > 48
            ? (top.anchorClaimText ?? '').slice(0, 48).trimEnd() + '…'
            : (top.anchorClaimText ?? '(unnamed)');
        items.push({ label: 'Most repeated', value: short, hint: `${top.docCount.toLocaleString()} posts` });
    }
    return items;
}

interface NarrativesProps {
    filters: Filters;
}

function Narratives({ filters }: NarrativesProps) {
    const [activeNarrative, setActiveNarrative] = useState<NarrativeSummary | null>(null);
    const [openParam, setOpenParam] = useDeepLinkParam('open');

    const { data, loading, error, refetch } = useFetch<NarrativesResponse>(
        () => fetchNarratives(filters.timeRange),
        [filters.timeRange],
        `narratives:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatusResponse>(
        () => fetchSnapshotStatus(), [], 'snapshot-status',
    );

    useEffect(() => {
        if (!data || !openParam) return;
        const target = data.narratives.find((n) => String(n.narrativeId) === openParam);
        if (target) setActiveNarrative(target);
        else setOpenParam(null);
    }, [data, openParam, setOpenParam]);

    if (error) return <ErrorState message={error.message} onRetry={refetch} />;
    if (loading) {
        return <div className="flex flex-col gap-4"><LoadingCard /><LoadingCard /></div>;
    }
    if (!data) return <EmptyState title="No stories available for this window." />;

    const ranked = [...data.narratives].sort((a, b) => b.docCount - a.docCount).slice(0, NARRATIVE_TOP_N);
    const refreshed = formatRefreshedAgo(pipelineRunTimestamp(snapshotStatus));

    return (
        <>
            <div className="dashboard-grid">
                <div className="col-span-12">
                    <GlobalTicker
                        items={buildTickerItems(data, filters.timeRange)}
                        refreshed={refreshed}
                        ariaLabel="Narratives overview"
                        legend={
                            <MethodPopover
                                title="How to read these numbers"
                                description={
                                    "A story is a claim we saw repeated across posts. Net tone = positive "
                                    + 'minus negative share of a story\'s posts, -100 to +100.'
                                }
                            />
                        }
                    />
                    <RangeCaption range={data.range} />
                </div>

                <div className="col-span-12">
                    <div className="reads-as-today">
                        <span className="eyebrow reads-as-today-eyebrow">{asOfTodayEyebrow(filters.timeRange)}</span>
                        <p className="lead" style={{ margin: 0 }}>
                            The recurring talking points we've picked up across coverage.
                        </p>
                    </div>
                </div>

                <div className="col-span-12">
                    {ranked.length > 0 ? (
                        <Card title="Top stories" subtitle="Ranked by member-doc count. Click a row for the full detail.">
                            <div className="lifecycle-rows">
                                {ranked.map((n) => (
                                    <NarrativeRow key={n.narrativeId} narrative={n} onOpen={() => setActiveNarrative(n)} />
                                ))}
                            </div>
                        </Card>
                    ) : (
                        <EmptyState title="No recurring stories in this window." />
                    )}
                </div>

                <div className="col-span-12">
                    <CollapsibleInfo>
                        <p className="text-sm">
                            A "story" here is a political claim we saw repeated across multiple posts.
                            Ranked by member-doc count; click a row for its daily volume, source mix,
                            citations, and the posts carrying it.
                        </p>
                    </CollapsibleInfo>
                </div>
            </div>

            {activeNarrative && (
                <NarrativeDetailModal
                    narrative={activeNarrative}
                    onClose={() => {
                        setActiveNarrative(null);
                        if (openParam) setOpenParam(null);
                    }}
                />
            )}
        </>
    );
}

export default Narratives;
