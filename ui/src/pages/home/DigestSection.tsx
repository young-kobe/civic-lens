import { MethodPopover, MoversTicker, TierRow } from '../../components/common';
import {
    fetchBotActivity, fetchMovers, fetchNarratives, fetchPropaganda, fetchSentiment,
} from '../../services/api';
import { deepLinkHref } from '../../services/deepLink';
import { formatCount, formatPct, formatPts } from '../../services/format';
import { coordinationLevel, saturationLevel } from '../../services/glossary';
import { transformPublicSentiment } from '../../services/transformers';
import { useFetch } from '../../services/useFetch';
import { COLORS } from '../../theme';
import type {
    BotData, EntitySentimentItem, MoversResult, NarrativeSummary,
    PropagandaOverview, PublicSentimentData,
} from '../../types';

// --------------------------------------------------------------------------- //
//  DigestSection — the live front page. Everything here reads from the same   //
//  7d snapshots the tabs use; every block deep-links into its tab. The old    //
//  Home was prose about what the data would show — this shows it.             //
// --------------------------------------------------------------------------- //

const DIGEST_WINDOW = '7d' as const;

function aggregateTier(items: EntitySentimentItem[] | undefined): { net: number | null; volume: number } {
    if (!items || items.length === 0) return { net: null, volume: 0 };
    let pos = 0, neg = 0, neu = 0;
    for (const it of items) {
        pos += it.positive;
        neg += it.negative;
        neu += it.neutral;
    }
    const total = pos + neg + neu;
    if (total === 0) return { net: null, volume: 0 };
    return { net: Math.round(((pos - neg) / total) * 1000) / 10, volume: total };
}

function toneVerb(net: number): string {
    if (net > 15) return 'clearly positive';
    if (net > 5) return 'slightly positive';
    if (net < -15) return 'clearly negative';
    if (net < -5) return 'slightly negative';
    return 'roughly neutral';
}

function toneColor(net: number): string {
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return 'var(--neutral-500)';
}

// Richer at-a-glance headline numbers for "this week in the sample" — the
// volume + overall tone + coverage the per-tier/signal blocks below don't state
// outright. All from the same 7d snapshots.
function HeadlineStats({
    sentiment, narratives,
}: {
    sentiment: PublicSentimentData | null;
    narratives: NarrativeSummary[] | null;
}) {
    const kpis: Array<{ label: string; value: string; detail: string; color?: string }> = [];
    if (sentiment) {
        kpis.push({
            label: 'Sampled posts',
            value: formatCount(sentiment.overview.volume),
            detail: 'scored in the last 7 days',
        });
        const net = sentiment.overview.netScore;
        kpis.push({
            label: 'Overall tone',
            value: formatPts(net),
            detail: toneVerb(net),
            color: toneColor(net),
        });
    }
    if (narratives) {
        kpis.push({
            label: 'Stories tracked',
            value: formatCount(narratives.length),
            detail: 'recurring claims',
        });
    }
    if (sentiment && sentiment.byTopic.length > 0) {
        const top = [...sentiment.byTopic].sort((a, b) => b.volume - a.volume)[0];
        kpis.push({
            label: 'Topics covered',
            value: formatCount(sentiment.byTopic.length),
            detail: top.topic ? `most on ${top.topic}` : 'across the sample',
        });
    }
    if (kpis.length === 0) return null;
    return (
        <div className="digest-kpis">
            {kpis.map((k) => (
                <div key={k.label} className="digest-kpi">
                    <span className="eyebrow">{k.label}</span>
                    <span
                        className="digest-kpi-value"
                        style={k.color ? { color: k.color } : undefined}
                    >
                        {k.value}
                    </span>
                    <span className="digest-kpi-detail">{k.detail}</span>
                </div>
            ))}
        </div>
    );
}

