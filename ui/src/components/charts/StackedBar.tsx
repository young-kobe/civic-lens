import { BarChart, Bar, XAxis, YAxis, ResponsiveContainer, Tooltip, Cell, TooltipProps } from 'recharts';
import type { SourceMixItem } from '../../types';

interface StackedBarProps {
    data: SourceMixItem[];
    height?: number;
    layout?: 'horizontal' | 'vertical';
}

/**
 * StackedBar - Source mix chart showing distribution by outlet type.
 */
function StackedBar({
    data,
    height = 200,
    layout = 'horizontal'
}: StackedBarProps) {
    // Colors for different source types
    const COLORS: Record<string, string> = {
        news: 'var(--neutral-700)',
        reddit: 'var(--accent)',
        social: 'var(--neutral-400)',
        other: 'var(--neutral-300)',
    };

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
                No data
            </div>
        );
    }

    const renderTooltip = ({ payload }: TooltipProps<number, string>) => {
        if (!payload || payload.length === 0) return null;
        const point = payload[0];
        return (
            <div className="chart-tooltip">
                <div className="chart-tooltip-label">{point.payload?.name}</div>
                <div className="chart-tooltip-value">{point.value} items</div>
            </div>
        );
    };

    return (
        <ResponsiveContainer width="100%" height={height}>
            <BarChart
                data={data}
                layout={layout === 'vertical' ? 'vertical' : 'horizontal'}
                margin={{ top: 0, right: 0, bottom: 0, left: 0 }}
            >
                {layout === 'vertical' ? (
                    <>
                        <XAxis type="number" hide />
                        <YAxis
                            dataKey="name"
                            type="category"
                            width={80}
                            tick={{ fontSize: 12, fill: 'var(--neutral-500)' }}
                            axisLine={false}
                            tickLine={false}
                        />
                    </>
                ) : (
                    <>
                        <XAxis
                            dataKey="name"
                            tick={{ fontSize: 12, fill: 'var(--neutral-500)' }}
                            axisLine={false}
                            tickLine={false}
                        />
                        <YAxis hide />
                    </>
                )}
                <Tooltip content={renderTooltip} />
                <Bar
                    dataKey="value"
                    radius={[4, 4, 4, 4]}
                >
                    {data.map((entry, index) => (
                        <Cell
                            key={`cell-${index}`}
                            fill={COLORS[entry.type] || COLORS.other}
                        />
                    ))}
                </Bar>
            </BarChart>
        </ResponsiveContainer>
    );
}

export default StackedBar;
