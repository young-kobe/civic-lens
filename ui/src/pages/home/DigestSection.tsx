import { MethodPopover, MoversTicker, TierRow } from '../../components/common';
import {
    fetchBotActivity, fetchMovers, fetchNarratives, fetchPropaganda, fetchSentiment,
    type MoversWindow,
} from '../../services/api';
import { deepLinkHref } from '../../services/deepLink';
import { formatCount, formatPct, formatPts } from '../../services/format';
import { saturationLevel } from '../../services/glossary';
import { useFetch } from '../../services/useFetch';
import { COLORS } from '../../theme';
import type {
    BotActivityResponse, MoversResponse, NarrativesResponse,
    PropagandaOverview, SentimentPanelResponse, TierSplit,
} from '../../types';

// --------------------------------------------------------------------------- //
//  DigestSection — the live front page. Everything here reads from the same   //
//  7d aggregates the tabs use; every block deep-links into its tab.           //
// --------------------------------------------------------------------------- //

const DIGEST_WINDOW = '7d' as const;

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

function HeadlineStats({
    sentiment, narratives,
}: {
    sentiment: SentimentPanelResponse | null;
    narratives: NarrativesResponse | null;
}) {
    const kpis: Array<{ label: string; value: string; detail: string; color?: string }> = [];
    if (sentiment) {
        kpis.push({ label: 'Sampled posts', value: formatCount(sentiment.overview.volume), detail: 'scored in the last 7 days' });
        const net = sentiment.overview.netScore;
        if (net != null) {
            kpis.push({ label: 'Overall tone', value: formatPts(net), detail: toneVerb(net), color: toneColor(net) });
        }
    }
    if (narratives) {
        kpis.push({ label: 'Stories tracked', value: formatCount(narratives.narratives.length), detail: 'recurring claims' });
    }
    if (sentiment && sentiment.byTopic.length > 0) {
        const top = [...sentiment.byTopic].sort((a, b) => b.volume - a.volume)[0];
        kpis.push({ label: 'Topics covered', value: formatCount(sentiment.byTopic.length), detail: `most on ${top.topic}` });
    }
    if (kpis.length === 0) return null;
    return (
        <div className="digest-kpis">
            {kpis.map((k) => (
                <div key={k.label} className="digest-kpi">
                    <span className="eyebrow">{k.label}</span>
                    <span className="digest-kpi-value" style={k.color ? { color: k.color } : undefined}>{k.value}</span>
                    <span className="digest-kpi-detail">{k.detail}</span>
                </div>
            ))}
        </div>
    );
}

const TIER_LABEL: Record<TierSplit['tier'], string> = {
    news: 'News articles are', officials: 'Officials are', general_public: 'The public is',
};

function ToneDigest({ data }: { data: SentimentPanelResponse }) {
    const order: TierSplit['tier'][] = ['news', 'officials', 'general_public'];
    const rows = order.map((tier) => ({ tier, row: data.byTier.find((t) => t.tier === tier) }));
    if (rows.every(({ row }) => !row || row.volume === 0)) return null;
    return (
        <a className="digest-block" href={deepLinkHref('sentiment')}>
            <div className="digest-block-head">
                <span className="eyebrow">This week's tone</span>
                <span className="digest-block-cta">Overall Tone →</span>
            </div>
            <div className="digest-tier-rows">
                {rows.map(({ tier, row }) => {
                    const net = row?.netScore ?? null;
                    return (
                        <TierRow
                            key={tier}
                            label={TIER_LABEL[tier]}
                            value={net != null ? formatPts(net) : '—'}
                            valueColor={net != null ? toneColor(net) : undefined}
                            verb={net != null ? `${toneVerb(net)} · ${formatCount(row?.volume ?? 0)} sampled posts` : 'no posts in this window'}
                            showZeroTick
                            dotPct={net != null ? ((net + 100) / 200) * 100 : undefined}
                            dotColor={net != null ? toneColor(net) : undefined}
                        />
                    );
                })}
            </div>
        </a>
    );
}

const DIGEST_STORY_COUNT = 4;
const SOURCE_DOT_COLOR: Record<string, string> = {
    news: 'var(--source-news)', reddit: 'var(--source-reddit)', x: 'var(--source-x)',
};
const SOURCE_LABEL: Record<string, string> = { news: 'News', reddit: 'Reddit', x: 'X' };

