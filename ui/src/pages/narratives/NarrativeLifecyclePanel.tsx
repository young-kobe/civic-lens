import { useId } from 'react';
import { Area, AreaChart, ResponsiveContainer, Tooltip, XAxis } from 'recharts';
import type { TooltipProps } from 'recharts';
import { Card, MethodPopover } from '../../components/common';
import { formatRelativeDate } from '../../services/format';
import { COLORS } from '../../theme';
import type { NarrativeSummary } from '../../types';

// --------------------------------------------------------------------------- //
//  NarrativeLifecyclePanel — the page's signature read: which stories are     //
//  rising and which are dying, at a glance. One row per narrative: name,      //
//  its daily-volume timeline (cached all along, previously modal-only),       //
//  the groups carrying it, and post count. Click opens the detail modal.      //
// --------------------------------------------------------------------------- //

// 5 to match the "Stories spreading across groups" panel it now shares a row with.
const LIFECYCLE_TOP_N = 5;

const TIER_LABEL: Record<string, string> = {
    news: 'News',
    officials: 'Officials',
    public: 'Public',
};

// Canonical speaker-tier palette (news / officials / public = blue / teal /
// amber) — the same trio the tone-trend lines and cross-tier chips use, so the
// origin line color and the tier chips beside it always agree. (Previously this
// borrowed source colors, which made officials render as the chrome accent and
// public reuse reddit-orange.)
const TIER_COLOR: Record<string, string> = {
    news: 'var(--tier-news)',
    officials: 'var(--tier-officials)',
    public: 'var(--tier-public)',
};

interface NarrativeLifecyclePanelProps {
    narratives: NarrativeSummary[];
    onOpen: (n: NarrativeSummary) => void;
    /** Groups present per narrative (news/officials/public), computed by the
     *  page's tierChipsForNarrative so both surfaces agree. */
    tiersFor: (n: NarrativeSummary) => string[];
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
    const data = narrative.timeline;
    if (data.length < 2) {
        return <span className="lifecycle-row-flat" aria-hidden />;
    }
    const originColor = TIER_COLOR[narrative.first_seen_tier_group ?? ''] ?? COLORS.chartAccent;
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

export function NarrativeLifecyclePanel({ narratives, onOpen, tiersFor }: NarrativeLifecyclePanelProps) {
    const rows = [...narratives]
        .sort((a, b) => b.supporting_doc_count - a.supporting_doc_count)
        .slice(0, LIFECYCLE_TOP_N);
    if (rows.length === 0) return null;

    return (
        <Card
            title="Story lifecycles"
            subtitle="The most-repeated claims and their day-by-day volume — rising lines are stories gaining repetition, falling lines are stories dying out. Line color = the group where we first saw the story."
            headerActions={
                <MethodPopover
                    description={
                        'Each row is a recurring claim; its line is the daily count of sampled '
                        + 'posts carrying it. "First seen" is the earliest post WE collected — '
                        + 'the claim may have started elsewhere before we picked it up. Chips '
                        + 'show which groups (news, officials, public) are repeating it now.'
                    }
                    limitations={[
                        'Volume counts our sample, not the whole internet — a flat line can mean we stopped sampling a source, not that the story died.',
                    ]}
                />
            }
        >
            <div className="lifecycle-rows">
                {rows.map((n) => {
                    const tiers = tiersFor(n);
                    return (
                        <button
                            key={n.narrative_id}
                            type="button"
                            className="lifecycle-row"
                            onClick={() => onOpen(n)}
                            aria-label={`${n.name}. ${n.supporting_doc_count} posts, first seen ${formatRelativeDate(n.first_seen_at)}. Open details.`}
                            title={n.name}
                        >
                            <span className="lifecycle-row-name-wrap">
                                <span className="lifecycle-row-name">{n.name || '(unnamed)'}</span>
                                <span className="lifecycle-row-meta">
                                    first seen {formatRelativeDate(n.first_seen_at)}
                                </span>
                            </span>
                            <span className="lifecycle-row-spark">
                                <LifecycleSparkline narrative={n} />
                            </span>
                            <span className="lifecycle-row-tiers">
                                {tiers.map((t) => (
                                    <span key={t} className={`cross-tier-chip cross-tier-chip-${t}`}>
                                        {TIER_LABEL[t] ?? t}
                                    </span>
                                ))}
                            </span>
                            <span className="lifecycle-row-count">
                                {n.supporting_doc_count.toLocaleString()}
                                <span className="lifecycle-row-count-label">posts</span>
                            </span>
                        </button>
                    );
                })}
            </div>
        </Card>
    );
}

export default NarrativeLifecyclePanel;
