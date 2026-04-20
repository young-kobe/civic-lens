import { SEMANTIC_COLORS } from '../../theme';

interface MiniDonutProps {
    positive: number;
    negative: number;
    neutral: number;
    size?: number;
    /** Accessible label describing the chart; falls back to a generic summary. */
    ariaLabel?: string;
}

/**
 * CSS-only conic-gradient donut chart. Decorative-by-default —
 * consumers who surface this as primary information should pass
 * `ariaLabel`; otherwise we mark it aria-hidden so assistive tech
 * doesn't read out a meaningless percentage out of context.
 */
export function MiniDonut({ positive, negative, neutral, size = 56, ariaLabel }: MiniDonutProps) {
    const total = positive + negative + neutral || 1;
    const negPct = (negative / total) * 100;
    const neuPct = (neutral / total) * 100;
    const posPct = (positive / total) * 100;

    const gradient = `conic-gradient(
        ${SEMANTIC_COLORS.negative} 0% ${negPct}%,
        ${SEMANTIC_COLORS.neutral} ${negPct}% ${negPct + neuPct}%,
        ${SEMANTIC_COLORS.positive} ${negPct + neuPct}% 100%
    )`;

    const accessibility = ariaLabel
        ? { role: 'img' as const, 'aria-label': ariaLabel }
        : { 'aria-hidden': true };

    return (
        <div
            {...accessibility}
            style={{
                width: size, height: size, borderRadius: '50%',
                background: gradient, position: 'relative', flexShrink: 0,
            }}
        >
            <div style={{
                position: 'absolute', inset: size * 0.22,
                borderRadius: '50%', background: 'var(--bg-card, #fff)',
            }} />
            <div style={{
                position: 'absolute', inset: 0, display: 'flex',
                alignItems: 'center', justifyContent: 'center',
                fontSize: '11px', fontWeight: 600, color: 'var(--text-primary)',
            }}>
                {posPct.toFixed(0)}%
            </div>
        </div>
    );
}
