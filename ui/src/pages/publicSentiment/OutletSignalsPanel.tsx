import { Card, MethodPopover } from '../../components/common';
import { fetchOutletProfiles, type TimeWindow } from '../../services/api';
import { formatCount, formatPct, formatPts } from '../../services/format';
import { useFetch } from '../../services/useFetch';
import { COLORS } from '../../theme';
import type { OutletProfilesResult } from '../../types';

// --------------------------------------------------------------------------- //
//  OutletSignalsPanel — per-domain net tone x bot rate side by side.          //
//                                                                             //
//  The one public surface that INCLUDES bot-flagged content: the bot rate    //
//  is the signal being shown, so excluding flagged posts would erase it.     //
//  The interesting read is divergence — a domain whose tone and automation   //
//  profile don't match its neighbors. Data from /outlet-profiles (cached     //
//  snapshot, wired in Phase 2e).                                             //
// --------------------------------------------------------------------------- //

const MAX_ROWS = 15;

const SOURCE_LABEL: Record<string, string> = {
    news: 'News',
    reddit_post: 'Reddit',
    reddit_comment: 'Reddit',
    x_post: 'X',
};

function toneColor(net: number): string {
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return 'var(--neutral-600)';
}

function botColor(ratePct: number): string {
    if (ratePct > 10) return COLORS.negative;
    if (ratePct > 3) return COLORS.warning;
    return 'var(--neutral-600)';
}

export function OutletSignalsPanel({ window }: { window: TimeWindow }) {
    const { data } = useFetch<OutletProfilesResult>(
        () => fetchOutletProfiles(window),
        [window],
        `outlet-profiles:${window}`,
    );
    if (!data || data.outlets.length === 0) return null;
    const rows = data.outlets.slice(0, MAX_ROWS);

    return (
        <Card
            title="Source signals, side by side"
            subtitle="Per-domain net tone next to the share of its scanned posts our detector flags. Sorted by flagged share."
            headerActions={
                <MethodPopover
                    description={
                        'One row per domain or subreddit we sampled. Unlike the rest of this '
                        + 'page, these figures INCLUDE bot-flagged content — the flagged share '
                        + 'is the signal being shown, so excluding it would erase it. Net tone '
                        + 'here can therefore differ from the bot-excluded cards above.'
                    }
                    limitations={[
                        'Sampled discourse, not a media-bias rating.',
                        'Domains with very few scored posts are omitted.',
                    ]}
                />
            }
        >
            <div className="desk-table-wrap">
                <table className="table">
                    <thead>
                        <tr>
                            <th>Source</th>
                            <th>Type</th>
                            <th className="num" title="Positive minus negative share of its sampled posts, -100..+100, bots included">
                                Net tone
                            </th>
                            <th className="num" title="Share of its scanned posts flagged bot-or-suspicious">
                                Flagged share
                            </th>
                            <th className="num">Posts</th>
                        </tr>
                    </thead>
                    <tbody>
                        {rows.map((o) => (
                            <tr key={o.outlet}>
                                <td style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-xs)' }}>
                                    {o.outlet}
                                </td>
                                <td>{SOURCE_LABEL[o.source_type] ?? o.source_type}</td>
                                <td className="num" style={o.net_tone != null ? { color: toneColor(o.net_tone) } : undefined}>
                                    {o.net_tone != null ? formatPts(o.net_tone) : '—'}
                                </td>
                                <td className="num" style={{ color: botColor(o.bot_rate_pct) }}>
                                    {o.total_scanned > 0 ? formatPct(o.bot_rate_pct) : '—'}
                                </td>
                                <td className="num">{formatCount(o.volume)}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
            <p className="card-note" style={{ marginTop: 'var(--space-2)' }}>{data.disclaimer}</p>
        </Card>
    );
}

export default OutletSignalsPanel;