function ToneDigest({ data }: { data: PublicSentimentData }) {
    const tiers = [
        { label: 'News articles are', agg: aggregateTier(data.byNewsOutlet) },
        { label: 'Officials are', agg: aggregateTier(data.byOfficial) },
        { label: 'The public is', agg: aggregateTier(data.byGeneralPublic) },
    ];
    if (tiers.every((t) => t.agg.net === null)) return null;
    return (
        <a className="digest-block" href={deepLinkHref('sentiment')}>
            <div className="digest-block-head">
                <span className="eyebrow">This week's tone</span>
                <span className="digest-block-cta">Overall Tone →</span>
            </div>
            <div className="digest-tier-rows">
                {tiers.map(({ label, agg }) => (
                    <TierRow
                        key={label}
                        label={label}
                        value={agg.net != null ? formatPts(agg.net) : '—'}
                        valueColor={agg.net != null ? toneColor(agg.net) : undefined}
                        verb={agg.net != null
                            ? `${toneVerb(agg.net)} · ${formatCount(agg.volume)} sampled posts`
                            : 'no posts in this window'}
                        showZeroTick
                        dotPct={agg.net != null ? ((agg.net + 100) / 200) * 100 : undefined}
                        dotColor={agg.net != null ? toneColor(agg.net) : undefined}
                    />
                ))}
            </div>
        </a>
    );
}

const DIGEST_STORY_COUNT = 4;

const SOURCE_DOT_COLOR: Record<string, string> = {
    news: 'var(--source-news)',
    reddit_post: 'var(--source-reddit)',
    reddit_comment: 'var(--source-reddit)',
    x_post: 'var(--source-x)',
};

const SOURCE_LABEL: Record<string, string> = {
    news: 'News',
    reddit_post: 'Reddit post',
    reddit_comment: 'Reddit comment',
    x_post: 'X post',
};

/** Legend keying the source-mix bar's colors — the bar carries no inline
 *  labels, so the color is the sole encoding without this. */
function SourceMixLegend() {
    const entries: Array<[string, string]> = [
        ['News', 'var(--source-news)'],
        ['Reddit', 'var(--source-reddit)'],
        ['X', 'var(--source-x)'],
    ];
    return (
        <div className="chart-swatch-legend" aria-hidden>
            {entries.map(([label, color]) => (
                <span key={label} className="chart-swatch-item">
                    <span className="chart-swatch" style={{ background: color }} />
                    {label}
                </span>
            ))}
        </div>
    );
}

function StoriesDigest({ narratives }: { narratives: NarrativeSummary[] }) {
    const top = [...narratives]
        .sort((a, b) => b.supporting_doc_count - a.supporting_doc_count)
        .slice(0, DIGEST_STORY_COUNT);
    if (top.length === 0) return null;
    return (
        <div className="digest-block digest-block-static">
            <div className="digest-block-head">
                <span className="eyebrow">Most repeated claims</span>
                <a className="digest-block-cta" href={deepLinkHref('narratives')}>
                    Political Narratives →
                </a>
            </div>
            <ul className="digest-story-list">
                {top.map((n) => {
                    const barTotal = n.source_breakdown.reduce((s, it) => s + it.count, 0);
                    return (
                        <li key={n.narrative_id}>
                            <a
                                className="digest-story-row"
                                href={deepLinkHref('narratives', { open: String(n.narrative_id) })}
                                title={`${n.name} — ${n.supporting_doc_count} posts. Open on the Narratives page.`}
                            >
                                <span className="digest-story-name">{n.name || '(unnamed)'}</span>
                                {barTotal > 0 && (
                                    <span
                                        className="digest-story-bar"
                                        role="img"
                                        aria-label={`Source mix: ${n.source_breakdown
                                            .map((it) => `${Math.round((it.count / barTotal) * 100)}% ${SOURCE_LABEL[it.source_type] || it.source_type}`)
                                            .join(', ')}`}
                                    >
                                        {n.source_breakdown.map((it) => (
                                            <span
                                                key={it.source_type}
                                                title={`${SOURCE_LABEL[it.source_type] || it.source_type} — ${it.count} of ${barTotal} posts (${Math.round((it.count / barTotal) * 100)}%)`}
                                                style={{
                                                    width: `${(it.count / barTotal) * 100}%`,
                                                    background: SOURCE_DOT_COLOR[it.source_type] || 'var(--neutral-400)',
                                                }}
                                            />
                                        ))}
                                    </span>
                                )}
                                <span className="digest-story-count">
                                    {formatCount(n.supporting_doc_count)} posts
                                </span>
                            </a>
                        </li>
                    );
                })}
            </ul>
            <SourceMixLegend />
        </div>
    );
}

