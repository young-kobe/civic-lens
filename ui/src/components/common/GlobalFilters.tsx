
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
];

interface GlobalFiltersProps {
    filters: Filters;
    onFilterChange: (filters: Filters) => void;
    /** When false, the tab ignores the time window (serves the full sample),
     *  so we hide the pills and say so outright instead of rendering controls
     *  that silently do nothing. Defaults to true. */
    windowScoped?: boolean;
}

/**
 * GlobalFilters - Persistent filter bar for time range. The previous
 * "Filter by sources" pills were removed once the three-tier split
 * (news / officials / public) made source separation a built-in part
 * of every page; the filter dimension would have been redundant.
 */
function GlobalFilters({
    filters,
    onFilterChange,
    windowScoped = true,
}: GlobalFiltersProps) {
    const { timeRange = '7d' } = filters;

    if (!windowScoped) {
        return (
            <div className="filter-bar">
                <span className="eyebrow text-muted">Showing everything we've collected — no date filter</span>
            </div>
        );
    }

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
