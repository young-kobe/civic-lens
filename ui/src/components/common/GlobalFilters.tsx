
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
    { id: 'reddit', label: 'Reddit' },
    { id: 'social', label: 'Social' },
];

interface GlobalFiltersProps {
    filters: Filters;
    onFilterChange: (filters: Filters) => void;
    showGeography?: boolean;
}

/**
 * GlobalFilters - Persistent filter bar for time range, source, and geography.
 */
function GlobalFilters({
    filters,
    onFilterChange,
    showGeography = false
}: GlobalFiltersProps) {
    const { timeRange = '7d', sourceType = 'all', geography = 'all' } = filters;

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

            {showGeography && (
                <>
                    <div style={{ width: '1px', height: '24px', background: 'var(--neutral-200)', margin: '0 8px' }} />
                    <select
                        className="input select"
                        value={geography}
                        onChange={(e) => onFilterChange({ ...filters, geography: e.target.value })}
                        style={{ width: 'auto', minWidth: '150px' }}
                    >
                        <option value="all">All Regions</option>
                        <option value="us">United States</option>
                        <option value="eu">Europe</option>
                        <option value="other">Other</option>
                    </select>
                </>
            )}

            {/* Clear filters button */}
            {(timeRange !== '7d' || sourceType !== 'all' || geography !== 'all') && (
                <button
                    className="btn btn-ghost btn-sm"
                    onClick={() => onFilterChange({ timeRange: '7d', sourceType: 'all', geography: 'all' })}
                >
                    Clear filters
                </button>
            )}
        </div>
    );
}

export default GlobalFilters;
