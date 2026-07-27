import { useEffect, useMemo, useState } from 'react';
import {
    Card, CollapsibleInfo, EmptyState, EntityHeader, EntityHubLinks,
    EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, MethodPopover, Modal, PostCardList,
    RangeCaption, ThreeWayColumn, ThreeWayGrid,
    entityExternalUrl, sampleToPostCard,
} from '../components/common';
import type { ColumnSorter, EntityStat, TickerItem } from '../components/common';
import type { EntityProfile } from '../types';
import { Sparkline } from '../components/charts';
import { fetchNarratives, fetchSnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPts, sourceLabel } from '../services/format';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import { useDeepLinkParam } from '../services/deepLink';
import { NarrativeLifecyclePanel } from './narratives/NarrativeLifecyclePanel';
import type {
    AccountProfile, Filters, NarrativeSummary, NarrativesResponse,
    SnapshotStatusResponse, SourceBreakdownItem,
} from '../types';

// --------------------------------------------------------------------------- //
//  Restored verbatim from pre-cutover-main (see docs/todos/                   //
//  ui-feature-restoration.md) onto the current strictly-live /narratives      //
//  contract (analysis/src/api/models/narratives.py). Renames only:            //
//  snake_case -> camelCase everywhere, first_seen_at is now an ISO string     //
//  (formatRefreshedAgo, not the old unix-seconds formatRelativeDate path),    //
//  source_breakdown collapsed from 4 granular source_types down to           //
//  {news, reddit, x} (SourceBar/tierChipsForNarrative adapted accordingly),   //
//  propaganda_score (0-1 intensity) is now propagandaFlaggedFraction (share   //
//  of flagged docs, same convention as botPushedFraction) -- relabeled        //
//  "Propaganda-flagged" rather than fabricating an intensity score the        //
//  backend no longer computes, and top_supporting_docs is now                //
//  ClassificationSample[] (was SupportingDoc[]), so the modal renders it      //
//  through sampleToPostCard instead of supportingDocToPostCard. Dropped:      //
//  external_citation_count and cross_narrative_citations (and the            //
//  onOpenNarrativeId cross-story jump they fed) -- the current contract       //
//  carries neither, and nothing here fabricates them. meanConfidence         //
//  (lifecycle row + detail modal) and RangeCaption are current-era           //
//  additions the owner approved keeping, matching every sibling page.        //
// --------------------------------------------------------------------------- //

// Shared tooltip copy — kept here so the same definitions read identically
// on the compact cards and inside the detail modals.
const NET_TONE_TITLE =
    'Positive minus negative share of this story\'s posts, from -100 (all negative) to +100 (all positive).';
const PROP_FLAGGED_TITLE =
    'Share of this story\'s scored posts flagged for at least one propaganda technique.';
const CITES_TITLE =
    'Links from other posts or articles in our sample that point to posts in this story. '
    + 'Counts only sources we track — not the whole web.';
const LINK_TYPE_LABELS: Record<string, string> = {
    url_citation: 'link',
    quote: 'quote',
    reply: 'reply',
    retweet: 'retweet',
};

const SOURCE_LABEL: Record<string, string> = {
    news: 'News',
    reddit: 'Reddit',
    x: 'X',
};


// --------------------------------------------------------------------------- //
//  Small helpers                                                              //
// --------------------------------------------------------------------------- //

function buildNarrativeTickerItems(data: NarrativeSummary[], window: Filters['timeRange']): TickerItem[] {
    const total = data.length;
    const dayCutoffMs = Date.now() - 24 * 60 * 60 * 1000;
    const freshCount = data.filter((n) => n.firstSeenAt != null && Date.parse(n.firstSeenAt) >= dayCutoffMs).length;

    let topClaim: NarrativeSummary | null = null;
    for (const n of data) {
        if (!topClaim || n.docCount > topClaim.docCount) topClaim = n;
    }

    // "Top stories / in window" rather than "Tracked / narratives" — the
    // /narratives endpoint returns at most ?limit (defaulted to 20 by the
    // UI), so this number is "the top-N most-supported in the selected
    // window," not a count of every narrative the system has on file.
    const items: TickerItem[] = [
        {
            label: 'Top stories', value: total.toLocaleString(), hint: 'in window',
            emphasis: true,
            ariaLabel: `${total} top stories in window`,
        },
        {
            label: 'New (24h)', value: freshCount.toLocaleString(),
            tone: freshCount > 0 ? 'accent' : 'neutral',
        },
        { label: 'Window', value: formatTimeWindow(window) },
    ];
    if (topClaim) {
        const short = topClaim.name.length > 48
            ? topClaim.name.slice(0, 48).trimEnd() + '…'
            : topClaim.name;
        items.push({
            label: 'Most repeated', value: short,
            hint: `${topClaim.docCount.toLocaleString()} posts`,
            ariaLabel: `Most repeated narrative: ${topClaim.name}, ${topClaim.docCount} supporting posts`,
        });
    }
    return items;
}

