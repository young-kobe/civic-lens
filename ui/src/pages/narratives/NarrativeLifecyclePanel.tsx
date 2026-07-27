import { useId, useState, type KeyboardEvent, type MouseEvent } from 'react';
import { Area, AreaChart, ResponsiveContainer, Tooltip, XAxis } from 'recharts';
import type { TooltipProps } from 'recharts';
import { Card, DocDetailModal, MethodPopover } from '../../components/common';
import { formatRefreshedAgo } from '../../services/freshness';
import { COLORS } from '../../theme';
import type { NarrativeSummary } from '../../types';
import { SOURCE_COLOR, SOURCE_LABEL, dominantSource, sourcesPresent } from './sourceMix';

// --------------------------------------------------------------------------- //
//  NarrativeLifecyclePanel — the page's signature read: which stories are     //
//  rising and which are dying, at a glance. One row per narrative: name,      //
//  daily-volume timeline, the sources carrying it, and post count. Click      //
//  opens the detail modal.                                                    //
//                                                                             //
//  Degraded from the pre-redesign version: that panel colored each row's      //
//  line by first_seen_tier_group, a field the current /narratives response   //
//  does not carry. This version colors by dominant source (sourceBreakdown)  //
//  instead. "First seen" now renders firstSeenAt (relative date, restored     //
//  2026-07-26) -- the source/entity split that field used to carry still     //
//  doesn't exist.                                                            //
// --------------------------------------------------------------------------- //

const LIFECYCLE_TOP_N = 5;

interface NarrativeLifecyclePanelProps {
    narratives: NarrativeSummary[];
    onOpen: (n: NarrativeSummary) => void;
}

function rowTooltip({ payload, label }: TooltipProps<number, string>) {
    if (!payload || payload.length === 0) return null;
    return (
        <div className="chart-tooltip">
            <div className="chart-tooltip-label">{String(label ?? '')}</div>
            <div className="chart-tooltip-value">
                {Number(payload[0].value ?? 0).toLocaleString()} posts
            </div>
        </div>
    );
}

function LifecycleSparkline({ narrative }: { narrative: NarrativeSummary }) {
    const gradientId = `lifecycle-${useId().replace(/:/g, '')}`;
    const data = narrative.timeline.map((t) => ({ date: t.day, count: t.docCount }));
    if (data.length < 2) {
        return <span className="lifecycle-row-flat" aria-hidden />;
    }
    const origin = dominantSource(narrative);
    const originColor = (origin && SOURCE_COLOR[origin]) || COLORS.chartAccent;
    return (
        <ResponsiveContainer width="100%" height={36}>
            <AreaChart data={data} margin={{ top: 2, right: 2, left: 2, bottom: 2 }}>
                <defs>
                    <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
                        <stop offset="0%" stopColor={originColor} stopOpacity={0.3} />
                        <stop offset="100%" stopColor={originColor} stopOpacity={0} />
                    </linearGradient>
                </defs>
                <XAxis dataKey="date" hide />
                <Area
                    type="monotone"
                    dataKey="count"
                    stroke={originColor}
                    strokeWidth={1.75}
                    fill={`url(#${gradientId})`}
                    activeDot={{ r: 3, fill: originColor, strokeWidth: 0 }}
                    isAnimationActive={false}
                />
                <Tooltip
                    content={rowTooltip}
                    cursor={{ stroke: 'var(--neutral-300)', strokeDasharray: '2 2' }}
                />
            </AreaChart>
        </ResponsiveContainer>
    );
}

export function NarrativeLifecyclePanel({ narratives, onOpen }: NarrativeLifecyclePanelProps) {
    const [firstSeenDocId, setFirstSeenDocId] = useState<number | null>(null);
    const rows = [...narratives]
        .sort((a, b) => b.docCount - a.docCount)
        .slice(0, LIFECYCLE_TOP_N);
    if (rows.length === 0) return null;

    return (
        <Card
            title="Story lifecycles"
            subtitle="The most-repeated claims and their day-by-day volume — rising lines are stories gaining repetition, falling lines are stories dying out. Line color = the source carrying the most of the story's posts."
            headerActions={
                <MethodPopover
                    description={
                        'Each row is a recurring claim; its line is the daily count of sampled '
                        + "posts carrying it. Chips show which sources (news, reddit, X) are "
                        + 'repeating it now.'
                    }
                    limitations={[
                        'Volume counts our sample, not the whole internet — a flat line can mean we stopped sampling a source, not that the story died.',
                    ]}
                />
            }
        >
            <div className="lifecycle-rows">
                {rows.map((n) => {
                    const sources = sourcesPresent(n);
                    const openRow = () => onOpen(n);
                    const onRowKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                            e.preventDefault();
                            openRow();
                        }
                    };
                    const onFirstSeenClick = (e: MouseEvent<HTMLButtonElement>) => {
                        e.stopPropagation();
                        if (n.firstSeenDocId != null) setFirstSeenDocId(n.firstSeenDocId);
                    };
                    return (
                        <div
                            key={n.narrativeId}
                            role="button"
                            tabIndex={0}
                            className="lifecycle-row"
                            onClick={openRow}
                            onKeyDown={onRowKeyDown}
                            aria-label={`${n.anchorClaimText ?? '(unnamed)'}. ${n.docCount} posts. Open details.`}
                            title={n.anchorClaimText ?? '(unnamed)'}
                        >
                            <span className="lifecycle-row-name-wrap">
                                <span className="lifecycle-row-name">
                                    {n.anchorClaimText || '(unnamed)'}
                                    {n.meanConfidence != null && (
                                        <span
                                            className="badge badge-neutral"
                                            style={{ marginLeft: 'var(--space-2)' }}
                                            title="Mean claim-match confidence across this story's member posts"
                                        >
                                            {Math.round(n.meanConfidence * 100)}% confidence
                                        </span>
                                    )}
                                </span>
                                <span className="lifecycle-row-meta">
                                    first seen{' '}
                                    {n.firstSeenAt ? formatRefreshedAgo(n.firstSeenAt) : '—'}
                                    {n.firstSeenDocId != null && (
                                        <>
                                            {' '}
                                            <button
                                                type="button"
                                                className="link-button"
                                                onClick={onFirstSeenClick}
                                            >
                                                (view doc)
                                            </button>
                                        </>
                                    )}
                                </span>
                            </span>
                            <span className="lifecycle-row-spark">
                                <LifecycleSparkline narrative={n} />
                            </span>
                            <span className="lifecycle-row-tiers">
                                {sources.map((s) => (
                                    <span
                                        key={s}
                                        className="cross-tier-chip"
                                        style={{ color: SOURCE_COLOR[s] }}
                                    >
                                        {SOURCE_LABEL[s] ?? s}
                                    </span>
                                ))}
                            </span>
                            <span className="lifecycle-row-count">
                                {n.docCount.toLocaleString()}
                                <span className="lifecycle-row-count-label">posts</span>
                            </span>
                        </div>
                    );
                })}
            </div>
            {firstSeenDocId != null && (
                <DocDetailModal docId={firstSeenDocId} onClose={() => setFirstSeenDocId(null)} />
            )}
        </Card>
    );
}

export default NarrativeLifecyclePanel;
