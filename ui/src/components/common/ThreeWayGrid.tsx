import { useState, type ReactNode } from 'react';

/**
 * Three-way entity frame shared across every data page: News Outlets /
 * Verified Officials / General Public, in that order.
 *
 * Before this extraction each of Tone / Narratives / Propaganda / Bot
 * Detector re-implemented the same column wrapper + empty-state fallback
 * inline (four copies of ~15 near-identical lines). The render-variance
 * is only in which card type shows inside each column — callers pass
 * `items` + `renderItem` (or pre-rendered `children` for static columns).
 *
 * Columns own two client-side controls when `items` is supplied:
 *  - a "Show all (N)" expander past `collapsedCount` (the payload already
 *    carries every row; only the default view is capped), and
 *  - an optional sort toggle cycling the caller's `sorters`.
 *
 * The CSS classes `.three-way-grid` and `.three-way-column*` live in
 * `index.css`.
 */

export function ThreeWayGrid({ children }: { children: ReactNode }) {
    return <div className="three-way-grid">{children}</div>;
}

/**
 * Two-tier variant for pages where one tier is not measured — the Bot
 * Detector scores social accounts only (news articles are not accounts).
 * Columns are the same ThreeWayColumn.
 */
export function TwoWayGrid({ children }: { children: ReactNode }) {
    return <div className="two-way-grid">{children}</div>;
}

export interface ColumnSorter<T> {
    /** Short toggle label, e.g. "volume", "net", "name". */
    label: string;
    compare: (a: T, b: T) => number;
}

interface ThreeWayColumnProps<T> {
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
    /** When true, renders `empty` instead of items/children. Derived from
     *  `items` automatically when items are supplied. */
    isEmpty?: boolean;
    /** Full item list — the column caps the default view itself. */
    items?: T[];
    renderItem?: (item: T) => ReactNode;
    /** Batch renderer for list-shaped content (e.g. RankedEntityList),
     *  which needs the whole visible slice at once to number its rows.
     *  Takes precedence over renderItem. */
    renderItems?: (items: T[]) => ReactNode;
    /** Sort options; the first is the default order. Omit to keep the
     *  caller's order with no toggle. */
    sorters?: Array<ColumnSorter<T>>;
    /** Rows shown before the "Show all" expander. */
    collapsedCount?: number;
    /** Pre-rendered content (legacy path; no expand/sort controls). */
    children?: ReactNode;
}

const DEFAULT_COLLAPSED_COUNT = 12;

export function ThreeWayColumn<T>({
    header, byline, empty, isEmpty, items, renderItem, renderItems, sorters,
    collapsedCount = DEFAULT_COLLAPSED_COUNT, children,
}: ThreeWayColumnProps<T>) {
    const [expanded, setExpanded] = useState(false);
    const [sortIdx, setSortIdx] = useState(0);

    const empty_ = isEmpty ?? (items ? items.length === 0 : false);

    let body: ReactNode = children;
    let controls: ReactNode = null;
    if (items && (renderItem || renderItems) && !empty_) {
        const sorted = sorters && sorters.length > 0
            ? [...items].sort(sorters[Math.min(sortIdx, sorters.length - 1)].compare)
            : items;
        const visible = expanded ? sorted : sorted.slice(0, collapsedCount);
        body = renderItems ? renderItems(visible) : visible.map(renderItem!);
        const activeSorter = sorters && sorters.length > 0
            ? sorters[Math.min(sortIdx, sorters.length - 1)]
            : null;
        // A sort toggle under a single card is noise, not control — and an
        // empty controls row is dead whitespace.
        const showSort = !!(sorters && sorters.length > 1 && activeSorter && items.length > 1);
        const showExpand = items.length > collapsedCount;
        controls = (showSort || showExpand) ? (
            <div className="three-way-column-controls">
                {showSort && activeSorter && (
                    <button
                        type="button"
                        className="three-way-column-control"
                        onClick={() => setSortIdx((i) => (i + 1) % sorters!.length)}
                        title="Cycle sort order"
                        aria-label={`Sorted by ${activeSorter.label}. Change sort order.`}
                    >
                        sorted by {activeSorter.label}
                    </button>
                )}
                {showExpand && (
                    <button
                        type="button"
                        className="three-way-column-control"
                        onClick={() => setExpanded((v) => !v)}
                    >
                        {expanded ? 'Show fewer' : `Show all (${items.length})`}
                    </button>
                )}
            </div>
        ) : null;
    }

    return (
        <div className="three-way-column">
            <div>
                <div className="three-way-column-header">{header}</div>
                <div className="three-way-column-byline">{byline}</div>
            </div>
            {empty_ ? (
                <p className="text-xs text-muted" style={{ padding: 'var(--space-3)' }}>
                    {empty}
                </p>
            ) : (
                <>
                    {body}
                    {controls}
                </>
            )}
        </div>
    );
}
