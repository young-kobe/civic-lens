import { useState } from 'react';
import {
    Card, CollapsibleInfo, EmptyState, ErrorState, GlobalTicker, LoadingCard,
    MethodPopover, RangeCaption, RankedEntityList, SampleCardList,
    ThreeWayColumn, ThreeWayGrid,
} from '../components/common';
import type { RankedEntity, TickerItem } from '../components/common';
import { saturationLevel } from '../services/glossary';
import { TechniqueExplorer } from './propaganda/TechniqueExplorer';
import { PropagandaEntityModal } from './propaganda/PropagandaEntityModal';
import { entityGroupLabel } from './propaganda/entityGroup';
import { fetchPropaganda, fetchSnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { useFetch } from '../services/useFetch';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPct } from '../services/format';
import { dedupeById } from '../services/dedupe';
import { COLORS } from '../theme';
import type {
    Filters, PropagandaEntityRow, PropagandaOverview as PropagandaOverviewType,
    PropagandaTechniqueName, PropagandaTierSplit, SnapshotStatusResponse,
} from '../types';

// --------------------------------------------------------------------------- //
//  Restored pre-cutover-main page geometry (docs/todos/                     //
//  ui-feature-restoration.md): /propaganda now carries byEntity (per-outlet/ //
//  subreddit/author-tier rows, top 20) and byTier (the real News/Officials/  //
//  Public three-way split), so the leaderboard and grid below rank actual    //
//  entities and tiers again instead of the degraded bySource (news vs        //
//  social) two-way stand-in. byParty still has its own section inside        //
//  TechniqueExplorer, so it isn't duplicated here. Per-entity example lists  //
//  still have no equivalent (see PropagandaEntityModal).                    //
// --------------------------------------------------------------------------- //

const TECHNIQUE_LABEL: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Loaded language', name_calling: 'Name-calling', ad_hominem: 'Ad hominem',
    appeal_to_fear: 'Appeal to fear', whataboutism: 'Whataboutism', doubt_casting: 'Doubt-casting',
};

const TIER_LABEL: Record<PropagandaTierSplit['group'], string> = {
    news: 'News', officials: 'Officials', public: 'Public',
};

function propagandaRateColor(ratePct: number): string {
    return ratePct > 25 ? COLORS.negative : ratePct > 10 ? COLORS.warning : 'var(--neutral-700)';
}

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
    const news = data.byTier.find((t) => t.group === 'news');
    const officials = data.byTier.find((t) => t.group === 'officials');
    const topTech = data.byTechnique[0];
    const topTechLabel = topTech
        ? (TECHNIQUE_LABEL[topTech.technique as PropagandaTechniqueName] || topTech.technique)
        : null;
    const parts: string[] = [];
    if (news && officials) {
        const gap = news.flaggedRatePct - officials.flaggedRatePct;
        if (Math.abs(gap) < 2) {
            parts.push('News and officials are leaning on persuasion techniques at about the same rate.');
        } else if (gap > 0) {
            parts.push('News is leaning on persuasion techniques more than officials right now.');
        } else {
            parts.push('Officials are leaning on persuasion techniques more than news right now.');
        }
    }
    if (topTechLabel && topTech && topTech.count > 0) {
        parts.push(`${topTechLabel} is the technique we're seeing the most.`);
    }
    return parts.length > 0 ? parts.join(' ') : 'No flagged posts in this window.';
}

// --------------------------------------------------------------------------- //
//  Top flagged leaderboard — restored per-entity ranking (byEntity, top 20). //
// --------------------------------------------------------------------------- //

function toRankedEntity(item: PropagandaEntityRow, onOpen: (item: PropagandaEntityRow) => void): RankedEntity {
    const ratePct = item.flaggedShare * 100;
    return {
        entity: { kind: null, displayName: item.displayName },
        description: entityGroupLabel(item.group),
        rateValue: formatPct(ratePct),
        ratePct,
        rateColor: propagandaRateColor(ratePct),
        detail: `${Math.round(item.flaggedShare * item.docCount).toLocaleString()} of ${item.docCount.toLocaleString()} scored`,
        onClick: () => onOpen(item),
    };
}