function netSentimentColor(net: number | null): string {
    if (net == null) return COLORS.neutral;
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return COLORS.neutral;
}

/** Short faction-aware author label used when the first-seen author is an
 *  X account with profile metadata. */
function authorLabel(author: AccountProfile | null): string | null {
    if (!author) return null;
    const titleShort: Record<string, string> = {
        'President': 'Pres.', 'Vice President': 'VP', 'Senator': 'Sen.', 'Representative': 'Rep.',
    };
    const title = author.officeTitle ? (titleShort[author.officeTitle] || author.officeTitle) : null;
    const name = author.fullName || author.handle;
    const nameBit = title && name ? `${title} ${name}` : name ? name : author.handle ? `@${author.handle}` : null;
    if (!nameBit) return null;
    const badges: string[] = [];
    if (author.party) badges.push(author.party);
    // chamber/stateOrDistrict have no PG equivalent and are always null on
    // the wire -- these branches degrade gracefully to the plain name+party
    // above rather than guessing a district.
    if (author.chamber === 'house' && author.stateOrDistrict) {
        const m = author.stateOrDistrict.match(/^([A-Z]{2})(\d+)$/);
        badges.push(m ? `${m[1]}-${m[2]}` : author.stateOrDistrict);
    } else if (author.stateOrDistrict) {
        badges.push(author.stateOrDistrict);
    }
    return badges.length > 0 ? `${nameBit} (${badges.join(', ')})` : nameBit;
}

function firstSeenLabel(n: NarrativeSummary): string {
    const authorDisp = authorLabel(n.firstSeenAuthor);
    if (authorDisp) return authorDisp;
    if (n.firstSeenSourceType) {
        // Shared builder — never renders the raw source_type enum. X rows
        // store the literal "x.com" as domain; without an author profile
        // there is no handle to show, so pass null → bare "X".
        return sourceLabel(
            n.firstSeenSourceType,
            n.firstSeenSourceType === 'x_post' ? null : n.firstSeenDomain,
        );
    }
    return 'unknown source';
}

const SOURCE_DOT_COLOR: Record<string, string> = {
    news:   COLORS.sourceNews,
    reddit: COLORS.sourceReddit,
    x:      COLORS.sourceX,
};

function SourceBar({ items, showLegend = false }: { items: SourceBreakdownItem[]; showLegend?: boolean }) {
    // Normalize widths by the sum of the breakdown counts themselves, NOT by
    // docCount from a different query — the two can disagree, which
    // made segments fail to sum to 100% (U-10).
    const barTotal = items.reduce((s, it) => s + it.docCount, 0);
    if (items.length === 0 || barTotal === 0) return null;
    // Wrapper-level title gives a one-line summary; per-segment titles expose
    // the exact count + share on hover. When showLegend is set, the same
    // summary also renders as visible text so the colors aren't the only key
    // (R-8) — used on the compact card, where the modal has its own dotted
    // legend below the bar.
    const summary = items
        .map((it) => `${Math.round((it.docCount / barTotal) * 100)}% ${SOURCE_LABEL[it.source] ?? it.source}`)
        .join(', ');
    return (
        <>
            <div
                className="narrative-source-bar"
                aria-label={`Source mix across ${barTotal} posts: ${summary}`}
                title={`Source mix across ${barTotal} posts: ${summary}.`}
            >
                {items.map((item) => {
                    const pct = (item.docCount / barTotal) * 100;
                    return (
                        <div
                            key={item.source}
                            title={`${SOURCE_LABEL[item.source] ?? item.source}: ${item.docCount} of ${barTotal} posts (${pct.toFixed(0)}%).`}
                            style={{
                                width: `${pct}%`,
                                background: SOURCE_DOT_COLOR[item.source] || 'var(--neutral-400)',
                            }}
                        />
                    );
                })}
            </div>
            {showLegend && (
                <div className="narrative-card-sources text-xs text-muted">{summary}</div>
            )}
        </>
    );
}

