import { useEffect, useState } from 'react';
import {
    Card, CollapsibleInfo, DefinitionChip, EmptyState, EntityHeader,
    EntityHubLinks, ErrorState, GlobalTicker, LoadingCard, MethodPopover,
    Modal, PostCardList,
    RankedEntityList, ThreeWayColumn, ThreeWayGrid,
    TierRow, TopMetricsBlock, entityExternalUrl, entityLeanAccent,
    parseEntityParam, propagandaExampleToPostCard,
} from '../components/common';
import type { ColumnSorter, RankedEntity, TickerItem, TierRowDot } from '../components/common';
import { useDeepLinkParam } from '../services/deepLink';
import { saturationLevel } from '../services/glossary';
import { TechniqueExplorer } from './propaganda/TechniqueExplorer';
import { fetchPropaganda, fetchSnapshotStatus, type SnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { useFetch } from '../services/useFetch';
import { formatRefreshedAgo, getSnapshotTimestamp } from '../services/freshness';
import { formatPct } from '../services/format';
import { dedupeById } from '../services/dedupe';
import { COLORS } from '../theme';
import type {
    Filters, PropagandaEntityItem, PropagandaExample,
    PropagandaOverview, PropagandaSourceSplit,
    PropagandaTechniqueName,
} from '../types';


// --------------------------------------------------------------------------- //
//  Dictionaries                                                               //
// --------------------------------------------------------------------------- //

const TECHNIQUE_LABEL: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Loaded language',
    name_calling: 'Name-calling',
    ad_hominem: 'Ad hominem',
    appeal_to_fear: 'Appeal to fear',
    whataboutism: 'Whataboutism',
    doubt_casting: 'Doubt-casting',
};

// Shared so the 0-to-1 mean-score scale reads identically everywhere it
// appears (top metrics, ticker, entity cards, modal, source split).
const MEAN_SCORE_TITLE =
    'Average technique-intensity score across scored posts, from 0 (none) to 1 (saturated).';

// Rate-axis endpoints for the TierRow dots, which sit on a 0-100% scale.
const RATE_ENDPOINTS: [string, string] = ['0%', '100%'];

// --------------------------------------------------------------------------- //
//  Top metrics block — compact Bloomberg-style                                //
// --------------------------------------------------------------------------- //

function PropagandaTopMetrics({
    data,
    windowLabel,
}: {
    data: PropagandaOverview;
    windowLabel: string;
}) {
    const newsSplit = data.by_source.find((s) => s.label === 'News');
    const socialSplit = data.by_source.find((s) => s.label === 'Social Media');
    const gapPct = newsSplit && socialSplit
        ? Math.abs(newsSplit.flagged_rate_pct - socialSplit.flagged_rate_pct)
        : 0;
    const leanMoreFlagged = !newsSplit || !socialSplit
        ? 'neither'
        : newsSplit.flagged_rate_pct > socialSplit.flagged_rate_pct
            ? 'news'
            : 'social media';
    const topTech = data.by_technique[0];
    const topTechLabel = topTech
        ? (TECHNIQUE_LABEL[topTech.technique as PropagandaTechniqueName] || topTech.technique)
        : '—';

    const flaggedDotColor = data.propaganda_rate_pct > 20
        ? COLORS.negative
        : data.propaganda_rate_pct > 10
            ? COLORS.warning
            : COLORS.positive;

    const splitDots: TierRowDot[] = [];
    if (newsSplit) splitDots.push({
        pct: newsSplit.flagged_rate_pct,
        color: 'var(--neutral-600)',
        title: `News ${formatPct(newsSplit.flagged_rate_pct)}`,
    });
    if (socialSplit) splitDots.push({
        pct: socialSplit.flagged_rate_pct,
        color: COLORS.warning,
        title: `Social ${formatPct(socialSplit.flagged_rate_pct)}`,
    });

    return (
        <TopMetricsBlock
            eyebrow={`As of ${windowLabel}`}
            meta={`${data.total_eligible_docs.toLocaleString()} scored posts`}
        >
            <TierRow
                label="Flagged rate"
                value={formatPct(data.propaganda_rate_pct)}
                verb={`${data.flagged_docs.toLocaleString()} flagged · ${saturationLevel(data.mean_score)} technique saturation`}
                dots={[{
                    pct: data.propaganda_rate_pct,
                    color: flaggedDotColor,
                    title: `${formatPct(data.propaganda_rate_pct)} of scored posts were flagged`,
                }]}
                endpoints={RATE_ENDPOINTS}
            />
            <TierRow
                label="News vs social"
                value={gapPct > 0 ? `${gapPct.toFixed(1)} pts` : '—'}
                verb={leanMoreFlagged === 'neither'
                    ? 'no data'
                    : `${leanMoreFlagged} uses more techniques`}
                dots={splitDots}
                endpoints={RATE_ENDPOINTS}
            />
            <TierRow
                label="Top technique"
                value={topTech ? formatPct(topTech.pct_of_flagged_docs, { decimals: 0 }) : '—'}
                verb={topTech
                    ? `${topTechLabel} · in ${topTech.count.toLocaleString()} flagged posts`
                    : topTechLabel}
                dotPct={topTech?.pct_of_flagged_docs}
                dotColor={topTech ? COLORS.negative : undefined}
                endpoints={RATE_ENDPOINTS}
            />
        </TopMetricsBlock>
    );
}


