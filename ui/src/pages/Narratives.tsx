import { useEffect, useMemo, useState } from 'react';
import {
    Card, CollapsibleInfo, DocDetailModal, EmptyState, EntityProfileCard, ErrorState, GlobalTicker,
    LeanLabel as LeanLabelView, LoadingCard, MethodPopover, Modal, RangeCaption, SampleCardList,
    ThreeWayColumn, ThreeWayGrid,
} from '../components/common';
import type { ColumnSorter, TickerItem } from '../components/common';
import { Sparkline } from '../components/charts';
import { fetchNarratives, fetchSnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPts } from '../services/format';
import { useFetch } from '../services/useFetch';
import { useDeepLinkParam } from '../services/deepLink';
import { NarrativeLifecyclePanel } from './narratives/NarrativeLifecyclePanel';
import { SOURCE_COLOR, SOURCE_LABEL, dominantSource, sourcesPresent } from './narratives/sourceMix';
import type { Filters, NarrativeSummary, NarrativesResponse, SnapshotStatusResponse } from '../types';

// --------------------------------------------------------------------------- //
//  Restored three-way / lifecycle geometry (pre-cutover-main), degraded to    //
//  what the current /narratives response carries.                            //
//                                                                             //
//  The pre-redesign page grouped narratives by first_seen_entity_profile /    //
//  first_seen_tier_group (news/officials/public) and drilled through a       //
//  per-entity modal. NarrativeSummary (analysis/src/api/models/narratives.py) //
//  carries no first-seen entity, tier, or timestamp at all -- that grouping   //
//  cannot be reconstructed without fabricating data. sourceBreakdown (news/   //
//  reddit/x member-doc counts) is the closest surviving cousin: the three-way //
//  grid below buckets each narrative by its DOMINANT source instead, and the  //
//  "spreading" list uses "carried by 2+ sources" instead of "first seen in    //
//  one tier, now repeated in another." Per-entity drill-down (NarrativeEntity //
//  Modal) has no replacement -- a column tile opens the narrative detail      //
//  modal directly. See the audit-trail entry for this restore for the full   //
//  writeup.                                                                   //
// --------------------------------------------------------------------------- //

function netSentimentColor(net: number | null): string {
    if (net == null) return 'var(--neutral-500)';
    if (net > 10) return 'var(--semantic-positive)';
    if (net < -10) return 'var(--semantic-negative)';
    return 'var(--neutral-500)';
}