/**
 * Static framing sentence for the Political Narratives page. The prior
 * version templated in raw counts ("Most claims (3 of 9) first surfaced in
 * news outlets. 5 narratives now cross ≥ 2 tiers.") and leaked internal
 * vocabulary (claims, tiers, the ≥ glyph) into the first sentence a casual
 * reader encounters. The grid + cross-tier panel below already surface the
 * counts in shapes built for them.
 */
function readsAsToday(_narratives: NarrativeSummary[]): string {
    return "The recurring talking points we've picked up across coverage.";
}


// --------------------------------------------------------------------------- //
//  Detail modal                                                               //
// --------------------------------------------------------------------------- //

interface NarrativeDetailModalProps {
    narrative: NarrativeSummary;
    onClose: () => void;
    /** When set, shows a ← arrow returning to the parent (entity) modal. */
    onBack?: () => void;
    backLabel?: string;
}

function NarrativeDetailModal({
    narrative, onClose, onBack, backLabel,
}: NarrativeDetailModalProps) {
    const timeline = useMemo(
        () => narrative.timeline.map((t) => ({ date: t.day, value: t.docCount })),
        [narrative.timeline],
    );
    const sentColor = netSentimentColor(narrative.netSentiment);
    const supportingDocs = narrative.topSupportingDocs ?? [];

    return (
        <Modal
            isOpen
            onClose={onClose}
            onBack={onBack}
            backLabel={backLabel}
            kicker="Narrative"
            title={narrative.name || '(unnamed narrative)'}
            subtitle={`First seen ${narrative.firstSeenAt ? formatRefreshedAgo(narrative.firstSeenAt) : 'unknown'} · ${firstSeenLabel(narrative)}`}
            maxWidth={1040}
        >
            {narrative.meanConfidence != null && (
                <span
                    className="badge badge-neutral"
                    title="Mean claim-match confidence across this story's member posts"
                >
                    {Math.round(narrative.meanConfidence * 100)}% confidence
                </span>
            )}

            <div className="narrative-modal-stats">
                <div>
                    <div className="eyebrow">Supporting posts</div>
                    <div className="metric-value">{narrative.docCount}</div>
                </div>
                <div>
                    <div className="eyebrow" title={NET_TONE_TITLE}>Net tone</div>
                    <div className="metric-value" style={{ color: sentColor }}>
                        {narrative.netSentiment != null ? formatPts(narrative.netSentiment) : '—'}
                    </div>
                </div>
                <div>
                    <div className="eyebrow" title={CITES_TITLE}>Inbound citations</div>
                    <div className="metric-value">{narrative.citationCount}</div>
                </div>
                {narrative.propagandaFlaggedFraction != null && (
                    <div>
                        <div className="eyebrow" title={PROP_FLAGGED_TITLE}>Propaganda-flagged</div>
                        <div className="metric-value">{Math.round(narrative.propagandaFlaggedFraction * 100)}%</div>
                    </div>
                )}
                {narrative.botPushedFraction != null && (
                    <div>
                        <div
                            className="eyebrow"
                            title={`${Math.round(narrative.botPushedFraction * 100)}% of the unique X accounts posting this claim show automated-behavior signals in our bot detector (an estimate, not proof).`}
                        >
                            Bot-pushed
                        </div>
                        <div className="metric-value">
                            {Math.round(narrative.botPushedFraction * 100)}%
                        </div>
                    </div>
                )}
            </div>

            {narrative.inboundByLinkType && Object.keys(narrative.inboundByLinkType).length > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">Citation edges</h3>
                    <p className="text-sm">
                        Inbound by type:{' '}
                        {Object.entries(narrative.inboundByLinkType)
                            .map(([t, n]) => `${LINK_TYPE_LABELS[t] ?? t} ${n}`)
                            .join(' · ')}
                    </p>
                    <p className="text-xs text-muted">
                        Citation edges connect documents we sampled — they do not
                        establish where a narrative originated or how it spread
                        outside our sample.
                    </p>
                </>
            )}

            <h3 className="card-title mt-4 mb-2">Daily volume</h3>
            {timeline.length >= 2 ? (
                <Sparkline
                    data={timeline}
                    dataKey="value"
                    height={200}
                    showXAxis
                    color="var(--neutral-700)"
                    ariaLabel={`Daily post volume for this narrative, ${timeline.length} days`}
                />
            ) : (
                /* A one-day-old story has a single point — an area chart
                   draws nothing. Say what we know instead of 200px of air. */
                <p className="text-sm text-muted">
                    {timeline.length === 1
                        ? `Collected on one day so far — ${timeline[0].value.toLocaleString()} post${timeline[0].value === 1 ? '' : 's'} on ${timeline[0].date}. The trend chart appears once there is a second day.`
                        : 'No daily volume recorded yet for this story.'}
                </p>
            )}

            <h3 className="card-title mt-4 mb-2">Source mix</h3>
            <SourceBar items={narrative.sourceBreakdown} showLegend />

            {narrative.firstSeenEntityProfile && (
                <>
                    <h3 className="card-title mt-4 mb-2">Where we first saw it</h3>
                    <p className="text-sm">
                        <strong>{narrative.firstSeenEntityProfile.displayName}</strong>
                        {narrative.firstSeenEntityProfile.kind !== 'catch_all' && (
                            <> — {narrative.firstSeenEntityProfile.blurb}</>
                        )}
                    </p>
                </>
            )}

            {supportingDocs.length > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">Top supporting posts</h3>
                    <PostCardList
                        posts={supportingDocs.map(sampleToPostCard)}
                        sampleNote="The strongest posts carrying this story in our sample — not every post that repeats it."
                    />
                </>
            )}
        </Modal>
    );
}


