import { useId, useState } from 'react';
import {
    Area, AreaChart, Bar, BarChart, Cell, Legend, Line, LineChart,
    ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import type { TooltipProps } from 'recharts';
import { Card, MethodPopover } from '../../components/common';
import { formatPts } from '../../services/format';
import { COLORS } from '../../theme';
import type { SentimentBreakdown, ToneTrendPoint, TrendPoint } from '../../types';

// --------------------------------------------------------------------------- //
//  ToneTrendPanel — the Tone page's time dimension.                           //
//                                                                             //
//  Primary series (when the snapshot carries it): the per-day per-tier       //
//  toneTrend — news vs officials vs public net tone, day by day, with        //
//  low-sample days rendered as gaps. The daily GOP net-favorability series   //
//  is the second view behind a toggle. A weekday-rhythm bar strip from       //
//  byDayOfWeek sits below. On pre-toneTrend cached snapshots the GOP series  //
//  renders alone, exactly as it did before Phase 2a.                         //
// --------------------------------------------------------------------------- //

interface ToneTrendPanelProps {
    toneTrend: ToneTrendPoint[] | null | undefined;
    gopTrend: TrendPoint[] | null | undefined;
    byDayOfWeek: SentimentBreakdown[] | undefined;
}

// Same tier colors as the divergence panel's dots so the two visuals read
// as one system (news = neutral gray, officials = ink blue, public = ochre).
const TIER_SERIES = [
    { key: 'news', label: 'News', color: COLORS.neutral },
    { key: 'officials', label: 'Officials', color: COLORS.accent },
    { key: 'public', label: 'Public', color: COLORS.warning },
] as const;

function trendTooltip({ payload, label }: TooltipProps<number, string>) {
    if (!payload || payload.length === 0) return null;
    const value = Number(payload[0].value ?? 0);
    return (
        <div className="chart-tooltip">
            <div className="chart-tooltip-label">{String(label ?? '')}</div>
            <div className="chart-tooltip-value">{formatPts(value)}</div>
        </div>
    );
}

/** "2026-07-04" → "Jul 4" for axis ticks. */
function shortDate(iso: string): string {
    const m = /^\d{4}-(\d{2})-(\d{2})$/.exec(iso);
    if (!m) return iso;
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    return `${months[Number(m[1]) - 1]} ${Number(m[2])}`;
}

// Flat row shape recharts can key directly: {date, news: net|null, ...}
// plus volumes for the tooltip.
interface TierTrendRow {
    date: string;
    news: number | null;
    officials: number | null;
    public: number | null;
    volumes: Record<string, number>;
}

function toTierRows(trend: ToneTrendPoint[]): TierTrendRow[] {
    return trend.map((p) => ({
        date: p.date,
        news: p.news.net,
        officials: p.officials.net,
        public: p.public.net,
        volumes: {
            news: p.news.volume,
            officials: p.officials.volume,
            public: p.public.volume,
        },
    }));
}

function tierTooltip({ payload, label }: TooltipProps<number, string>) {
    if (!payload || payload.length === 0) return null;
    const row = payload[0].payload as TierTrendRow;
    return (
        <div className="chart-tooltip">
            <div className="chart-tooltip-label">{String(label ?? '')}</div>
            {TIER_SERIES.map((tier) => {
                const net = row[tier.key];
                const volume = row.volumes[tier.key];
                return (
                    <div key={tier.key} className="chart-tooltip-value" style={{ color: tier.color }}>
                        {tier.label}: {net != null
                            ? `${formatPts(net)} · ${volume} posts`
                            : `low sample (${volume} post${volume === 1 ? '' : 's'})`}
                    </div>
                );
            })}
        </div>
    );
}

function TierTrendChart({ trend }: { trend: ToneTrendPoint[] }) {
    const rows = toTierRows(trend);
    return (
        <div
            role="img"
            aria-label={`Daily net tone by group (news, officials, public), ${rows.length} days`}
        >
            <ResponsiveContainer width="100%" height={220}>
                <LineChart data={rows} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
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
                    {TIER_SERIES.map((tier) => (
                        <Line
                            key={tier.key}
                            type="monotone"
                            dataKey={tier.key}
                            name={tier.label}
                            stroke={tier.color}
                            strokeWidth={2}
                            dot={false}
                            activeDot={{ r: 4, fill: tier.color, strokeWidth: 0 }}
                            // Low-sample days arrive as null — draw a gap, not a
                            // bridge, so suppressed readings never fake a line.
                            connectNulls={false}
                            isAnimationActive={false}
                        />
                    ))}
                    <Legend
                        wrapperStyle={{ fontSize: 11 }}
                        iconType="plainline"
                        iconSize={14}
                    />
                    <Tooltip
                        content={tierTooltip}
                        cursor={{ stroke: 'var(--neutral-300)', strokeDasharray: '2 2' }}
                    />
                </LineChart>
            </ResponsiveContainer>
        </div>
    );
}

function DailyTrendChart({ trend }: { trend: TrendPoint[] }) {
    const gradientId = `tone-trend-${useId().replace(/:/g, '')}`;
    const last = trend[trend.length - 1];

    return (
        <div
            role="img"
            aria-label={`Daily net tone toward the GOP, ${trend.length} days, latest ${formatPts(last?.value ?? null)}`}
        >
            <ResponsiveContainer width="100%" height={200}>
                <AreaChart data={trend} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                    <defs>
                        <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
                            <stop offset="0%" stopColor={COLORS.chartAccent} stopOpacity={0.25} />
                            <stop offset="100%" stopColor={COLORS.chartAccent} stopOpacity={0} />
                        </linearGradient>
                    </defs>
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
                    <Area
                        type="monotone"
                        dataKey="value"
                        stroke={COLORS.chartAccent}
                        strokeWidth={2}
                        fill={`url(#${gradientId})`}
                        activeDot={{ r: 4, fill: COLORS.chartAccent, strokeWidth: 0 }}
                        isAnimationActive={false}
                    />
                    <Tooltip
                        content={trendTooltip}
                        cursor={{ stroke: 'var(--neutral-300)', strokeDasharray: '2 2' }}
                    />
                </AreaChart>
            </ResponsiveContainer>
        </div>
    );
}

// --------------------------------------------------------------------------- //
//  Weekday rhythm strip                                                       //
// --------------------------------------------------------------------------- //

interface WeekdayRow {
    day: string;
    net: number;
    volume: number;
}

function weekdayRows(byDayOfWeek: SentimentBreakdown[]): WeekdayRow[] {
    const rows: WeekdayRow[] = [];
    for (const row of byDayOfWeek) {
        if (!row.day) continue;
        const total = row.positive + row.negative + row.neutral;
        if (total === 0) continue;
        rows.push({
            day: row.day,
            net: Math.round(((row.positive - row.negative) / total) * 1000) / 10,
            volume: row.volume,
        });
    }
    return rows;
}

function weekdayTooltip({ payload }: TooltipProps<number, string>) {
    if (!payload || payload.length === 0) return null;
    const row = payload[0].payload as WeekdayRow;
    return (
        <div className="chart-tooltip">
            <div className="chart-tooltip-label">{row.day}</div>
            <div className="chart-tooltip-value">{formatPts(row.net)}</div>
            <div className="chart-tooltip-value" style={{ fontSize: 11, color: 'var(--neutral-500)' }}>
                {row.volume.toLocaleString()} posts
            </div>
        </div>
    );
}

function WeekdayStrip({ rows }: { rows: WeekdayRow[] }) {
    return (
        <div role="img" aria-label={`Net tone by weekday across ${rows.length} weekdays`}>
            <ResponsiveContainer width="100%" height={96}>
                <BarChart data={rows} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
                    <XAxis
                        dataKey="day"
                        tick={{ fontSize: 11, fill: 'var(--neutral-500)', fontFamily: 'var(--font-mono)' }}
                        axisLine={{ stroke: 'var(--chart-grid)' }}
                        tickLine={false}
                    />
                    <YAxis hide domain={['auto', 'auto']} />
                    <ReferenceLine y={0} stroke="var(--neutral-300)" />
                    <Bar dataKey="net" radius={[3, 3, 0, 0]} isAnimationActive={false} maxBarSize={36}>
                        {rows.map((row) => (
                            <Cell
                                key={row.day}
                                fill={row.net >= 0 ? COLORS.chartPositive : COLORS.chartNegative}
                            />
                        ))}
                    </Bar>
                    <Tooltip
                        content={weekdayTooltip}
                        cursor={{ fill: 'var(--bg-inset)' }}
                    />
                </BarChart>
            </ResponsiveContainer>
        </div>
    );
}

// --------------------------------------------------------------------------- //
//  Panel                                                                      //
// --------------------------------------------------------------------------- //

export function ToneTrendPanel({ toneTrend, gopTrend, byDayOfWeek }: ToneTrendPanelProps) {
    const tierTrend = (toneTrend ?? []).filter((p) => !!p.date);
    const gop = (gopTrend ?? []).filter((p) => Number.isFinite(p.value));
    const weekdays = weekdayRows(byDayOfWeek ?? []);
    const hasTiers = tierTrend.length >= 2;
    const hasGop = gop.length >= 2;
    const [view, setView] = useState<'tiers' | 'gop'>('tiers');
    if (!hasTiers && !hasGop && weekdays.length === 0) return null;

    // Pre-2a cached snapshots carry no toneTrend — the GOP series renders
    // alone with no toggle, exactly the Phase 1 behavior.
    const activeView = hasTiers ? (view === 'gop' && hasGop ? 'gop' : 'tiers') : 'gop';

    return (
        <Card
            title="Tone over time"
            subtitle={activeView === 'tiers'
                ? 'Daily net tone of sampled posts, split by who is talking: news outlets, officials, the public. Gaps are low-sample days we refuse to score.'
                : 'Daily net tone of sampled posts toward the GOP.'}
            headerActions={
                <>
                    {hasTiers && hasGop && (
                        <div className="trend-view-toggle" role="tablist" aria-label="Trend series">
                            <button
                                type="button"
                                role="tab"
                                aria-selected={activeView === 'tiers'}
                                className={activeView === 'tiers' ? 'trend-view-btn trend-view-btn-active' : 'trend-view-btn'}
                                onClick={() => setView('tiers')}
                            >
                                By group
                            </button>
                            <button
                                type="button"
                                role="tab"
                                aria-selected={activeView === 'gop'}
                                className={activeView === 'gop' ? 'trend-view-btn trend-view-btn-active' : 'trend-view-btn'}
                                onClick={() => setView('gop')}
                            >
                                Toward GOP
                            </button>
                        </div>
                    )}
                    <MethodPopover
                        description={
                            '"By group" tracks the net tone of each group\'s own sampled posts per '
                            + 'day on a -100 to +100 scale; a day with too few posts from a group is '
                            + 'suppressed and drawn as a gap, never a zero. "Toward GOP" tracks the '
                            + 'net stance of sampled posts toward Republican-party entities. The '
                            + 'weekday bars show net tone of ALL sampled posts by day of week. All '
                            + 'summarize the posts we collected — samples, not polls.'
                        }
                        limitations={[
                            'Days with few posts swing harder — the lines are not volume-weighted.',
                        ]}
                    />
                </>
            }
        >
            {activeView === 'tiers' && hasTiers ? (
                <TierTrendChart trend={tierTrend} />
            ) : hasGop ? (
                <DailyTrendChart trend={gop} />
            ) : (
                <p className="text-sm text-muted">
                    Not enough daily readings in this window to draw a trend yet.
                </p>
            )}
            {weekdays.length > 0 && (
                <>
                    <div className="eyebrow" style={{ margin: 'var(--space-3) 0 var(--space-1)' }}>
                        Weekday rhythm · all sampled posts
                    </div>
                    <WeekdayStrip rows={weekdays} />
                </>
            )}
        </Card>
    );
}

export default ToneTrendPanel;
