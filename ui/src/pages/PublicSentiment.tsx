import { Card, MethodPopover, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { SentimentBar, TrendStrip } from '../components/charts';
import type { Filters, PublicSentimentData, SentimentBreakdown, SocialVsNewsSentiment, PollingSocialComparison, TrendPoint } from '../types';

/* ------------------------------------------------------------------ */
/*  Day-of-week / time-window sentiment card                          */
/* ------------------------------------------------------------------ */

interface DayOfWeekCardProps {
    byDayOfWeek?: SentimentBreakdown[];
    byTimeWindow: SentimentBreakdown[];
}

function DayOfWeekCard({ byDayOfWeek, byTimeWindow }: DayOfWeekCardProps) {
    const items = byDayOfWeek ?? byTimeWindow;
    const isDow = Boolean(byDayOfWeek);
    const title = isDow ? 'Sentiment by Day of Week' : 'Sentiment by Time Window';
    const subtitle = isDow
        ? 'How tone shifts across weekdays vs weekends in the current window'
        : 'Age buckets of docs in the current window';

    if (items.length === 0) {
        return (
            <Card title={title}>
                <p className="text-muted text-sm">No breakdown available for this window.</p>
            </Card>
        );
    }

    return (
        <Card title={title} subtitle={subtitle}>
            <div
                style={{
                    display: 'grid',
                    gridTemplateColumns: `repeat(${Math.min(items.length, 7)}, minmax(0, 1fr))`,
                    gap: 'var(--space-2)',
                }}
            >
                {items.map((item, i) => {
                    const bucketLabel = item.day ?? item.window ?? '—';
                    const total = item.positive + item.negative + item.neutral || 1;
                    const net = ((item.positive - item.negative) / total) * 100;
                    const netColor = net >= 5 ? 'var(--semantic-positive)'
                        : net <= -5 ? 'var(--semantic-negative)'
                        : 'var(--neutral-500)';
                    return (
                        <div
                            key={i}
                            style={{
                                background: 'var(--bg-inset)',
                                borderRadius: 'var(--radius-sm)',
                                padding: 'var(--space-3)',
                                display: 'flex',
                                flexDirection: 'column',
                                gap: 'var(--space-2)',
                            }}
                        >
                            <div className="flex items-baseline justify-between" style={{ gap: 4 }}>
                                <span
                                    className="text-sm font-semibold"
                                    style={{ letterSpacing: '-0.01em' }}
                                >
                                    {bucketLabel}
                                </span>
                                <span
                                    className="num"
                                    style={{
                                        fontVariantNumeric: 'tabular-nums',
                                        fontSize: '11px',
                                        fontWeight: 600,
                                        color: netColor,
                                    }}
                                >
                                    {net >= 0 ? '+' : ''}{net.toFixed(0)}%
                                </span>
                            </div>
                            <SentimentBar
                                positive={item.positive}
                                negative={item.negative}
                                neutral={item.neutral}
                                height={16}
                                showLabels={false}
                            />
                            <span
                                className="text-muted num"
                                style={{
                                    fontVariantNumeric: 'tabular-nums',
                                    fontSize: '10px',
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.06em',
                                }}
                            >
                                {item.volume.toLocaleString()} docs
                            </span>
                        </div>
                    );
                })}
            </div>
        </Card>
    );
}

import { fetchSentiment } from '../services/api';
import { transformPublicSentiment } from '../services/transformers';
import { useFetch } from '../services/useFetch';
import { SEMANTIC_COLORS } from '../theme';
import { SentimentDistributionCard } from './publicSentiment/SentimentDistributionCard';
import { SentimentOverviewHeader } from './publicSentiment/SentimentOverviewHeader';
import { TopicRow } from './publicSentiment/TopicRow';

const LABEL_BADGE_STYLES: Record<string, { bg: string; text: string }> = {
    POSITIVE: { bg: SEMANTIC_COLORS.positiveLight, text: SEMANTIC_COLORS.positive },
    NEGATIVE: { bg: SEMANTIC_COLORS.negativeLight, text: SEMANTIC_COLORS.negative },
    NEUTRAL: { bg: SEMANTIC_COLORS.neutralLight, text: '#45454d' },
    MIXED: { bg: SEMANTIC_COLORS.warningLight, text: SEMANTIC_COLORS.warning },
};





/* ------------------------------------------------------------------ */
/*  Topic Sentiment Card (replaces flat-bar breakdown for topics)     */
/* ------------------------------------------------------------------ */

interface TopicSentimentCardProps {
    data: SentimentBreakdown[];
}

function TopicSentimentCard({ data }: TopicSentimentCardProps) {
    return (
        <Card
            title="Sentiment by Topic"
            headerActions={
                <MethodPopover
                    description="Topics are extracted via keyword matching. Each topic shows a donut chart of sentiment proportions, a net sentiment badge, and expandable LLM reasoning samples."
                    limitations={[
                        'Sentiment classification may miss sarcasm or irony (flagged when detected)',
                        'Sample may not be representative of all discourse',
                    ]}
                />
            }
        >
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                {data.map((item, i) => (
                    <TopicRow key={i} item={item} labelBadgeStyles={LABEL_BADGE_STYLES} />
                ))}
            </div>
        </Card>
    );
}




interface ComparisonPanelProps {
    badgeClassName: string;
    badgeLabel: string;
    netScore: number;
    volume: number;
    positive: number;
    negative: number;
    neutral: number;
    formatNetScore: (n: number) => string;
    getScoreColor: (n: number) => string;
}

/**
 * One side of SocialVsNewsCard. Designed to be dense and legible:
 * - Badge + volume inline at top (no center gutter)
 * - Net score anchored left with an eyebrow label to its right
 * - SentimentBar fills remaining width
 *
 * Replaces a prior layout that centered the big score over empty whitespace
 * and stacked two independent rows of vertical padding.
 */
function ComparisonPanel({
    badgeClassName, badgeLabel, netScore, volume,
    positive, negative, neutral,
    formatNetScore, getScoreColor,
}: ComparisonPanelProps) {
    return (
        <div
            style={{
                background: 'var(--bg-inset)',
                borderRadius: 'var(--radius-md)',
                padding: 'var(--space-4)',
                display: 'flex',
                flexDirection: 'column',
                gap: 'var(--space-3)',
            }}
        >
            <div className="flex justify-between items-center" style={{ gap: 'var(--space-2)' }}>
                <span className={badgeClassName}>{badgeLabel}</span>
                <span
                    className="text-xs text-muted num"
                    style={{ fontVariantNumeric: 'tabular-nums' }}
                >
                    {volume.toLocaleString()} docs
                </span>
            </div>

            <div className="flex items-baseline" style={{ gap: 'var(--space-2)' }}>
                <span
                    style={{
                        fontFamily: 'var(--font-mono)',
                        fontVariantNumeric: 'tabular-nums',
                        fontSize: 'var(--text-3xl)',
                        fontWeight: 600,
                        letterSpacing: '-0.02em',
                        lineHeight: 1,
                        color: getScoreColor(netScore),
                    }}
                >
                    {formatNetScore(netScore)}
                </span>
                <span className="eyebrow">Net Score</span>
            </div>

            <SentimentBar
                positive={positive}
                negative={negative}
                neutral={neutral}
                height={20}
                showLabels={false}
            />
        </div>
    );
}

interface SocialVsNewsCardProps {
    data: SocialVsNewsSentiment | null | undefined;
}

function SocialVsNewsCard({ data }: SocialVsNewsCardProps) {
    if (!data) {
        return (
            <Card title="Social Media vs News Outlets">
                <p className="text-muted text-sm">No comparison data available.</p>
            </Card>
        );
    }

    const formatNetScore = (score: number) => {
        const sign = score >= 0 ? '+' : '';
        return `${sign}${score.toFixed(1)}%`;
    };

    const getScoreColor = (score: number) => {
        if (score > 10) return '#16a34a';
        if (score < -10) return '#dc2626';
        return '#9ca3af';
    };

    return (
        <Card
            title="Sampled Social Media vs News Outlets"
            subtitle="Comparing sentiment between sampled social posts (Reddit + X) and news coverage"
            headerActions={
                <MethodPopover
                    description="The left column is drawn from sampled political discussions on Reddit and X. Posts are not a statistical sample of the wider population, only of content we ingested through public APIs. The right column is news-article sentiment."
                    limitations={[
                        'Social sample over-represents engaged users and the specific subreddits / X queries we follow',
                        'News outlet sentiment may reflect editorial framing',
                        'Other platforms (TikTok, Facebook, private Discords, etc.) are not included',
                    ]}
                />
            }
        >
            <div className="grid-2 gap-4">
                <ComparisonPanel
                    badgeClassName="badge badge-accent"
                    badgeLabel="Sampled Social (Reddit + X)"
                    netScore={data.social.netScore}
                    volume={data.social.volume}
                    positive={data.social.positive}
                    negative={data.social.negative}
                    neutral={data.social.neutral}
                    formatNetScore={formatNetScore}
                    getScoreColor={getScoreColor}
                />
                <ComparisonPanel
                    badgeClassName="badge badge-neutral"
                    badgeLabel="News Outlets"
                    netScore={data.news.netScore}
                    volume={data.news.volume}
                    positive={data.news.positive}
                    negative={data.news.negative}
                    neutral={data.news.neutral}
                    formatNetScore={formatNetScore}
                    getScoreColor={getScoreColor}
                />
            </div>

            {/* Disparity indicator */}
            {data.social.volume > 0 && data.news.volume > 0 && (
                <div className="card-note mt-4">
                    {Math.abs(data.social.netScore - data.news.netScore) > 20 ? (
                        <strong>Significant disparity detected:</strong>
                    ) : null}
                    {' '}Sampled social (Reddit + X) sentiment is {data.social.netScore > data.news.netScore ? 'more favorable' : 'less favorable'} than news coverage
                    by {Math.abs(data.social.netScore - data.news.netScore).toFixed(1)} percentage points.
                </div>
            )}
        </Card>
    );
}

function MethodTransparencyPanel() {
    return (
        <Card
            title="Methodology"
            note="This section explains how sentiment scores are derived and their limitations."
        >
            <div className="flex flex-col gap-4 text-sm">
                <div>
                    <h4 className="font-medium mb-1">Data Sources</h4>
                    <p className="text-muted">
                        Sentiment is derived from news articles, sampled Reddit discussions, and sampled X (Twitter) posts.
                        The social-media data is explicitly labeled as "sampled" because it only covers the specific subreddits
                        and X queries we ingest through public APIs. It does not represent the full population of online discourse.
                    </p>
                </div>

                <div>
                    <h4 className="font-medium mb-1">Classification Method</h4>
                    <p className="text-muted">
                        Text is classified by an LLM (Gemini or local Ollama) with evidence-span validation and per-sample
                        confidence scores. Classifications with unverifiable evidence are capped at low confidence.
                    </p>
                </div>

                <div>
                    <h4 className="font-medium mb-1">Known Limitations</h4>
                    <ul className="text-muted" style={{ margin: 0, paddingLeft: 'var(--space-5)' }}>
                        <li>Sarcasm and irony may be misclassified</li>
                        <li>Non-English content is excluded from analysis</li>
                        <li>Social media samples may over-represent highly engaged users</li>
                        <li>Sentiment does not equal opinion polling</li>
                    </ul>
                </div>
            </div>
        </Card>
    );
}

interface PublicSentimentProps {
    filters: Filters;
}

function PublicSentiment({ filters }: PublicSentimentProps) {
    const { data, loading, error, refetch } = useFetch<PublicSentimentData>(
        async () => transformPublicSentiment(await fetchSentiment(filters.timeRange)),
        [filters.timeRange],
        `sentiment:${filters.timeRange}`,
    );

    if (error) {
        return <ErrorState message={error.message} onRetry={refetch} />;
    }


    if (loading) {
        return (
            <div className="flex flex-col gap-4">
                <LoadingCard />
                <div className="grid-2">
                    <LoadingCard />
                    <LoadingCard />
                </div>
            </div>
        );
    }

    if (!data) {
        return <EmptyState title="No sentiment data available" />;
    }

    return (
        <div className="dashboard-grid">
            {/* Sampling disclaimer — invariant: never imply universal American sentiment */}
            <div
                className="col-span-12"
                style={{
                    padding: 'var(--space-3) var(--space-4)',
                    background: '#fffbeb',
                    border: '1px solid #fbbf24',
                    borderRadius: 'var(--radius-md)',
                    fontSize: 'var(--text-xs)',
                    color: '#92400e',
                }}
            >
                <strong>Sampled political discourse:</strong> sentiment reflects US political news articles plus
                sampled political Reddit and X discussions from the last 30 days. This is a snapshot of what the
                sources we ingest are saying about politics. It is not a scientific poll and not a representation
                of the full population.
            </div>

            {/* Row: compact overview (5) + social vs news comparison (7) */}
            <div className="col-span-5">
                <SentimentOverviewHeader data={data.overview} />
            </div>
            <div className="col-span-7">
                <SocialVsNewsCard data={data.socialVsNews} />
            </div>

            {/* Row: topic list (5) + day-of-week tiles (7).
                DoW's 7 tiles breathe better in the wider slot; Topic's list fits
                the narrower one because each row is vertically compact. */}
            <div className="col-span-5">
                <TopicSentimentCard data={data.byTopic} />
            </div>
            <div className="col-span-7">
                <DayOfWeekCard
                    byDayOfWeek={data.byDayOfWeek && data.byDayOfWeek.length > 0 ? data.byDayOfWeek : undefined}
                    byTimeWindow={data.byTimeWindow}
                />
            </div>

            {/* Row: distribution (full) — 5-seg bar + drill-down modal want full width */}
            <div className="col-span-12">
                <SentimentDistributionCard
                    data={data.distribution}
                    overview={data.overview}
                    byPlatform={data.byPlatform}
                    samples={data.distributionSamples}
                />
            </div>

            {/* Row: GOP favorability (full) — trend chart wants horizontal room;
                internal hero is a 2-col layout so the big number doesn't leave whitespace. */}
            {data.gopFavorability && (
                <div className="col-span-12">
                    <GOPFavorabilityCard
                        favorability={data.gopFavorability}
                        trend={data.gopTrend}
                        pollingVsSocial={data.pollingVsSocial}
                    />
                </div>
            )}

            {/* Row: methodology (full) */}
            <div className="col-span-12">
                <MethodTransparencyPanel />
            </div>
        </div>
    );
}

interface GOPFavorabilityCardProps {
    favorability: NonNullable<PublicSentimentData['gopFavorability']>;
    trend: TrendPoint[] | null | undefined;
    pollingVsSocial: PollingSocialComparison | null | undefined;
}

function GOPFavorabilityCard({ favorability, trend, pollingVsSocial }: GOPFavorabilityCardProps) {
    const netColor = favorability.netFavorability > 0
        ? '#16a34a' : favorability.netFavorability < 0
            ? '#dc2626' : '#9ca3af';

    return (
        <Card
            title="GOP Favorability"
            subtitle={`Based on ${favorability.sampleSize.toLocaleString()} analyzed documents across ${favorability.sourceCount} platforms`}
            headerActions={
                <MethodPopover
                    description="GOP favorability is derived from stance indicators found in the same content analyzed for sentiment. Stance is determined via LLM classification with proximity matching to GOP entities."
                    limitations={['Stance may not reflect personal opinion of the author', 'Neutral stance may simply indicate factual reporting']}
                />
            }
        >
            {/* Compact hero: net favorability + stance distribution side-by-side.
                Replaces the old centered big-number stack which left a lot of
                whitespace on wide viewports. */}
            <div
                style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
                    gap: 'var(--space-4)',
                    alignItems: 'center',
                    marginBottom: 'var(--space-4)',
                }}
            >
                <div>
                    <div className="eyebrow mb-1">Net Favorability</div>
                    <div
                        style={{
                            fontSize: 'var(--text-4xl)',
                            fontWeight: 600,
                            color: netColor,
                            letterSpacing: '-0.02em',
                            lineHeight: 1,
                            fontFamily: 'var(--font-mono)',
                            fontVariantNumeric: 'tabular-nums',
                        }}
                    >
                        {favorability.netFavorability >= 0 ? '+' : ''}{favorability.netFavorability.toFixed(1)}%
                    </div>
                    <div className="text-xs text-muted mt-2">
                        {favorability.sampleSize.toLocaleString()} docs &middot; {favorability.sourceCount} platforms
                    </div>
                </div>
                <div
                    style={{
                        paddingLeft: 'var(--space-4)',
                        borderLeft: '1px solid var(--neutral-200)',
                    }}
                >
                    <div className="eyebrow mb-2">Stance Distribution</div>
                    <SentimentBar
                        positive={favorability.favorable}
                        negative={favorability.unfavorable}
                        neutral={favorability.neutral}
                        height={28}
                        showLabels={true}
                    />
                </div>
            </div>

            {/* Trend chart */}
            {trend && trend.length > 0 && (
                <div className="mt-4 pt-4" style={{ borderTop: '1px solid var(--neutral-150)' }}>
                    <div className="eyebrow mb-2">Favorability Trend</div>
                    <TrendStrip
                        data={trend}
                        dataKey="value"
                        xKey="date"
                        height={160}
                        color={netColor}
                        unit="%"
                    />
                </div>
            )}

            {/* Polling comparison */}
            {pollingVsSocial && (
                <GOPPollingComparison data={pollingVsSocial} />
            )}
        </Card>
    );
}

