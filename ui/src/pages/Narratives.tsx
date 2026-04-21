import { useMemo } from 'react';
import { Card, MethodPopover, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { Sparkline } from '../components/charts';
import { fetchNarratives } from '../services/api';
import { useFetch } from '../services/useFetch';
import { SEMANTIC_COLORS } from '../theme';
import type { Filters, NarrativeSummary, NarrativeSourceBreakdownItem, AccountProfile } from '../types';

// Short human-readable label for the first-seen author.
// Examples: "Rep Adams (D, NC-12)", "Sec. Hegseth (R, DoD)", "President Trump (R)",
// "@GOP (Republican Party)", "@handle" when we only know the handle.
function authorLabel(author: AccountProfile | null): string | null {
    if (!author) return null;
    const pieces: string[] = [];
    const titleShort: Record<string, string> = {
        'President': 'Pres.',
        'Vice President': 'VP',
        'Senator': 'Sen.',
        'Representative': 'Rep.',
    };
    const title = author.office_title
        ? (titleShort[author.office_title] || author.office_title)
        : null;
    const name = author.full_name || author.handle;
    if (title && name) pieces.push(`${title} ${name}`);
    else if (name) pieces.push(name);
    else if (author.handle) pieces.push(`@${author.handle}`);

    const badges: string[] = [];
    if (author.party) badges.push(author.party);
    if (author.chamber === 'house' && author.state_or_district) {
        // House district like "NC12" -> "NC-12"
        const sd = author.state_or_district;
        const m = sd.match(/^([A-Z]{2})(\d+)$/);
        badges.push(m ? `${m[1]}-${m[2]}` : sd);
    } else if (author.state_or_district) {
        badges.push(author.state_or_district);
    }

    if (pieces.length === 0) return null;
    return badges.length > 0 ? `${pieces.join(' ')} (${badges.join(', ')})` : pieces[0];
}

const SOURCE_DOT_COLOR: Record<string, string> = {
    news: '#0a3d62',
    reddit_post: '#d35400',
    reddit_comment: '#d35400',
    x_post: '#1d1d1f',
};

function netSentimentColor(net: number): string {
    if (net > 10) return SEMANTIC_COLORS.positive;
    if (net < -10) return SEMANTIC_COLORS.negative;
    return SEMANTIC_COLORS.neutral;
}

function formatRelativeDate(unixSeconds: number): string {
    if (!unixSeconds) return '—';
    const d = new Date(unixSeconds * 1000);
    const now = new Date();
    const diffMs = now.getTime() - d.getTime();
    const days = Math.floor(diffMs / (1000 * 60 * 60 * 24));
    if (days === 0) return 'today';
    if (days === 1) return '1 day ago';
    if (days < 30) return `${days} days ago`;
    return d.toISOString().slice(0, 10);
}

interface SourceBarProps {
    items: NarrativeSourceBreakdownItem[];
    total: number;
}

function SourceBar({ items, total }: SourceBarProps) {
    if (items.length === 0 || total === 0) return null;
    return (
        <div style={{ display: 'flex', height: 6, borderRadius: 2, overflow: 'hidden', background: 'var(--neutral-100)' }}>
            {items.map((item) => {
                const pct = (item.count / total) * 100;
                return (
                    <div
                        key={item.source_type}
                        title={`${item.label}: ${item.count}`}
                        style={{
                            width: `${pct}%`,
                            background: SOURCE_DOT_COLOR[item.source_type] || 'var(--neutral-400)',
                        }}
                    />
                );
            })}
        </div>
    );
}

interface NarrativeRowProps {
    narrative: NarrativeSummary;
}

function NarrativeRow({ narrative }: NarrativeRowProps) {
    const sentColor = netSentimentColor(narrative.net_sentiment);
    const timelineData = useMemo(
        () => narrative.timeline.map((t) => ({ date: t.date, value: t.count })),
        [narrative.timeline],
    );
    // Prefer faction-aware author label when we have one; fall back to
    // source_type + domain.
    const authorDisplay = authorLabel(narrative.first_seen_author);
    const firstSeenLabel = authorDisplay
        ? authorDisplay
        : narrative.first_seen_source_type
            ? narrative.first_seen_domain
                ? `${narrative.first_seen_source_type} · ${narrative.first_seen_domain}`
                : narrative.first_seen_source_type
            : 'unknown source';

    return (
        <div
            style={{
                display: 'grid',
                gridTemplateColumns: '1fr 100px 80px 90px 80px',
                gap: 'var(--space-4)',
                padding: 'var(--space-3) var(--space-4)',
                borderBottom: '1px solid var(--neutral-150)',
                alignItems: 'center',
            }}
        >
            {/* Claim + origin */}
            <div style={{ minWidth: 0 }}>
                <div
                    style={{
                        fontWeight: 600,
                        fontSize: 'var(--text-sm)',
                        marginBottom: 4,
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                    }}
                    title={narrative.name}
                >
                    {narrative.name || '(unnamed narrative)'}
                </div>
                <div className="eyebrow num" style={{ color: 'var(--neutral-500)' }}>
                    first seen in {firstSeenLabel} · {formatRelativeDate(narrative.first_seen_at)}
                </div>
                <div style={{ marginTop: 6 }}>
                    <SourceBar items={narrative.source_breakdown} total={narrative.supporting_doc_count} />
                </div>
                {/* Walkthrough 043: propaganda + bot-pushed overlay badges.
                    Shown only when the narrative has a value for the signal. */}
                {(narrative.propaganda_score !== null || narrative.bot_pushed_fraction !== null) && (
                    <div style={{ marginTop: 6, display: 'flex', gap: 'var(--space-2)', flexWrap: 'wrap' }}>
                        {narrative.propaganda_score !== null && (
                            <span
                                title="Mean propaganda score across supporting docs"
                                style={{
                                    padding: '1px 6px',
                                    background: narrative.propaganda_score >= 0.4 ? '#fef2f2' : '#f4f4f5',
                                    border: `1px solid ${narrative.propaganda_score >= 0.4 ? '#fecaca' : 'var(--neutral-200)'}`,
                                    borderRadius: 3,
                                    fontSize: 'var(--text-xs)',
                                    color: narrative.propaganda_score >= 0.4 ? '#b91c1c' : 'var(--neutral-600)',
                                }}
                            >
                                prop {narrative.propaganda_score.toFixed(2)}
                            </span>
                        )}
                        {narrative.bot_pushed_fraction !== null && (
                            <span
                                title="Fraction of unique X supporting authors whose bot score is >= 0.5 or who have any bot-labeled post"
                                style={{
                                    padding: '1px 6px',
                                    background: narrative.bot_pushed_fraction >= 0.3 ? '#fef3c7' : '#f4f4f5',
                                    border: `1px solid ${narrative.bot_pushed_fraction >= 0.3 ? '#fde68a' : 'var(--neutral-200)'}`,
                                    borderRadius: 3,
                                    fontSize: 'var(--text-xs)',
                                    color: narrative.bot_pushed_fraction >= 0.3 ? '#92400e' : 'var(--neutral-600)',
                                }}
                            >
                                bot-pushed {Math.round(narrative.bot_pushed_fraction * 100)}%
                            </span>
                        )}
                    </div>
                )}
            </div>

            {/* Sparkline */}
            <div>
                <Sparkline data={timelineData} dataKey="value" height={28} color="var(--neutral-700)" />
            </div>

            {/* Doc count */}
            <div className="num" style={{ textAlign: 'right', fontWeight: 600, fontSize: 'var(--text-base)' }}>
                {narrative.supporting_doc_count}
            </div>

            {/* Net sentiment */}
            <div className="num" style={{ textAlign: 'right', color: sentColor, fontWeight: 600 }}>
                {narrative.net_sentiment >= 0 ? '+' : ''}
                {narrative.net_sentiment.toFixed(1)}%
            </div>

            {/* Inbound citations */}
            <div className="num" style={{ textAlign: 'right', color: 'var(--neutral-500)' }}>
                {narrative.inbound_citation_count}
            </div>
        </div>
    );
}

interface NarrativesProps {
    filters: Filters;
}

function Narratives({ filters }: NarrativesProps) {
    const { data, loading, error, refetch } = useFetch<NarrativeSummary[]>(
        () => fetchNarratives(filters.timeRange),
        [filters.timeRange],
        `narratives:${filters.timeRange}`,
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

    const newsNarratives = data.filter((n) => n.first_seen_source_type === 'news');

    // Social = x_post + reddit_*; further split x_post by author tier.
    const isSocial = (n: NarrativeSummary) =>
        n.first_seen_source_type === 'x_post'
        || n.first_seen_source_type === 'reddit_post'
        || n.first_seen_source_type === 'reddit_comment';
    const socialNarratives = data.filter(isSocial);
    // Tier split only applies to X-origin narratives; Reddit-origin narratives
    // stay together in a "Reddit" group because we don't classify Reddit authors.
    const electedX = socialNarratives.filter(
        (n) => n.first_seen_source_type === 'x_post' && n.first_seen_tier === 'elected_official',
    );
    const affiliatedX = socialNarratives.filter(
        (n) => n.first_seen_source_type === 'x_post' && n.first_seen_tier === 'affiliated',
    );
    const generalPublicX = socialNarratives.filter(
        (n) => n.first_seen_source_type === 'x_post' && (n.first_seen_tier === 'general_public' || n.first_seen_tier === null),
    );
    const redditNarratives = socialNarratives.filter(
        (n) => n.first_seen_source_type === 'reddit_post' || n.first_seen_source_type === 'reddit_comment',
    );

    const orphanNarratives = data.filter(
        (n) => !n.first_seen_source_type
            || ['news', 'x_post', 'reddit_post', 'reddit_comment'].indexOf(n.first_seen_source_type) === -1,
    );

    return (
        <div className="flex flex-col gap-4">
            {/* Plain-language disclaimer — invariant: never imply universal sentiment */}
            <div
                style={{
                    padding: 'var(--space-3) var(--space-4)',
                    background: '#fffbeb',
                    border: '1px solid #fbbf24',
                    borderRadius: 'var(--radius-md)',
                    fontSize: 'var(--text-xs)',
                    color: '#92400e',
                }}
            >
                <strong>What a narrative is here:</strong> a recurring <em>political</em> claim we detected across the
                docs we ingested (US-political news articles, Reddit posts and comments, and X posts). Narratives
                below are split by <em>where we first saw the claim</em> (news vs social), and social X-origin
                narratives are further split by <em>who first said it</em> (elected official, politically-affiliated
                figure, or general public). "First seen" is the earliest doc in our sample, not the true world-origin.
            </div>

            <NarrativeSection
                title="News Media Narratives"
                subtitle={`Claims first seen in news articles we ingested (${filters.timeRange})`}
                narratives={newsNarratives}
                emptyHint="No news-originated narratives detected in this window."
            />

            <div
                style={{
                    padding: 'var(--space-3) var(--space-4)',
                    background: 'var(--bg-panel)',
                    border: '1px solid var(--neutral-200)',
                    borderRadius: 'var(--radius-md)',
                }}
            >
                <div className="eyebrow" style={{ marginBottom: 'var(--space-1)' }}>Social Media Narratives</div>
                <div style={{ fontSize: 'var(--text-xs)', color: 'var(--neutral-600)', lineHeight: 'var(--leading-normal)' }}>
                    X-origin narratives are grouped by the author's classification: <strong>elected officials</strong>
                    {' '}(current/former officeholders and institutional accounts), <strong>politically affiliated</strong>
                    {' '}(journalists, pundits, party committees, PACs, think tanks), and <strong>general public</strong>
                    {' '}(everyone else). Elected and affiliated classifications come from a curated list plus an LLM
                    classifier. Everyone unclassified defaults to general public. Reddit-origin narratives are not
                    tier-split and share one list.
                </div>
            </div>

            <NarrativeSection
                title="Social · Elected Officials (X)"
                subtitle={`Claims first seen in X posts from elected officials or institutional accounts (${filters.timeRange})`}
                narratives={electedX}
                emptyHint="No elected-official-originated narratives detected in this window."
                methodNote="Tier assignment: curated list at data/known_accounts.yaml (high-confidence institutional) plus LLM classifier for individuals."
            />

            <NarrativeSection
                title="Social · Politically Affiliated (X)"
                subtitle={`Claims first seen in X posts from journalists, pundits, PACs, or party orgs (${filters.timeRange})`}
                narratives={affiliatedX}
                emptyHint="No affiliated-originated narratives detected in this window."
                methodNote="Tier assignment: curated list for major orgs (RNC, DNC, think tanks) plus LLM classifier for individual journalists and strategists."
            />

            <NarrativeSection
                title="Social · General Public (X)"
                subtitle={`Claims first seen in X posts from non-classified accounts (${filters.timeRange})`}
                narratives={generalPublicX}
                emptyHint="No general-public-originated narratives detected in this window."
                methodNote="Default bucket: any X author not matched by the curated list or classified as elected/affiliated by the LLM."
            />

            <NarrativeSection
                title="Social · Reddit"
                subtitle={`Claims first seen in Reddit posts or comments we ingested (${filters.timeRange})`}
                narratives={redditNarratives}
                emptyHint="No Reddit-originated narratives detected in this window."
                methodNote="Reddit authors are not tier-classified in this release. Electeds and formal political orgs are rare on Reddit and we do not have an equivalent signal."
            />

            {orphanNarratives.length > 0 && (
                <NarrativeSection
                    title="Other Narratives"
                    subtitle="Narratives with no identified first-seen source"
                    narratives={orphanNarratives}
                />
            )}
        </div>
    );
}

interface NarrativeSectionProps {
    title: string;
    subtitle: string;
    narratives: NarrativeSummary[];
    emptyHint?: string;
    methodNote?: string;
}

function NarrativeSection({ title, subtitle, narratives, emptyHint, methodNote }: NarrativeSectionProps) {
    return (
        <Card
            title={title}
            subtitle={subtitle}
            headerActions={
                <MethodPopover
                    description="Each row is a narrative: a recurring claim detected by extracting canonical claim statements from doc text and clustering similar claims together. Doc count is the number of distinct docs in the time window that contributed a matching claim."
                    limitations={[
                        '"First seen" means the earliest ingested doc carrying this claim, not world-origin.',
                        ...(methodNote ? [methodNote] : []),
                        'Synonyms can split a single semantic narrative into two rows (e.g. "Trump won" vs "Trump victory"). An embedding-mode clusterer is available via CIVIC_NARRATIVE_SIMILARITY_MODE=embedding.',
                        'Inbound citations are a partial link graph: they count only edges where the cited doc is one we also ingested. External citations (to news outlets or X accounts we do not track) are not represented.',
                        'Claim extraction quality depends on the configured LLM. Small Ollama models will return few or no claims.',
                    ]}
                />
            }
        >
            {narratives.length === 0 ? (
                <div className="eyebrow" style={{ padding: 'var(--space-4)', color: 'var(--neutral-500)' }}>
                    {emptyHint || 'No narratives in this section.'}
                </div>
            ) : (
                <>
                    <div
                        style={{
                            display: 'grid',
                            gridTemplateColumns: '1fr 100px 80px 90px 80px',
                            gap: 'var(--space-4)',
                            padding: 'var(--space-2) var(--space-4)',
                            borderBottom: '1px solid var(--neutral-200)',
                        }}
                        className="eyebrow"
                    >
                        <span>Narrative · First Seen · Source Mix</span>
                        <span style={{ textAlign: 'right' }}>Daily</span>
                        <span style={{ textAlign: 'right' }}>Docs</span>
                        <span style={{ textAlign: 'right' }}>Net Sent.</span>
                        <span
                            style={{ textAlign: 'right' }}
                            title="Partial link graph: counts only citation edges where the cited doc is one we also ingested. External citations are not tracked."
                        >
                            Inbound (partial)
                        </span>
                    </div>
                    {narratives.map((n) => (
                        <NarrativeRow key={n.narrative_id} narrative={n} />
                    ))}
                </>
            )}
        </Card>
    );
}

export default Narratives;