// --------------------------------------------------------------------------- //
//  Entity grouping — narratives rolled up by first-seen entity                //
// --------------------------------------------------------------------------- //

/** One row of the entity-grouped three-way grid. Each first-seen entity with
 *  ≥ 1 narrative becomes a card; the modal lists that entity's narratives. */
interface NarrativeEntityGroup {
    profile: EntityProfile;
    narratives: NarrativeSummary[];
    // Precomputed summary stats.
    count: number;
    totalDocs: number;
    avgNetSentiment: number;        // doc-weighted
    crossTierCount: number;
    mostRecent: string | null;      // ISO first-seen timestamp of the most recent narrative in the group
}

/** Group narratives by firstSeenEntityProfile (keyed by kind+key so
 *  catch-alls across tiers don't collide). Narratives with no first-seen
 *  entity profile at all (firstSeenEntityProfile null) have no group to
 *  join and are dropped from the three-way grid — the same behavior as the
 *  pre-cutover page, which only ever grouped narratives that carried a
 *  first-seen entity. Sorts each group's narratives by docCount desc,
 *  groups themselves by narrative count desc. */
function groupNarrativesByEntity(narratives: NarrativeSummary[]): NarrativeEntityGroup[] {
    const byKey = new Map<string, NarrativeEntityGroup>();
    for (const n of narratives) {
        if (!n.firstSeenEntityProfile) continue;
        const profile = n.firstSeenEntityProfile;
        const key = `${profile.kind}:${profile.key}`;
        let group = byKey.get(key);
        if (!group) {
            group = {
                profile,
                narratives: [],
                count: 0,
                totalDocs: 0,
                avgNetSentiment: 0,
                crossTierCount: 0,
                mostRecent: null,
            };
            byKey.set(key, group);
        }
        group.narratives.push(n);
    }

    const groups = Array.from(byKey.values());
    for (const g of groups) {
        g.narratives.sort((a, b) => b.docCount - a.docCount);
        g.count = g.narratives.length;
        g.totalDocs = g.narratives.reduce((s, n) => s + n.docCount, 0);
        g.crossTierCount = g.narratives.filter((n) => n.crossTier).length;
        g.mostRecent = g.narratives.reduce<string | null>((mx, n) => {
            if (!n.firstSeenAt) return mx;
            if (!mx || Date.parse(n.firstSeenAt) > Date.parse(mx)) return n.firstSeenAt;
            return mx;
        }, null);
        g.avgNetSentiment = g.totalDocs > 0
            ? g.narratives.reduce((s, n) => s + (n.netSentiment ?? 0) * n.docCount, 0) / g.totalDocs
            : 0;
    }
    groups.sort((a, b) => b.count - a.count || b.totalDocs - a.totalDocs);
    return groups;
}

