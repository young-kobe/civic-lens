import { useMemo, useState } from 'react';
import { COLORS } from '../../theme';
import type { PostingCadenceCell } from '../../types';

// --------------------------------------------------------------------------- //
//  PostingCadenceHeatmap — day-of-week x hour-of-day bot-flagged posting-      //
//  cadence grid. Restores the pre-cutover Bot Detector's heatmap (dropped     //
//  pre-cutover because the backend only emitted day:0 for every row --       //
//  see the removed `Heatmap` component, git 77dd37d). postingCadenceGrid now //
//  carries real (dayOfWeek, hour) UTC buckets (all 7*24=168 cells present    //
//  even at count 0), so the geometry is restored here rather than left      //
//  dropped. Self-contained (no dependency on the deleted shared component). //
// --------------------------------------------------------------------------- //

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const HOURS = Array.from({ length: 24 }, (_, i) => i);
const COLOR_SCALE = [
    'var(--neutral-100)',
    'rgba(35, 80, 138, 0.18)',
    'rgba(35, 80, 138, 0.48)',
    COLORS.chartAccent,
];

function formatHour(h: number): string {
    return `${String(h).padStart(2, '0')}:00`;
}

export function PostingCadenceHeatmap({ cells }: { cells: PostingCadenceCell[] }) {
    const [active, setActive] = useState<PostingCadenceCell | null>(null);

    const { valueMap, quartiles, total, peak } = useMemo(() => {
        const map = new Map<string, number>();
        const values: number[] = [];
        for (const c of cells) {
            map.set(`${c.dayOfWeek}-${c.hour}`, c.docCount);
            if (c.docCount > 0) values.push(c.docCount);
        }
        values.sort((a, b) => a - b);
        const q = (p: number) => (values.length === 0 ? 0 : values[Math.floor((values.length - 1) * p)]);
        const totalCount = cells.reduce((s, c) => s + c.docCount, 0);
        const peakCell = cells.reduce<PostingCadenceCell | null>(
            (top, c) => (top == null || c.docCount > top.docCount ? c : top),
            null,
        );
        return {
            valueMap: map,
            quartiles: { q25: q(0.25), q75: q(0.75) },
            total: totalCount,
            peak: peakCell && peakCell.docCount > 0 ? peakCell : null,
        };
    }, [cells]);

    if (total === 0) {
        return (
            <p className="text-sm text-muted" style={{ margin: 0 }}>
                No bot-flagged posts in this window to chart by day and hour.
            </p>
        );
    }

    const colorFor = (value: number): string => {
        if (!value) return COLOR_SCALE[0];
        if (value <= quartiles.q25) return COLOR_SCALE[1];
        if (value <= quartiles.q75) return COLOR_SCALE[2];
        return COLOR_SCALE[3];
    };

    return (
        <div style={{ position: 'relative' }}>
            <div
                className="flex justify-between text-xs text-muted mb-2"
                style={{ fontVariantNumeric: 'tabular-nums' }}
            >
                <span>{total.toLocaleString()} bot-flagged posts</span>
                {peak && (
                    <span>
                        Peak: {DAYS[peak.dayOfWeek]} {formatHour(peak.hour)} UTC · {peak.docCount.toLocaleString()}
                    </span>
                )}
            </div>
            <div style={{ display: 'flex', gap: 2 }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 2, marginRight: 4 }}>
                    <div style={{ height: 12 }} aria-hidden />
                    {DAYS.map((d) => (
                        <span
                            key={d}
                            className="text-xs text-muted"
                            style={{ height: 12, lineHeight: '12px' }}
                        >
                            {d}
                        </span>
                    ))}
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 2, flex: 1, overflowX: 'auto' }}>
                    <div style={{ display: 'flex', gap: 2 }}>
                        {HOURS.map((h) => (
                            <span
                                key={h}
                                className="text-xs text-muted"
                                style={{ width: 12, textAlign: 'center', fontSize: '9px' }}
                            >
                                {h % 3 === 0 ? h : ''}
                            </span>
                        ))}
                    </div>
                    {DAYS.map((_, dayOfWeek) => (
                        <div key={dayOfWeek} style={{ display: 'flex', gap: 2 }}>
                            {HOURS.map((hour) => {
                                const value = valueMap.get(`${dayOfWeek}-${hour}`) ?? 0;
                                return (
                                    <button
                                        key={hour}
                                        type="button"
                                        onMouseEnter={() => setActive({ dayOfWeek, hour, docCount: value })}
                                        onMouseLeave={() => setActive(null)}
                                        onFocus={() => setActive({ dayOfWeek, hour, docCount: value })}
                                        onBlur={() => setActive(null)}
                                        aria-label={`${DAYS[dayOfWeek]} ${formatHour(hour)} UTC: ${value.toLocaleString()} bot-flagged posts`}
                                        style={{
                                            width: 12,
                                            height: 12,
                                            padding: 0,
                                            border: 'none',
                                            background: colorFor(value),
                                            cursor: value > 0 ? 'pointer' : 'default',
                                        }}
                                    />
                                );
                            })}
                        </div>
                    ))}
                </div>
            </div>
            {active && active.docCount > 0 && (
                <div className="chart-tooltip" style={{ position: 'absolute', top: -8, right: 0 }}>
                    <div className="chart-tooltip-label">{DAYS[active.dayOfWeek]} {formatHour(active.hour)} UTC</div>
                    <div className="chart-tooltip-value">{active.docCount.toLocaleString()} bot-flagged posts</div>
                </div>
            )}
        </div>
    );
}

export default PostingCadenceHeatmap;
