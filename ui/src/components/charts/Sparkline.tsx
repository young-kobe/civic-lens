import { LineChart, Line, ResponsiveContainer, Tooltip, TooltipProps } from 'recharts';
import type { ChartDataPoint } from '../../types';

interface SparklineProps {
    data: ChartDataPoint[];
    dataKey?: string;
    color?: string;
    height?: number;
    showTooltip?: boolean;
    /** Accessible label summarising what the spark represents. */
    ariaLabel?: string;
}

/**
 * Sparkline - Mini trend line chart.
 */
function Sparkline({
    data,
    dataKey = 'value',
    color = 'var(--accent)',
    height = 40,
    showTooltip = true,
    ariaLabel,
}: SparklineProps) {
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
                    fontSize: 'var(--text-xs)'
                }}
            >
                No data
            </div>
        );
    }

    const describedLabel = ariaLabel
        ?? `Trend sparkline, ${data.length} points, latest value ${data[data.length - 1]?.[dataKey as keyof ChartDataPoint] ?? ''}`;

    const renderTooltip = ({ payload }: TooltipProps<number, string>) => {
        if (!payload || payload.length === 0) return null;
        const point = payload[0];
        return (
            <div className="chart-tooltip">
                <div className="chart-tooltip-value">{point.value}</div>
            </div>
        );
    };

    return (
        <div role="img" aria-label={describedLabel} style={{ width: '100%' }}>
            <ResponsiveContainer width="100%" height={height}>
                <LineChart data={data}>
                    <Line
                        type="monotone"
                        dataKey={dataKey}
                        stroke={color}
                        strokeWidth={2}
                        dot={false}
                    />
                    {showTooltip && <Tooltip content={renderTooltip} />}
                </LineChart>
            </ResponsiveContainer>
        </div>
    );
}

export default Sparkline;
