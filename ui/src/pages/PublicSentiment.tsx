import { useState, useEffect } from 'react';
import { Card, ConfidenceBadge, MethodPopover, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { SentimentBar } from '../components/charts';
import type { Filters, PublicSentimentData, SentimentOverview, SentimentBreakdown, SentimentDistribution, SocialVsNewsSentiment } from '../types';

import { fetchSentiment } from '../services/api';
import { transformPublicSentiment } from '../services/transformers';


interface SentimentOverviewHeaderProps {
    data: SentimentOverview;
}

function SentimentOverviewHeader({ data }: SentimentOverviewHeaderProps) {
    const getScoreDisplay = (score: number) => {
        if (score > 0.1) return { label: 'Positive', class: 'metric-delta-positive' };
        if (score < -0.1) return { label: 'Negative', class: 'metric-delta-negative' };
        return { label: 'Neutral', class: 'metric-delta-neutral' };
    };

    const scoreInfo = getScoreDisplay(data.netScore);

    return (
        <Card className="mb-6">
            <div className="flex items-start justify-between">
                <div>
                    <div className="text-xs font-medium text-muted mb-1">Net Sentiment Score</div>
                    <div className="flex items-baseline gap-3">
                        <span className="metric-value-lg">
                            {data.netScore >= 0 ? '+' : ''}{(data.netScore * 100).toFixed(1)}%
                        </span>
                        <span className={`text-lg font-medium ${scoreInfo.class}`}>
                            {scoreInfo.label}
                        </span>
                    </div>
                </div>
                <div className="text-right">
                    <div className="text-2xl font-semibold">{data.volume.toLocaleString()}</div>
                    <div className="text-sm text-muted">Total volume</div>
                </div>
            </div>
            <div className="mt-4 pt-4" style={{ borderTop: '1px solid var(--neutral-100)' }}>
                <ConfidenceBadge
                    coverage={data.coverage}
                    confidence={data.confidence}
                    sampleSize={data.volume}
                />
            </div>
        </Card>
    );
}

interface SentimentBreakdownCardProps {
    title: string;
    data: SentimentBreakdown[];
    labelKey: 'topic' | 'platform' | 'window';
    methodDescription?: string;
}

function SentimentBreakdownCard({ title, data, labelKey, methodDescription }: SentimentBreakdownCardProps) {
    return (
        <Card
            title={title}
            headerActions={
                methodDescription ? (
                    <MethodPopover
                        description={methodDescription}
                        limitations={['Sentiment classification may miss sarcasm or irony', 'Sample may not be representative of all discourse']}
                    />
                ) : undefined
            }
        >
            <div className="flex flex-col gap-4">
                {data.map((item, i) => (
                    <div key={i}>
                        <div className="flex justify-between items-center mb-2">
                            <span className="font-medium text-sm">{item[labelKey]}</span>
                            <span className="text-xs text-muted">{item.volume.toLocaleString()} items</span>
                        </div>
                        <SentimentBar
                            positive={item.positive}
                            negative={item.negative}
                            neutral={item.neutral}
                            height={24}
                            showLabels={false}
                        />
                    </div>
                ))}
            </div>
        </Card>
    );
}

interface SentimentDistributionCardProps {
    data: SentimentDistribution;
}

function SentimentDistributionCard({ data }: SentimentDistributionCardProps) {
    const total = Object.values(data).reduce((a, b) => a + b, 0) || 1;

    const segments = [
        { label: 'Strong unfavorable', value: data.strongNegative, color: '#991b1b' },
        { label: 'Mild unfavorable', value: data.mildNegative, color: '#dc2626' },
        { label: 'Neutral', value: data.neutral, color: '#9ca3af' },
        { label: 'Mild favorable', value: data.mildPositive, color: '#22c55e' },
        { label: 'Strong favorable', value: data.strongPositive, color: '#16a34a' },
    ];

    return (
        <Card
            title="Sentiment Distribution"
            subtitle="Including intensity levels"
            headerActions={
                <MethodPopover
                    description="Sentiment intensity classified using a 5-point scale based on lexical and contextual signals."
                    limitations={['Intensity thresholds are model-dependent']}
                />
            }
        >
            {/* Stacked bar */}
            <div
                style={{
                    display: 'flex',
                    height: '48px',
                    borderRadius: 'var(--radius-md)',
                    overflow: 'hidden',
                    marginBottom: 'var(--space-4)'
                }}
            >
                {segments.map((seg, i) => (
                    <div
                        key={i}
                        style={{
                            width: `${(seg.value / total) * 100}%`,
                            background: seg.color,
                            transition: 'width var(--transition-base)',
                        }}
                        title={`${seg.label}: ${seg.value}`}
                    />
                ))}
            </div>

            {/* Legend */}
            <div className="grid-2 gap-2">
                {segments.map((seg, i) => (
                    <div key={i} className="flex items-center gap-2">
                        <div
                            style={{
                                width: '12px',
                                height: '12px',
                                borderRadius: 'var(--radius-sm)',
                                background: seg.color
                            }}
                        />
                        <span className="text-sm">{seg.label}</span>
                        <span className="text-xs text-muted ml-auto">
                            {((seg.value / total) * 100).toFixed(1)}%
                        </span>
                    </div>
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
            title="Social Media vs News Outlets"
            subtitle="Compare sentiment between social platforms and news coverage"
            headerActions={
                <MethodPopover
                    description="Shows sentiment breakdown from social media (Reddit, Twitter) versus news outlets. Useful for detecting narrative divergence."
                    limitations={['Social media sample may over-represent engaged users', 'News outlet sentiment may reflect editorial framing']}
                />
            }
        >
            <div className="grid-2 gap-6">
                {/* Social Media */}
                <div className="card" style={{ background: 'var(--neutral-50)', border: 'none', padding: 'var(--space-4)' }}>
                    <div className="flex justify-between items-center mb-3">
                        <span className="badge badge-accent">Social Media</span>
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
                    {' '}Social media sentiment is {data.social.netScore > data.news.netScore ? 'more favorable' : 'less favorable'} than news coverage
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
                        Sentiment is derived from news articles, sampled Reddit discussions, and sampled social media posts.
                        Reddit and social data are explicitly labeled as "sampled" because they do not represent the full population of discourse.
                    </p>
                </div>

                <div>
                    <h4 className="font-medium mb-1">Classification Method</h4>
                    <p className="text-muted">
                        Text is classified using a transformer-based sentiment model fine-tuned on political and news content.
                        Each classification includes a confidence score (not shown in aggregate views).
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
    const [data, setData] = useState<PublicSentimentData | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const loadData = async () => {
            setLoading(true);
            setError(null);
            try {
                const rawData = await fetchSentiment(filters.timeRange);
                const processedData = transformPublicSentiment(rawData);
                setData(processedData);
            } catch (err: any) {
                console.error("Failed to load sentiment:", err);
                setError(err.message || "Failed to load sentiment data.");
            } finally {
                setLoading(false);
            }
        };
        loadData();
    }, [filters]);

    if (error) {
        return <ErrorState message={error} onRetry={() => window.location.reload()} />;
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
            {/* Overview Header */}
            <SentimentOverviewHeader data={data.overview} />

            {/* Social vs News Comparison */}
            <SocialVsNewsCard data={data.socialVsNews} />

            {/* Breakdown Cards */}
            <SentimentBreakdownCard
                title="Sentiment by Topic"
                data={data.byTopic}
                labelKey="topic"
                methodDescription="Topics are automatically clustered from article content using unsupervised learning."
            />

            {/* Distribution */}
            <SentimentDistributionCard data={data.distribution} />

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

            {/* Method Transparency */}
            <MethodTransparencyPanel />
        </div>
    );
}

export default PublicSentiment;