// --------------------------------------------------------------------------- //
//  Ticker + reads-as-today                                                    //
// --------------------------------------------------------------------------- //

function buildPropagandaTickerItems(data: PropagandaOverview): TickerItem[] {
    const topTech = data.by_technique[0];
    const topTechLabel = topTech
        ? (TECHNIQUE_LABEL[topTech.technique as PropagandaTechniqueName] || topTech.technique)
        : '—';
    const rate = data.propaganda_rate_pct;
    return [
        {
            label: 'Flagged rate',
            value: formatPct(rate),
            tone: rate > 20 ? 'negative' : rate > 10 ? 'accent' : 'positive',
            emphasis: true,
            ariaLabel: `Flagged rate ${rate.toFixed(1)} percent`,
        },
        {
            label: 'Flagged posts',
            value: data.flagged_docs.toLocaleString(),
            hint: `of ${data.total_eligible_docs.toLocaleString()}`,
        },
        {
            label: 'Saturation',
            value: saturationLevel(data.mean_score),
            hint: `${data.mean_score.toFixed(2)} / 1`,
        },
        {
            label: 'Top technique',
            value: topTechLabel,
            hint: topTech ? `${formatPct(topTech.pct_of_flagged_docs, { decimals: 0 })} of flagged` : undefined,
        },
    ];
}

/** Reader-facing headline: which side leans harder on these techniques,
 *  plus which technique shows up most. Intentionally avoids the "X% of
 *  flagged posts" phrasing — with multiple techniques per post that
 *  figure can read >100% and confuse everyday readers. */
function readsAsToday(data: PropagandaOverview): string {
    const news = data.by_source.find((s) => s.label === 'News');
    const social = data.by_source.find((s) => s.label === 'Social Media');
    const topTech = data.by_technique[0];
    const topTechLabel = topTech
        ? (TECHNIQUE_LABEL[topTech.technique as PropagandaTechniqueName] || topTech.technique)
        : null;

    const parts: string[] = [];
    if (news && social) {
        const gap = news.flagged_rate_pct - social.flagged_rate_pct;
        if (Math.abs(gap) < 2) {
            parts.push('News and social media are leaning on persuasion techniques (loaded language, name-calling, fear appeals) at about the same rate.');
        } else if (gap > 0) {
            parts.push('News is leaning on persuasion techniques (loaded language, name-calling, fear appeals) more than social media right now.');
        } else {
            parts.push('Social media is leaning on persuasion techniques (loaded language, name-calling, fear appeals) more than news right now.');
        }
    }
    if (topTechLabel && topTech && topTech.count > 0) {
        parts.push(`${topTechLabel} is the technique we're seeing the most.`);
    }
    return parts.length > 0
        ? parts.join(' ')
        : 'No flagged posts in this window.';
}


// --------------------------------------------------------------------------- //
//  Propaganda leaderboard row — the grid form for this page is a ranked       //
//  list, not profile cards: rates sort naturally, and "who leans hardest"     //
//  reads faster as a leaderboard. Profile depth stays in the modal.           //
// --------------------------------------------------------------------------- //

