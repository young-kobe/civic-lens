
import type { Filters } from '../../types';

interface TimeRange {
    id: Filters['timeRange'];
    label: string;
}

interface SourceType {
    id: Filters['sourceType'];
    label: string;
}

const TIME_RANGES: TimeRange[] = [
    { id: '24h', label: '24 hours' },
    { id: '7d', label: '7 days' },
    { id: '30d', label: '30 days' },
    { id: '90d', label: '90 days' },
];

const SOURCE_TYPES: SourceType[] = [
    { id: 'all', label: 'All Sources' },
    { id: 'news', label: 'News' },
    { id: 'social', label: 'Social' },
];

interface GlobalFiltersProps {
    filters: Filters;
    onFilterChange: (filters: Filters) => void;
    /**
     * Whether the Source pills should render. Defaults to true. Pages where
     * the source filter isn't yet wired (requires backend aggregation that
     * accepts a source param) pass `false` so we don't surface a dead control.
     */
    showSourceType?: boolean;
}

/**
 * GlobalFilters - Persistent filter bar for time range and source.
 */
function GlobalFilters({
    filters,
    onFilterChange,
    showSourceType = true,
}: GlobalFiltersProps) {
    const { timeRange = '7d', sourceType = 'all' } = filters;

    return (
        <div className="filter-bar">
            {/* Time Range */}
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

            {showSourceType && (
                <>
                    <div style={{ width: '1px', height: '24px', background: 'var(--neutral-200)', margin: '0 8px' }} />

                    {/* Source Type */}
                    <div className="flex gap-1">
                        {SOURCE_TYPES.map((source) => (
                            <button
                                key={source.id}
                                className={`filter-pill ${sourceType === source.id ? 'filter-pill-active' : ''}`}
                                onClick={() => onFilterChange({ ...filters, sourceType: source.id })}
                            >
                                {source.label}
                            </button>
                        ))}
                    </div>
                </>
            )}

            {/* Clear filters button */}
            {(timeRange !== '7d' || (showSourceType && sourceType !== 'all')) && (
                <button
                    className="btn btn-ghost btn-sm"
                    onClick={() => onFilterChange({ timeRange: '7d', sourceType: 'all' })}
                >
                    Clear filters
                </button>
            )}
        </div>
    );
}

export default GlobalFilters;
