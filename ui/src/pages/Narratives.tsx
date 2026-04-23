import { useMemo, useState } from 'react';
import {
    Card, CollapsibleInfo, EmptyState, EntityHeader, EntityProfileCard,
    ErrorState, GlobalTicker, LoadingCard, Modal, MoversTicker, SupportingDocsTable,
    entityExternalUrl, entityLeanAccent,
} from '../components/common';
import type { EntityStat, TickerItem } from '../components/common';
import type { EntityProfile } from '../types';
import { Sparkline } from '../components/charts';
import { fetchMovers, fetchNarratives } from '../services/api';
import { asOfTodayEyebrow } from '../services/timeWindow';
import { useFetch } from '../services/useFetch';
import { COLORS } from '../theme';
import type {
    AccountProfile, Filters, MoversResult, NarrativeSourceBreakdownItem, NarrativeSummary,
} from '../types';


// --------------------------------------------------------------------------- //
//  Small helpers                                                              //
// --------------------------------------------------------------------------- //

function buildNarrativeTickerItems(data: NarrativeSummary[], window: string): TickerItem[] {
    const total = data.length;
    const nowSec = Date.now() / 1000;
    const dayCutoff = nowSec - 24 * 60 * 60;
    const freshCount = data.filter((n) => n.first_seen_at >= dayCutoff).length;

    let topClaim: NarrativeSummary | null = null;
    for (const n of data) {
        if (!topClaim || n.supporting_doc_count > topClaim.supporting_doc_count) topClaim = n;
    }

    const items: TickerItem[] = [
        {
            label: 'Tracked', value: total.toLocaleString(), hint: 'narratives',
            emphasis: true,
            ariaLabel: `${total} narratives tracked`,
        },
        {
            label: 'New (24h)', value: freshCount.toLocaleString(),
            tone: freshCount > 0 ? 'accent' : 'neutral',
        },
        { label: 'Window', value: window },
    ];
    if (topClaim) {
        const short = topClaim.name.length > 48
            ? topClaim.name.slice(0, 48).trimEnd() + '…'
            : topClaim.name;
        items.push({
            label: 'Top Amplified', value: short,
            hint: `${topClaim.supporting_doc_count.toLocaleString()} docs`,
            ariaLabel: `Top amplified narrative: ${topClaim.name}, ${topClaim.supporting_doc_count} supporting docs`,
        });
    }
    return items;
}

function netSentimentColor(net: number): string {
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return COLORS.neutral;
}

function formatRelativeDate(unixSeconds: number): string {
    if (!unixSeconds) return '—';
    const d = new Date(unixSeconds * 1000);
    const days = Math.floor((Date.now() - d.getTime()) / (1000 * 60 * 60 * 24));
    if (days === 0) return 'today';
    if (days === 1) return '1 day ago';
    if (days < 30) return `${days} days ago`;
    return d.toISOString().slice(0, 10);
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
        return n.first_seen_domain
            ? `${n.first_seen_source_type} · ${n.first_seen_domain}`
            : n.first_seen_source_type;
    }
    return 'unknown source';
}

const SOURCE_DOT_COLOR: Record<string, string> = {
    news:           COLORS.sourceNews,
    reddit_post:    COLORS.sourceReddit,
    reddit_comment: COLORS.sourceReddit,
    x_post:         COLORS.sourceX,
};

function SourceBar({ items, total }: { items: NarrativeSourceBreakdownItem[]; total: number }) {
    if (items.length === 0 || total === 0) return null;
    return (
        <div className="narrative-source-bar" aria-label="Source mix">
            {items.map((item) => (
                <div
                    key={item.source_type}
                    title={`${item.label}: ${item.count}`}
                    style={{
                        width: `${(item.count / total) * 100}%`,
                        background: SOURCE_DOT_COLOR[item.source_type] || 'var(--neutral-400)',
                    }}
                />
            ))}
        </div>
    );
}

/** Headline sentence above the three-way grid. Names the tier with the most
 *  fresh narratives, and flags cross-tier activity when it's meaningful. */
