import { useState } from 'react';
import { COLORS } from '../../theme';

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

    // Palettes pull from the central COLORS dictionary — source of
    // truth is :root in index.css; COLORS is its typed TS wrapper.
    const palette = colorScheme === 'political'
        ? {
            positive: { gradient: COLORS.stanceGradSupportive, solid: COLORS.stanceSupportive, label: 'Supportive' },
            neutral:  { gradient: COLORS.stanceGradNeutral,    solid: COLORS.stanceNeutral,    label: 'Neutral' },
            negative: { gradient: COLORS.stanceGradOpposed,    solid: COLORS.stanceOpposed,    label: 'Opposed' },
        }
        : {
            positive: { gradient: COLORS.gradPositive, solid: COLORS.favSolid,       label: 'Favorable' },
            neutral:  { gradient: COLORS.gradNeutral,  solid: COLORS.stanceNeutral,  label: 'Neutral' },
            negative: { gradient: COLORS.gradNegative, solid: COLORS.unfavSolid,     label: 'Unfavorable' },
        };

    const segments: { key: SegKey; value: number; gradient: string; solid: string; label: string }[] = [
        { key: 'negative', value: negative, ...palette.negative },
        { key: 'neutral', value: neutral, ...palette.neutral },
        { key: 'positive', value: positive, ...palette.positive },
    ];

    return (
        <div style={{ position: 'relative' }}>
            <div
                style={{
                    display: 'flex',
                    height,
                    borderRadius: 'var(--radius-md)',
                    overflow: 'hidden',
                    background: COLORS.bgInset,
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

            {active && (() => {
                const seg = segments.find(s => s.key === active)!;
                return (
                    <div
                        className="popover"
                        role="tooltip"
                        aria-live="polite"
                        style={{
                            top: `calc(${height}px + 8px)`,
                            left: 0,
                            fontVariantNumeric: 'tabular-nums',
                            padding: 'var(--space-2) var(--space-3)',
                            minWidth: 0,
                        }}
                    >
                        <strong style={{ color: seg.solid }}>{seg.label}</strong>
                        {' '}&middot; {seg.value.toLocaleString()} docs
                        {' '}&middot; {((seg.value / total) * 100).toFixed(1)}%
                    </div>
                );
            })()}
        </div>
    );
}

export default SentimentBar;