function SignalTiles({
    propaganda, bots,
}: {
    propaganda: PropagandaOverview | null;
    bots: BotData | null;
}) {
    if (!propaganda && !bots) return null;
    return (
        <div className="digest-tiles">
            {propaganda && (
                <a className="digest-tile" href={deepLinkHref('propaganda')}>
                    <span className="eyebrow">Persuasion techniques</span>
                    <span className="digest-tile-value">
                        {formatPct(propaganda.propaganda_rate_pct)}
                    </span>
                    <span className="digest-tile-detail">
                        of scored posts flagged · {saturationLevel(propaganda.mean_score)} saturation
                    </span>
                    <span className="digest-block-cta">Propaganda →</span>
                </a>
            )}
            {bots && (
                <a className="digest-tile" href={deepLinkHref('bots')}>
                    <span className="eyebrow">Suspected automation</span>
                    <span className="digest-tile-value">
                        {formatPct(bots.overview.suspectedAutomationRate, { decimals: 0 })}
                    </span>
                    <span className="digest-tile-detail">
                        of scanned posts · {coordinationLevel(bots.overview.coordinationIndex)} coordination
                    </span>
                    <span className="digest-block-cta">Bot Detector →</span>
                </a>
            )}
        </div>
    );
}

export function DigestSection() {
    const { data: sentiment } = useFetch<PublicSentimentData>(
        async () => transformPublicSentiment(await fetchSentiment(DIGEST_WINDOW)),
        [],
        `sentiment:${DIGEST_WINDOW}`,
    );
    const { data: narratives } = useFetch<NarrativeSummary[]>(
        () => fetchNarratives(DIGEST_WINDOW), [], `narratives:${DIGEST_WINDOW}`,
    );
    const { data: movers } = useFetch<MoversResult>(
        () => fetchMovers(DIGEST_WINDOW), [], `movers:${DIGEST_WINDOW}`,
    );
    const { data: propaganda } = useFetch<PropagandaOverview>(
        () => fetchPropaganda(DIGEST_WINDOW), [], `propaganda:${DIGEST_WINDOW}`,
    );
    const { data: bots } = useFetch<BotData>(
        () => fetchBotActivity(DIGEST_WINDOW), [], `bot-activity:${DIGEST_WINDOW}`,
    );

    // The digest renders only what loaded — a failed block disappears
    // rather than blocking the page (the prose below still explains the
    // product). No spinners: Home should read instantly.
    if (!sentiment && !narratives && !movers && !propaganda && !bots) return null;

    return (
        <section aria-label="This week at a glance">
            <div className="digest-head">
                <div className="eyebrow" style={{ marginBottom: 0 }}>
                    This week in the sample
                </div>
                <MethodPopover
                    description={
                        'All figures cover the last 7 days of sampled posts and match the '
                        + 'tabs they link to. They summarize what we collected — samples, '
                        + 'not polls of the public.'
                    }
                />
            </div>
            <HeadlineStats sentiment={sentiment} narratives={narratives} />
            {movers && <MoversTicker data={movers} />}
            <div className="digest-grid">
                {sentiment && <ToneDigest data={sentiment} />}
                {narratives && <StoriesDigest narratives={narratives} />}
            </div>
            <SignalTiles propaganda={propaganda} bots={bots} />
        </section>
    );
}

export default DigestSection;