function readsAsToday(narratives: NarrativeSummary[]): string {
    if (narratives.length === 0) return 'No narratives tracked in this window.';

    const byTier: Record<string, number> = { news: 0, officials: 0, public: 0, unknown: 0 };
    let crossTier = 0;
    for (const n of narratives) {
        const tg = n.first_seen_tier_group ?? 'unknown';
        byTier[tg] = (byTier[tg] ?? 0) + 1;
        if (n.cross_tier) crossTier += 1;
    }

    const tierPairs = (['news', 'officials', 'public'] as const)
        .map((t) => [t, byTier[t]] as const)
        .sort((a, b) => b[1] - a[1]);
    const leader = tierPairs[0];
    if (leader[1] === 0) return `${narratives.length} narratives tracked — no tier dominates yet.`;

    const label: Record<string, string> = {
        news: 'news outlets',
        officials: 'verified officials',
        public: 'the general public',
    };
    const leaderSentence = `Most claims (${leader[1]} of ${narratives.length}) first surfaced in ${label[leader[0]]}.`;
    if (crossTier === 0) return leaderSentence;
    return `${leaderSentence} ${crossTier} narrative${crossTier === 1 ? '' : 's'} now cross ≥ 2 tiers.`;
}


// --------------------------------------------------------------------------- //
//  Compact narrative card (used in the three-way grid + cross-tier panel)     //
// --------------------------------------------------------------------------- //

interface NarrativeCardProps {
    narrative: NarrativeSummary;
    onOpen: (n: NarrativeSummary) => void;
}

