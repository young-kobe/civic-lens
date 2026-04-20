/**
 * Design tokens mirrored from index.css `:root` for use in JS/TS.
 *
 * Keep these in lockstep with the CSS custom properties — when you change a
 * palette value, change it in both places (or the CSS becomes the lone
 * source of truth and the pages that import hex here drift). The pages
 * that need runtime access (SVG fills computed via d3-style threshold
 * functions, canvas charts, etc.) import from here; everything else
 * should prefer `var(--semantic-*)` in the stylesheet.
 */

export const SEMANTIC_COLORS = {
  positive: '#008a4c',
  positiveLight: '#e3f6eb',
  negative: '#d41e0e',
  negativeLight: '#fbe7e4',
  neutral: '#62626a',
  neutralLight: '#f4f4f5',
  warning: '#b26100',
  warningLight: '#fdf0dd',
} as const;

/**
 * Map a sentiment label to its semantic foreground color.
 * Mirrors the CSS `.badge-*` / `.metric-delta-*` classes.
 */
export function sentimentColor(label: 'POSITIVE' | 'NEGATIVE' | 'NEUTRAL' | 'MIXED' | string): string {
  if (label === 'POSITIVE') return SEMANTIC_COLORS.positive;
  if (label === 'NEGATIVE') return SEMANTIC_COLORS.negative;
  if (label === 'MIXED') return SEMANTIC_COLORS.warning;
  return SEMANTIC_COLORS.neutral;
}

/**
 * Map a numeric net-sentiment score to a threshold-based color.
 * Used by narratives, heatmap, and review screens for consistent coloring.
 */
export function netSentimentColor(score: number, strongThreshold = 0.3, mildThreshold = 0.1): string {
  if (score >= strongThreshold) return SEMANTIC_COLORS.positive;
  if (score >= mildThreshold) return SEMANTIC_COLORS.positiveLight;
  if (score <= -strongThreshold) return SEMANTIC_COLORS.negative;
  if (score <= -mildThreshold) return SEMANTIC_COLORS.negativeLight;
  return SEMANTIC_COLORS.neutral;
}

/**
 * Accuracy-bucket color for review stats: green high, warning mid, negative low.
 */
export function accuracyColor(accuracyPct: number): string {
  if (accuracyPct >= 80) return SEMANTIC_COLORS.positive;
  if (accuracyPct >= 60) return SEMANTIC_COLORS.warning;
  return SEMANTIC_COLORS.negative;
}
