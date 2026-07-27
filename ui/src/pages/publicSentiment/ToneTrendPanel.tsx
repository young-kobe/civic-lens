import { useMemo, useState } from 'react';
import {
    CartesianGrid, Line, LineChart,
    ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import type { TooltipProps } from 'recharts';
import { Card, MethodPopover } from '../../components/common';
import { formatPts } from '../../services/format';
import { COLORS } from '../../theme';
import type { EntitySentimentItem, ToneTrendPoint } from '../../types';

// --------------------------------------------------------------------------- //
//  ToneTrendPanel — the Tone page's time dimension. Restored verbatim from    //
//  `pre-cutover-main` (docs/todos/ui-feature-restoration.md, Wave 3 UI) now   //
//  that `SentimentPanelResponse.toneTrend` (day-by-tier net tone) and each    //
//  `EntitySentimentItem.dailyTone` (Wave 2 API additions) restore the data    //
//  this chart needs. The daily GOP net-favorability second view is dropped,  //
//  not restored — GOP favorability is retired for good (owner decision       //
//  2026-07-25, docs/todos/ui-feature-restoration.md), so there is no gopTrend//
//  field to read and no fabricated substitute here.                          //
//                                                                             //
//  Primary series: the per-day per-tier toneTrend — news vs officials vs     //
//  public net tone, day by day, with low-sample days rendered as gaps.       //
//  Clicking a tier in the legend drills into its individual entities         //
//  (dailyTone); clicking an entity opens the shared entity modal. Clicking a //
//  point on the chart opens that day's sampled posts.                        //
// --------------------------------------------------------------------------- //

type TierKey = 'news' | 'officials' | 'public';

interface ToneTrendPanelProps {
    toneTrend: ToneTrendPoint[] | null | undefined;
    /** Per-tier entity items (each carrying dailyTone) for the drill-down. */
    entitiesByTier?: Record<TierKey, EntitySentimentItem[]>;
    /** Opens the shared entity modal (live classified evidence) on click. */
    onOpenEntity?: (item: EntitySentimentItem) => void;
    /** Opens a modal of that calendar day's sampled posts when a chart point
     *  is clicked (mirrors the tone-intensity segment drill-down). */
    onDateClick?: (date: string) => void;
}

// High-contrast speaker-tier hues, shared with the top-metrics tier
// sparklines so the two visuals read as one system
// (news = slate, officials = teal, public = amber).
const TIER_SERIES: { key: TierKey; label: string; color: string }[] = [
    { key: 'news', label: 'News', color: COLORS.tierNews },
    { key: 'officials', label: 'Officials', color: COLORS.tierOfficials },
    { key: 'public', label: 'Public', color: COLORS.tierPublic },
];

const TIER_EXPLAIN: Record<TierKey, string> = {
    news: "Net tone of news outlets' own sampled posts, day by day. Click to break out the individual outlets driving it.",
    officials: "Net tone of tracked officials' own posts. Click to break out individual officials.",
    public: 'Net tone of subreddits and public X accounts. Click to break out individual voices.',
};

// Ordered categorical hues for the per-entity drill-down lines, drawn from the
// existing token set (accent / tier teal + amber / lean plum / slate / brick).
// Capped at 6 lines — beyond that a tier's chart stops reading as divergence.
const ENTITY_SERIES_COLORS = [
    'var(--accent)', 'var(--tier-officials)', 'var(--tier-public)',
    'var(--lean-right)', 'var(--tier-news)', 'var(--source-reddit)',
];
const ENTITY_MAX = 6;

interface SeriesDef { key: string; label: string; color: string; }
interface LineRow {
    date: string;
    volumes: Record<string, number>;
    [seriesKey: string]: number | null | string | Record<string, number>;
}

/** "2026-07-04" → "Jul 4" for axis ticks. */
function shortDate(iso: string): string {
    const m = /^\d{4}-(\d{2})-(\d{2})$/.exec(iso);
    if (!m) return iso;
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    return `${months[Number(m[1]) - 1]} ${Number(m[2])}`;
}

// Flat rows recharts keys directly: {date, <seriesKey>: net|null, ..., volumes}.
function toTierRows(trend: ToneTrendPoint[]): LineRow[] {
    return trend.map((p) => ({
        date: p.date,
        news: p.news.net,
        officials: p.officials.net,
        public: p.public.net,
        volumes: { news: p.news.volume, officials: p.officials.volume, public: p.public.volume },
    }));
}

/** Merge each entity's dailyTone into date-keyed rows; one series per entity,
 *  top ENTITY_MAX by volume (beyond that the chart stops reading). */
function toEntityRows(entities: EntitySentimentItem[]): { rows: LineRow[]; series: SeriesDef[] } {
    const top = entities
        .filter((e) => (e.dailyTone?.length ?? 0) > 0)
        .sort((a, b) => b.volume - a.volume)
        .slice(0, ENTITY_MAX);
    const series: SeriesDef[] = top.map((e, i) => ({
        key: e.key,
        label: e.entityProfile.displayName,
        color: ENTITY_SERIES_COLORS[i % ENTITY_SERIES_COLORS.length],
    }));
    const byDate = new Map<string, LineRow>();
    for (const e of top) {
        for (const pt of e.dailyTone ?? []) {
            let row = byDate.get(pt.date);
            if (!row) { row = { date: pt.date, volumes: {} }; byDate.set(pt.date, row); }
            row[e.key] = pt.net;
            (row.volumes as Record<string, number>)[e.key] = pt.volume;
        }
    }
    const rows = [...byDate.values()].sort((a, b) => (a.date < b.date ? -1 : 1));
    return { rows, series };
}

function multiTooltip(series: SeriesDef[]) {
    return function MultiTooltip({ payload, label }: TooltipProps<number, string>) {
        if (!payload || payload.length === 0) return null;
        const row = payload[0].payload as LineRow;
        const vols = row.volumes as Record<string, number>;
        return (
            <div className="chart-tooltip">
                <div className="chart-tooltip-label">{String(label ?? '')}</div>
                {series.map((s) => {
                    const net = row[s.key] as number | null | undefined;
                    const volume = vols[s.key] ?? 0;
                    if (net == null && volume === 0) return null;
                    return (
                        <div key={s.key} className="chart-tooltip-value" style={{ color: s.color }}>
                            {s.label}: {net != null
                                ? `${formatPts(net)} · ${volume} posts`
                                : `low sample (${volume})`}
                        </div>
                    );
                })}
            </div>
        );
    };
}

/** Shared overlaid-line chart. `highlightKey` dims the other lines so hovering
 *  a legend item isolates one series. */
function MultiLineChart({
    rows, series, highlightKey, ariaLabel, onDateClick,
}: {
    rows: LineRow[];
    series: SeriesDef[];
    highlightKey: string | null;
    ariaLabel: string;
    onDateClick?: (date: string) => void;
}) {
    return (
        <div role="img" aria-label={ariaLabel}>
            <ResponsiveContainer width="100%" height={240}>
                <LineChart
                    data={rows}
                    margin={{ top: 8, right: 8, left: 0, bottom: 0 }}
                    onClick={onDateClick
                        ? (s: { activeLabel?: string | number }) => { if (s?.activeLabel) onDateClick(String(s.activeLabel)); }
                        : undefined}
                    style={onDateClick ? { cursor: 'pointer' } : undefined}
                >
                    <CartesianGrid vertical={false} stroke="var(--chart-grid)" strokeDasharray="2 4" />
                    <XAxis
                        dataKey="date"
                        tickFormatter={shortDate}
                        tick={{ fontSize: 11, fill: 'var(--neutral-500)', fontFamily: 'var(--font-mono)' }}
                        axisLine={{ stroke: 'var(--chart-grid)' }}
                        tickLine={false}
                        minTickGap={24}
                    />
                    <YAxis
                        domain={[-100, 100]}
                        ticks={[-100, -50, 0, 50, 100]}
                        tick={{ fontSize: 11, fill: 'var(--neutral-500)', fontFamily: 'var(--font-mono)' }}
                        axisLine={false}
                        tickLine={false}
                        width={36}
                    />
                    <ReferenceLine y={0} stroke="var(--neutral-300)" strokeDasharray="3 3" />
                    {series.map((s) => {
                        const dim = highlightKey != null && highlightKey !== s.key;
                        return (
                            <Line
                                key={s.key}
                                type="monotone"
                                dataKey={s.key}
                                name={s.label}
                                stroke={s.color}
                                strokeWidth={dim ? 1 : 2}
                                strokeOpacity={dim ? 0.2 : 1}
                                dot={false}
                                activeDot={{ r: 4, fill: s.color, strokeWidth: 0 }}
                                // Low-sample days arrive as null — draw a gap, not a
                                // bridge, so suppressed readings never fake a line.
                                connectNulls={false}
                                isAnimationActive={false}
                            />
                        );
                    })}
                    <Tooltip
                        content={multiTooltip(series)}
                        cursor={{ stroke: 'var(--neutral-300)', strokeDasharray: '2 2' }}
                    />
                </LineChart>
            </ResponsiveContainer>
        </div>
    );
}

interface LegendItem {
    key: string;
    label: string;
    color: string;
    title: string;
    onClick?: () => void;
}

/** Color-coded legend: each item explains itself on hover (title) and is
 *  clickable — tiers drill into their entities, entities open evidence. */
function ToneLegend({
    items, onHover,
}: {
    items: LegendItem[];
    onHover: (key: string | null) => void;
}) {
    return (
        <div className="tone-legend">
            {items.map((it) => (
                <button
                    key={it.key}
                    type="button"
                    className={`tone-legend-item${it.onClick ? '' : ' tone-legend-item-static'}`}
                    title={it.title}
                    onClick={it.onClick}
                    onMouseEnter={() => onHover(it.key)}
                    onMouseLeave={() => onHover(null)}
                    onFocus={() => onHover(it.key)}
                    onBlur={() => onHover(null)}
                >
                    <span className="tone-legend-swatch" style={{ background: it.color }} aria-hidden />
                    {it.label}
                </button>
            ))}
        </div>
    );
}

// --------------------------------------------------------------------------- //
//  Panel                                                                      //
// --------------------------------------------------------------------------- //

export function ToneTrendPanel({
    toneTrend, entitiesByTier, onOpenEntity, onDateClick,
}: ToneTrendPanelProps) {
    const tierTrend = (toneTrend ?? []).filter((p) => !!p.date);
    const hasTiers = tierTrend.length >= 2;
    // Which tier the chart is drilled into (null = the 3-tier overview).
    const [drillTier, setDrillTier] = useState<TierKey | null>(null);
    const [highlightKey, setHighlightKey] = useState<string | null>(null);

    // A tier can be drilled only if we have its entity series (post-dailyTone).
    const canDrill = (tier: TierKey): boolean =>
        (entitiesByTier?.[tier] ?? []).some((e) => (e.dailyTone?.length ?? 0) > 0);

    const tierRows = useMemo(() => toTierRows(tierTrend), [tierTrend]);
    const drill = useMemo(
        () => (drillTier ? toEntityRows(entitiesByTier?.[drillTier] ?? []) : null),
        [drillTier, entitiesByTier],
    );
    const entityByKey = useMemo(() => {
        const m = new Map<string, EntitySentimentItem>();
        if (drillTier) for (const e of entitiesByTier?.[drillTier] ?? []) m.set(e.key, e);
        return m;
    }, [drillTier, entitiesByTier]);

    if (!hasTiers) return null;

    const inDrill = drillTier != null && !!drill && drill.series.length > 0;

    const tierLabel = (t: TierKey) => TIER_SERIES.find((s) => s.key === t)?.label ?? t;

    const clickHint = onDateClick ? " Click a point to read that day's posts." : '';
    const subtitle = (inDrill
        ? `Daily net tone of individual ${tierLabel(drillTier!).toLowerCase()} — click a name to read its classified posts.`
        : 'Daily net tone by group: news, officials, the public. Click a group in the legend to break out who is driving it.')
        + clickHint;

    const tierLegend: LegendItem[] = TIER_SERIES.map((s) => ({
        key: s.key,
        label: s.label,
        color: s.color,
        title: canDrill(s.key)
            ? TIER_EXPLAIN[s.key]
            : `${TIER_EXPLAIN[s.key].split('.')[0]}. (No per-entity breakout in this window.)`,
        onClick: canDrill(s.key) ? () => { setDrillTier(s.key); setHighlightKey(null); } : undefined,
    }));

    const entityLegend: LegendItem[] = (drill?.series ?? []).map((s) => {
        const item = entityByKey.get(s.key);
        return {
            key: s.key,
            label: s.label,
            color: s.color,
            title: item?.entityProfile.blurb
                ? `${item.entityProfile.blurb} — click to read its classified posts.`
                : 'Click to read its classified posts.',
            onClick: item && onOpenEntity ? () => onOpenEntity(item) : undefined,
        };
    });

    return (
        <Card
            title="Tone over time"
            subtitle={subtitle}
            headerActions={
                <>
                    {inDrill && (
                        <button
                            type="button"
                            className="trend-view-btn"
                            onClick={() => { setDrillTier(null); setHighlightKey(null); }}
                        >
                            &larr; All groups
                        </button>
                    )}
                    <MethodPopover
                        description={
                            'Tracks the net tone of each group\'s own sampled posts per day on a '
                            + '-100 to +100 scale; a day with too few posts from a group is '
                            + 'suppressed and drawn as a gap, never a zero. Click a group to break it '
                            + 'into its individual outlets/officials/accounts, then click one to read '
                            + 'its classified posts. Summarizes the posts we collected — a sample, '
                            + 'not a poll.'
                        }
                        limitations={[
                            'Days with few posts swing harder — the lines are not volume-weighted.',
                        ]}
                    />
                </>
            }
        >
            {inDrill ? (
                <MultiLineChart
                    rows={drill!.rows}
                    series={drill!.series}
                    highlightKey={highlightKey}
                    ariaLabel={`Daily net tone of individual ${tierLabel(drillTier!)}, ${drill!.rows.length} days`}
                    onDateClick={onDateClick}
                />
            ) : (
                <MultiLineChart
                    rows={tierRows}
                    series={TIER_SERIES}
                    highlightKey={highlightKey}
                    ariaLabel={`Daily net tone by group (news, officials, public), ${tierRows.length} days`}
                    onDateClick={onDateClick}
                />
            )}
            <ToneLegend
                items={inDrill ? entityLegend : tierLegend}
                onHover={setHighlightKey}
            />
        </Card>
    );
}

export default ToneTrendPanel;
