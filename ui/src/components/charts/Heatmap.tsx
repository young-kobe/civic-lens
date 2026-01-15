import type { HeatmapDataPoint } from '../../types';

interface HeatmapProps {
    data?: HeatmapDataPoint[];
    colorScale?: [string, string, string];
    cellSize?: number;
    gap?: number;
}

/**
 * Heatmap - Posting cadence or activity heatmap.
 */
function Heatmap({
    data,
    colorScale = ['var(--neutral-100)', 'var(--accent-light)', 'var(--accent)'],
    cellSize = 16,
    gap = 2
}: HeatmapProps) {
    const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
    const hours = Array.from({ length: 24 }, (_, i) => i);

    // Build lookup map
    const valueMap = new Map<string, number>();
    let maxValue = 0;
    if (data) {
        data.forEach(d => {
            const key = `${d.day}-${d.hour}`;
            valueMap.set(key, d.value);
            if (d.value > maxValue) maxValue = d.value;
        });
    }

    const getColor = (value: number): string => {
        if (!value || maxValue === 0) return colorScale[0];
        const intensity = value / maxValue;
        if (intensity < 0.33) return colorScale[0];
        if (intensity < 0.66) return colorScale[1];
        return colorScale[2];
    };

    return (
        <div>
            {/* Hour labels */}
            <div
                className="flex gap-1 mb-1 text-xs text-muted"
                style={{ marginLeft: '40px' }}
            >
                {[0, 6, 12, 18].map(h => (
                    <span key={h} style={{ width: `${(cellSize + gap) * 6}px` }}>
                        {h}:00
                    </span>
                ))}
            </div>

            {/* Grid */}
            {days.map((day, dayIndex) => (
                <div key={day} className="flex items-center gap-1 mb-1">
                    <span
                        className="text-xs text-muted"
                        style={{ width: '36px', textAlign: 'right' }}
                    >
                        {day}
                    </span>
                    <div className="flex gap-1">
                        {hours.map(hour => {
                            const value = valueMap.get(`${dayIndex}-${hour}`) || 0;
                            return (
                                <div
                                    key={hour}
                                    style={{
                                        width: cellSize,
                                        height: cellSize,
                                        borderRadius: 'var(--radius-sm)',
                                        background: getColor(value),
                                        transition: 'background var(--transition-fast)',
                                    }}
                                    title={`${day} ${hour}:00 - ${value} posts`}
                                />
                            );
                        })}
                    </div>
                </div>
            ))}

            {/* Legend */}
            <div className="flex items-center gap-2 mt-3 text-xs text-muted" style={{ marginLeft: '40px' }}>
                <span>Less</span>
                {colorScale.map((color, i) => (
                    <div
                        key={i}
                        style={{
                            width: cellSize,
                            height: cellSize,
                            borderRadius: 'var(--radius-sm)',
                            background: color,
                        }}
                    />
                ))}
                <span>More</span>
            </div>
        </div>
    );
}

export default Heatmap;