function entityStatsForNarratives(g: NarrativeEntityGroup): EntityStat[] {
    if (g.count === 0) return [];
    const sentColor = netSentimentColor(g.avgNetSentiment);
    const stats: EntityStat[] = [
        {
            label: g.count === 1 ? 'Story' : 'Stories',
            value: g.count.toLocaleString(),
            emphasis: true,
        },
        {
            label: 'Avg tone',
            value: formatPts(g.avgNetSentiment),
            color: sentColor,
            title: NET_TONE_TITLE,
        },
        {
            label: 'Supporting posts',
            value: g.totalDocs.toLocaleString(),
        },
    ];
    if (g.crossTierCount > 0) {
        stats.push({
            label: 'Crossing groups',
            value: g.crossTierCount.toLocaleString(),
        });
    }
    return stats;
}


// --------------------------------------------------------------------------- //
//  Entity modal — lists one entity's narratives                               //
// --------------------------------------------------------------------------- //

function NarrativeEntityModal({
    group, onClose, onOpenNarrative,
}: {
    group: NarrativeEntityGroup;
    onClose: () => void;
    onOpenNarrative: (n: NarrativeSummary) => void;
}) {
    const { profile } = group;
    const sentColor = netSentimentColor(group.avgNetSentiment);
    const sourceUrl = entityExternalUrl(profile);

    const subtitle = [
        `${group.count} ${group.count === 1 ? 'story' : 'stories'} first seen here in our sample`,
        group.mostRecent ? `most recent ${formatRefreshedAgo(group.mostRecent)}` : null,
    ].filter(Boolean).join(' · ');

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle={subtitle}
        >
            <EntityHeader profile={profile} />

            <div className="entity-modal-stats">
                <div>
                    <div className="eyebrow">Stories</div>
                    <div className="metric-value">{group.count.toLocaleString()}</div>
                </div>
                <div>
                    <div className="eyebrow" title={NET_TONE_TITLE}>Avg tone</div>
                    <div className="metric-value" style={{ color: sentColor }}>
                        {formatPts(group.avgNetSentiment)}
                    </div>
                </div>
                <div>
                    <div className="eyebrow">Supporting posts</div>
                    <div className="metric-value">{group.totalDocs.toLocaleString()}</div>
                </div>
                {group.crossTierCount > 0 && (
                    <div>
                        <div className="eyebrow">Crossing groups</div>
                        <div className="metric-value">{group.crossTierCount.toLocaleString()}</div>
                    </div>
                )}
            </div>

            {sourceUrl && (
                <div className="entity-modal-links">
                    <a href={sourceUrl} target="_blank" rel="noreferrer">
                        Visit {profile.displayName}
                    </a>
                </div>
            )}

            <EntityHubLinks entityId={profile.entityId ?? null} currentTab="narratives" />

            <h3 className="card-title mt-4 mb-2">
                {group.count === 1 ? 'The story' : 'The stories'}
            </h3>
            {/* Compact rows, not full NarrativeCards — the reader just came
                from the grid; repeating whole cards inside the modal made it
                a duplicate of the page. Each row opens the detail modal. */}
            <div className="narrative-entity-rows">
                {group.narratives.map((n) => (
                    <button
                        key={n.narrativeId}
                        type="button"
                        className="narrative-entity-row"
                        onClick={() => onOpenNarrative(n)}
                        title="Open this story's details"
                    >
                        <span className="narrative-entity-row-name">{n.name}</span>
                        <span
                            className="narrative-entity-row-tone"
                            style={{ color: netSentimentColor(n.netSentiment) }}
                        >
                            {n.netSentiment != null ? formatPts(n.netSentiment) : '—'}
                        </span>
                        <span className="narrative-entity-row-docs">
                            {n.docCount.toLocaleString()} posts
                        </span>
                        <span className="narrative-entity-row-chevron" aria-hidden>&rsaquo;</span>
                    </button>
                ))}
            </div>
        </Modal>
    );
}


