import type { Filters } from '../types';

// --------------------------------------------------------------------------- //
//  Single source of truth for time-window labels.                             //
//  Used by every page's "As of {window}" eyebrow and by any card that        //
//  that surfaces the active filter in its meta strip.                         //
// --------------------------------------------------------------------------- //

const WINDOW_LABEL: Record<Filters['timeRange'], string> = {
    '24h': 'last 24 hours',
    '7d':  'last 7 days',
    '30d': 'last 30 days',
    '90d': 'last 90 days',
    'all': 'all time',
};

/** "last 7 days" for the 7d filter, etc. Falls back to the raw key if the
 *  map ever goes out of sync with the Filters union. */
export function formatTimeWindow(range: Filters['timeRange']): string {
    return WINDOW_LABEL[range] || range;
}

/** "Last 7 days" — eyebrow text for the reads-as-today cards and the
 *  TopMetricsBlock header. Standard shape across every page so the reader
 *  always knows which window the page is showing. */
export function asOfTodayEyebrow(range: Filters['timeRange']): string {
    const label = formatTimeWindow(range);
    return label.charAt(0).toUpperCase() + label.slice(1);
}
