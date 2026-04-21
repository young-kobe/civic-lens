import { useMemo, useState } from 'react';
import type { HeatmapDataPoint } from '../../types';

interface HeatmapProps {
    data?: HeatmapDataPoint[];
    colorScale?: [string, string, string, string];
    cellSize?: number;
    gap?: number;
}

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const HOURS = Array.from({ length: 24 }, (_, i) => i);

/**
 * Day-of-week × hour-of-day cadence heatmap. Cells are bucketed into
 * quartiles of the non-zero distribution so the brightest cells actually
 * pop even on a spiky dataset. Hover surfaces a rich card; empty cells
 * stay silent.
 */
function Heatmap({
    data,
    colorScale = [
        'var(--neutral-100)',
        'rgba(0, 71, 179, 0.18)',
        'rgba(0, 71, 179, 0.48)',
        'var(--chart-accent)',
    ],
    cellSize = 16,
    gap = 2,
}: HeatmapProps) {
    const [active, setActive] = useState<{ day: number; hour: number; value: number } | null>(null);

    const { valueMap, quartiles } = useMemo(() => {
        const map = new Map<string, number>();
        const values: number[] = [];
        if (data) {
            for (const d of data) {
                map.set(`${d.day}-${d.hour}`, d.value);
                if (d.value > 0) values.push(d.value);
            }
        }
        values.sort((a, b) => a - b);
        const q = (p: number) => values.length === 0 ? 0 : values[Math.floor((values.length - 1) * p)];
        return { valueMap: map, quartiles: { q25: q(0.25), q50: q(0.5), q75: q(0.75), max: values[values.length - 1] ?? 0 } };
    }, [data]);

    const getColor = (value: number): string => {
        if (!value) return colorScale[0];
        if (value <= quartiles.q25) return colorScale[1];
        if (value <= quartiles.q75) return colorScale[2];
        return colorScale[3];
    };

    const formatHour = (h: number) => `${String(h).padStart(2, '0')}:00`;
    const total = useMemo(
        () => (data ? data.reduce((sum, d) => sum + d.value, 0) : 0),
        [data],
    );
    const peak = useMemo(() => {
        if (!data || data.length === 0) return null;
        return data.reduce((top, d) => (d.value > top.value ? d : top), data[0]);
    }, [data]);

    return (
        <div style={{ position: 'relative' }}>
            {/* Summary strip */}
            <div className="flex justify-between text-xs text-muted mb-2" style={{ fontVariantNumeric: 'tabular-nums' }}>
                <span>{total.toLocaleString()} posts</span>
                {peak && peak.value > 0 && (
                    <span>
                        Peak: {DAYS[peak.day]} {formatHour(peak.hour)} &middot; {peak.value.toLocaleString()}
                    </span>
                )}
            </div>

            {/* Hour labels row */}
            <div className="flex gap-1 mb-1 text-xs text-muted" style={{ marginLeft: '40px' }}>
                {[0, 6, 12, 18].map(h => (
                    <span key={h} style={{ width: `${(cellSize + gap) * 6}px` }}>
                        {formatHour(h)}
                    </span>
                ))}
            </div>

            {/* Grid */}
            <div onMouseLeave={() => setActive(null)}>
                {DAYS.map((day, dayIndex) => (
                    <div key={day} className="flex items-center gap-1 mb-1">
                        <span
                            className="text-xs text-muted"
                            style={{ width: '36px', textAlign: 'right' }}
                        >
                            {day}
                        </span>
                        <div className="flex gap-1">
                            {HOURS.map(hour => {
                                const value = valueMap.get(`${dayIndex}-${hour}`) || 0;
                                const isActive = active?.day === dayIndex && active?.hour === hour;
                                return (
                                    <div
                                        key={hour}
                                        tabIndex={value > 0 ? 0 : -1}
                                        role={value > 0 ? 'button' : undefined}
                                        aria-label={value > 0 ? `${day} ${formatHour(hour)}: ${value} posts` : undefined}
                                        onMouseEnter={() => value > 0 && setActive({ day: dayIndex, hour, value })}
                                        onFocus={() => value > 0 && setActive({ day: dayIndex, hour, value })}
                                        onBlur={() => setActive(null)}
                                        style={{
                                            width: cellSize,
                                            height: cellSize,
                                            borderRadius: 'var(--radius-sm)',
                                            background: getColor(value),
                                            transition: 'transform var(--transition-fast), box-shadow var(--transition-fast)',
                                            transform: isActive ? 'scale(1.25)' : 'scale(1)',
                                            boxShadow: isActive ? '0 2px 8px rgba(0, 71, 179, 0.35)' : 'none',
                                            cursor: value > 0 ? 'default' : 'default',
                                        }}
                                    />
                                );
                            })}
                        </div>
                    </div>
                ))}
            </div>

            {/* Quartile legend */}
            <div
                className="flex items-center gap-2 mt-3 text-xs text-muted"
                style={{ marginLeft: '40px', fontVariantNumeric: 'tabular-nums' }}
            >
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
                {quartiles.max > 0 && (
                    <span className="ml-auto" title="Color thresholds (quartiles of non-zero cells)">
                        ≤{quartiles.q25} / ≤{quartiles.q75} / ≤{quartiles.max}
                    </span>
                )}
            </div>

            {/* Hover card */}
            {active && (
                <div
                    aria-live="polite"
                    style={{
                        marginTop: 'var(--space-2)',
                        padding: 'var(--space-2) var(--space-3)',
                        borderRadius: 'var(--radius-sm)',
                        background: 'var(--bg-inset)',
                        border: '1px solid var(--neutral-150)',
                        fontSize: 'var(--text-xs)',
                        fontVariantNumeric: 'tabular-nums',
                    }}
                >
                    <strong>{DAYS[active.day]} {formatHour(active.hour)}</strong>
                    {' '}&middot; {active.value.toLocaleString()} posts
                    {total > 0 && (
                        <>
                            {' '}&middot; {((active.value / total) * 100).toFixed(1)}% of window
                        </>
                    )}
                </div>
            )}
        </div>
    );
}

export default Heatmap;