// --------------------------------------------------------------------------- //
//  Three-way grid — entity-profile split, mirrors Overall Tone layout         //
// --------------------------------------------------------------------------- //

const NARRATIVE_GROUP_SORTERS: ColumnSorter<NarrativeEntityGroup>[] = [
    { label: 'stories', compare: (a, b) => b.count - a.count || b.totalDocs - a.totalDocs },
    { label: 'posts', compare: (a, b) => b.totalDocs - a.totalDocs },
    { label: 'name', compare: (a, b) => a.profile.displayName.localeCompare(b.profile.displayName) },
];

interface NarrativeColumnProps {
    header: string;
    byline: string;
    groups: NarrativeEntityGroup[];
    onOpen: (g: NarrativeEntityGroup) => void;
    emptyCopy: string;
}

function NarrativeThreeWayColumn({ header, byline, groups, onOpen, emptyCopy }: NarrativeColumnProps) {
    return (
        <ThreeWayColumn
            header={header}
            byline={byline}
            empty={emptyCopy}
            items={groups}
            sorters={NARRATIVE_GROUP_SORTERS}
            renderItem={(g) => {
                const readsAs = g.count === 1
                    ? 'One story first seen here in our sample.'
                    : `${g.count} stories first seen here in our sample.`;
                return (
                    <EntityProfileCard
                        key={`${g.profile.kind}:${g.profile.key}`}
                        profile={g.profile}
                        stats={entityStatsForNarratives(g)}
                        readsAs={readsAs}
                        onClick={() => onOpen(g)}
                        emptyNote="Tracked — no stories first seen here in this window."
                        variant="compact"
                    />
                );
            }}
        />
    );
}


// --------------------------------------------------------------------------- //
//  Cross-tier + amplification panels                                          //
// --------------------------------------------------------------------------- //

const TIER_LABEL: Record<string, string> = {
    news: 'News',
    officials: 'Officials',
    public: 'Public',
};

function tierChipsForNarrative(n: NarrativeSummary): string[] {
    // Chips reflect the groups actually present in sourceBreakdown — not
    // firstSeenTierGroup alone, which could stamp a group that contributed
    // no supporting posts (U-9). sourceBreakdown now carries only three
    // buckets (news/reddit/x, collapsed from the old four source_types), so
    // an X-carried story still can't be split officials-vs-public from the
    // breakdown alone; we fall back to the origin tier for those, same
    // limitation the pre-cutover page had.
    const seen = new Set<string>();
    for (const item of n.sourceBreakdown) {
        if (item.docCount <= 0) continue;
        if (item.source === 'news') seen.add('news');
        else if (item.source === 'x') {
            seen.add(n.firstSeenTierGroup === 'officials' ? 'officials' : 'public');
        } else if (item.source === 'reddit') {
            seen.add('public');
        }
    }
    return Array.from(seen);
}

const CROSS_TIER_LIMIT = 5;

/**
 * The same story is showing up in more than one group (the news is
 * talking about it AND officials are AND/OR the public is). Capped at
 * CROSS_TIER_LIMIT to match the mockups — reads as a scannable list,
 * not an exhaustive feed.
 */
