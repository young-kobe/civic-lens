import { Card, MethodPopover, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { SentimentBar, TrendStrip } from '../components/charts';
import type { Filters, PublicSentimentData, SentimentBreakdown, SocialVsNewsSentiment, PollingSocialComparison, TrendPoint } from '../types';

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
            <div className="grid-2 gap-6">
                {/* Sampled Social (Reddit + X) */}
                <div className="card" style={{ background: 'var(--neutral-50)', border: 'none', padding: 'var(--space-4)' }}>
                    <div className="flex justify-between items-center mb-3">
                        <span className="badge badge-accent">Sampled Social (Reddit + X)</span>
                        <span className="text-xs text-muted">{data.social.volume.toLocaleString()} items</span>
                    </div>
                    <div className="text-center mb-3">
                        <div className="text-2xl font-bold" style={{ color: getScoreColor(data.social.netScore) }}>
                            {formatNetScore(data.social.netScore)}
                        </div>
                        <div className="text-xs text-muted">Net Score</div>
                    </div>
                    <SentimentBar
                        positive={data.social.positive}
                        negative={data.social.negative}
                        neutral={data.social.neutral}
                        height={24}
                        showLabels={false}
                    />
                </div>

                {/* News Outlets */}
                <div className="card" style={{ background: 'var(--neutral-50)', border: 'none', padding: 'var(--space-4)' }}>
                    <div className="flex justify-between items-center mb-3">
                        <span className="badge badge-neutral">News Outlets</span>
                        <span className="text-xs text-muted">{data.news.volume.toLocaleString()} items</span>
                    </div>
                    <div className="text-center mb-3">
                        <div className="text-2xl font-bold" style={{ color: getScoreColor(data.news.netScore) }}>
                            {formatNetScore(data.news.netScore)}
                        </div>
                        <div className="text-xs text-muted">Net Score</div>
                    </div>
                    <SentimentBar
                        positive={data.news.positive}
                        negative={data.news.negative}
                        neutral={data.news.neutral}
                        height={24}
                        showLabels={false}
                    />
                </div>
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
        <div className="flex flex-col gap-6">
            {/* Sampling disclaimer — invariant: never imply universal American sentiment */}
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
                <strong>Sampled political discourse:</strong> sentiment reflects US political news articles plus
                sampled political Reddit and X discussions from the last 30 days. This is a snapshot of what the
                sources we ingest are saying about politics. It is not a scientific poll and not a representation
                of the full population.
            </div>

            {/* Overview Header */}
            <SentimentOverviewHeader data={data.overview} />

            {/* Social Media vs News Comparison */}
            <SocialVsNewsCard data={data.socialVsNews} />

            {/* Topic Sentiment (new design with reasoning) */}
            <TopicSentimentCard data={data.byTopic} />

            {/* Distribution */}
            <SentimentDistributionCard
                data={data.distribution}
                overview={data.overview}
                byPlatform={data.byPlatform}
            />

            {/* Time Window Breakdown */}
            <Card title="Sentiment by Time Window">
                <div className="grid-3">
                    {data.byTimeWindow.map((item: any, i: number) => (
                        <div key={i} className="card" style={{ background: 'var(--neutral-50)', border: 'none' }}>
                            <div className="text-sm font-medium mb-2">{item.window}</div>
                            <SentimentBar
                                positive={item.positive}
                                negative={item.negative}
                                neutral={item.neutral}
                                height={20}
                                showLabels={true}
                            />
                            <div className="text-xs text-muted mt-2">{item.volume.toLocaleString()} items</div>
                        </div>
                    ))}
                </div>
            </Card>

            {/* GOP Favorability (merged) */}
            {data.gopFavorability && (
                <GOPFavorabilityCard
                    favorability={data.gopFavorability}
                    trend={data.gopTrend}
                    pollingVsSocial={data.pollingVsSocial}
                />
            )}

            {/* Method Transparency */}
            <MethodTransparencyPanel />
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
            {/* Net favorability hero metric */}
            <div className="text-center mb-4">
                <div className="text-3xl font-bold" style={{ color: netColor }}>
                    {favorability.netFavorability >= 0 ? '+' : ''}{favorability.netFavorability.toFixed(1)}%
                </div>
                <div className="text-xs text-muted">Net Favorability</div>
            </div>

            {/* Stance distribution using SentimentBar */}
            <div className="mb-4">
                <div className="flex justify-between items-center mb-2">
                    <span className="font-medium text-sm">Stance Distribution</span>
                    <span className="text-xs text-muted">{favorability.sampleSize.toLocaleString()} docs</span>
                </div>
                <SentimentBar
                    positive={favorability.favorable}
                    negative={favorability.unfavorable}
                    neutral={favorability.neutral}
                    height={32}
                    showLabels={true}
                />
            </div>

            {/* Trend chart */}
            {trend && trend.length > 0 && (
                <div className="mt-6 pt-4" style={{ borderTop: '1px solid var(--neutral-100)' }}>
                    <h4 className="font-medium text-sm mb-3">Favorability Trend</h4>
                    <TrendStrip
                        data={trend}
                        dataKey="value"
                        xKey="date"
                        height={160}
                        color={netColor}
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