function SourceMixLegend() {
    const entries: Array<[string, string]> = [
        ['News', 'var(--source-news)'], ['Reddit', 'var(--source-reddit)'], ['X', 'var(--source-x)'],
    ];
    return (
        <div className="chart-swatch-legend" aria-hidden>
            {entries.map(([label, color]) => (
                <span key={label} className="chart-swatch-item">
                    <span className="chart-swatch" style={{ background: color }} />{label}
                </span>
            ))}
        </div>
    );
}

function StoriesDigest({ narratives }: { narratives: NarrativesResponse }) {
    const top = [...narratives.narratives].sort((a, b) => b.docCount - a.docCount).slice(0, DIGEST_STORY_COUNT);
    if (top.length === 0) return null;
    return (
        <div className="digest-block digest-block-static">
            <div className="digest-block-head">
                <span className="eyebrow">Most repeated claims</span>
                <a className="digest-block-cta" href={deepLinkHref('narratives')}>Political Narratives →</a>
            </div>
            <ul className="digest-story-list">
                {top.map((n) => {
                    const barTotal = n.sourceBreakdown.reduce((s, it) => s + it.docCount, 0);
                    return (
                        <li key={n.narrativeId}>
                            <a
                                className="digest-story-row"
                                href={deepLinkHref('narratives', { open: String(n.narrativeId) })}
                                title={`${n.anchorClaimText ?? '(unnamed)'} — ${n.docCount} posts. Open on the Narratives page.`}
                            >
                                <span className="digest-story-name">{n.anchorClaimText || '(unnamed)'}</span>
                                {barTotal > 0 && (
                                    <span className="digest-story-bar" role="img" aria-label="Source mix">
                                        {n.sourceBreakdown.map((it) => (
                                            <span
                                                key={it.source}
                                                title={`${SOURCE_LABEL[it.source] || it.source} — ${it.docCount} of ${barTotal} posts`}
                                                style={{ width: `${(it.docCount / barTotal) * 100}%`, background: SOURCE_DOT_COLOR[it.source] || 'var(--neutral-400)' }}
                                            />
                                        ))}
                                    </span>
                                )}
                                <span className="digest-story-count">{formatCount(n.docCount)} posts</span>
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
    bots: BotActivityResponse | null;
}) {
    if (!propaganda && !bots) return null;
    return (
        <div className="digest-tiles">
            {propaganda && (
                <a className="digest-tile" href={deepLinkHref('propaganda')}>
                    <span className="eyebrow">Persuasion techniques</span>
                    <span className="digest-tile-value">{formatPct(propaganda.flaggedRatePct)}</span>
                    <span className="digest-tile-detail">of scored posts flagged · {saturationLevel(propaganda.meanScore)} saturation</span>
                    <span className="digest-block-cta">Propaganda →</span>
                </a>
            )}
            {bots && (
                <a className="digest-tile" href={deepLinkHref('bots')}>
                    <span className="eyebrow">Suspected automation</span>
                    <span className="digest-tile-value">{formatPct(bots.automationRatePct, { decimals: 0 })}</span>
                    <span className="digest-tile-detail">of {formatCount(bots.analyzedDocCount)} scored posts</span>
                    <span className="digest-block-cta">Bot Detector →</span>
                </a>
            )}
        </div>
    );
}

export function DigestSection() {
    const { data: sentiment } = useFetch<SentimentPanelResponse>(
        () => fetchSentiment(DIGEST_WINDOW), [], `sentiment:${DIGEST_WINDOW}`,
    );
    const { data: narratives } = useFetch<NarrativesResponse>(
        () => fetchNarratives(DIGEST_WINDOW), [], `narratives:${DIGEST_WINDOW}`,
    );
    const moversWindow: MoversWindow = DIGEST_WINDOW;
    const { data: movers } = useFetch<MoversResponse>(
        () => fetchMovers(moversWindow), [], `movers:${moversWindow}`,
    );
    const { data: propaganda } = useFetch<PropagandaOverview>(
        () => fetchPropaganda(DIGEST_WINDOW), [], `propaganda:${DIGEST_WINDOW}`,
    );
    const { data: bots } = useFetch<BotActivityResponse>(
        () => fetchBotActivity(DIGEST_WINDOW), [], `bot-activity:${DIGEST_WINDOW}`,
    );

    // The digest renders only what loaded — a failed block disappears
    // rather than blocking the page.
    if (!sentiment && !narratives && !movers && !propaganda && !bots) return null;

    return (
        <section aria-label="This week at a glance">
            <div className="digest-head">
                <div className="eyebrow" style={{ marginBottom: 0 }}>This week in the sample</div>
                <MethodPopover
                    description={
                        'All figures cover the last 7 days of sampled posts and match the tabs they '
                        + 'link to. They summarize what we collected — samples, not polls.'
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
