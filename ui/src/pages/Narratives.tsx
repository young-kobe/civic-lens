import { useEffect, useMemo, useState } from 'react';
import {
    Card, CollapsibleInfo, EmptyState, EntityHeader, EntityHubLinks,
    EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, MethodPopover, Modal, PostCardList,
    ThreeWayColumn, ThreeWayGrid,
    entityExternalUrl, supportingDocToPostCard,
} from '../components/common';
import type { ColumnSorter, EntityStat, TickerItem } from '../components/common';
import type { EntityProfile } from '../types';
import { Sparkline } from '../components/charts';
import { fetchNarratives, fetchSnapshotStatus, type SnapshotStatus } from '../services/api';
import { asOfTodayEyebrow, formatTimeWindow } from '../services/timeWindow';
import { formatRefreshedAgo, getSnapshotTimestamp } from '../services/freshness';
import { formatPts, formatRelativeDate, sourceLabel } from '../services/format';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import { useDeepLinkParam } from '../services/deepLink';
import { NarrativeLifecyclePanel } from './narratives/NarrativeLifecyclePanel';

// Shared tooltip copy — kept here so the same definitions read identically
// on the compact cards and inside the detail modals.
const NET_TONE_TITLE =
    'Positive minus negative share of this story\'s posts, from -100 (all negative) to +100 (all positive).';
const PROP_SCORE_TITLE =
    'Average technique-intensity score across scored posts, from 0 (none) to 1 (saturated).';
const CITES_TITLE =
    'Links from other posts or articles in our sample that point to posts in this story. '
    + 'Counts only sources we track — not the whole web.';
const LINK_TYPE_LABELS: Record<string, string> = {
    url_citation: 'link',
    quote: 'quote',
    reply: 'reply',
    retweet: 'retweet',
};
import type {
    AccountProfile, Filters, NarrativeSourceBreakdownItem, NarrativeSummary,
} from '../types';


// --------------------------------------------------------------------------- //
//  Small helpers                                                              //
// --------------------------------------------------------------------------- //

function buildNarrativeTickerItems(data: NarrativeSummary[], window: Filters['timeRange']): TickerItem[] {
    const total = data.length;
    const nowSec = Date.now() / 1000;
    const dayCutoff = nowSec - 24 * 60 * 60;
    const freshCount = data.filter((n) => n.first_seen_at >= dayCutoff).length;

    let topClaim: NarrativeSummary | null = null;
    for (const n of data) {
        if (!topClaim || n.supporting_doc_count > topClaim.supporting_doc_count) topClaim = n;
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
            hint: `${topClaim.supporting_doc_count.toLocaleString()} posts`,
            ariaLabel: `Most repeated narrative: ${topClaim.name}, ${topClaim.supporting_doc_count} supporting posts`,
        });
    }
    return items;
}

function netSentimentColor(net: number): string {
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
    const title = author.office_title ? (titleShort[author.office_title] || author.office_title) : null;
    const name = author.full_name || author.handle;
    const nameBit = title && name ? `${title} ${name}` : name ? name : author.handle ? `@${author.handle}` : null;
    if (!nameBit) return null;
    const badges: string[] = [];
    if (author.party) badges.push(author.party);
    if (author.chamber === 'house' && author.state_or_district) {
        const m = author.state_or_district.match(/^([A-Z]{2})(\d+)$/);
        badges.push(m ? `${m[1]}-${m[2]}` : author.state_or_district);
    } else if (author.state_or_district) {
        badges.push(author.state_or_district);
    }
    return badges.length > 0 ? `${nameBit} (${badges.join(', ')})` : nameBit;
}

function firstSeenLabel(n: NarrativeSummary): string {
    const authorDisp = authorLabel(n.first_seen_author);
    if (authorDisp) return authorDisp;
    if (n.first_seen_source_type) {
        // Shared builder — never renders the raw source_type enum. X rows
        // store the literal "x.com" as domain; without an author profile
        // there is no handle to show, so pass null → bare "X".
        return sourceLabel(
            n.first_seen_source_type,
            n.first_seen_source_type === 'x_post' ? null : n.first_seen_domain,
        );
    }
    return 'unknown source';
}

const SOURCE_DOT_COLOR: Record<string, string> = {
    news:           COLORS.sourceNews,
    reddit_post:    COLORS.sourceReddit,
    reddit_comment: COLORS.sourceReddit,
    x_post:         COLORS.sourceX,
};

