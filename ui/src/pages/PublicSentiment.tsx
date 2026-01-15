import { useState, useEffect } from 'react';
import { Card, MetricCard, ConfidenceBadge, MethodPopover, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { SentimentBar } from '../components/charts';
import type { Filters, SentimentData, SentimentOverview, SentimentBreakdown, SentimentDistribution, CoverageLevel, ConfidenceLevel } from '../types';

// Mock data for demonstration
const MOCK_SENTIMENT_DATA: SentimentData = {
    overview: {
        netScore: 0.12,
        volume: 24589,
        coverage: 'high',
        confidence: 'medium',
    },
    byTopic: [
        { topic: 'Economy', positive: 2340, negative: 1890, neutral: 1456, volume: 5686 },
        { topic: 'Healthcare', positive: 1234, negative: 2345, neutral: 890, volume: 4469 },
        { topic: 'Immigration', positive: 890, negative: 3456, neutral: 567, volume: 4913 },
        { topic: 'Climate', positive: 2100, negative: 1200, neutral: 700, volume: 4000 },
        { topic: 'Education', positive: 1800, negative: 900, neutral: 1100, volume: 3800 },
    ],
    byPlatform: [
        { platform: 'News Media', positive: 4500, negative: 3200, neutral: 2100, volume: 9800 },
        { platform: 'Reddit (sampled)', positive: 2800, negative: 4100, neutral: 1200, volume: 8100 },
        { platform: 'Social (sampled)', positive: 1800, negative: 2500, neutral: 2389, volume: 6689 },
    ],
    byTimeWindow: [
        { window: 'Last 24 hours', positive: 1200, negative: 980, neutral: 567, volume: 2747 },
        { window: 'Last 7 days', positive: 4500, negative: 3800, neutral: 2100, volume: 10400 },
        { window: 'Last 30 days', positive: 9100, negative: 8000, neutral: 7489, volume: 24589 },
    ],
    distribution: {
        strongPositive: 2345,
        mildPositive: 4567,
        neutral: 5678,
        mildNegative: 3456,
        strongNegative: 2345,
    },
};

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
        { label: 'Strong unfavorable (R)', value: data.strongNegative, color: '#991b1b' },
        { label: 'Mild unfavorable (R)', value: data.mildNegative, color: '#dc2626' },
        { label: 'Neutral', value: data.neutral, color: '#9ca3af' },
        { label: 'Mild favorable (D)', value: data.mildPositive, color: '#3b82f6' },
        { label: 'Strong favorable (D)', value: data.strongPositive, color: '#1d4ed8' },
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
    const [data, setData] = useState<SentimentData | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        setLoading(true);
        const timer = setTimeout(() => {
            setData(MOCK_SENTIMENT_DATA);
            setLoading(false);
        }, 600);
        return () => clearTimeout(timer);
    }, [filters]);

    if (error) {
        return <ErrorState message={error} onRetry={() => setError(null)} />;
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

            {/* Breakdown Cards - Primary Row */}
            <div className="grid-2">
                <SentimentBreakdownCard
                    title="Sentiment by Topic"
                    data={data.byTopic}
                    labelKey="topic"
                    methodDescription="Topics are automatically clustered from article content using unsupervised learning."
                />
                <SentimentBreakdownCard
                    title="Sentiment by Platform"
                    data={data.byPlatform}
                    labelKey="platform"
                    methodDescription="Platform-specific sentiment. Reddit and social samples may not be representative of full platform discourse."
                />
            </div>

            {/* Distribution */}
            <SentimentDistributionCard data={data.distribution} />

            {/* Time Window Breakdown */}
            <Card title="Sentiment by Time Window">
                <div className="grid-3">
                    {data.byTimeWindow.map((item, i) => (
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
