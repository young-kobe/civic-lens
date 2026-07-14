import { useState } from 'react';
import { Card, MethodPopover, Modal, PostCardList, sampleToPostCard } from '../../components/common';
import { fetchOutletProfiles, type TimeWindow } from '../../services/api';
import { formatCount, formatPct, formatPts } from '../../services/format';
import { useFetch } from '../../services/useFetch';
import { COLORS } from '../../theme';
import type { ClassificationSample, OutletProfileItem, OutletProfilesResult } from '../../types';

// --------------------------------------------------------------------------- //
//  OutletSignalsPanel — per-domain net tone x bot rate side by side.          //
//                                                                             //
//  The one public surface that INCLUDES bot-flagged content: the bot rate    //
//  is the signal being shown, so excluding flagged posts would erase it.     //
//  The interesting read is divergence — a domain whose tone and automation   //
//  profile don't match its neighbors. Data from /outlet-profiles (cached     //
//  snapshot, wired in Phase 2e).                                             //
// --------------------------------------------------------------------------- //

const MAX_ROWS = 9;

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

/** Distinct narratives among a source's samples, most-frequent first — the
 *  recurring stories driving its net tone. */
function narrativeBreakdown(samples: ClassificationSample[]): { name: string; count: number }[] {
    const counts = new Map<string, number>();
    for (const s of samples) {
        if (s.narrative) counts.set(s.narrative, (counts.get(s.narrative) ?? 0) + 1);
    }
    return [...counts.entries()]
        .map(([name, count]) => ({ name, count }))
        .sort((a, b) => b.count - a.count);
}

/** Ties a source's net-tone number to the stories + posts behind it. */
function OutletSamplesModal({ outlet, onClose }: { outlet: OutletProfileItem; onClose: () => void }) {
    const samples = outlet.samples ?? [];
    const stories = narrativeBreakdown(samples);
    return (
        <Modal
            isOpen
            onClose={onClose}
            kicker="Source signals"
            title={outlet.outlet}
            subtitle={`${outlet.net_tone != null ? formatPts(outlet.net_tone) : '—'} net tone · ${formatCount(outlet.volume)} scored posts, bots included`}
        >
            {stories.length > 0 && (
                <div className="outlet-stories">
                    <div className="eyebrow">Stories driving this tone</div>
                    <ul className="outlet-stories-list">
                        {stories.map((s) => (
                            <li key={s.name}>
                                <span className="outlet-story-count">{s.count}</span>
                                {s.name}
                            </li>
                        ))}
                    </ul>
                </div>
            )}
            {samples.length > 0 ? (
                <PostCardList
                    posts={samples.map(sampleToPostCard)}
                    sampleNote="This source's highest-confidence scored posts — the articles and events behind the number. Highlighted text is the evidence the model quoted."
                />
            ) : (
                <p className="text-sm text-muted">
                    No stored example posts for this source in the current snapshot.
                    They fill in on the next data refresh.
                </p>
            )}
        </Modal>
    );
}

export function OutletSignalsPanel({ window }: { window: TimeWindow }) {
    const { data } = useFetch<OutletProfilesResult>(
        () => fetchOutletProfiles(window),
        [window],
        `outlet-profiles:${window}`,
    );
    const [active, setActive] = useState<OutletProfileItem | null>(null);
    if (!data || data.outlets.length === 0) return null;
    const rows = data.outlets.slice(0, MAX_ROWS);

    return (
        <Card
            title="Source signals, side by side"
            subtitle="Per-domain net tone next to the share of its scanned posts our detector flags. Click a source to see the stories driving its tone. Sorted by flagged share."
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
                            <tr
                                key={o.outlet}
                                className="outlet-signal-row"
                                onClick={() => setActive(o)}
                                title={`See the stories and posts driving ${o.outlet}'s tone`}
                            >
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
            {active && <OutletSamplesModal outlet={active} onClose={() => setActive(null)} />}
        </Card>
    );
}

export default OutletSignalsPanel;
