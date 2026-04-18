import { LineChart, Line, XAxis, YAxis, ResponsiveContainer, Tooltip, ReferenceDot, TooltipProps } from 'recharts';
import type { TrendPoint, TrendAnnotation } from '../../types';

interface TrendStripProps {
    data: TrendPoint[];
    dataKey?: string;
    xKey?: string;
    height?: number;
    annotations?: TrendAnnotation[];
    color?: string;
}

/**
 * TrendStrip - Annotated trend line with inflection points.
 */
function TrendStrip({
    data,
    dataKey = 'value',
    xKey = 'date',
    height = 160,
    annotations = [],
    color = 'var(--accent)'
}: TrendStripProps) {
    if (!data || data.length === 0) {
        return (
            <div
                style={{
                    height,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    color: 'var(--neutral-400)',
                    fontSize: 'var(--text-xs)'
                }}
            >
                No trend data available
            </div>
        );
    }

    // Find annotation points in data
    const annotationPoints = annotations.map(a => {
        const point = data.find(d => d[xKey as keyof TrendPoint] === a.x);
        return point ? { ...a, value: point[dataKey as keyof TrendPoint] as number } : null;
    }).filter((p): p is TrendAnnotation & { value: number } => p !== null);

    const renderTooltip = ({ payload, label }: TooltipProps<number, string>) => {
        if (!payload || payload.length === 0) return null;
        const point = payload[0];
        const annotation = annotations.find(a => a.x === label);
        return (
            <div className="chart-tooltip">
                <div className="chart-tooltip-label">{label}</div>
                <div className="chart-tooltip-value">{point.value}</div>
                {annotation && (
                    <div className="text-xs text-muted mt-1">{annotation.label}</div>
                )}
            </div>
        );
    };

    return (
        <ResponsiveContainer width="100%" height={height}>
            <LineChart data={data} margin={{ top: 20, right: 20, bottom: 20, left: 0 }}>
                <XAxis
                    dataKey={xKey}
                    tick={{ fontSize: 11, fill: 'var(--neutral-400)' }}
                    axisLine={{ stroke: 'var(--neutral-200)' }}
                    tickLine={false}
                />
                <YAxis
                    tick={{ fontSize: 11, fill: 'var(--neutral-400)' }}
                    axisLine={false}
                    tickLine={false}
                    width={40}
                />
                <Tooltip content={renderTooltip} />
                <Line
                    type="monotone"
                    dataKey={dataKey}
                    stroke={color}
                    strokeWidth={2}
                    dot={false}
                    activeDot={{ r: 4, fill: color }}
                />
                {annotationPoints.map((point, i) => (
                    <ReferenceDot
                        key={i}
                        x={point.x}
                        y={point.value}
                        r={6}
                        fill="white"
                        stroke={color}
                        strokeWidth={2}
                    />
                ))}
            </LineChart>
        </ResponsiveContainer>
    );
}

export default TrendStrip;
