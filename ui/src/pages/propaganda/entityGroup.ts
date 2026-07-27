import type { PropagandaEntityRow } from '../../types';

/**
 * Humanize a PropagandaEntityRow.group value ('news' / 'subreddit' / an
 * author tier such as 'elected_official') into display text. The field is a
 * plain string, not a fixed enum -- see PropagandaEntityRow's docstring in
 * analysis/src/api/models/propaganda.py -- so this only title-cases and
 * de-underscores rather than mapping a closed set of keys.
 */
export function entityGroupLabel(group: PropagandaEntityRow['group']): string {
    return group
        .split('_')
        .map((w) => (w ? w.charAt(0).toUpperCase() + w.slice(1) : w))
        .join(' ');
}
