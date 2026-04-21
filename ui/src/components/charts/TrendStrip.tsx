import { useId } from 'react';
import {
    AreaChart, Area, XAxis, YAxis,
    ResponsiveContainer, Tooltip, ReferenceDot, TooltipProps, CartesianGrid,
} from 'recharts';
import type { TrendPoint, TrendAnnotation } from '../../types';

interface TrendStripProps {
    data: TrendPoint[];
    dataKey?: string;
    xKey?: string;
    height?: number;
    annotations?: TrendAnnotation[];
    color?: string;
    /** Unit label for tooltip values (e.g. '%', 'docs'). */
    unit?: string;
}

function TrendStrip({
    data,
    dataKey = 'value',
    xKey = 'date',
    height = 160,
    annotations = [],
    color = 'var(--chart-accent)',
    unit = '',
}: TrendStripProps) {
    const gradientId = `trend-${useId().replace(/:/g, '')}`;

    if (!data || data.length === 0) {
        return (
            <div
                style={{
                    height,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    color: 'var(--neutral-400)',
                    fontSize: 'var(--text-xs)',
                }}
            >
                No trend data available
            </div>
        );
    }

    const first = Number(data[0]?.[dataKey as keyof TrendPoint] ?? 0);
    const last = Number(data[data.length - 1]?.[dataKey as keyof TrendPoint] ?? 0);
    const delta = last - first;
    const values = data.map(d => Number(d[dataKey as keyof TrendPoint] ?? 0));
    const minV = Math.min(...values);
    const maxV = Math.max(...values);
    const deltaClass = delta > 0 ? 'tick-up' : delta < 0 ? 'tick-down' : 'tick-flat';
    const deltaArrow = delta > 0 ? '▲' : delta < 0 ? '▼' : '•';

    const annotationPoints = annotations.map(a => {
        const point = data.find(d => d[xKey as keyof TrendPoint] === a.x);
        return point ? { ...a, value: point[dataKey as keyof TrendPoint] as number } : null;
    }).filter((p): p is TrendAnnotation & { value: number } => p !== null);

    const renderTooltip = ({ payload, label }: TooltipProps<number, string>) => {
        if (!payload || payload.length === 0) return null;
        const value = Number(payload[0].value ?? 0);
        const annotation = annotations.find(a => a.x === label);
        const pointDelta = value - first;
        const signClass = pointDelta > 0 ? 'tick-up' : pointDelta < 0 ? 'tick-down' : 'tick-flat';
        const sign = pointDelta > 0 ? '+' : '';
        return (
            <div className="chart-tooltip">
                <div className="chart-tooltip-label">{String(label)}</div>
                <div className="chart-tooltip-value">
                    {value.toFixed(value % 1 === 0 ? 0 : 2)}{unit}
                </div>
                <div className={`chart-tooltip-value ${signClass}`} style={{ fontSize: 10 }}>
                    {sign}{pointDelta.toFixed(2)} vs. start
                </div>
                {annotation && (
                    <div className="text-xs text-muted mt-1" style={{ maxWidth: 200 }}>{annotation.label}</div>
                )}
            </div>
        );
    };

    return (
        <div>
            {/* Delta summary strip */}
            <div className="flex items-center justify-between mb-2 text-xs" style={{ fontVariantNumeric: 'tabular-nums' }}>
                <span className="text-muted uppercase" style={{ letterSpacing: '0.08em', fontWeight: 600 }}>
                    {data.length} points
                </span>
                <span className={deltaClass} style={{ fontWeight: 600 }}>
                    <span aria-hidden style={{ marginRight: 4 }}>{deltaArrow}</span>
                    {delta >= 0 ? '+' : ''}{delta.toFixed(2)}{unit} &middot; range {minV.toFixed(1)}–{maxV.toFixed(1)}{unit}
                </span>
            </div>

            <ResponsiveContainer width="100%" height={height}>
                <AreaChart data={data} margin={{ top: 12, right: 12, bottom: 12, left: 0 }}>
                    <defs>
                        <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
                            <stop offset="0%" stopColor={color} stopOpacity={0.32} />
                            <stop offset="100%" stopColor={color} stopOpacity={0} />
                        </linearGradient>
                    </defs>
                    <CartesianGrid stroke="var(--chart-grid)" vertical={false} />
                    <XAxis
                        dataKey={xKey}
                        tick={{ fontSize: 10, fill: 'var(--neutral-500)' }}
                        axisLine={{ stroke: 'var(--neutral-200)' }}
                        tickLine={false}
                        interval="preserveStartEnd"
                    />
                    <YAxis
                        tick={{ fontSize: 10, fill: 'var(--neutral-500)' }}
                        axisLine={false}
                        tickLine={false}
                        width={40}
                    />
                    <Tooltip
                        content={renderTooltip}
                        cursor={{ stroke: 'var(--neutral-400)', strokeDasharray: '2 2' }}
                    />
                    <Area
                        type="monotone"
                        dataKey={dataKey}
                        stroke={color}
                        strokeWidth={2}
                        fill={`url(#${gradientId})`}
                        activeDot={{ r: 4, fill: color, strokeWidth: 2, stroke: 'white' }}
                        isAnimationActive={false}
                    />
                    {annotationPoints.map((point, i) => (
                        <ReferenceDot
                            key={i}
                            x={point.x}
                            y={point.value}
                            r={5}
                            fill="white"
                            stroke={color}
                            strokeWidth={2}
                        />
                    ))}
                </AreaChart>
            </ResponsiveContainer>
        </div>
    );
}

export default TrendStrip;
