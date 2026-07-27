import { Bar, BarChart, ResponsiveContainer, Tooltip, XAxis } from 'recharts';
import type { TooltipProps } from 'recharts';
import { Card, MethodPopover } from '../../components/common';
import { COLORS } from '../../theme';
import type { PostingCadenceBucket } from '../../types';

// --------------------------------------------------------------------------- //
//  CoordinationEvidencePanel — the pre-redesign Bot Detector's signature      //
//  strip: a chain-of-evidence funnel (posts scanned -> flagged -> posting in  //
//  lockstep -> shared link domains).                                         //
//                                                                             //
//  Degraded: the funnel's lockstep-timing stage now renders the real         //
//  postingCadence histogram (24 UTC-hour buckets, restored 2026-07-26 --     //
//  the same data coordinationIndex is computed over). The shared-link-domain //
//  stage still has no equivalent in the current /bot-activity response      //
//  (analysis/src/api/models/bots.py has no domain-cluster field) -- rather   //
//  than fabricate that reading, this states plainly what isn't measured yet. //
// --------------------------------------------------------------------------- //

function cadenceTooltip({ payload, label }: TooltipProps<number, string>) {
    if (!payload || payload.length === 0) return null;
    return (
        <div className="chart-tooltip">
            <div className="chart-tooltip-label">{String(label ?? '')}:00 UTC</div>
            <div className="chart-tooltip-value">
                {Number(payload[0].value ?? 0).toLocaleString()} bot-flagged posts
            </div>
        </div>
    );
}

function PostingCadenceChart({ buckets }: { buckets: PostingCadenceBucket[] }) {
    const total = buckets.reduce((s, b) => s + b.docCount, 0);
    if (total === 0) {
        return (
            <p className="text-sm text-muted" style={{ margin: 0 }}>
                No bot-flagged posts in this window to chart by hour.
            </p>
        );
    }
    const data = [...buckets].sort((a, b) => a.hour - b.hour).map((b) => ({ hour: b.hour, count: b.docCount }));
    return (
        <ResponsiveContainer width="100%" height={140}>
            <BarChart data={data} margin={{ top: 4, right: 4, left: 4, bottom: 4 }}>
                <XAxis
                    dataKey="hour"
                    tickFormatter={(h: number) => `${h}`}
                    interval={3}
                    tick={{ fontSize: 11, fill: 'var(--neutral-500)' }}
                    axisLine={{ stroke: 'var(--chart-grid)' }}
                    tickLine={false}
                />
                <Tooltip content={cadenceTooltip} cursor={{ fill: 'var(--bg-inset)' }} />
                <Bar dataKey="count" fill={COLORS.chartAccent} radius={[2, 2, 0, 0]} isAnimationActive={false} />
            </BarChart>
        </ResponsiveContainer>
    );
}

export function CoordinationEvidencePanel({ postingCadence }: { postingCadence: PostingCadenceBucket[] }) {
    return (
        <Card
            title="The chain of evidence"
            subtitle="How the detector narrows the sample down to suspect activity. Each stage is a lead, not a verdict."
            headerActions={
                <MethodPopover
                    description={
                        'The detector scores every scanned post from behavioral signals '
                        + '(posting rate, text repetition, account age). Flagged posts can '
                        + 'then be checked for lockstep behavior -- identical text, '
                        + 'synchronized timing -- and for shared link targets.'
                    }
                    limitations={[
                        'Some real users post in bot-like ways, and some real bots post like humans.',
                        'Coordination can be organic (a hashtag campaign) as well as automated.',
                    ]}
                />
            }
        >
            <div className="eyebrow mb-2">Bot-flagged posts by hour of day (UTC)</div>
            <PostingCadenceChart buckets={postingCadence} />
            <p className="text-sm text-muted mt-3" style={{ margin: 0 }}>
                Shared-link-domain detection is not part of this build's bot-activity data yet — see
                the automation rate and account-age distribution below for what else the detector
                currently measures.
            </p>
        </Card>
    );
}

export default CoordinationEvidencePanel;
