/**
 * Display-formatting helpers for numbers we render to readers.
 *
 * These exist as a defensive layer between the backend contract and the
 * rendered UI: the backend *should* emit well-formed percentages in
 * [0, 100] (or [-100, 100] for signed measures), but aggregator bugs
 * can produce values outside those ranges — e.g. a historical
 * `pct_of_flagged_docs` bug that counted multiple evidence spans per
 * doc rendered as "Loaded language: 197% of flagged posts." Clamping
 * + a dev-only warning catches those regressions before they reach a
 * reader.
 */

export interface FormatPctOpts {
    /** Decimal places. Default 1. */
    decimals?: number;
    /** Allowed minimum. Default 0. Pass -100 for signed measures. */
    min?: number;
    /** Allowed maximum. Default 100. */
    max?: number;
    /**
     * When true, prefix positive values with "+" (for signed measures
     * like net sentiment where the sign conveys direction).
     */
    signed?: boolean;
    /** Fallback when the value is unusable. Default "—". */
    fallback?: string;
}

/**
 * Format a number as a percentage string for display.
 *
 *   formatPct(34.5)                        → "34.5%"
 *   formatPct(197)                         → "100%" (+ dev warning)
 *   formatPct(null)                        → "—"
 *   formatPct(12.3, { decimals: 0 })       → "12%"
 *   formatPct(12.3, { signed: true })      → "+12.3%"
 *   formatPct(-15, { min: -100, signed: true }) → "-15.0%"
 */
export function formatPct(
    value: number | null | undefined,
    opts: FormatPctOpts = {},
): string {
    const fallback = opts.fallback ?? '—';
    if (value === null || value === undefined || !Number.isFinite(value)) {
        return fallback;
    }
    const min = opts.min ?? 0;
    const max = opts.max ?? 100;
    const clamped = Math.max(min, Math.min(max, value));
    if (clamped !== value && typeof import.meta !== 'undefined' && (import.meta as ImportMeta).env?.DEV) {
        // eslint-disable-next-line no-console
        console.warn(
            `formatPct: value ${value} clamped to [${min}, ${max}] → ${clamped}. ` +
            `Likely a backend aggregator bug — investigate the source field.`,
        );
    }
    const decimals = opts.decimals ?? 1;
    const sign = opts.signed && clamped > 0 ? '+' : '';
    return `${sign}${clamped.toFixed(decimals)}%`;
}

/**
 * Clamp a number to a CSS width percentage — returns a plain number in
 * [0, 100]. Use when piping a backend rate straight into a
 * `style={{ width: `${x}%` }}` expression, so a buggy 197 doesn't
 * overflow the bar's visual container.
 */
export function clampWidthPct(value: number | null | undefined): number {
    if (value === null || value === undefined || !Number.isFinite(value)) return 0;
    return Math.max(0, Math.min(100, value));
}
