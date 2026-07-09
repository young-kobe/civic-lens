/**
 * Display-formatting helpers for numbers we render to readers.
 *
 * These exist as a defensive layer between the backend contract and the
 * rendered UI: the backend *should* emit well-formed percentages in
 * [0, 100] (or [-100, 100] for signed measures), but aggregator bugs
 * can produce values outside those ranges — e.g. a historical
 * `pct_of_flagged_docs` bug counted multiple evidence spans per doc and
 * rendered as "Loaded language: 197% of flagged posts."
 *
 * Policy: when a value is outside its declared range, render the
 * fallback ("—" by default), NOT a clamped edge value. Clamping 197
 * to "100%" would still show a false number — and a false number in a
 * data-integrity dashboard is the failure mode we explicitly don't
 * accept. "—" plus a dev-mode console warning is the honest signal:
 * something is wrong upstream, fix it at the source.
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
 *   formatPct(197)                         → "—" (+ dev warning)
 *   formatPct(null)                        → "—"
 *   formatPct(12.3, { decimals: 0 })       → "12%"
 *   formatPct(12.3, { signed: true })      → "+12.3%"
 *   formatPct(-15, { min: -100, signed: true }) → "-15.0%"
 *
 * Out-of-range values return the fallback rather than clamping to the
 * edge, because a clamped "100%" derived from a buggy 197 is still a
 * false number — and the reader has no way to tell those apart. "—"
 * reads honestly as "we don't have a trustworthy number here."
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
    if (value < min || value > max) {
        if (typeof import.meta !== 'undefined' && (import.meta as ImportMeta).env?.DEV) {
            // eslint-disable-next-line no-console
            console.warn(
                `formatPct: value ${value} outside [${min}, ${max}] — rendering "${fallback}". ` +
                `Likely a backend aggregator bug — investigate the source field.`,
            );
        }
        return fallback;
    }
    const decimals = opts.decimals ?? 1;
    const sign = opts.signed && value > 0 ? '+' : '';
    return `${sign}${value.toFixed(decimals)}%`;
}

/**
 * Format a signed net-tone / net-difference value on the -100..+100 scale
 * as points ("pts"), e.g. "+12 pts" / "-8 pts" / "0 pts".
 *
 * A net difference between two shares is measured in percentage *points*,
 * not a percentage. Rendering it with a bare "%" makes it read as a share
 * and collides with the confidence and rate percents shown elsewhere on
 * the same cards, so net-tone surfaces use this helper instead. Signed by
 * default; out-of-range values return the fallback, matching formatPct's
 * honesty policy (see the module docstring).
 *
 *   formatPts(12.3)              → "+12.3 pts"
 *   formatPts(-8, {decimals: 0}) → "-8 pts"
 *   formatPts(0)                 → "0 pts"
 *   formatPts(null)              → "—"
 */
export function formatPts(
    value: number | null | undefined,
    opts: FormatPctOpts = {},
): string {
    const fallback = opts.fallback ?? '—';
    if (value === null || value === undefined || !Number.isFinite(value)) {
        return fallback;
    }
    const min = opts.min ?? -100;
    const max = opts.max ?? 100;
    if (value < min || value > max) {
        if (typeof import.meta !== 'undefined' && (import.meta as ImportMeta).env?.DEV) {
            // eslint-disable-next-line no-console
            console.warn(
                `formatPts: value ${value} outside [${min}, ${max}] — rendering "${fallback}".`,
            );
        }
        return fallback;
    }
    const decimals = opts.decimals ?? 1;
    const signed = opts.signed ?? true;
    const sign = signed && value > 0 ? '+' : '';
    return `${sign}${value.toFixed(decimals)} pts`;
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