function SourceBar({ narrative }: { narrative: NarrativeSummary }) {
    const total = narrative.sourceBreakdown.reduce((s, it) => s + it.docCount, 0);
    if (total === 0) return null;
    const summary = narrative.sourceBreakdown
        .filter((it) => it.docCount > 0)
        .map((it) => `${Math.round((it.docCount / total) * 100)}% ${SOURCE_LABEL[it.source] ?? it.source}`)
        .join(', ');
    return (
        <div
            className="narrative-source-bar"
            title={`Source mix across ${total} posts: ${summary}.`}
        >
            {narrative.sourceBreakdown.map((item) => (
                <div
                    key={item.source}
                    title={`${SOURCE_LABEL[item.source] ?? item.source}: ${item.docCount} of ${total} posts`}
                    style={{ width: `${(item.docCount / total) * 100}%`, background: SOURCE_COLOR[item.source] || 'var(--neutral-400)' }}
                />
            ))}
        </div>
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

// --------------------------------------------------------------------------- //
//  Detail modal                                                               //
// --------------------------------------------------------------------------- //

function NarrativeDetailModal({ narrative, onClose }: { narrative: NarrativeSummary; onClose: () => void }) {
    const [firstSeenDocOpen, setFirstSeenDocOpen] = useState(false);
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
            {narrative.meanConfidence != null && (
                <span
                    className="badge badge-neutral"
                    title="Mean claim-match confidence across this story's member posts"
                >
                    {Math.round(narrative.meanConfidence * 100)}% confidence
                </span>
            )}

            <p className="text-xs text-muted mt-1">
                First seen{' '}
                {narrative.firstSeenAt ? formatRefreshedAgo(narrative.firstSeenAt) : '— (predates first-seen tracking)'}
                {narrative.firstSeenDocId != null && (
                    <>
                        {' · '}
                        <button
                            type="button"
                            className="link-button"
                            onClick={() => setFirstSeenDocOpen(true)}
                        >
                            view the first-ingested doc
                        </button>
                    </>
                )}
            </p>

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

            {firstSeenDocOpen && narrative.firstSeenDocId != null && (
                <DocDetailModal docId={narrative.firstSeenDocId} onClose={() => setFirstSeenDocOpen(false)} />
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

// --------------------------------------------------------------------------- //
//  Cross-source spreading list                                                //
// --------------------------------------------------------------------------- //

const CROSS_SOURCE_LIMIT = 5;

/**
 * The same story is showing up in more than one source (news AND reddit
 * AND/OR X). Cousin of the pre-redesign cross-tier list, which flagged a
 * narrative first seen in one group (news/officials/public) now also
 * repeated in another -- that first-seen concept no longer exists, so this
 * flags narratives whose member docs simply span 2+ sources.
 */
function ClaimsSpreadingPanel({ narratives, onOpen }: { narratives: NarrativeSummary[]; onOpen: (n: NarrativeSummary) => void }) {
    const crossSource = narratives.filter((n) => sourcesPresent(n).length >= 2);
    if (crossSource.length === 0) {
        return (
            <Card
                title="Stories spreading across sources"
                subtitle="No story has surfaced across more than one source yet in this window — see the per-source breakdown above for what each is talking about."
            >
                <p className="text-muted text-sm">
                    We'll list stories here as soon as the same recurring claim is being repeated across at least two sources (news, reddit, X).
                </p>
            </Card>
        );
    }

    const visible = crossSource.slice(0, CROSS_SOURCE_LIMIT);
    return (
        <Card
            title="Stories spreading across sources"
            subtitle={`${visible.length} ${visible.length === 1 ? 'story is' : 'stories are'} being repeated across more than one source.`}
        >
            <div className="cross-tier-list">
                {visible.map((n) => {
                    const sources = sourcesPresent(n);
                    return (
                        <button
                            key={n.narrativeId}
                            type="button"
                            className="cross-tier-row"
                            onClick={() => onOpen(n)}
                            aria-label={`${n.anchorClaimText ?? '(unnamed)'}. Repeated across ${sources.map((s) => SOURCE_LABEL[s] ?? s).join(', ')}. Click for details.`}
                        >
                            <span className="cross-tier-row-claim" title={n.anchorClaimText ?? '(unnamed)'}>
                                {n.anchorClaimText || '(unnamed)'}
                            </span>
                            <span className="cross-tier-row-tiers">
                                {sources.map((s) => (
                                    <span key={s} className="cross-tier-chip" style={{ color: SOURCE_COLOR[s] }}>
                                        {SOURCE_LABEL[s] ?? s}
                                    </span>
                                ))}
                            </span>
                            <span className="cross-tier-row-docs">
                                {n.docCount}
                                <span className="cross-tier-row-docs-label">posts</span>
                            </span>
                        </button>
                    );
                })}
            </div>
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Three-way grid — bucketed by dominant source, not first-seen entity        //
//  (see the file-header note: that field no longer exists). Each tile is one  //
//  narrative; there is no entity to group several narratives under, so a      //
//  tile opens the narrative detail modal directly.                            //
// --------------------------------------------------------------------------- //

const NARRATIVE_SORTERS: ColumnSorter<NarrativeSummary>[] = [
    { label: 'posts', compare: (a, b) => b.docCount - a.docCount },
    { label: 'net tone', compare: (a, b) => (b.netSentiment ?? 0) - (a.netSentiment ?? 0) },
    { label: 'name', compare: (a, b) => (a.anchorClaimText ?? '').localeCompare(b.anchorClaimText ?? '') },
];

interface NarrativeThreeWayColumnProps {
    header: string;
    byline: string;
    narratives: NarrativeSummary[];
    onOpen: (n: NarrativeSummary) => void;
    emptyCopy: string;
}

function NarrativeThreeWayColumn({ header, byline, narratives, onOpen, emptyCopy }: NarrativeThreeWayColumnProps) {
    return (
        <ThreeWayColumn
            header={header}
            byline={byline}
            empty={emptyCopy}
            items={narratives}
            sorters={NARRATIVE_SORTERS}
            renderItem={(n) => (
                <EntityProfileCard
                    key={n.narrativeId}
                    entity={{ kind: null, displayName: n.anchorClaimText || '(unnamed)', lean: n.lean }}
                    stats={[
                        { label: 'Posts', value: n.docCount.toLocaleString(), emphasis: true },
                        {
                            label: 'Net tone',
                            value: n.netSentiment != null ? formatPts(n.netSentiment) : '—',
                            color: netSentimentColor(n.netSentiment),
                        },
                        ...(n.meanConfidence != null
                            ? [{
                                label: 'Confidence',
                                value: `${Math.round(n.meanConfidence * 100)}%`,
                                title: "Mean claim-match confidence across this story's member posts",
                            }]
                            : []),
                    ]}
                    onClick={() => onOpen(n)}
                    emptyNote="Tracked — no stories dominated by this source in this window."
                />
            )}
        />
    );
}

// --------------------------------------------------------------------------- //
//  Page                                                                       //
// --------------------------------------------------------------------------- //

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

    const ranked = [...data.narratives].sort((a, b) => b.docCount - a.docCount);
    const refreshed = formatRefreshedAgo(pipelineRunTimestamp(snapshotStatus));

    const newsNarratives = ranked.filter((n) => dominantSource(n) === 'news');
    const redditNarratives = ranked.filter((n) => dominantSource(n) === 'reddit');
    const xNarratives = ranked.filter((n) => dominantSource(n) === 'x');

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

                <div className="col-span-6">
                    <NarrativeLifecyclePanel narratives={ranked} onOpen={setActiveNarrative} />
                </div>
                <div className="col-span-6">
                    <ClaimsSpreadingPanel narratives={ranked} onOpen={setActiveNarrative} />
                </div>

                <div className="col-span-12">
                    <ThreeWayGrid>
                        <NarrativeThreeWayColumn
                            header="News"
                            byline="Stories where news posts make up the largest share of member docs"
                            narratives={newsNarratives}
                            onOpen={setActiveNarrative}
                            emptyCopy="No stories dominated by news sources in this window."
                        />
                        <NarrativeThreeWayColumn
                            header="Reddit"
                            byline="Stories where reddit posts make up the largest share of member docs"
                            narratives={redditNarratives}
                            onOpen={setActiveNarrative}
                            emptyCopy="No stories dominated by reddit in this window."
                        />
                        <NarrativeThreeWayColumn
                            header="X"
                            byline="Stories where X posts make up the largest share of member docs"
                            narratives={xNarratives}
                            onOpen={setActiveNarrative}
                            emptyCopy="No stories dominated by X in this window."
                        />
                    </ThreeWayGrid>
                </div>

                <div className="col-span-12">
                    <CollapsibleInfo>
                        <p className="text-sm">
                            A "story" here is a political claim we saw repeated across multiple posts.
                            The three columns bucket each story by whichever source (news, reddit, X)
                            contributed the most of its posts. "Stories spreading across sources" lists
                            stories whose posts span more than one of the three.
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