function propagandaRateColor(ratePct: number): string {
    return ratePct > 25
        ? COLORS.negative
        : ratePct > 10
            ? COLORS.warning
            : 'var(--neutral-700)';
}

function toRankedEntity(
    item: PropagandaEntityItem,
    onOpen: (item: PropagandaEntityItem) => void,
): RankedEntity {
    const rateColor = propagandaRateColor(item.flagged_rate_pct);
    return {
        profile: item.entity_profile,
        rateValue: formatPct(item.flagged_rate_pct),
        ratePct: item.flagged_rate_pct,
        rateColor,
        detail: `${item.total_docs.toLocaleString()} scored`,
        onClick: () => onOpen(item),
    };
}


// --------------------------------------------------------------------------- //
//  Per-entity detail modal — reads the per-entity examples bucket the         //
//  backend builds in lockstep with PropagandaEntityItem.key (see              //
//  propaganda.py::_resolve_entity_key). Previously this filtered the          //
//  global ``examples`` list (capped at 10) and almost always read empty.     //
// --------------------------------------------------------------------------- //

function PropagandaEntityModal({
    item, examplesByEntity, onClose,
}: {
    item: PropagandaEntityItem;
    examplesByEntity: Record<string, PropagandaExample[]>;
    onClose: () => void;
}) {
    const profile = item.entity_profile;
    const rateColor = propagandaRateColor(item.flagged_rate_pct);
    const sourceUrl = entityExternalUrl(profile);
    const matching = dedupeById(examplesByEntity[item.key] ?? [], (ex) => ex.doc_id);

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle={`${item.flagged_docs.toLocaleString()} flagged · ${item.total_docs.toLocaleString()} scored`}
            accentColor={entityLeanAccent(profile)}
        >
            <EntityHeader profile={profile} />

            <div className="entity-modal-stats">
                <div>
                    <div className="eyebrow">Flagged rate</div>
                    <div className="metric-value" style={{ color: rateColor }}>
                        {formatPct(item.flagged_rate_pct)}
                    </div>
                </div>
                <div>
                    <div className="eyebrow">
                        <DefinitionChip entry="mean_score" label="Saturation" />
                    </div>
                    <div className="metric-value">{saturationLevel(item.mean_score)}</div>
                    <div className="text-xs text-muted" title={MEAN_SCORE_TITLE}>
                        {item.mean_score.toFixed(2)} on a 0-to-1 scale
                    </div>
                </div>
                <div>
                    <div className="eyebrow">Posts scored</div>
                    <div className="metric-value">{item.total_docs.toLocaleString()}</div>
                </div>
            </div>

            {(sourceUrl || profile.leanSource) && (
                <div className="entity-modal-links">
                    {sourceUrl && (
                        <a href={sourceUrl} target="_blank" rel="noreferrer">
                            Visit {profile.displayName} ↗
                        </a>
                    )}
                    {profile.leanSource && (
                        <span className="text-xs text-muted">
                            Political lean rated by: {profile.leanSource}
                        </span>
                    )}
                </div>
            )}

            <EntityHubLinks profile={profile} currentTab="propaganda" />

            <h3 className="card-title mt-4 mb-2">
                {matching.length === 0 ? 'No flagged examples in this window'
                    : matching.length === 1 ? 'Flagged example'
                    : `Flagged examples (${matching.length})`}
            </h3>
            <PostCardList
                posts={matching.map(propagandaExampleToPostCard)}
                sampleNote="Highlighted text is the verbatim evidence behind each technique flag."
                emptyNote="No flagged examples available for this entity in the current window."
            />
        </Modal>
    );
}


// --------------------------------------------------------------------------- //
//  Three-way grid — uses the by_news_outlet / by_official / by_general_public //
// --------------------------------------------------------------------------- //

const PROPAGANDA_SORTERS: ColumnSorter<PropagandaEntityItem>[] = [
    { label: 'flagged rate', compare: (a, b) => b.flagged_rate_pct - a.flagged_rate_pct },
    { label: 'posts scored', compare: (a, b) => b.total_docs - a.total_docs },
    { label: 'name', compare: (a, b) => a.entity_profile.displayName.localeCompare(b.entity_profile.displayName) },
];