function SourceBar({ items, showLegend = false }: { items: NarrativeSourceBreakdownItem[]; showLegend?: boolean }) {
    // Normalize widths by the sum of the breakdown counts themselves, NOT by
    // supporting_doc_count from a different query — the two can disagree, which
    // made segments fail to sum to 100% (U-10).
    const barTotal = items.reduce((s, it) => s + it.count, 0);
    if (items.length === 0 || barTotal === 0) return null;
    // Wrapper-level title gives a one-line summary; per-segment titles expose
    // the exact count + share on hover. When showLegend is set, the same
    // summary also renders as visible text so the colors aren't the only key
    // (R-8) — used on the compact card, where the modal has its own dotted
    // legend below the bar.
    const summary = items
        .map((it) => `${Math.round((it.count / barTotal) * 100)}% ${it.label}`)
        .join(', ');
    return (
        <>
            <div
                className="narrative-source-bar"
                aria-label={`Source mix across ${barTotal} posts: ${summary}`}
                title={`Source mix across ${barTotal} posts: ${summary}.`}
            >
                {items.map((item) => {
                    const pct = (item.count / barTotal) * 100;
                    return (
                        <div
                            key={item.source_type}
                            title={`${item.label}: ${item.count} of ${barTotal} posts (${pct.toFixed(0)}%).`}
                            style={{
                                width: `${pct}%`,
                                background: SOURCE_DOT_COLOR[item.source_type] || 'var(--neutral-400)',
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
//  Compact narrative card (used in the three-way grid + cross-tier panel)     //
// --------------------------------------------------------------------------- //

// NarrativeCard was deleted 2026-07-11: its last consumer (the entity
// modal's story stack) now renders compact .narrative-entity-row rows;
// the page-level grid was already the lifecycle strip + claims panel.


// --------------------------------------------------------------------------- //
//  Detail modal                                                               //
// --------------------------------------------------------------------------- //

interface NarrativeDetailModalProps {
    narrative: NarrativeSummary;
    onClose: () => void;
    /** When set, shows a ← arrow returning to the parent (entity) modal. */
    onBack?: () => void;
    backLabel?: string;
    /** Jump to another narrative by id (cross-narrative citation links).
     *  Unknown ids no-op — the target may be outside the loaded window. */
    onOpenNarrativeId?: (narrativeId: number) => void;
}

function NarrativeDetailModal({
    narrative, onClose, onBack, backLabel, onOpenNarrativeId,
}: NarrativeDetailModalProps) {
    const timeline = useMemo(
        () => narrative.timeline.map((t) => ({ date: t.date, value: t.count })),
        [narrative.timeline],
    );
    const sentColor = netSentimentColor(narrative.net_sentiment);
    const supportingDocs = narrative.top_supporting_docs ?? [];

    return (
        <Modal
            isOpen
            onClose={onClose}
            onBack={onBack}
            backLabel={backLabel}
            kicker="Narrative"
            title={narrative.name || '(unnamed narrative)'}
            subtitle={`First seen ${formatRelativeDate(narrative.first_seen_at)} · ${firstSeenLabel(narrative)}`}
            maxWidth={1040}
        >
            <div className="narrative-modal-stats">
                <div>
                    <div className="eyebrow">Supporting posts</div>
                    <div className="metric-value">{narrative.supporting_doc_count}</div>
                </div>
                <div>
                    <div className="eyebrow" title={NET_TONE_TITLE}>Net tone</div>
                    <div className="metric-value" style={{ color: sentColor }}>
                        {formatPts(narrative.net_sentiment)}
                    </div>
                </div>
                <div>
                    <div className="eyebrow" title={CITES_TITLE}>Inbound citations</div>
                    <div className="metric-value">{narrative.inbound_citation_count}</div>
                </div>
                {narrative.propaganda_score != null && (
                    <div>
                        <div className="eyebrow" title={PROP_SCORE_TITLE}>Propaganda score</div>
                        <div className="metric-value">{narrative.propaganda_score.toFixed(2)} / 1</div>
                    </div>
                )}
                {narrative.bot_pushed_fraction != null && (
                    <div>
                        <div
                            className="eyebrow"
                            title={`${Math.round(narrative.bot_pushed_fraction * 100)}% of the unique X accounts posting this claim show automated-behavior signals in our bot detector (an estimate, not proof).`}
                        >
                            Bot-pushed
                        </div>
                        <div className="metric-value">
                            {Math.round(narrative.bot_pushed_fraction * 100)}%
                        </div>
                    </div>
                )}
            </div>

            {((narrative.inbound_by_link_type && Object.keys(narrative.inbound_by_link_type).length > 0)
                || (narrative.external_citation_count ?? 0) > 0
                || (narrative.cross_narrative_citations?.length ?? 0) > 0) && (
                <>
                    <h3 className="card-title mt-4 mb-2">Citation edges</h3>
                    {narrative.inbound_by_link_type && Object.keys(narrative.inbound_by_link_type).length > 0 && (
                        <p className="text-sm">
                            Inbound by type:{' '}
                            {Object.entries(narrative.inbound_by_link_type)
                                .map(([t, n]) => `${LINK_TYPE_LABELS[t] ?? t} ${n}`)
                                .join(' · ')}
                        </p>
                    )}
                    {(narrative.external_citation_count ?? 0) > 0 && (
                        <p className="text-sm">
                            Links out to {narrative.external_citation_count} source
                            {narrative.external_citation_count === 1 ? '' : 's'} we did not ingest.
                        </p>
                    )}
                    {(narrative.cross_narrative_citations?.length ?? 0) > 0 && (
                        <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
                            {narrative.cross_narrative_citations!.map((edge) => (
                                <li key={`${edge.direction}-${edge.narrative_id}`} className="text-sm">
                                    {edge.direction === 'cites'
                                        ? 'Posts carrying this story link out to'
                                        : 'This story is linked to by posts carrying'}{' '}
                                    {onOpenNarrativeId ? (
                                        <button
                                            type="button"
                                            className="link-button"
                                            onClick={() => onOpenNarrativeId(edge.narrative_id)}
                                            title="Open this story's details"
                                        >
                                            {edge.name}
                                        </button>
                                    ) : (
                                        <strong>{edge.name}</strong>
                                    )}
                                    {' '}({edge.edge_count} link{edge.edge_count === 1 ? '' : 's'} between them)
                                </li>
                            ))}
                        </ul>
                    )}
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
            <SourceBar items={narrative.source_breakdown} showLegend />

            {narrative.first_seen_entity_profile && (
                <>
                    <h3 className="card-title mt-4 mb-2">Where we first saw it</h3>
                    <p className="text-sm">
                        <strong>{narrative.first_seen_entity_profile.displayName}</strong>
                        {narrative.first_seen_entity_profile.kind !== 'catch_all' && (
                            <> — {narrative.first_seen_entity_profile.blurb}</>
                        )}
                    </p>
                </>
            )}

            {supportingDocs.length > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">Top supporting posts</h3>
                    <PostCardList
                        posts={supportingDocs.map(supportingDocToPostCard)}
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
    mostRecent: number;             // unix seconds of most recent first_seen
}

/** Group narratives by first_seen_entity_profile (keyed by kind+key so
 *  catch-alls across tiers don't collide). Sorts each group's narratives
 *  by supporting_doc_count desc, groups themselves by narrative count desc. */
function groupNarrativesByEntity(narratives: NarrativeSummary[]): NarrativeEntityGroup[] {
    const byKey = new Map<string, NarrativeEntityGroup>();
    for (const n of narratives) {
        if (!n.first_seen_entity_profile) continue;
        const profile = n.first_seen_entity_profile;
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
                mostRecent: 0,
            };
            byKey.set(key, group);
        }
        group.narratives.push(n);
    }

    const groups = Array.from(byKey.values());
    for (const g of groups) {
        g.narratives.sort((a, b) => b.supporting_doc_count - a.supporting_doc_count);
        g.count = g.narratives.length;
        g.totalDocs = g.narratives.reduce((s, n) => s + n.supporting_doc_count, 0);
        g.crossTierCount = g.narratives.filter((n) => n.cross_tier).length;
        g.mostRecent = g.narratives.reduce((mx, n) => Math.max(mx, n.first_seen_at), 0);
        g.avgNetSentiment = g.totalDocs > 0
            ? g.narratives.reduce((s, n) => s + n.net_sentiment * n.supporting_doc_count, 0) / g.totalDocs
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
        group.mostRecent ? `most recent ${formatRelativeDate(group.mostRecent)}` : null,
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

            <EntityHubLinks profile={profile} currentTab="narratives" />

            <h3 className="card-title mt-4 mb-2">
                {group.count === 1 ? 'The story' : 'The stories'}
            </h3>
            {/* Compact rows, not full NarrativeCards — the reader just came
                from the grid; repeating whole cards inside the modal made it
                a duplicate of the page. Each row opens the detail modal. */}
            <div className="narrative-entity-rows">
                {group.narratives.map((n) => (
                    <button
                        key={n.narrative_id}
                        type="button"
                        className="narrative-entity-row"
                        onClick={() => onOpenNarrative(n)}
                        title="Open this story's details"
                    >
                        <span className="narrative-entity-row-name">{n.name}</span>
                        <span
                            className="narrative-entity-row-tone"
                            style={{ color: netSentimentColor(n.net_sentiment) }}
                        >
                            {formatPts(n.net_sentiment)}
                        </span>
                        <span className="narrative-entity-row-docs">
                            {n.supporting_doc_count.toLocaleString()} posts
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
    // Chips reflect the groups actually present in source_breakdown — not
    // first_seen_tier_group alone, which could stamp a group that contributed
    // no supporting posts (U-9). One known limit: x_post rows can't be split
    // officials-vs-public from the breakdown, so we fall back to the origin
    // tier for those; a story carried by BOTH officials and public over X may
    // therefore under-report one of them until the backend exposes the split.
    const seen = new Set<string>();
    for (const item of n.source_breakdown) {
        if (item.count <= 0) continue;
        if (item.source_type === 'news') seen.add('news');
        else if (item.source_type === 'x_post') {
            seen.add(n.first_seen_tier_group === 'officials' ? 'officials' : 'public');
        } else if (item.source_type === 'reddit_post' || item.source_type === 'reddit_comment') {
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
                            key={n.narrative_id}
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
                                {n.supporting_doc_count}
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

    const { data, loading, error, refetch } = useFetch<NarrativeSummary[]>(
        () => fetchNarratives(filters.timeRange),
        [filters.timeRange],
        `narratives:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatus>(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );

    // Resolve the open= param once data lands. Unknown ids (outside the
    // loaded window/top-N) clear the param instead of erroring.
    useEffect(() => {
        if (!data || !openParam) return;
        const target = data.find((n) => String(n.narrative_id) === openParam);
        if (target) {
            setActiveNarrative(target);
        } else {
            setOpenParam(null);
        }
    }, [data, openParam, setOpenParam]);

    /** Jump between narratives by id (cross-narrative citation links). */
    const openNarrativeById = (narrativeId: number) => {
        const target = data?.find((n) => n.narrative_id === narrativeId);
        if (target) {
            setActiveEntity(null);
            setActiveNarrative(target);
        }
    };

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

    const visible = data;

    // Three-way split by first_seen_tier_group (walkthrough 058), then
    // rolled up by first_seen_entity_profile so each entity gets one card.
    // The three-tier split (news / officials / public) is the source
    // separation now — the global "Filter by sources" pills were removed.
    const newsGroups = groupNarrativesByEntity(
        visible.filter((n) => n.first_seen_tier_group === 'news'),
    );
    const officialGroups = groupNarrativesByEntity(
        visible.filter((n) => n.first_seen_tier_group === 'officials'),
    );
    const publicGroups = groupNarrativesByEntity(
        visible.filter((n) => n.first_seen_tier_group === 'public'),
    );
    const crossTier = visible.filter((n) => n.cross_tier);

    const tickerItems = buildNarrativeTickerItems(data, filters.timeRange);
    const refreshed = formatRefreshedAgo(
        getSnapshotTimestamp(snapshotStatus, `narratives_${filters.timeRange}`),
    );

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
                                    + 'in points on a -100 to +100 scale. Propaganda score = average '
                                    + 'technique-intensity across scored posts, 0 (none) to 1 (saturated).'
                                }
                            />
                        }
                    />
                </div>

                {/* Reads-as-today headline card. */}
                <div className="col-span-12">
                    <div className="reads-as-today">
                        <span className="eyebrow reads-as-today-eyebrow">
                            {asOfTodayEyebrow(filters.timeRange)}
                        </span>
                        <p className="lead" style={{ margin: 0 }}>{readsAsToday(data)}</p>
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
                    onOpenNarrativeId={openNarrativeById}
                />
            )}
        </>
    );
}

export default Narratives;
