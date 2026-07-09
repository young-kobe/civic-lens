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
}

export function TopMetricsBlock({
    eyebrow,
    meta,
    children,
    aux,
}: TopMetricsBlockProps) {
    return (
        <div className="top-metrics">
            <div className="top-metrics-head">
                {eyebrow && <span className="eyebrow">{eyebrow}</span>}
                {meta && <span className="top-metrics-meta">{meta}</span>}
            </div>

            <div className="top-metrics-rows">{children}</div>

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
}

export function TierRow({
    label, value, verb, dotPct, dotColor, dots, valueColor, showZeroTick, endpoints,
}: TierRowProps) {
    const resolvedDots: TierRowDot[] = dots ?? (
        dotPct != null && dotColor
            ? [{ pct: dotPct, color: dotColor }]
            : []
    );
    const resolvedEndpoints = endpoints ?? (showZeroTick ? ['−100', '+100'] as const : undefined);
    const valueStyle: CSSProperties | undefined = valueColor ? { color: valueColor } : undefined;

    return (
        <div className="tier-row">
            <span className="tier-row-label">{label}</span>
            <div className="tier-row-axis">
                {showZeroTick && <span className="tier-row-zero" aria-hidden />}
                {resolvedEndpoints && (
                    <>
                        <span className="tier-row-endpoint tier-row-endpoint-left" aria-hidden>{resolvedEndpoints[0]}</span>
                        <span className="tier-row-endpoint tier-row-endpoint-right" aria-hidden>{resolvedEndpoints[1]}</span>
                    </>
                )}
                {resolvedDots.map((d, i) => (
                    <span
                        key={i}
                        className="tier-row-dot"
                        title={d.title}
                        style={{
                            left: `${Math.max(0, Math.min(100, d.pct))}%`,
                            background: d.color,
                        }}
                    />
                ))}
            </div>
            <span className="tier-row-value" style={valueStyle}>{value}</span>
            {verb != null && <span className="tier-row-verb">{verb}</span>}
        </div>
    );
}

export default TopMetricsBlock;
