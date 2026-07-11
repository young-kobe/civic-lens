import type { CSSProperties, ReactNode } from 'react';

// --------------------------------------------------------------------------- //
//  TopMetricsBlock — shared Bloomberg-style dense header.                     //
//  Rendered as:                                                               //
//     <eyebrow>        <meta>                                                 //
//     [rows via children]                                                     //
//     [aux mini-metrics (optional)]                                           //
//                                                                             //
//  Consumers (Overall Tone, Propaganda, Bot) own their row content — this     //
//  primitive just enforces layout + spacing + the `.top-metrics*` classes.    //
// --------------------------------------------------------------------------- //

interface TopMetricsBlockProps {
    eyebrow?: string;
    meta?: ReactNode;
    /** Axis-based tier rows. Use <TierRow /> or free-form JSX. */
    children: ReactNode;
    /** Optional mini-metric tiles rendered below the rows. */
    aux?: ReactNode;
    /** Extra class on the rows container to opt into a stacked layout variant
     *  (e.g. "propaganda-tier-rows" — full-width bar, value right, verb wrapped
     *  below, roomy spacing). */
    rowsClassName?: string;
}

export function TopMetricsBlock({
    eyebrow,
    meta,
    children,
    aux,
    rowsClassName,
}: TopMetricsBlockProps) {
    return (
        <div className="top-metrics">
            <div className="top-metrics-head">
                {eyebrow && <span className="eyebrow">{eyebrow}</span>}
                {meta && <span className="top-metrics-meta">{meta}</span>}
            </div>

            <div className={rowsClassName ? `top-metrics-rows ${rowsClassName}` : 'top-metrics-rows'}>
                {children}
            </div>

            {aux && <div className="top-metrics-aux">{aux}</div>}
        </div>
    );
}


// --------------------------------------------------------------------------- //
//  TierRow — reusable axis-based row. Supports one primary dot or several.    //
// --------------------------------------------------------------------------- //

export interface TierRowDot {
    /** 0-100 position on the axis. */
    pct: number;
    color: string;
    title?: string;
}

interface TierRowProps {
    label: string;
    value: ReactNode;
    /** Short sentence below/after the value describing the row. */
    verb?: ReactNode;
    /** Shorthand for a single dot. Ignored if `dots` is provided. */
    dotPct?: number;
    dotColor?: string;
    dots?: TierRowDot[];
    /** Optional color override for the value cell. */
    valueColor?: string;
    /** Render a subtle zero-midpoint tick (for -100..+100 axes). */
    showZeroTick?: boolean;
    /**
     * Faint left/right axis-end labels so a reader knows what the dot's
     * position means. Defaults to the tone scale ("−100" / "+100") when
     * `showZeroTick` is set; callers on other scales (e.g. a 0–100 rate)
     * pass their own endpoints.
     */
    endpoints?: [string, string];
    /** Optional trailing visual (e.g. a per-tier trend sparkline). Hidden
     *  below the 900px breakpoint. */
    trail?: ReactNode;
}

export function TierRow({
    label, value, verb, dotPct, dotColor, dots, valueColor, showZeroTick, endpoints, trail,
}: TierRowProps) {
    const resolvedDots: TierRowDot[] = dots ?? (
        dotPct != null && dotColor
            ? [{ pct: dotPct, color: dotColor }]
            : []
    );
    const resolvedEndpoints = endpoints ?? (showZeroTick ? ['−100', '+100'] as const : undefined);
    const valueStyle: CSSProperties | undefined = valueColor ? { color: valueColor } : undefined;

    return (
        <div className={trail != null ? 'tier-row tier-row-has-trail' : 'tier-row'}>
            <span className="tier-row-label">{label}</span>
            <div className="tier-row-axis">
                {showZeroTick && <span className="tier-row-zero" aria-hidden />}
                {resolvedEndpoints && (
                    <>
                        <span className="tier-row-endpoint tier-row-endpoint-left" aria-hidden>{resolvedEndpoints[0]}</span>
                        <span className="tier-row-endpoint tier-row-endpoint-right" aria-hidden>{resolvedEndpoints[1]}</span>
                    </>
                )}
                {resolvedDots.length === 1 ? (
                    // Single value → a filled bar (far more legible than a dot).
                    // Tone axis (showZeroTick): grows from the 50% zero baseline
                    // out to the value. Rate axis: fills from the left edge.
                    (() => {
                        const pct = Math.max(0, Math.min(100, resolvedDots[0].pct));
                        const bar = showZeroTick
                            ? (pct >= 50
                                ? { left: 50, width: pct - 50 }
                                : { left: pct, width: 50 - pct })
                            : { left: 0, width: pct };
                        return (
                            <span
                                className="tier-row-bar"
                                title={resolvedDots[0].title}
                                style={{
                                    left: `${bar.left}%`,
                                    width: `${bar.width}%`,
                                    background: resolvedDots[0].color,
                                }}
                            />
                        );
                    })()
                ) : (
                    // Multiple values on one axis (e.g. news vs social) can't be a
                    // single bar — keep enlarged high-contrast markers.
                    resolvedDots.map((d, i) => (
                        <span
                            key={i}
                            className="tier-row-dot"
                            title={d.title}
                            style={{
                                left: `${Math.max(0, Math.min(100, d.pct))}%`,
                                background: d.color,
                            }}
                        />
                    ))
                )}
            </div>
            <span className="tier-row-value" style={valueStyle}>{value}</span>
            {verb != null && <span className="tier-row-verb">{verb}</span>}
            {trail != null && <span className="tier-row-trail">{trail}</span>}
        </div>
    );
}

export default TopMetricsBlock;
