import { useState } from 'react';
import {
    Bar, BarChart, CartesianGrid, Cell, Line, LineChart, ReferenceLine,
    ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import type { TooltipProps } from 'recharts';
import { Card, MethodPopover } from '../../components/common';
import { formatPts } from '../../services/format';
import { COLORS } from '../../theme';
import type { DailySentiment, DayOfWeekSentiment, TimeOfDaySentiment } from '../../types';

// --------------------------------------------------------------------------- //
//  ToneTrendPanel — the pre-cutover daily line chart is restored as the       //
//  primary view now that SentimentPanelResponse.daily exists (a single       //
//  pooled net-tone series, unlike the old per-tier/per-entity multi-line     //
//  chart, which needs a daily breakout the current contract doesn't carry).  //
//  Day-of-week and time-of-day stay as secondary toggle views.               //
// --------------------------------------------------------------------------- //

type View = 'daily' | 'dayOfWeek' | 'timeOfDay';

interface Row {
    label: string;
    net: number | null;
    volume: number;
}

function toneColor(net: number): string {
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return 'var(--neutral-400)';
}

function barTooltip({ active, payload, label }: TooltipProps<number, string>) {
    if (!active || !payload || payload.length === 0) return null;
    const row = payload[0].payload as Row;
    return (
        <div className="chart-tooltip">
            <div className="chart-tooltip-label">{String(label ?? '')}</div>
            <div className="chart-tooltip-value">
                {row.net != null
                    ? `${formatPts(row.net)} · ${row.volume.toLocaleString()} posts`
                    : `low sample (${row.volume})`}
            </div>
        </div>
    );
}

function ToneBarChart({ rows, ariaLabel }: { rows: Row[]; ariaLabel: string }) {
    return (
        <div role="img" aria-label={ariaLabel}>
            <ResponsiveContainer width="100%" height={220}>
                <BarChart data={rows} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                    <CartesianGrid vertical={false} stroke="var(--chart-grid)" strokeDasharray="2 4" />
                    <XAxis
                        dataKey="label"
                        tick={{ fontSize: 11, fill: 'var(--neutral-500)', fontFamily: 'var(--font-mono)' }}
                        axisLine={{ stroke: 'var(--chart-grid)' }}
                        tickLine={false}
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
                    <Bar dataKey="net" isAnimationActive={false}>
                        {rows.map((r, i) => (
                            <Cell key={i} fill={r.net != null ? toneColor(r.net) : 'var(--neutral-200)'} />
                        ))}
                    </Bar>
                    <Tooltip content={barTooltip} cursor={{ fill: 'var(--neutral-100)' }} />
                </BarChart>
            </ResponsiveContainer>
        </div>
    );
}

/** "2026-07-04" → "Jul 4" for axis ticks, matching the pre-cutover panel. */
function shortDate(iso: string): string {
    const m = /^\d{4}-(\d{2})-(\d{2})$/.exec(iso);
    if (!m) return iso;
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    return `${months[Number(m[1]) - 1]} ${Number(m[2])}`;
}

function dailyTooltip({ active, payload, label }: TooltipProps<number, string>) {
    if (!active || !payload || payload.length === 0) return null;
    const row = payload[0].payload as { date: string; net: number | null; volume: number };
    return (
        <div className="chart-tooltip">
            <div className="chart-tooltip-label">{shortDate(String(label ?? row.date))}</div>
            <div className="chart-tooltip-value">
                {row.net != null
                    ? `${formatPts(row.net)} · ${row.volume.toLocaleString()} posts`
                    : `low sample (${row.volume})`}
            </div>
        </div>
    );
}

function ToneLineChart({ rows, ariaLabel }: { rows: DailySentiment[]; ariaLabel: string }) {
    return (
        <div role="img" aria-label={ariaLabel}>
            <ResponsiveContainer width="100%" height={240}>
                <LineChart data={rows} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
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
                    <Line
                        type="monotone"
                        dataKey="netScore"
                        stroke="var(--accent)"
                        strokeWidth={2}
                        dot={false}
                        activeDot={{ r: 4, fill: 'var(--accent)', strokeWidth: 0 }}
                        // Low-sample days arrive as null netScore -- draw a gap,
                        // not a bridge, so a suppressed reading is never faked.
                        connectNulls={false}
                        isAnimationActive={false}
                    />
                    <Tooltip content={dailyTooltip} cursor={{ stroke: 'var(--neutral-300)', strokeDasharray: '2 2' }} />
                </LineChart>
            </ResponsiveContainer>
        </div>
    );
}

const DAY_ORDER = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const TIME_OF_DAY_ORDER: TimeOfDaySentiment['bucket'][] = ['Morning', 'Afternoon', 'Evening', 'Night'];

const VIEW_LABEL: Record<View, string> = {
    daily: 'Daily',
    dayOfWeek: 'By day of week',
    timeOfDay: 'By time of day',
};

interface ToneTrendPanelProps {
    daily: DailySentiment[];
    byDayOfWeek: DayOfWeekSentiment[];
    byTimeOfDay: TimeOfDaySentiment[];
}

export function ToneTrendPanel({ daily, byDayOfWeek, byTimeOfDay }: ToneTrendPanelProps) {
    const dailyRows = [...daily].sort((a, b) => (a.date < b.date ? -1 : 1));
    const dayRows: Row[] = DAY_ORDER
        .map((day) => byDayOfWeek.find((d) => d.day === day))
        .filter((d): d is DayOfWeekSentiment => !!d)
        .map((d) => ({ label: d.day.slice(0, 3), net: d.netScore, volume: d.volume }));
    const timeRows: Row[] = TIME_OF_DAY_ORDER
        .map((bucket) => byTimeOfDay.find((t) => t.bucket === bucket))
        .filter((t): t is TimeOfDaySentiment => !!t)
        .map((t) => ({ label: t.bucket, net: t.netScore, volume: t.volume }));

    const hasDaily = dailyRows.some((r) => r.volume > 0);
    const hasDay = dayRows.some((r) => r.volume > 0);
    const hasTime = timeRows.some((r) => r.volume > 0);
    const [view, setView] = useState<View>('daily');

    if (!hasDaily && !hasDay && !hasTime) return null;

    const available: View[] = [
        ...(hasDaily ? ['daily' as const] : []),
        ...(hasDay ? ['dayOfWeek' as const] : []),
        ...(hasTime ? ['timeOfDay' as const] : []),
    ];
    const activeView: View = available.includes(view) ? view : available[0];

    return (
        <Card
            title="Tone over time"
            subtitle="Net tone of sampled posts, day by day, with day-of-week and time-of-day breakdowns as secondary views."
            headerActions={
                <>
                    {available.length > 1 && (
                        <div className="trend-view-toggle" role="tablist" aria-label="Trend breakdown">
                            {available.map((v) => (
                                <button
                                    key={v}
                                    type="button"
                                    role="tab"
                                    aria-selected={activeView === v}
                                    className={activeView === v ? 'trend-view-btn trend-view-btn-active' : 'trend-view-btn'}
                                    onClick={() => setView(v)}
                                >
                                    {VIEW_LABEL[v]}
                                </button>
                            ))}
                        </div>
                    )}
                    <MethodPopover
                        description={
                            'Net tone = positive minus negative share of sampled posts published in each '
                            + 'bucket, on a -100 to +100 scale. Buckets with few posts still render their '
                            + 'true reading; volume rides along in the tooltip so a thin bucket is easy to '
                            + 'spot. Summarizes the posts we collected — a sample, not a poll.'
                        }
                    />
                </>
            }
        >
            {activeView === 'daily'
                ? <ToneLineChart rows={dailyRows} ariaLabel="Net tone by day" />
                : <ToneBarChart rows={activeView === 'dayOfWeek' ? dayRows : timeRows} ariaLabel={`Net tone ${VIEW_LABEL[activeView]}`} />}
        </Card>
    );
}

export default ToneTrendPanel;