function ThreeWayEntityGrid({
    data, onOpen,
}: {
    data: PropagandaOverview;
    onOpen: (item: PropagandaEntityItem) => void;
}) {
    const ranked = (label: string) => (items: PropagandaEntityItem[]) => (
        <RankedEntityList
            items={items.map((it) => toRankedEntity(it, onOpen))}
            ariaLabel={label}
        />
    );

    return (
        <ThreeWayGrid>
            <ThreeWayColumn
                header="The News"
                byline="News outlets ranked by the share of their posts flagged for these techniques"
                empty="No news articles scored yet."
                items={data.by_news_outlet ?? []}
                renderItems={ranked('News outlets by flagged rate')}
                sorters={PROPAGANDA_SORTERS}
            />
            <ThreeWayColumn
                header="Politicians & Officials"
                byline="Tracked officeholders ranked by the share of their posts flagged for these techniques"
                empty="No officials scored yet."
                items={data.by_official ?? []}
                renderItems={ranked('Officials by flagged rate')}
                sorters={PROPAGANDA_SORTERS}
            />
            <ThreeWayColumn
                header="The Public"
                byline="Political subreddits, plus X users we don't track individually, ranked by flagged share"
                empty="No social posts scored yet."
                items={data.by_general_public ?? []}
                renderItems={ranked('Public sources by flagged rate')}
                sorters={PROPAGANDA_SORTERS}
            />
        </ThreeWayGrid>
    );
}


// --------------------------------------------------------------------------- //
//  News vs Social split                                                       //
// --------------------------------------------------------------------------- //

const SPLIT_DOT_COLOR: Record<string, string> = {
    'News':         'var(--neutral-600)',
    'Social Media': COLORS.warning,
};

function NewsVsSocialCard({ splits }: { splits: PropagandaSourceSplit[] }) {
    return (
        <Card
            title="News vs. social media"
            subtitle="Flagged rate and mean score for news outlets vs. social posts"
        >
            <div className="source-split-rows">
                {splits.map((s) => (
                    <div key={s.label} className="source-split-row">
                        <span className="source-split-row-label">
                            <span
                                className="source-split-dot"
                                style={{ background: SPLIT_DOT_COLOR[s.label] ?? 'var(--neutral-500)' }}
                                aria-hidden
                            />
                            {s.label}
                        </span>
                        <span className="source-split-row-total">
                            {s.total_docs.toLocaleString()} posts
                        </span>
                        <span className="source-split-row-rate">
                            {formatPct(s.flagged_rate_pct)}
                            <span className="source-split-row-sub">flagged</span>
                        </span>
                        <span className="source-split-row-score" title={MEAN_SCORE_TITLE}>
                            {saturationLevel(s.mean_score)}
                            <span className="source-split-row-sub">
                                saturation · {s.mean_score.toFixed(2)} / 1
                            </span>
                        </span>
                    </div>
                ))}
            </div>
        </Card>
    );
}


// --------------------------------------------------------------------------- //
//  How this works                                                             //
// --------------------------------------------------------------------------- //

function HowThisWorks() {
    return (
        <CollapsibleInfo>
            <p className="text-sm">
                Each post is scored for six rhetorical techniques: loaded language, name-calling,
                ad hominem, appeal to fear, whataboutism, and doubt-casting. The model has to
                quote a verbatim phrase from the source as evidence, so flags aren't hand-waved.
            </p>
            <p className="text-sm">
                A high flagged rate means the sample leaned on these techniques — it's a measure
                of technique density, not authorial intent. Straight reporting reads as unflagged;
                opinion and activist content tends to light up.
            </p>
        </CollapsibleInfo>
    );
}


// --------------------------------------------------------------------------- //
//  Page                                                                       //
// --------------------------------------------------------------------------- //

interface PropagandaProps {
    filters: Filters;
}