function ClaimsSpreadingPanel({ narratives, onOpen }: { narratives: NarrativeSummary[]; onOpen: (n: NarrativeSummary) => void }) {
    if (narratives.length === 0) {
        return (
            <Card
                title="Stories spreading across groups"
                subtitle="No story has surfaced in more than one group yet in this window — see the per-group breakdown above for what each is talking about."
            >
                <p className="text-muted text-sm">
                    We'll list stories here as soon as the same recurring claim is being repeated by at least two of the three groups (news, officials, the public).
                </p>
            </Card>
        );
    }

    const visible = narratives.slice(0, CROSS_TIER_LIMIT);
    return (
        <Card
            title="Stories spreading across groups"
            subtitle={`${visible.length} ${visible.length === 1 ? 'story is' : 'stories are'} being repeated across more than one group — at least two of news, officials, and the public.`}
        >
            <div className="cross-tier-list">
                {visible.map((n) => {
                    const tiers = tierChipsForNarrative(n);
                    return (
                        <button
                            key={n.narrativeId}
                            type="button"
                            className="cross-tier-row"
                            onClick={() => onOpen(n)}
                            aria-label={`${n.name}. Being repeated by ${tiers.map((t) => TIER_LABEL[t] || t).join(', ')}. Click for details.`}
                        >
                            <span className="cross-tier-row-claim" title={n.name}>
                                {n.name || '(unnamed)'}
                            </span>
                            <span className="cross-tier-row-tiers">
                                {tiers.map((t) => (
                                    <span key={t} className={`cross-tier-chip cross-tier-chip-${t}`}>
                                        {TIER_LABEL[t] || t}
                                    </span>
                                ))}
                            </span>
                            <span className="cross-tier-row-docs">
                                {n.docCount}
                                <span className="cross-tier-row-docs-label">posts</span>
                            </span>
                        </button>
                    );
                })}
            </div>
        </Card>
    );
}


// --------------------------------------------------------------------------- //
//  Page                                                                       //
// --------------------------------------------------------------------------- //

interface NarrativesProps {
    filters: Filters;
}