function TopFlaggedLeaderboard({ data, onOpen }: { data: PropagandaOverviewType; onOpen: (item: PropagandaEntityRow) => void }) {
    return (
        <Card
            title="Most flagged sources"
            subtitle="Registry entities (outlets, subreddits, elected officials) ranked by flagged share, top 20."
            headerActions={
                <MethodPopover
                    description={
                        'Flagged share = flagged posts / scored posts for this entity. '
                        + 'A density measure, not intent.'
                    }
                />
            }
        >
            {data.byEntity.length > 0 ? (
                <RankedEntityList
                    items={[...data.byEntity]
                        .sort((a, b) => b.flaggedShare - a.flaggedShare)
                        .map((it) => toRankedEntity(it, onOpen))}
                    ariaLabel="Entities by flagged share"
                />
            ) : (
                <p className="text-sm text-muted">No registry entity had scored posts in this window.</p>
            )}
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Three-way grid over byTier — the real News/Officials/Public split         //
//  restored (docs/todos/ui-feature-restoration.md), replacing the previous   //
//  two-way bySource (news vs social) stand-in.                              //
// --------------------------------------------------------------------------- //

function ByTierCardBody({ item }: { item: PropagandaTierSplit }) {
    return (
        <div className="entity-card entity-card-empty" style={{ cursor: 'default' }}>
            <div className="entity-card-head">
                <span className="entity-avatar entity-avatar-mono" aria-hidden>
                    {TIER_LABEL[item.group].charAt(0)}
                </span>
                <div className="entity-card-head-text">
                    <h4 className="entity-card-name">{TIER_LABEL[item.group]}</h4>
                </div>
            </div>
            <div className="entity-card-stats">
                <span className="entity-card-stat">
                    <span className="entity-card-stat-value" style={{ color: propagandaRateColor(item.flaggedRatePct) }}>
                        {formatPct(item.flaggedRatePct)}
                    </span>
                    <span className="entity-card-stat-label">Flagged rate</span>
                </span>
                <span className="entity-card-stat">
                    <span className="entity-card-stat-value">{item.totalDocs.toLocaleString()}</span>
                    <span className="entity-card-stat-label">Scored</span>
                </span>
                <span className="entity-card-stat">
                    <span className="entity-card-stat-value">{saturationLevel(item.meanScore)}</span>
                    <span className="entity-card-stat-label">Saturation</span>
                </span>
            </div>
        </div>
    );
}

function ByTierGrid({ data }: { data: PropagandaOverviewType }) {
    const news = data.byTier.filter((t) => t.group === 'news');
    const officials = data.byTier.filter((t) => t.group === 'officials');
    const publicTier = data.byTier.filter((t) => t.group === 'public');
    return (
        <ThreeWayGrid>
            <ThreeWayColumn
                header="News"
                byline="Flagged rate and technique saturation across news posts"
                empty="No news articles scored yet."
                items={news}
                renderItem={(item) => <ByTierCardBody key={item.group} item={item} />}
            />
            <ThreeWayColumn
                header="Officials"
                byline="Flagged rate and technique saturation across elected-official posts"
                empty="No official posts scored yet."
                items={officials}
                renderItem={(item) => <ByTierCardBody key={item.group} item={item} />}
            />
            <ThreeWayColumn
                header="Public"
                byline="Flagged rate and technique saturation across all other posts"
                empty="No public posts scored yet."
                items={publicTier}
                renderItem={(item) => <ByTierCardBody key={item.group} item={item} />}
            />
        </ThreeWayGrid>
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
    const [activeEntity, setActiveEntity] = useState<PropagandaEntityRow | null>(null);
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
        <>
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

                <div className="col-span-5">
                    <TopFlaggedLeaderboard data={data} onOpen={setActiveEntity} />
                </div>
                <div className="col-span-7">
                    <TechniqueExplorer techniques={data.byTechnique} parties={data.byParty} />
                </div>

                <div className="col-span-12">
                    <ByTierGrid data={data} />
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

            {activeEntity && (
                <PropagandaEntityModal item={activeEntity} onClose={() => setActiveEntity(null)} />
            )}
        </>
    );
}

export default Propaganda;
