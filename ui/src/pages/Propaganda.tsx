import { useEffect, useState } from 'react';
import {
    Card, CollapsibleInfo, DefinitionChip, EmptyState, EntityHeader,
    EntityHubLinks, ErrorState, GlobalTicker, LoadingCard, MethodPopover,
    Modal, PostCardList,
    RankedEntityList, TECHNIQUE_LABEL, ThreeWayColumn, ThreeWayGrid,
    entityExternalUrl,
    parseEntityParam, propagandaExampleToPostCard,
} from '../components/common';
import type { ColumnSorter, RankedEntity, TickerItem } from '../components/common';
import { useDeepLinkParam } from '../services/deepLink';
import { saturationLevel } from '../services/glossary';
import { TechniqueExplorer } from './propaganda/TechniqueExplorer';
import { fetchPropaganda, fetchSnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { useFetch } from '../services/useFetch';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPct } from '../services/format';
import { dedupeById } from '../services/dedupe';
import { COLORS } from '../theme';
import type {
    Filters, PropagandaEntityItem, PropagandaExample,
    PropagandaOverview, PropagandaTechniqueName, SnapshotStatusResponse,
} from '../types';

// Shared so the "saturation" naming and its 0-to-1 scale read identically
// everywhere they appear (top metrics, ticker, entity cards, modal, source
// split) rather than switching between "saturation" and "mean score".
const SATURATION_TITLE =
    'How saturated flagged posts are with persuasion techniques, from 0 (none) to 1 (wall-to-wall).';

// --------------------------------------------------------------------------- //
//  Ticker + reads-as-today                                                    //
// --------------------------------------------------------------------------- //

function buildPropagandaTickerItems(data: PropagandaOverview): TickerItem[] {
    const topTech = data.byTechnique[0];
    const topTechLabel = topTech
        ? (TECHNIQUE_LABEL[topTech.technique as PropagandaTechniqueName] || topTech.technique)
        : '—';
    const rate = data.flaggedRatePct;
    return [
        {
            label: 'Flagged rate',
            value: formatPct(rate),
            tone: rate > 20 ? 'negative' : rate > 10 ? 'warning' : 'positive',
            emphasis: true,
            ariaLabel: `Flagged rate ${rate.toFixed(1)} percent`,
        },
        {
            label: 'Flagged posts',
            value: data.flaggedDocs.toLocaleString(),
            hint: `of ${data.totalEligibleDocs.toLocaleString()}`,
        },
        {
            label: 'Saturation',
            value: saturationLevel(data.meanScore),
            hint: `${data.meanScore.toFixed(2)} / 1`,
        },
        {
            label: 'Top technique',
            value: topTechLabel,
            hint: topTech ? `${formatPct(topTech.pctOfFlaggedDocs, { decimals: 0 })} of flagged` : undefined,
        },
    ];
}

/** Reader-facing headline: which side leans harder on these techniques,
 *  plus which technique shows up most. Intentionally avoids the "X% of
 *  flagged posts" phrasing — with multiple techniques per post that
 *  figure can read >100% and confuse everyday readers. */
function readsAsToday(data: PropagandaOverview): string {
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
    const rateColor = propagandaRateColor(item.flaggedRatePct);
    return {
        profile: item.entityProfile,
        rateValue: formatPct(item.flaggedRatePct),
        ratePct: item.flaggedRatePct,
        rateColor,
        detail: `${item.totalDocs.toLocaleString()} scored`,
        onClick: () => onOpen(item),
    };
}

// A source needs at least this many FLAGGED posts to rank as an "offender" — a
// lone flagged post would otherwise read as a 100% rate at the top of the
// board. This is the only hard gate: a genuine small-sample offender (e.g. an
// official with 8 scored posts) still ranks by its true rate, but carries the
// low-sample caveat below so it isn't read as a stable rate. Previously a flat
// 10-scored-post floor hid these entirely, which made the card contradict the
// per-tier columns underneath it (they show sub-10 sources sorted by rate).
const MIN_OFFENDER_FLAGGED = 2;

// Below this many scored posts, a source's flagged rate can swing on one or two
// posts, so ranking rows at/under it show a visible "low sample" caveat. Mirrors
// the "N scored" context already shown on the detail columns.
const LOW_SAMPLE_OFFENDER_DOCS = 10;

/** The 3 highest flagged-rate sources across ALL tiers in the window (catch-all
 *  buckets and single-flag sources excluded; low-sample sources are kept but
 *  caveated). */