function NarrativeCard({ narrative, onOpen }: NarrativeCardProps) {
    const sentColor = netSentimentColor(narrative.net_sentiment);
    const propFlag = narrative.propaganda_score != null && narrative.propaganda_score >= 0.4;
    const botFlag = narrative.bot_pushed_fraction != null && narrative.bot_pushed_fraction >= 0.3;

    return (
        <button
            type="button"
            className="narrative-card"
            onClick={() => onOpen(narrative)}
            aria-label={`${narrative.name}. ${narrative.supporting_doc_count} docs, net sentiment ${narrative.net_sentiment.toFixed(1)}%. Open details.`}
            title={narrative.name}
        >
            <div className="narrative-card-claim">{narrative.name || '(unnamed)'}</div>
            <div className="narrative-card-origin">
                first seen {formatRelativeDate(narrative.first_seen_at)} · {firstSeenLabel(narrative)}
            </div>
            <SourceBar items={narrative.source_breakdown} total={narrative.supporting_doc_count} />
            <div className="narrative-card-metrics">
                <span>
                    <span className="narrative-card-metric-value">{narrative.supporting_doc_count}</span>
                    <span className="narrative-card-metric-label">docs</span>
                </span>
                <span>
                    <span className="narrative-card-metric-value" style={{ color: sentColor }}>
                        {narrative.net_sentiment >= 0 ? '+' : ''}
                        {narrative.net_sentiment.toFixed(1)}%
                    </span>
                    <span className="narrative-card-metric-label">net</span>
                </span>
                {narrative.inbound_citation_count > 0 && (
                    <span>
                        <span className="narrative-card-metric-value">{narrative.inbound_citation_count}</span>
                        <span className="narrative-card-metric-label">cites</span>
                    </span>
                )}
            </div>
            {(propFlag || botFlag || narrative.cross_tier) && (
                <div className="narrative-card-flags">
                    {propFlag && (
                        <span className="narrative-flag narrative-flag-prop"
                            title={`Mean propaganda score ${narrative.propaganda_score?.toFixed(2)}`}>
                            prop {narrative.propaganda_score?.toFixed(2)}
                        </span>
                    )}
                    {botFlag && (
                        <span className="narrative-flag narrative-flag-bot"
                            title={`${Math.round((narrative.bot_pushed_fraction ?? 0) * 100)}% of unique X authors flagged bot-pushed`}>
                            bot-pushed {Math.round((narrative.bot_pushed_fraction ?? 0) * 100)}%
                        </span>
                    )}
                    {narrative.cross_tier && (
                        <span className="narrative-flag narrative-flag-cross"
                            title="Supporting docs span two or more tiers">
                            cross-tier
                        </span>
                    )}
                </div>
            )}
        </button>
    );
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

function NarrativeDetailModal({ narrative, onClose, onBack, backLabel }: NarrativeDetailModalProps) {
    const timeline = useMemo(
        () => narrative.timeline.map((t) => ({ date: t.date, value: t.count })),
        [narrative.timeline],
    );
    const sentColor = netSentimentColor(narrative.net_sentiment);
    const sign = narrative.net_sentiment >= 0 ? '+' : '';
    const supportingDocs = narrative.top_supporting_docs ?? [];

    return (
        <Modal
            isOpen
            onClose={onClose}
            onBack={onBack}
            backLabel={backLabel}
            title={narrative.name || '(unnamed narrative)'}
            subtitle={`First seen ${formatRelativeDate(narrative.first_seen_at)} · ${firstSeenLabel(narrative)}`}
            accentColor={sentColor}
            maxWidth={1040}
        >
            <div className="narrative-modal-stats">
                <div>
                    <div className="eyebrow">Supporting docs</div>
                    <div className="metric-value">{narrative.supporting_doc_count}</div>
                </div>
                <div>
                    <div className="eyebrow">Net sentiment</div>
                    <div className="metric-value" style={{ color: sentColor }}>
                        {sign}{narrative.net_sentiment.toFixed(1)}%
                    </div>
                </div>
                <div>
                    <div className="eyebrow">Inbound citations</div>
                    <div className="metric-value">{narrative.inbound_citation_count}</div>
                </div>
                {narrative.propaganda_score != null && (
                    <div>
                        <div className="eyebrow">Propaganda score</div>
                        <div className="metric-value">{narrative.propaganda_score.toFixed(2)}</div>
                    </div>
                )}
                {narrative.bot_pushed_fraction != null && (
                    <div>
                        <div className="eyebrow">Bot-pushed</div>
                        <div className="metric-value">
                            {Math.round(narrative.bot_pushed_fraction * 100)}%
                        </div>
                    </div>
                )}
            </div>

            <h3 className="card-title mt-4 mb-2">Daily volume</h3>
            <Sparkline data={timeline} dataKey="value" height={80} color="var(--neutral-700)" />

            <h3 className="card-title mt-4 mb-2">Source mix</h3>
            <SourceBar items={narrative.source_breakdown} total={narrative.supporting_doc_count} />
            <ul style={{ listStyle: 'none', padding: 0, marginTop: 'var(--space-2)', display: 'flex', gap: 'var(--space-3)', flexWrap: 'wrap' }}>
                {narrative.source_breakdown.map((item) => (
                    <li key={item.source_type} className="text-xs text-muted">
                        <span
                            style={{
                                display: 'inline-block', width: 10, height: 10, borderRadius: '50%',
                                background: SOURCE_DOT_COLOR[item.source_type] || 'var(--neutral-400)',
                                marginRight: 6, verticalAlign: 'middle',
                            }}
                        />
                        {item.label}: {item.count}
                    </li>
                ))}
            </ul>

            {narrative.first_seen_entity_profile && (
                <>
                    <h3 className="card-title mt-4 mb-2">First-seen entity</h3>
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
                    <h3 className="card-title mt-4 mb-2">Top supporting documents</h3>
                    <SupportingDocsTable docs={supportingDocs} />
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
    const sign = g.avgNetSentiment >= 0 ? '+' : '';
    const sentColor = netSentimentColor(g.avgNetSentiment);
    const stats: EntityStat[] = [
        {
            label: g.count === 1 ? 'Story' : 'Stories',
            value: g.count.toLocaleString(),
            emphasis: true,
        },
        {
            label: 'Avg tone',
            value: `${sign}${g.avgNetSentiment.toFixed(1)}%`,
            color: sentColor,
        },
        {
            label: 'Supporting docs',
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
    const sign = group.avgNetSentiment >= 0 ? '+' : '';
    const sentColor = netSentimentColor(group.avgNetSentiment);
    const sourceUrl = entityExternalUrl(profile);

    const subtitle = [
        `${group.count} ${group.count === 1 ? 'story' : 'stories'} first surfaced here`,
        group.mostRecent ? `most recent ${formatRelativeDate(group.mostRecent)}` : null,
    ].filter(Boolean).join(' · ');

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle={subtitle}
            accentColor={entityLeanAccent(profile)}
        >
            <EntityHeader profile={profile} />

            <div className="entity-modal-stats">
                <div>
                    <div className="eyebrow">Stories</div>
                    <div className="metric-value">{group.count.toLocaleString()}</div>
                </div>
                <div>
                    <div className="eyebrow">Avg tone</div>
                    <div className="metric-value" style={{ color: sentColor }}>
                        {sign}{group.avgNetSentiment.toFixed(1)}%
                    </div>
                </div>
                <div>
                    <div className="eyebrow">Supporting docs</div>
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
                        Visit {profile.displayName} ↗
                    </a>
                </div>
            )}

            <h3 className="card-title mt-4 mb-2">
                {group.count === 1 ? 'The story' : 'The stories'}
            </h3>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
                {group.narratives.map((n) => (
                    <NarrativeCard
                        key={n.narrative_id}
                        narrative={n}
                        onOpen={onOpenNarrative}
                    />
                ))}
            </div>
        </Modal>
    );
}


// --------------------------------------------------------------------------- //
//  Three-way grid — entity-profile split, mirrors Overall Tone layout         //
// --------------------------------------------------------------------------- //

const TOP_N = 12;

interface ThreeWayColumnProps {
    header: string;
    byline: string;
    groups: NarrativeEntityGroup[];
    onOpen: (g: NarrativeEntityGroup) => void;
    emptyCopy: string;
}

function ThreeWayColumn({ header, byline, groups, onOpen, emptyCopy }: ThreeWayColumnProps) {
    return (
        <div className="three-way-column">
            <div>
                <div className="three-way-column-header">{header}</div>
                <div className="three-way-column-byline">{byline}</div>
            </div>
            {groups.length === 0 ? (
                <p className="text-xs text-muted" style={{ padding: 'var(--space-3)' }}>{emptyCopy}</p>
            ) : (
                groups.slice(0, TOP_N).map((g) => {
                    const readsAs = g.count === 1
                        ? 'One story first surfaced here.'
                        : `${g.count} stories first surfaced here.`;
                    return (
                        <EntityProfileCard
                            key={`${g.profile.kind}:${g.profile.key}`}
                            profile={g.profile}
                            stats={entityStatsForNarratives(g)}
                            readsAs={readsAs}
                            onClick={() => onOpen(g)}
                            emptyNote="Tracked — no stories originated here in this window."
                        />
                    );
                })
            )}
        </div>
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
    const seen = new Set<string>();
    for (const item of n.source_breakdown) {
        if (item.source_type === 'news') seen.add('news');
        else if (item.source_type === 'x_post') {
            // We can't cheaply tell officials vs public here without re-querying,
            // but first_seen_tier_group is a reasonable proxy for the origin tier.
            if (n.first_seen_tier_group === 'officials') seen.add('officials');
            else seen.add('public');
        } else if (item.source_type === 'reddit_post' || item.source_type === 'reddit_comment') {
            seen.add('public');
        }
    }
    // Always include the origin tier as a fallback.
    if (n.first_seen_tier_group) seen.add(n.first_seen_tier_group);
    return Array.from(seen);
}

/**
 * The same story is showing up in more than one group (the news is
 * talking about it AND officials are AND/OR the public is). Rename
 * softened from the jargon-y "cross-tier" to everyday language.
 */
function ClaimsSpreadingPanel({ narratives, onOpen }: { narratives: NarrativeSummary[]; onOpen: (n: NarrativeSummary) => void }) {
    if (narratives.length === 0) {
        return (
            <Card
                title="Claims spreading between groups"
                subtitle="Stories we've seen pop up in more than one of The News / Officials / The Public"
            >
                <p className="text-muted text-sm">
                    No stories in this window have surfaced in more than one group yet.
                </p>
            </Card>
        );
    }

    return (
        <Card
            title="Top political narratives"
            subtitle={`${narratives.length} ${narratives.length === 1 ? 'story is' : 'stories are'} being repeated by more than one group — the news, officials, and the public are all talking about them.`}
        >
            <div className="cross-tier-list">
                {narratives.map((n) => {
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

    const { data, loading, error, refetch } = useFetch<NarrativeSummary[]>(
        () => fetchNarratives(filters.timeRange),
        [filters.timeRange],
        `narratives:${filters.timeRange}`,
    );
    const { data: movers } = useFetch<MoversResult>(
        () => fetchMovers(filters.timeRange),
        [filters.timeRange],
        `movers:${filters.timeRange}`,
    );

    if (error) return <ErrorState message={error.message} onRetry={refetch} />;
    if (loading) {
        return (
            <div className="flex flex-col gap-4">
                <LoadingCard />
                <LoadingCard />
            </div>
        );
    }
    if (!data || data.length === 0) {
        return (
            <EmptyState
                title="No narratives detected"
                description="No claims have been clustered yet for this time window. Run the analysis pipeline (claims + narratives tasks) to populate this view."
            />
        );
    }

    // Client-side source filter (API already returns all sources).
    const sourceMatches = (st: string | null): boolean => {
        if (filters.sourceType === 'all') return true;
        if (filters.sourceType === 'news') return st === 'news';
        if (filters.sourceType === 'reddit') return st === 'reddit_post' || st === 'reddit_comment';
        if (filters.sourceType === 'social') return st === 'reddit_post' || st === 'reddit_comment' || st === 'x_post';
        return true;
    };
    const filtered = data.filter((n) => sourceMatches(n.first_seen_source_type));

    // Three-way split by first_seen_tier_group (walkthrough 058), then
    // rolled up by first_seen_entity_profile so each entity gets one card.
    const newsGroups = groupNarrativesByEntity(
        filtered.filter((n) => n.first_seen_tier_group === 'news'),
    );
    const officialGroups = groupNarrativesByEntity(
        filtered.filter((n) => n.first_seen_tier_group === 'officials'),
    );
    const publicGroups = groupNarrativesByEntity(
        filtered.filter((n) => n.first_seen_tier_group === 'public'),
    );
    const crossTier = filtered.filter((n) => n.cross_tier);

    const tickerItems = buildNarrativeTickerItems(filtered, filters.timeRange);
    const refreshed = new Date().toISOString().slice(0, 19).replace('T', ' ') + ' UTC';

    return (
        <>
            <div className="dashboard-grid">
                {/* Ticker — tracked count, new this window, top amplified. */}
                <div className="col-span-12">
                    <GlobalTicker
                        items={tickerItems}
                        refreshed={refreshed}
                        ariaLabel="Narratives overview"
                    />
                </div>

                {movers && (
                    <div className="col-span-12">
                        <MoversTicker data={movers} />
                    </div>
                )}

                {/* Reads-as-today headline card. */}
                <div className="col-span-12">
                    <div className="reads-as-today">
                        <span className="eyebrow reads-as-today-eyebrow">
                            {asOfTodayEyebrow(filters.timeRange)}
                        </span>
                        <p className="lead" style={{ margin: 0 }}>{readsAsToday(filtered)}</p>
                    </div>
                </div>

                {/* Three-way grid — one profile card per first-seen entity. */}
                <div className="col-span-12">
                    <div className="three-way-grid">
                        <ThreeWayColumn
                            header="The News"
                            byline="Outlets that first surfaced each story, with editorial lean"
                            groups={newsGroups}
                            onOpen={setActiveEntity}
                            emptyCopy="No news-originated stories in this window."
                        />
                        <ThreeWayColumn
                            header="Politicians & Officials"
                            byline="Tracked officeholders whose posts first surfaced each story"
                            groups={officialGroups}
                            onOpen={setActiveEntity}
                            emptyCopy="No official-originated stories yet. Coverage grows as we pull more posts directly from tracked officials."
                        />
                        <ThreeWayColumn
                            header="The Public"
                            byline="Subreddits and X accounts that first surfaced each story"
                            groups={publicGroups}
                            onOpen={setActiveEntity}
                            emptyCopy="No public-originated stories in this window."
                        />
                    </div>
                </div>

                {/* Claims spreading between groups (was "Cross-tier narratives"). */}
                <div className="col-span-12">
                    <ClaimsSpreadingPanel narratives={crossTier} onOpen={setActiveNarrative} />
                </div>

                {/* How this page works — self-documenting content + collapsible backup. */}
                <div className="col-span-12">
                    <CollapsibleInfo>
                        <p className="text-sm">
                            A "story" here is a political claim we saw repeated across multiple posts.
                            Each one is placed in the column where we first saw it: a news outlet, a
                            verified official, or someone in the general public. "First seen" means the
                            earliest post we've ingested — not necessarily where the claim started in the
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
                        // narrative modal via the X / backdrop / Esc.
                        setActiveNarrative(null);
                        setActiveEntity(null);
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
