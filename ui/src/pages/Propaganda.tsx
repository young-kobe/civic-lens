import {
    Card, CollapsibleInfo, EmptyState, ErrorState, GlobalTicker, LoadingCard,
    MethodPopover, RangeCaption, SampleCardList,
} from '../components/common';
import type { TickerItem } from '../components/common';
import { saturationLevel } from '../services/glossary';
import { TechniqueExplorer } from './propaganda/TechniqueExplorer';
import { fetchPropaganda, fetchSnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { useFetch } from '../services/useFetch';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPct } from '../services/format';
import { dedupeById } from '../services/dedupe';
import type {
    Filters, PropagandaOverview as PropagandaOverviewType, PropagandaTechniqueName,
    SnapshotStatusResponse,
} from '../types';

// --------------------------------------------------------------------------- //
//  Phase 10 adaptation note: the pre-redesign per-entity flagged-rate         //
//  leaderboard, three-way (News/Officials/Public) entity grid, and per-doc    //
//  technique breakdown on each example have no equivalent in the strictly-    //
//  live /propaganda response (PropagandaOverviewModel carries only the       //
//  overall rate, by-technique/by-source/by-party rollups, and plain          //
//  SampleDocModel examples with no per-doc technique tie-in) -- removed      //
//  rather than faked.                                                        //
// --------------------------------------------------------------------------- //

const TECHNIQUE_LABEL: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Loaded language', name_calling: 'Name-calling', ad_hominem: 'Ad hominem',
    appeal_to_fear: 'Appeal to fear', whataboutism: 'Whataboutism', doubt_casting: 'Doubt-casting',
};

function buildTickerItems(data: PropagandaOverviewType): TickerItem[] {
    const topTech = data.byTechnique[0];
    const topTechLabel = topTech
        ? (TECHNIQUE_LABEL[topTech.technique as PropagandaTechniqueName] || topTech.technique)
        : '—';
    const rate = data.flaggedRatePct;
    return [
        {
            label: 'Flagged rate', value: formatPct(rate),
            tone: rate > 20 ? 'negative' : rate > 10 ? 'warning' : 'positive',
            emphasis: true,
        },
        { label: 'Flagged posts', value: data.flaggedDocs.toLocaleString(), hint: `of ${data.totalEligibleDocs.toLocaleString()}` },
        { label: 'Saturation', value: saturationLevel(data.meanScore), hint: `${data.meanScore.toFixed(2)} / 1` },
        { label: 'Top technique', value: topTechLabel },
    ];
}

function readsAsToday(data: PropagandaOverviewType): string {
    const news = data.bySource.find((s) => s.source === 'news');
    const social = data.bySource.find((s) => s.source === 'social');
    const topTech = data.byTechnique[0];
    const topTechLabel = topTech
        ? (TECHNIQUE_LABEL[topTech.technique as PropagandaTechniqueName] || topTech.technique)
        : null;
    const parts: string[] = [];
    if (news && social) {
        const gap = news.flaggedRatePct - social.flaggedRatePct;
        if (Math.abs(gap) < 2) {
            parts.push('News and social media are leaning on persuasion techniques at about the same rate.');
        } else if (gap > 0) {
            parts.push('News is leaning on persuasion techniques more than social media right now.');
        } else {
            parts.push('Social media is leaning on persuasion techniques more than news right now.');
        }
    }
    if (topTechLabel && topTech && topTech.count > 0) {
        parts.push(`${topTechLabel} is the technique we're seeing the most.`);
    }
    return parts.length > 0 ? parts.join(' ') : 'No flagged posts in this window.';
}

function BySourceCard({ data }: { data: PropagandaOverviewType }) {
    return (
        <Card title="News vs social" subtitle="Flagged rate and technique saturation by source type.">
            <div className="desk-table-wrap">
                <table className="table">
                    <thead>
                        <tr><th>Source</th><th className="num">Scored</th><th className="num">Flagged</th><th className="num">Rate</th><th className="num">Mean score</th></tr>
                    </thead>
                    <tbody>
                        {data.bySource.map((s) => (
                            <tr key={s.source}>
                                <td style={{ textTransform: 'capitalize' }}>{s.source}</td>
                                <td className="num">{s.totalDocs.toLocaleString()}</td>
                                <td className="num">{s.flaggedDocs.toLocaleString()}</td>
                                <td className="num">{formatPct(s.flaggedRatePct)}</td>
                                <td className="num">{s.meanScore.toFixed(2)}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </Card>
    );
}

function HowThisWorks() {
    return (
        <CollapsibleInfo>
            <p className="text-sm">
                Each post is scored for six rhetorical techniques: loaded language, name-calling,
                ad hominem, appeal to fear, whataboutism, and doubt-casting. The model has to quote a
                verbatim phrase from the source as evidence, so flags aren't hand-waved.
            </p>
            <p className="text-sm">
                A high flagged rate means the sample leaned on these techniques — it's a measure of
                technique density, not authorial intent.
            </p>
        </CollapsibleInfo>
    );
}

interface PropagandaProps {
    filters: Filters;
}

function Propaganda({ filters }: PropagandaProps) {
    const { data, loading, error, refetch } = useFetch<PropagandaOverviewType>(
        () => fetchPropaganda(filters.timeRange),
        [filters.timeRange],
        `propaganda:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatusResponse>(
        () => fetchSnapshotStatus(), [], 'snapshot-status',
    );

    if (error) return <ErrorState message={error.message} onRetry={refetch} />;
    if (loading) return <div className="flex flex-col gap-4"><LoadingCard /><LoadingCard /></div>;
    if (!data) return <EmptyState title="No rhetoric analysis available for this window." />;

    const windowLabel = formatTimeWindow(filters.timeRange);
    const refreshed = formatRefreshedAgo(pipelineRunTimestamp(snapshotStatus));
    const examples = dedupeById(data.examples, (e) => e.docId);

    return (
        <div className="dashboard-grid">
            <div className="col-span-12">
                <GlobalTicker
                    items={buildTickerItems(data)}
                    refreshed={refreshed}
                    ariaLabel="Propaganda overview"
                    legend={
                        <MethodPopover
                            title="How to read these numbers"
                            description={
                                'We flag six persuasion techniques in political posts. A flag measures '
                                + "rhetorical style — not truth, intent, or whether the post is 'propaganda' "
                                + 'in the everyday sense. Mean score runs 0 (none) to 1 (saturated).'
                            }
                        />
                    }
                />
                <RangeCaption range={data.range} />
            </div>

            <div className="col-span-12">
                <div className="reads-as-today">
                    <span className="eyebrow reads-as-today-eyebrow">{asOfTodayEyebrow(filters.timeRange)} · as of {windowLabel}</span>
                    <p className="lead" style={{ margin: 0 }}>{readsAsToday(data)}</p>
                </div>
            </div>

            <div className="col-span-5"><BySourceCard data={data} /></div>
            <div className="col-span-7">
                <TechniqueExplorer techniques={data.byTechnique} parties={data.byParty} />
            </div>

            <div className="col-span-12">
                <Card title="Flagged examples">
                    <SampleCardList
                        samples={examples}
                        sampleNote="Flagged posts, highest technique-density first — a sample, not a complete feed."
                        emptyNote="No flagged examples in this window."
                    />
                </Card>
            </div>

            <div className="col-span-12"><HowThisWorks /></div>
        </div>
    );
}

export default Propaganda;
