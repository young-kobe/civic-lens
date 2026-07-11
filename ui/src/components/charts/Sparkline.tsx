import { useId } from 'react';
import { AreaChart, Area, XAxis, ResponsiveContainer, Tooltip, TooltipProps } from 'recharts';
import { COLORS } from '../../theme';
import type { ChartDataPoint } from '../../types';

interface SparklineProps {
    data: ChartDataPoint[];
    dataKey?: string;
    xKey?: string;
    color?: string;
    height?: number;
    showTooltip?: boolean;
    /** Render a readable date axis (first/last + a few ticks). Off by
     *  default — sparklines are usually context, but a page-scale chart
     *  (e.g. the narrative timeline) needs real dates. */
    showXAxis?: boolean;
    ariaLabel?: string;
}

/**
 * Mini trend line with a subtle gradient area fill underneath.
 * Tooltip shows label (date if present), value, and delta vs. the first point.
 */
function Sparkline({
    data,
    dataKey = 'value',
    xKey = 'date',
    color = COLORS.chartAccent,
    height = 40,
    showTooltip = true,
    showXAxis = false,
    ariaLabel,
}: SparklineProps) {
    const gradientId = `spark-${useId().replace(/:/g, '')}`;

    if (!data || data.length === 0) {
        return (
            <div
                role="img"
                aria-label={ariaLabel ? `${ariaLabel}: no data` : 'No data'}
                style={{
                    height,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    color: 'var(--neutral-400)',
                    fontSize: 'var(--text-xs)',
                }}
            >
                No data
            </div>
        );
    }

    const first = Number(data[0]?.[dataKey] ?? 0);
    const last = Number(data[data.length - 1]?.[dataKey] ?? 0);
    const describedLabel = ariaLabel ?? `Trend, ${data.length} points, latest ${last}`;
    const hasXKey = data[0]?.[xKey] != null;

    const renderTooltip = ({ payload, label }: TooltipProps<number, string>) => {
        if (!payload || payload.length === 0) return null;
        const value = Number(payload[0].value ?? 0);
        const delta = value - first;
        const deltaClass = delta > 0 ? 'tick-up' : delta < 0 ? 'tick-down' : 'tick-flat';
        const deltaSign = delta > 0 ? '+' : '';
        return (
            <div className="chart-tooltip">
                {label != null && label !== '' && <div className="chart-tooltip-label">{String(label)}</div>}
                <div className="chart-tooltip-value">{value}</div>
                <div className={`chart-tooltip-value ${deltaClass}`} style={{ fontSize: 11 }}>
                    {deltaSign}{delta.toFixed(2)} vs. start
                </div>
            </div>
        );
    };

    return (
        <div role="img" aria-label={describedLabel} style={{ width: '100%' }}>
            <ResponsiveContainer width="100%" height={height}>
                <AreaChart data={data} margin={{ top: 2, right: 2, left: 2, bottom: 2 }}>
                    <defs>
                        <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
                            <stop offset="0%" stopColor={color} stopOpacity={0.3} />
                            <stop offset="100%" stopColor={color} stopOpacity={0} />
                        </linearGradient>
                    </defs>
                    {hasXKey && (
                        <XAxis
                            dataKey={xKey}
                            hide={!showXAxis}
                            tick={{ fontSize: 11, fill: 'var(--neutral-500)' }}
                            tickLine={false}
                            axisLine={{ stroke: 'var(--chart-grid)' }}
                            minTickGap={48}
                        />
                    )}
                    <Area
                        type="monotone"
                        dataKey={dataKey}
                        stroke={color}
                        strokeWidth={1.75}
                        fill={`url(#${gradientId})`}
                        activeDot={{ r: 3, fill: color, strokeWidth: 0 }}
                        isAnimationActive={false}
                    />
                    {showTooltip && (
                        <Tooltip
                            content={renderTooltip}
                            cursor={{ stroke: 'var(--neutral-300)', strokeDasharray: '2 2' }}
                        />
                    )}
                </AreaChart>
            </ResponsiveContainer>
        </div>
    );
}

export default Sparkline;
