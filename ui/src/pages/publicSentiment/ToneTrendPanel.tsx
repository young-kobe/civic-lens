import { useId } from 'react';
import {
    Area, AreaChart, Bar, BarChart, Cell, ReferenceLine, ResponsiveContainer,
    Tooltip, XAxis, YAxis,
} from 'recharts';
import type { TooltipProps } from 'recharts';
import { Card, MethodPopover } from '../../components/common';
import { formatPts } from '../../services/format';
import { COLORS } from '../../theme';
import type { SentimentBreakdown, TrendPoint } from '../../types';

// --------------------------------------------------------------------------- //
//  ToneTrendPanel — the Tone page's time dimension.                           //
//                                                                             //
//  Renders the daily GOP net-favorability series (gopTrend) as a page-scale   //
//  area chart with a zero reference line, plus a weekday-rhythm bar strip     //
//  from byDayOfWeek. Both series were computed by the aggregator all along    //
//  and sat unrendered in the snapshot; this panel is UI-only. When the        //
//  backend ships per-tier daily tone (toneTrend), it lands here as the        //
//  primary series and gopTrend becomes a toggle.                              //
// --------------------------------------------------------------------------- //

interface ToneTrendPanelProps {
    gopTrend: TrendPoint[] | null | undefined;
    byDayOfWeek: SentimentBreakdown[] | undefined;
}

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
                        tick={{ fontSize: 10, fill: 'var(--neutral-500)', fontFamily: 'var(--font-mono)' }}
                        axisLine={{ stroke: 'var(--chart-grid)' }}
                        tickLine={false}
                        minTickGap={24}
                    />
                    <YAxis
                        domain={[-100, 100]}
                        ticks={[-100, -50, 0, 50, 100]}
                        tick={{ fontSize: 10, fill: 'var(--neutral-500)', fontFamily: 'var(--font-mono)' }}
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
            <div className="chart-tooltip-value" style={{ fontSize: 10, color: 'var(--neutral-500)' }}>
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
                        tick={{ fontSize: 10, fill: 'var(--neutral-500)', fontFamily: 'var(--font-mono)' }}
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

export function ToneTrendPanel({ gopTrend, byDayOfWeek }: ToneTrendPanelProps) {
    const trend = (gopTrend ?? []).filter((p) => Number.isFinite(p.value));
    const weekdays = weekdayRows(byDayOfWeek ?? []);
    if (trend.length < 2 && weekdays.length === 0) return null;

    return (
        <Card
            title="Tone over time"
            subtitle="Daily net tone of sampled posts toward the GOP, with the weekday rhythm of the whole sample."
            headerActions={
                <MethodPopover
                    description={
                        'The line tracks the net stance of sampled posts toward Republican-party '
                        + 'entities, day by day, on a -100 to +100 scale — 0 means positive and '
                        + 'negative mentions balance out. The weekday bars show net tone of ALL '
                        + 'sampled posts by day of week. Both summarize the posts we collected — '
                        + 'samples, not polls.'
                    }
                    limitations={[
                        'Days with few posts swing harder — the line is not volume-weighted.',
                        'Per-group daily tone (news vs officials vs public) is not computed yet; this is the one daily series the pipeline produces today.',
                    ]}
                />
            }
        >
            {trend.length >= 2 ? (
                <DailyTrendChart trend={trend} />
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