function topFlaggedOffenders(data: PropagandaOverview): PropagandaEntityItem[] {
    return [
        ...(data.byNewsOutlet ?? []),
        ...(data.byOfficial ?? []),
        ...(data.byGeneralPublic ?? []),
    ]
        .filter((it) => it.kind !== 'catch_all' && it.flaggedDocs >= MIN_OFFENDER_FLAGGED)
        .sort((a, b) => (b.flaggedRatePct - a.flaggedRatePct)
            || (b.flaggedDocs - a.flaggedDocs))
        .slice(0, 3);
}

const OFFENDER_BLURB_MAX = 96;

/** Leaderboard row with an under-name explanation: who they are (blurb) and why
 *  they rank (flagged count + technique saturation). */
function toOffenderRow(
    item: PropagandaEntityItem,
    onOpen: (item: PropagandaEntityItem) => void,
): RankedEntity {
    const blurb = item.entityProfile.blurb;
    const clamped = blurb && blurb.length > OFFENDER_BLURB_MAX
        ? `${blurb.slice(0, OFFENDER_BLURB_MAX).trimEnd()}…`
        : blurb;
    const lowSample = item.totalDocs < LOW_SAMPLE_OFFENDER_DOCS;
    return {
        ...toRankedEntity(item, onOpen),
        detail: undefined,
        description: (
            <>
                {clamped && <span className="ranked-entity-blurb">{clamped}</span>}
                <span className="ranked-entity-why">
                    {item.flaggedDocs.toLocaleString()} of {item.totalDocs.toLocaleString()} posts
                    {' '}flagged · {saturationLevel(item.meanScore)}{' '}
                    <DefinitionChip entry="mean_score" label="technique saturation" />
                    {lowSample && (
                        <span
                            className="ranked-entity-lowsample"
                            title="Too few scored posts for a stable rate — one or two posts can move it."
                        >
                            {' · '}low sample
                        </span>
                    )}
                </span>
            </>
        ),
    };
}

/** Top-3 flagged-rate leaderboard — same ranked row-card as the three-way
 *  columns. Replaces the "As of last N" top-metrics block, scoped to the date
 *  range selector (the payload is fetched per window). */
function TopFlaggedLeaderboard({
    data, windowLabel, onOpen,
}: {
    data: PropagandaOverview;
    windowLabel: string;
    onOpen: (item: PropagandaEntityItem) => void;
}) {
    const top = topFlaggedOffenders(data);
    return (
        <Card
            title="Highest flagged rates"
            subtitle={`The highest flagged-rate sources across news, officials, and the public · as of ${windowLabel}`}
            headerActions={
                <MethodPopover
                    description={
                        'The three sources with the highest flagged rate (flagged posts / scored '
                        + 'posts) in the selected window, across all tiers. Catch-all buckets and '
                        + `sources with fewer than ${MIN_OFFENDER_FLAGGED} flagged posts are excluded so `
                        + "a one-off flag can't top the board. Sources with fewer than "
                        + `${LOW_SAMPLE_OFFENDER_DOCS} scored posts still rank but are marked low sample. `
                        + 'A density measure, not intent.'
                    }
                />
            }
        >
            {top.length > 0 ? (
                <RankedEntityList
                    items={top.map((it) => toOffenderRow(it, onOpen))}
                    ariaLabel="Highest flagged-rate sources across all groups"
                />
            ) : (
                <p className="text-sm text-muted">
                    No source had enough flagged posts to rank in this window.
                </p>
            )}
        </Card>
    );
}


// --------------------------------------------------------------------------- //
//  Per-entity detail modal — reads the per-entity examples bucket the         //
//  backend builds in lockstep with PropagandaEntityItem.key (see              //
//  analysis/src/api/queries/propaganda.py). Filters the global examples       //
//  bucket by the entity's own key rather than showing the window-wide list.  //
// --------------------------------------------------------------------------- //

