import type { ReactNode } from 'react';

/**
 * Three-way entity frame shared across every data page: News Outlets /
 * Verified Officials / General Public, in that order.
 *
 * Before this extraction each of Tone / Narratives / Propaganda / Bot
 * Detector re-implemented the same column wrapper + empty-state fallback
 * inline (four copies of ~15 near-identical lines). The render-variance
 * is only in which card type shows inside each column — callers pass
 * mapped nodes as `children` and a boolean `isEmpty` to trigger the
 * per-column fallback copy.
 *
 * The CSS classes `.three-way-grid` and `.three-way-column*` live in
 * `index.css` and were already shared before this component landed.
 */

export function ThreeWayGrid({ children }: { children: ReactNode }) {
    return <div className="three-way-grid">{children}</div>;
}

interface ThreeWayColumnProps {
    /** Short uppercase column heading, e.g. "The News". */
    header: string;
    /** Italic deck standfirst under the heading, e.g. "Top outlets by…". */
    byline: string;
    /**
     * Copy shown in place of children when the column has no data in the
     * active window. Keep it specific to the measurement, not generic —
     * "No news articles in this window." reads honestly;
     * "No data" does not.
     */
    empty: string;
    /** When true, renders `empty` instead of `children`. */
    isEmpty: boolean;
    children: ReactNode;
}

export function ThreeWayColumn({ header, byline, empty, isEmpty, children }: ThreeWayColumnProps) {
    return (
        <div className="three-way-column">
            <div>
                <div className="three-way-column-header">{header}</div>
                <div className="three-way-column-byline">{byline}</div>
            </div>
            {isEmpty ? (
                <p className="text-xs text-muted" style={{ padding: 'var(--space-3)' }}>
                    {empty}
                </p>
            ) : (
                children
            )}
        </div>
    );
}