function Narratives({ filters }: NarrativesProps) {
    const [activeNarrative, setActiveNarrative] = useState<NarrativeSummary | null>(null);
    const [activeEntity, setActiveEntity] = useState<NarrativeEntityGroup | null>(null);
    // Deep-link target ("#narratives?open=<id>") — set by cross-page links
    // (Bot Detector amplification cards, Home digest, tone modals).
    const [openParam, setOpenParam] = useDeepLinkParam('open');

    const { data, loading, error, refetch } = useFetch<NarrativesResponse>(
        () => fetchNarratives(filters.timeRange),
        [filters.timeRange],
        `narratives:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatusResponse>(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );

    // Resolve the open= param once data lands. Unknown ids (outside the
    // loaded window/top-N) clear the param instead of erroring.
    useEffect(() => {
        if (!data || !openParam) return;
        const target = data.narratives.find((n) => String(n.narrativeId) === openParam);
        if (target) {
            setActiveNarrative(target);
        } else {
            setOpenParam(null);
        }
    }, [data, openParam, setOpenParam]);

    if (error) return <ErrorState message={error.message} onRetry={refetch} />;
    if (loading) {
        return (
            <div className="flex flex-col gap-4">
                <LoadingCard />
                <LoadingCard />
            </div>
        );
    }
    // Early-return only when the fetch itself yielded nothing. An empty
    // narratives array (zero clusters) is a valid state — render the frame
    // with per-column "No X-originated stories in this window" copy so
    // readers can see which axes are empty, matching Tone's behavior.
    if (!data) return <EmptyState title="No stories available for this window." />;

    const visible = data.narratives;

    // Three-way split by firstSeenTierGroup (walkthrough 058), then
    // rolled up by firstSeenEntityProfile so each entity gets one card.
    // The three-tier split (news / officials / public) is the source
    // separation now — the global "Filter by sources" pills were removed.
    const newsGroups = groupNarrativesByEntity(
        visible.filter((n) => n.firstSeenTierGroup === 'news'),
    );
    const officialGroups = groupNarrativesByEntity(
        visible.filter((n) => n.firstSeenTierGroup === 'officials'),
    );
    const publicGroups = groupNarrativesByEntity(
        visible.filter((n) => n.firstSeenTierGroup === 'public'),
    );
    const crossTier = visible.filter((n) => n.crossTier);

    const tickerItems = buildNarrativeTickerItems(visible, filters.timeRange);
    const refreshed = formatRefreshedAgo(pipelineRunTimestamp(snapshotStatus));

    return (
        <>
            <div className="dashboard-grid">
                {/* Ticker — tracked count, new this window, top amplified. */}
                <div className="col-span-12">
                    <GlobalTicker
                        items={tickerItems}
                        refreshed={refreshed}
                        ariaLabel="Narratives overview"
                        legend={
                            <MethodPopover
                                title="How to read these numbers"
                                description={
                                    "A story is a claim we saw repeated across posts. 'First seen' = the "
                                    + 'earliest post we collected carrying it, not where it started in the '
                                    + 'world. Net tone = positive minus negative share of a story\'s posts, '
                                    + 'in points on a -100 to +100 scale. Propaganda-flagged = share of '
                                    + 'this story\'s scored posts flagged for at least one technique.'
                                }
                            />
                        }
                    />
                    <RangeCaption range={data.range} />
                </div>

                {/* Reads-as-today headline card. */}
                <div className="col-span-12">
                    <div className="reads-as-today">
                        <span className="eyebrow reads-as-today-eyebrow">
                            {asOfTodayEyebrow(filters.timeRange)}
                        </span>
                        <p className="lead" style={{ margin: 0 }}>{readsAsToday(visible)}</p>
                    </div>
                </div>

                {/* Story lifecycles + Stories-spreading share a row (both capped
                    at 5). */}
                <div className="col-span-6">
                    <NarrativeLifecyclePanel
                        narratives={visible}
                        onOpen={setActiveNarrative}
                        tiersFor={tierChipsForNarrative}
                    />
                </div>
                <div className="col-span-6">
                    <ClaimsSpreadingPanel narratives={crossTier} onOpen={setActiveNarrative} />
                </div>

                {/* Three-way grid — one profile card per first-seen entity. */}
                <div className="col-span-12">
                    <ThreeWayGrid>
                        <NarrativeThreeWayColumn
                            header="The News"
                            byline="Outlets where we first saw each story, with editorial lean"
                            groups={newsGroups}
                            onOpen={setActiveEntity}
                            emptyCopy="No stories first seen from news sources in this window."
                        />
                        <NarrativeThreeWayColumn
                            header="Politicians & Officials"
                            byline="Tracked officeholders whose posts we first saw carrying each story"
                            groups={officialGroups}
                            onOpen={setActiveEntity}
                            emptyCopy="No stories first seen from officials yet. Coverage grows as we pull more posts directly from tracked officials."
                        />
                        <NarrativeThreeWayColumn
                            header="The Public"
                            byline="Subreddits and X accounts where we first saw each story"
                            groups={publicGroups}
                            onOpen={setActiveEntity}
                            emptyCopy="No stories first seen from the public in this window."
                        />
                    </ThreeWayGrid>
                </div>

                {/* How this page works — self-documenting content + collapsible backup. */}
                <div className="col-span-12">
                    <CollapsibleInfo>
                        <p className="text-sm">
                            A "story" here is a political claim we saw repeated across multiple posts.
                            Each one is placed in the column where we first saw it: a news outlet, a
                            verified official, or someone in the general public. "First seen" means the
                            earliest post we've collected — not necessarily where the claim started in the
                            world.
                        </p>
                        <p className="text-sm">
                            The "claims spreading between groups" panel lists stories that have since
                            surfaced in more than one of those three groups.
                        </p>
                    </CollapsibleInfo>
                </div>
            </div>

            {activeEntity && !activeNarrative && (
                <NarrativeEntityModal
                    group={activeEntity}
                    onClose={() => setActiveEntity(null)}
                    onOpenNarrative={setActiveNarrative}
                />
            )}

            {activeNarrative && (
                <NarrativeDetailModal
                    narrative={activeNarrative}
                    onClose={() => {
                        // Close the whole drill-down chain when exiting the
                        // narrative modal via the X / backdrop / Esc, and
                        // clear the deep-link param so the URL stays honest.
                        setActiveNarrative(null);
                        setActiveEntity(null);
                        if (openParam) setOpenParam(null);
                    }}
                    onBack={activeEntity
                        ? () => setActiveNarrative(null)
                        : undefined}
                    backLabel={activeEntity?.profile.displayName}
                />
            )}
        </>
    );
}

export default Narratives;
