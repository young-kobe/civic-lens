import { useState } from 'react';
import { AdmissionBadge, Card, MethodPopover, Modal, RangeCaption } from '../../components/common';
import { fetchOutletProfiles, type TimeWindow } from '../../services/api';
import { formatCount, formatPct, formatPts, formatRelativeDate } from '../../services/format';
import { useFetch } from '../../services/useFetch';
import { COLORS } from '../../theme';
import type { OutletProfile, OutletProfilesResponse, SampleDoc } from '../../types';

// --------------------------------------------------------------------------- //
//  OutletSignalsPanel — per-domain net tone x bot rate side by side.          //
//                                                                             //
//  The one public surface that INCLUDES bot-flagged content: the bot rate    //
//  is the signal being shown, so excluding flagged posts would erase it.     //
//                                                                             //
//  Renders `outlet.samples` (`SampleDoc[]`) with a small local thin card     //
//  instead of the shared `SampleCardList`: that component re-exports         //
//  `PostCard`/`PostCardList` from `./PostCard.tsx`, which Wave 3 UI restored //
//  to the rich `ClassificationSample`-based pre-cutover card (see            //
//  `PostCard.tsx`'s module docstring) — a different, thinner shape than      //
//  `SampleDoc`. See this restoration's report for the cross-scope note on    //
//  `SampleCard.tsx` needing its own `SampleDoc` implementation again.        //
// --------------------------------------------------------------------------- //

/** "1234567890" (unix seconds) → "3 days ago". */
function unixSecondsFromIso(iso: string | null | undefined): number | null {
    if (!iso) return null;
    const t = Date.parse(iso);
    return Number.isNaN(t) ? null : Math.floor(t / 1000);
}

function OutletSampleCard({ sample }: { sample: SampleDoc }) {
    return (
        <article className="post-card">
            <header className="post-card-head">
                <span className="post-card-when">{formatRelativeDate(unixSecondsFromIso(sample.publishedAt))}</span>
                <a
                    className="post-card-permalink"
                    href={sample.sourceUrl}
                    target="_blank"
                    rel="noreferrer"
                    aria-label="Open the original post in a new tab"
                    title="Open the original in a new tab"
                >
                    View original
                </a>
            </header>
            {sample.snippet && <p className="post-card-body">{sample.snippet}</p>}
            <div className="post-card-analysis">
                <AdmissionBadge admissionClass={sample.admissionClass} />
                <span className="post-card-confidence" title="Model confidence in this run's label">
                    {formatPct(sample.confidence * 100, { decimals: 0 })} confidence
                </span>
            </div>
        </article>
    );
}

function OutletSampleList({ samples }: { samples: SampleDoc[] }) {
    if (samples.length === 0) {
        return (
            <p className="text-sm text-muted" style={{ fontStyle: 'italic' }}>
                No stored example posts for this source in this window.
            </p>
        );
    }
    return (
        <div className="post-card-list">
            <p className="post-card-list-note">
                This source's highest-confidence scored posts — the posts behind the number.
            </p>
            {samples.map((s) => <OutletSampleCard key={s.docId} sample={s} />)}
        </div>
    );
}

const MAX_ROWS = 9;

const SOURCE_LABEL: Record<string, string> = {
    news: 'News', reddit_post: 'Reddit', reddit_comment: 'Reddit', x_post: 'X',
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

function OutletSamplesModal({ outlet, onClose }: { outlet: OutletProfile; onClose: () => void }) {
    return (
        <Modal
            isOpen
            onClose={onClose}
            kicker="Source signals"
            title={outlet.outletKey}
            subtitle={`${outlet.netTone != null ? formatPts(outlet.netTone) : '—'} net tone · ${formatCount(outlet.volume)} scored posts, bots included`}
        >
            <OutletSampleList samples={outlet.samples} />
        </Modal>
    );
}

export function OutletSignalsPanel({ window }: { window: TimeWindow }) {
    const { data } = useFetch<OutletProfilesResponse>(
        () => fetchOutletProfiles(window),
        [window],
        `outlet-profiles:${window}`,
    );
    const [active, setActive] = useState<OutletProfile | null>(null);
    if (!data || data.outlets.length === 0) return null;
    const rows = [...data.outlets]
        .sort((a, b) => b.botRatePct - a.botRatePct)
        .slice(0, MAX_ROWS);

    return (
        <Card
            title="Source signals, side by side"
            subtitle="Per-domain net tone next to the share of its scanned posts our detector flags. Click a source to see the posts driving its tone. Sorted by flagged share."
            headerActions={
                <MethodPopover
                    description={
                        'One row per domain or subreddit we sampled. Unlike the rest of this '
                        + 'page, these figures INCLUDE bot-flagged content — the flagged share '
                        + 'is the signal being shown, so excluding it would erase it.'
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
                                key={o.outletKey}
                                className="outlet-signal-row"
                                onClick={() => setActive(o)}
                                title={`See the posts driving ${o.outletKey}'s tone`}
                            >
                                <td style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-xs)' }}>
                                    {o.outletKey}
                                </td>
                                <td>{SOURCE_LABEL[o.sourceType] ?? o.sourceType}</td>
                                <td className="num" style={o.netTone != null ? { color: toneColor(o.netTone) } : undefined}>
                                    {o.netTone != null ? formatPts(o.netTone) : '—'}
                                </td>
                                <td className="num" style={{ color: botColor(o.botRatePct) }}>
                                    {o.totalScanned > 0 ? formatPct(o.botRatePct) : '—'}
                                </td>
                                <td className="num">{formatCount(o.volume)}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
            <p className="card-note" style={{ marginTop: 'var(--space-2)' }}>{data.disclaimer}</p>
            <RangeCaption range={data.range} />
            {active && <OutletSamplesModal outlet={active} onClose={() => setActive(null)} />}
        </Card>
    );
}

export default OutletSignalsPanel;
