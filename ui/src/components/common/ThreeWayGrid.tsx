import { useState, type ReactNode } from 'react';
import { leanClass } from '../../theme';

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
 *  - a sort toggle in the column header cycling the caller's `sorters`, and
 *  - an internal scroll region past `collapsedCount` cards, so a long tier
 *    scrolls inside its own column instead of pushing page height (the whole
 *    payload is always rendered — only the visible band is bounded).
 *
 * A page-level `ThreeWayToolbar` sits above the grid for cross-column
 * controls (lean/party filter, entity search); the pages own that state and
 * pass already-filtered `items` down.
 *
 * The CSS classes `.three-way-grid` and `.three-way-column*` live in
 * `index.css`.
 */

/**
 * A single wireframe box: an outer border encapsulates the columns, an optional
 * `toolbar` renders as a header band above them, and vertical hairlines divide
 * the columns (CSS). Every page's three-way grid reads as one contained module.
 */
export function ThreeWayGrid({ children, toolbar }: { children: ReactNode; toolbar?: ReactNode }) {
    return (
        <div className="three-way-frame">
            {toolbar}
            <div className="three-way-grid">{children}</div>
        </div>
    );
}

/**
 * Two-tier variant for pages where one tier is not measured — the Bot
 * Detector scores social accounts only (news articles are not accounts).
 * Columns are the same ThreeWayColumn.
 */
export function TwoWayGrid({ children, toolbar }: { children: ReactNode; toolbar?: ReactNode }) {
    return (
        <div className="three-way-frame">
            {toolbar}
            <div className="two-way-grid">{children}</div>
        </div>
    );
}

export interface ColumnSorter<T> {
    /** Short toggle label, e.g. "volume", "net", "name". */
    label: string;
    compare: (a: T, b: T) => number;
}

// --------------------------------------------------------------------------- //
//  Lean / party filter — shared by the toolbar and the pages' item filter    //
// --------------------------------------------------------------------------- //

/** Political-lean filter buckets. 'all' is the no-op default. */
export type LeanFilter = 'all' | 'left' | 'center' | 'right';

const LEAN_FILTER_OPTIONS: Array<{ key: LeanFilter; label: string }> = [
    { key: 'all', label: 'All' },
    { key: 'left', label: 'Left' },
    { key: 'center', label: 'Center' },
    { key: 'right', label: 'Right' },
];

/**
 * True when a profile passes the active lean filter. Unifies outlet/subreddit
 * `lean` and official/account `party` through the shared `leanClass` map, so
 * "Left" catches both left-leaning outlets and Democratic officials. The
 * "Center" pill also admits mixed/neutral so no card silently vanishes under
 * a specific pill (only "All" shows literally everything).
 */
export function matchesLeanFilter(
    profile: { kind: string; party?: string; lean?: string | null } | undefined | null,
    filter: LeanFilter,
): boolean {
    if (filter === 'all') return true;
    if (!profile) return filter === 'center';
    const cls = leanClass(profile);
    if (filter === 'center') return cls === 'center' || cls === 'mixed' || cls === 'neutral';
    return cls === filter;
}

interface ThreeWayToolbarProps {
    /** Lean filter state; omit the pair to hide the lean pills. */
    leanFilter?: LeanFilter;
    onLeanFilterChange?: (f: LeanFilter) => void;
}

/**
 * Header band of the three-way frame — hosts the lean/party filter. Returns
 * null when the filter isn't wired so the frame has no empty band.
 */
export function ThreeWayToolbar({ leanFilter, onLeanFilterChange }: ThreeWayToolbarProps) {
    const showLean = leanFilter !== undefined && !!onLeanFilterChange;
    if (!showLean) return null;
    return (
        <div className="three-way-toolbar">
            <div
                className="three-way-toolbar-filter"
                role="group"
                aria-label="Filter by political lean or party"
            >
                <span className="eyebrow three-way-toolbar-label">Lean</span>
                {LEAN_FILTER_OPTIONS.map((opt) => (
                    <button
                        key={opt.key}
                        type="button"
                        className={`filter-pill ${leanFilter === opt.key ? 'filter-pill-active' : ''}`}
                        onClick={() => onLeanFilterChange!(opt.key)}
                        aria-pressed={leanFilter === opt.key}
                    >
                        {opt.label}
                    </button>
                ))}
            </div>
        </div>
    );
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
    /** Full item list — the column renders all of it and scrolls internally
     *  past `collapsedCount`. */
    items?: T[];
    renderItem?: (item: T) => ReactNode;
    /** Batch renderer for list-shaped content (e.g. RankedEntityList),
     *  which needs the whole visible slice at once to number its rows.
     *  Takes precedence over renderItem. */
    renderItems?: (items: T[]) => ReactNode;
    /** Sort options; the first is the default order unless `defaultSortIdx`
     *  is set. Omit to keep the caller's order with no toggle. */
    sorters?: Array<ColumnSorter<T>>;
    /** Which sorter to start on (e.g. officials default to engagement). */
    defaultSortIdx?: number;
    /** Cards shown before the column body starts scrolling internally. */
    collapsedCount?: number;
    /** Rendered at the bottom of the column, after the cards — used to fill
     *  a thin tier (e.g. the public column's "who they're talking about"). */
    footer?: ReactNode;
    /** Pre-rendered content (legacy path; no sort control or scroll). */
    children?: ReactNode;
}

const DEFAULT_COLLAPSED_COUNT = 10;

export function ThreeWayColumn<T>({
    header, byline, empty, isEmpty, items, renderItem, renderItems, sorters,
    defaultSortIdx = 0, collapsedCount = DEFAULT_COLLAPSED_COUNT, footer, children,
}: ThreeWayColumnProps<T>) {
    const [sortIdx, setSortIdx] = useState(defaultSortIdx);

    const empty_ = isEmpty ?? (items ? items.length === 0 : false);

    let body: ReactNode = children;
    let sortControl: ReactNode = null;
    let overflow = false;
    if (items && (renderItem || renderItems) && !empty_) {
        const activeSorter = sorters && sorters.length > 0
            ? sorters[Math.min(sortIdx, sorters.length - 1)]
            : null;
        const sorted = activeSorter ? [...items].sort(activeSorter.compare) : items;
        // The whole list is always rendered; past the cap the body becomes a
        // fixed-height scroll region so a long tier can't stretch page height.
        overflow = items.length > collapsedCount;
        body = renderItems ? renderItems(sorted) : sorted.map(renderItem!);
        // A sort toggle under a single card is noise, not control.
        const showSort = !!(sorters && sorters.length > 1 && activeSorter && items.length > 1);
        sortControl = showSort && activeSorter ? (
            <button
                type="button"
                className="three-way-column-control"
                onClick={() => setSortIdx((i) => (i + 1) % sorters!.length)}
                title="Cycle sort order"
                aria-label={`Sorted by ${activeSorter.label}. Change sort order.`}
            >
                sorted by {activeSorter.label}
            </button>
        ) : null;
    }

    return (
        <div className="three-way-column">
            <div className="three-way-column-head">
                <div className="three-way-column-header">{header}</div>
                <div className="three-way-column-byline">{byline}</div>
                {sortControl}
            </div>
            {empty_ ? (
                <p className="text-xs text-muted" style={{ padding: 'var(--space-3)' }}>
                    {empty}
                </p>
            ) : (
                <>
                    {overflow
                        ? <div className="three-way-column-scroll">{body}</div>
                        : body}
                    {footer}
                </>
            )}
        </div>
    );
}