function Propaganda({ filters }: PropagandaProps) {
    const [activeEntity, setActiveEntity] = useState<PropagandaEntityItem | null>(null);
    // Cross-page entity deep link ("#propaganda?entity=outlet:nypost.com").
    const [entityParam, setEntityParam] = useDeepLinkParam('entity');
    const { data, loading, error, refetch } = useFetch<PropagandaOverview>(
        () => fetchPropaganda(filters.timeRange),
        [filters.timeRange],
        `propaganda:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatus>(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );

    // Resolve entity= once data lands; unknown entities clear the param.
    useEffect(() => {
        if (!data || !entityParam) return;
        const target = parseEntityParam(entityParam);
        if (target) {
            const lists = [data.by_news_outlet, data.by_official, data.by_general_public];
            for (const list of lists) {
                const item = (list ?? []).find(
                    (it) => it.kind === target.kind && it.key === target.key,
                );
                if (item) {
                    setActiveEntity(item);
                    return;
                }
            }
        }
        setEntityParam(null);
    }, [data, entityParam, setEntityParam]);

    if (error) return <ErrorState message={error.message} onRetry={refetch} />;
    if (loading) {
        return (
            <div className="flex flex-col gap-4">
                <LoadingCard />
                <LoadingCard />
            </div>
        );
    }
    // Early-return only when the fetch itself yielded nothing. Zero flagged
    // docs is a legitimate state that the page should render honestly —
    // GlobalTicker + three-way grid with per-column empty copy, same as
    // Tone and Narratives do. Hiding the frame on empty data was
    // misleading: a stalled propaganda-detection cron looked identical to
    // a clean run with nothing to flag.
    if (!data) return <EmptyState title="No rhetoric analysis available for this window." />;

    const windowLabel = formatTimeWindow(filters.timeRange);
    const tickerItems = buildPropagandaTickerItems(data);
    const refreshed = formatRefreshedAgo(
        getSnapshotTimestamp(snapshotStatus, `propaganda_${filters.timeRange}`),
    );

    return (
        <>
            <div className="dashboard-grid">
                <div className="col-span-12">
                    <GlobalTicker
                        items={tickerItems}
                        refreshed={refreshed}
                        ariaLabel="Propaganda overview"
                        legend={
                            <MethodPopover
                                title="How to read these numbers"
                                description={
                                    'We flag six persuasion techniques (loaded language, name-calling, fear '
                                    + 'appeals, ad hominem, whataboutism, doubt-casting) in political posts. A '
                                    + "flag measures rhetorical style — not truth, intent, or whether the post is "
                                    + "'propaganda' in the everyday sense. Mean score runs 0 (none) to 1 (saturated)."
                                }
                            />
                        }
                    />
                </div>

                <div className="col-span-12">
                    <div className="reads-as-today">
                        <span className="eyebrow reads-as-today-eyebrow">
                            {asOfTodayEyebrow(filters.timeRange)}
                        </span>
                        <p className="lead" style={{ margin: 0 }}>{readsAsToday(data)}</p>
                        <p className="text-xs text-muted" style={{ margin: 'var(--space-2) 0 0' }}>
                            We flag six persuasion techniques in political posts. A flag measures rhetorical
                            style — not truth, intent, or whether the post is "propaganda" in the everyday sense.
                        </p>
                    </div>
                </div>

                <div className="col-span-12">
                    <PropagandaTopMetrics data={data} windowLabel={windowLabel} />
                </div>

                {/* Always render the three-way frame, even when every tier is
                    empty. Per-column empty copy inside ThreeWayEntityGrid is
                    the honest shape — matches Tone/Narratives/Bot. */}
                <div className="col-span-12">
                    <ThreeWayEntityGrid data={data} onOpen={setActiveEntity} />
                </div>

                {/* Technique explorer — the page's signature interaction:
                    pick a technique, read its evidence. */}
                <div className="col-span-12">
                    <TechniqueExplorer
                        techniques={data.by_technique}
                        examples={data.examples}
                    />
                </div>

                {/* News-vs-social is a 2-row mini table — pair it with the
                    How-this-works panel instead of two stacked full-bleed rows. */}
                <div className="col-span-5">
                    <NewsVsSocialCard splits={data.by_source} />
                </div>

                <div className="col-span-7">
                    <HowThisWorks />
                </div>
            </div>

            {activeEntity && (
                <PropagandaEntityModal
                    item={activeEntity}
                    examplesByEntity={data.examples_by_entity ?? {}}
                    onClose={() => {
                        setActiveEntity(null);
                        if (entityParam) setEntityParam(null);
                    }}
                />
            )}
        </>
    );
}

export default Propaganda;