function GOPPollingComparison({ data }: { data: PollingSocialComparison }) {
    return (
        <div className="mt-6 pt-4" style={{ borderTop: '1px solid var(--neutral-100)' }}>
            <h4 className="font-medium text-sm mb-3">Polling vs Online Sentiment</h4>
            <div className="grid-2 gap-4">
                <div className="card" style={{ background: 'var(--neutral-50)', border: 'none', padding: 'var(--space-3)' }}>
                    <div className="flex items-center gap-2 mb-2">
                        <span className="badge badge-accent">Online Sentiment</span>
                    </div>
                    <SentimentBar
                        positive={data.onlineSentiment?.favorable ?? 0}
                        negative={data.onlineSentiment?.unfavorable ?? 0}
                        neutral={data.onlineSentiment?.neutral ?? 0}
                        height={24}
                        showLabels={true}
                    />
                </div>
                <div className="card" style={{ background: 'var(--neutral-50)', border: 'none', padding: 'var(--space-3)' }}>
                    <div className="flex items-center gap-2 mb-2">
                        <span className="badge badge-neutral">Live Polling</span>
                        {data.pollingData?.source && (
                            <span style={{ fontSize: '10px', color: 'var(--text-muted)', marginLeft: 'auto' }}>
                                {data.pollingData.source}
                            </span>
                        )}
                    </div>
                    {data.pollingData ? (
                        <>
                            <SentimentBar
                                positive={data.pollingData.favorable}
                                negative={data.pollingData.unfavorable}
                                neutral={data.pollingData.neutral}
                                height={24}
                                showLabels={true}
                            />
                            {data.pollingData.date && (
                                <div style={{ fontSize: '10px', color: 'var(--text-muted)', marginTop: '4px', textAlign: 'right' }}>
                                    Last updated: {data.pollingData.date}
                                </div>
                            )}
                        </>
                    ) : (
                        <div style={{
                            padding: '8px 12px', borderRadius: 'var(--radius-sm)',
                            background: 'rgba(234,179,8,0.08)', border: '1px solid rgba(234,179,8,0.2)',
                        }}>
                            <p className="text-muted text-xs" style={{ margin: 0 }}>
                                Live polling data is currently unavailable. This may be due to a network issue or a change in the source page structure.
                            </p>
                        </div>
                    )}
                </div>
            </div>
            <div className="card-note mt-3">
                Online sentiment is derived from sampled social media discourse and should not be interpreted as equivalent to scientific polling data.
            </div>
        </div>
    );
}

export default PublicSentiment;
