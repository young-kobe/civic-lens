
import type { Filters } from '../../types';

interface TimeRange {
    id: Filters['timeRange'];
    label: string;
}

const TIME_RANGES: TimeRange[] = [
    { id: '24h', label: '24 hours' },
    { id: '7d', label: '7 days' },
    { id: '30d', label: '30 days' },
    { id: '90d', label: '90 days' },
    // 'All time' is exposed on every panel except Movers (GET /movers
    // rejects window='all' -- an unbounded range has no preceding equal-
    // length period to compare against). Movers-consuming pages fall back
    // to a bounded window when this is selected; see DataDesk.tsx.
    { id: 'all', label: 'All time' },
];

interface GlobalFiltersProps {
    filters: Filters;
    onFilterChange: (filters: Filters) => void;
}

/**
 * GlobalFilters - Persistent filter bar for time range. The previous
 * "Filter by sources" pills were removed once the three-tier split
 * (news / officials / public) made source separation a built-in part
 * of every page; the filter dimension would have been redundant. Every
 * tab is window-scoped now that the Bot Detector fetches per-window
 * snapshots like its siblings (the old `windowScoped` escape hatch is
 * gone with it).
 */
function GlobalFilters({
    filters,
    onFilterChange,
}: GlobalFiltersProps) {
    const { timeRange = '7d' } = filters;

    return (
        <div className="filter-bar">
            <span className="eyebrow text-muted">Time range</span>
            <div className="flex gap-1">
                {TIME_RANGES.map((range) => (
                    <button
                        key={range.id}
                        className={`filter-pill ${timeRange === range.id ? 'filter-pill-active' : ''}`}
                        onClick={() => onFilterChange({ ...filters, timeRange: range.id })}
                    >
                        {range.label}
                    </button>
                ))}
            </div>

            {timeRange !== '7d' && (
                <button
                    className="btn btn-ghost btn-sm"
                    onClick={() => onFilterChange({ timeRange: '7d' })}
                >
                    Reset to 7 days
                </button>
            )}
        </div>
    );
}

export default GlobalFilters;
