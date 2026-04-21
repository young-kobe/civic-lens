import { useState } from 'react';

interface SentimentBarProps {
    positive?: number;
    negative?: number;
    neutral?: number;
    height?: number;
    showLabels?: boolean;
    /** Color scheme: 'sentiment' (green/grey/red) or 'political' (blue/grey/red) */
    colorScheme?: 'sentiment' | 'political';
}

type SegKey = 'negative' | 'neutral' | 'positive';

/**
 * Horizontal 3-segment bar used for sentiment / favorability splits.
 * Segments use gradient fills and surface an in-chart hover card on hover
 * rather than relying on the browser's native title tooltip.
 */
function SentimentBar({
    positive = 0,
    negative = 0,
    neutral = 0,
    height = 40,
    showLabels = true,
    colorScheme = 'sentiment',
}: SentimentBarProps) {
    const [active, setActive] = useState<SegKey | null>(null);
    const total = positive + negative + neutral || 1;

    const palette = colorScheme === 'political'
        ? {
            positive: { gradient: 'linear-gradient(135deg, #1e4fd1 0%, #3d82e6 100%)', solid: '#2563eb', label: 'Supportive' },
            neutral: { gradient: 'linear-gradient(135deg, #8b919e 0%, #c0c4cd 100%)', solid: '#9ca3af', label: 'Neutral' },
            negative: { gradient: 'linear-gradient(135deg, #a0160a 0%, #e0261a 100%)', solid: '#dc2626', label: 'Opposed' },
        }
        : {
            positive: { gradient: 'linear-gradient(135deg, #00a358 0%, #3ec37d 100%)', solid: '#16a34a', label: 'Favorable' },
            neutral: { gradient: 'linear-gradient(135deg, #8b919e 0%, #c0c4cd 100%)', solid: '#9ca3af', label: 'Neutral' },
            negative: { gradient: 'linear-gradient(135deg, #a0160a 0%, #e0261a 100%)', solid: '#dc2626', label: 'Unfavorable' },
        };

    const segments: { key: SegKey; value: number; gradient: string; solid: string; label: string }[] = [
        { key: 'negative', value: negative, ...palette.negative },
        { key: 'neutral', value: neutral, ...palette.neutral },
        { key: 'positive', value: positive, ...palette.positive },
    ];

    return (
        <div>
            <div
                style={{
                    display: 'flex',
                    height,
                    borderRadius: 'var(--radius-md)',
                    overflow: 'hidden',
                    background: 'var(--bg-inset)',
                    boxShadow: 'inset 0 1px 0 rgba(15, 20, 35, 0.04)',
                }}
                onMouseLeave={() => setActive(null)}
            >
                {segments.map((seg) => {
                    const pct = (seg.value / total) * 100;
                    if (pct === 0) return null;
                    const isActive = active === seg.key;
                    return (
                        <div
                            key={seg.key}
                            className={`seg ${isActive ? 'is-active' : ''}`}
                            role="button"
                            tabIndex={0}
                            aria-label={`${seg.label}: ${seg.value.toLocaleString()} (${pct.toFixed(1)}%)`}
                            onMouseEnter={() => setActive(seg.key)}
                            onFocus={() => setActive(seg.key)}
                            onBlur={() => setActive(null)}
                            style={{
                                width: `${pct}%`,
                                background: seg.gradient,
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                color: 'white',
                                fontSize: height >= 28 ? '11px' : '10px',
                                fontWeight: 700,
                                letterSpacing: '0.04em',
                                textShadow: '0 1px 2px rgba(0, 0, 0, 0.2)',
                                cursor: 'default',
                            }}
                        >
                            {pct >= 12 && height >= 20 ? `${pct.toFixed(0)}%` : ''}
                        </div>
                    );
                })}
            </div>

            {showLabels && (
                <div className="flex justify-between mt-2 text-xs" style={{ fontVariantNumeric: 'tabular-nums' }}>
                    <span style={{ color: palette.negative.solid, fontWeight: 600 }}>
                        {((negative / total) * 100).toFixed(0)}% {palette.negative.label.toLowerCase()}
                    </span>
                    <span style={{ color: palette.neutral.solid }}>
                        {((neutral / total) * 100).toFixed(0)}% neutral
                    </span>
                    <span style={{ color: palette.positive.solid, fontWeight: 600 }}>
                        {((positive / total) * 100).toFixed(0)}% {palette.positive.label.toLowerCase()}
                    </span>
                </div>
            )}

            {active && (
                <div
                    aria-live="polite"
                    className="text-xs"
                    style={{
                        marginTop: 'var(--space-2)',
                        padding: 'var(--space-1) var(--space-2)',
                        borderRadius: 'var(--radius-sm)',
                        background: 'var(--bg-inset)',
                        color: 'var(--neutral-700)',
                        fontVariantNumeric: 'tabular-nums',
                    }}
                >
                    <strong style={{ color: segments.find(s => s.key === active)!.solid }}>
                        {segments.find(s => s.key === active)!.label}
                    </strong>
                    {' '}&middot; {segments.find(s => s.key === active)!.value.toLocaleString()} docs
                    {' '}&middot; {((segments.find(s => s.key === active)!.value / total) * 100).toFixed(1)}%
                </div>
            )}
        </div>
    );
}

export default SentimentBar;