function PropagandaEntityModal({
    item, examplesByEntity, onClose,
}: {
    item: PropagandaEntityItem;
    examplesByEntity: Record<string, PropagandaExample[]>;
    onClose: () => void;
}) {
    const profile = item.entityProfile;
    const rateColor = propagandaRateColor(item.flaggedRatePct);
    const sourceUrl = entityExternalUrl(profile);
    const matching = dedupeById(examplesByEntity[item.key] ?? [], (ex) => ex.docId);

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle={`${item.flaggedDocs.toLocaleString()} flagged · ${item.totalDocs.toLocaleString()} scored`}
        >
            <EntityHeader profile={profile} />

            <div className="entity-modal-stats">
                <div>
                    <div className="eyebrow">Flagged rate</div>
                    <div className="metric-value" style={{ color: rateColor }}>
                        {formatPct(item.flaggedRatePct)}
                    </div>
                </div>
                <div>
                    <div className="eyebrow">
                        <DefinitionChip entry="mean_score" label="Saturation" />
                    </div>
                    <div className="metric-value">{saturationLevel(item.meanScore)}</div>
                    <div className="text-xs text-muted" title={SATURATION_TITLE}>
                        {item.meanScore.toFixed(2)} on a 0-to-1 scale
                    </div>
                </div>
                <div>
                    <div className="eyebrow">Posts scored</div>
                    <div className="metric-value">{item.totalDocs.toLocaleString()}</div>
                </div>
            </div>

            {(sourceUrl || profile.leanSource) && (
                <div className="entity-modal-links">
                    {sourceUrl && (
                        <a href={sourceUrl} target="_blank" rel="noreferrer">
                            Visit {profile.displayName}
                        </a>
                    )}
                    {profile.leanSource && (
                        <span className="text-xs text-muted">
                            Political lean rated by: {profile.leanSource}
                        </span>
                    )}
                </div>
            )}

            <EntityHubLinks entityId={profile.entityId ?? null} currentTab="propaganda" />

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
//  Three-way grid — uses the byNewsOutlet / byOfficial / byGeneralPublic      //
// --------------------------------------------------------------------------- //

const PROPAGANDA_SORTERS: ColumnSorter<PropagandaEntityItem>[] = [
    { label: 'flagged rate', compare: (a, b) => b.flaggedRatePct - a.flaggedRatePct },
    { label: 'posts scored', compare: (a, b) => b.totalDocs - a.totalDocs },
    { label: 'name', compare: (a, b) => a.entityProfile.displayName.localeCompare(b.entityProfile.displayName) },
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
                items={data.byNewsOutlet ?? []}
                renderItems={ranked('News outlets by flagged rate')}
                sorters={PROPAGANDA_SORTERS}
            />
            <ThreeWayColumn
                header="Politicians & Officials"
                byline="Tracked officeholders ranked by the share of their posts flagged for these techniques"
                empty="No officials scored yet."
                items={data.byOfficial ?? []}
                renderItems={ranked('Officials by flagged rate')}
                sorters={PROPAGANDA_SORTERS}
            />
            <ThreeWayColumn
                header="The Public"
                byline="Political subreddits, plus X users we don't track individually, ranked by flagged share"
                empty="No social posts scored yet."
                items={data.byGeneralPublic ?? []}
                renderItems={ranked('Public sources by flagged rate')}
                sorters={PROPAGANDA_SORTERS}
            />
        </ThreeWayGrid>
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
    const { data: snapshotStatus } = useFetch<SnapshotStatusResponse>(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );

    // Resolve entity= once data lands; unknown entities clear the param.
    useEffect(() => {
        if (!data || !entityParam) return;
        const target = parseEntityParam(entityParam);
        if (target == null) {
            setEntityParam(null);
            return;
        }
        const lists = [data.byNewsOutlet, data.byOfficial, data.byGeneralPublic];
        for (const list of lists) {
            const item = (list ?? []).find((it) => it.entityProfile.entityId === target);
            if (item) {
                setActiveEntity(item);
                return;
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
    const refreshed = formatRefreshedAgo(pipelineRunTimestamp(snapshotStatus));

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
                                    + "'propaganda' in the everyday sense. Saturation runs 0 (none) to 1 (wall-to-wall)."
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

                {/* One row: the "As of last N days" top-metrics block beside the
                    density constellation. Both read from the same windowed
                    `data`, so they filter in step; pairing them condenses two
                    full-width rows into one. Technique chips filter the
                    constellation and carry the ?technique= deep link. */}
                <div className="col-span-5">
                    <TopFlaggedLeaderboard data={data} windowLabel={windowLabel} onOpen={setActiveEntity} />
                </div>
                <div className="col-span-7">
                    <TechniqueExplorer data={data} />
                </div>

                {/* Always render the three-way frame, even when every tier is
                    empty. Per-column empty copy inside ThreeWayEntityGrid is
                    the honest shape — matches Tone/Narratives/Bot. */}
                <div className="col-span-12">
                    <ThreeWayEntityGrid data={data} onOpen={setActiveEntity} />
                </div>

                {/* News-vs-social removed (its numbers already appear in the
                    top-metrics "News vs social" tier row + the reads-as-today
                    line); How-this-works spans full width so nothing is stranded. */}
                <div className="col-span-12">
                    <HowThisWorks />
                </div>
            </div>

            {activeEntity && (
                <PropagandaEntityModal
                    item={activeEntity}
                    examplesByEntity={data.examplesByEntity ?? {}}
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
