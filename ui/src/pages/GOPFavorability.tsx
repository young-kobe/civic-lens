import { useState, useEffect } from 'react';
import { Card, ConfidenceBadge, MethodPopover, LoadingCard, ErrorState } from '../components/common';
import { TrendStrip, SentimentBar } from '../components/charts';
import type { Filters, FavorabilityData, FavorabilityOverall, PollingSocialComparison } from '../types';


import { fetchFavorability } from '../services/api';
import { transformFavorability } from '../services/transformers';

interface HeroMetricProps {
    data: FavorabilityOverall;
}

function HeroMetric({ data }: HeroMetricProps) {
    return (
        <div
            className="card"
            style={{
                background: 'linear-gradient(135deg, var(--neutral-900) 0%, var(--neutral-800) 100%)',
                color: 'white',
                textAlign: 'center',
                padding: 'var(--space-8)',
            }}
        >
            <div className="text-sm" style={{ opacity: 0.7, marginBottom: 'var(--space-2)' }}>
                Net Favorability
            </div>
            <div
                className="metric-value-lg"
                style={{
                    fontSize: 'var(--text-4xl)',
                    color: data.netFavorability >= 0 ? 'var(--semantic-positive)' : '#f87171',
                    marginBottom: 'var(--space-4)'
                }}
            >
                {data.netFavorability >= 0 ? '+' : ''}{data.netFavorability.toFixed(1)}%
            </div>

            <div className="flex justify-center gap-8 mt-4">
                <div>
                    <div className="text-2xl font-semibold">{data.favorable.toFixed(1)}%</div>
                    <div className="text-xs" style={{ opacity: 0.7 }}>Favorable</div>
                </div>
                <div style={{ width: '1px', background: 'rgba(255,255,255,0.2)' }} />
                <div>
                    <div className="text-2xl font-semibold">{data.unfavorable.toFixed(1)}%</div>
                    <div className="text-xs" style={{ opacity: 0.7 }}>Unfavorable</div>
                </div>
                <div style={{ width: '1px', background: 'rgba(255,255,255,0.2)' }} />
                <div>
                    <div className="text-2xl font-semibold">{data.neutral.toFixed(1)}%</div>
                    <div className="text-xs" style={{ opacity: 0.7 }}>Neutral</div>
                </div>
            </div>

            <div className="mt-6 pt-4" style={{ borderTop: '1px solid rgba(255,255,255,0.1)' }}>
                <ConfidenceBadge
                    coverage="high"
                    confidence="medium"
                    sampleSize={data.sampleSize}
                    sourceCount={data.sourceCount}
                />
            </div>
        </div>
    );
}



interface SourceComparisonProps {
    data: PollingSocialComparison;
}

function SourceComparison({ data }: SourceComparisonProps) {
    return (
        <Card
            title="Polling vs Online Sentiment"
            headerActions={
                <MethodPopover
                    description="Comparison between traditional polling data and online sentiment proxy derived from social media analysis."
                    limitations={[
                        'Online sentiment is not equivalent to scientific polling',
                        'Social media users may not be representative of general population',
                        'Polling methodologies vary across sources',
                    ]}
                />
            }
        >
            <div className="grid-2 gap-6">
                <div>
                    <div className="flex items-center gap-2 mb-3">
                        <span className="badge badge-neutral">Polling-based</span>
                    </div>
                    <SentimentBar
                        positive={data.polling.favorable}
                        negative={data.polling.unfavorable}
                        neutral={data.polling.neutral}
                        height={32}
                    />
                </div>
                <div>
                    <div className="flex items-center gap-2 mb-3">
                        <span className="badge badge-accent">Online sentiment proxy</span>
                    </div>
                    <SentimentBar
                        positive={data.social.favorable}
                        negative={data.social.unfavorable}
                        neutral={data.social.neutral}
                        height={32}
                    />
                </div>
            </div>
            <div className="card-note mt-4">
                Online sentiment is derived from sampled social media discourse and should not be interpreted as equivalent to scientific polling data.
            </div>
        </Card>
    );
}

interface GOPFavorabilityProps {
    filters: Filters;
}

function GOPFavorability({ filters }: GOPFavorabilityProps) {
    const [data, setData] = useState<FavorabilityData | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const loadData = async () => {
            setLoading(true);
            setError(null);
            try {
                const rawData = await fetchFavorability(filters.timeRange);
                const processedData = transformFavorability(rawData);
                setData(processedData);
            } catch (err: any) {
                console.error("Failed to load favorability:", err);
                setError(err.message || "Failed to load favorability data.");
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
            <div className="flex flex-col gap-6">
                <LoadingCard />
                <LoadingCard />
                <div className="grid-3">
                    <LoadingCard />
                    <LoadingCard />
                    <LoadingCard />
                </div>
            </div>
        );
    }

    if (!data) {
        return null;
    }

    const formatDate = (isoString: string): string => {
        return new Date(isoString).toLocaleDateString('en-US', {
            month: 'short',
            day: 'numeric',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
        });
    };

    return (
        <div className="flex flex-col gap-6">
            {/* Page Header */}
            <div className="flex justify-between items-start">
                <div>
                    <h2 className="text-xl font-semibold">GOP Favorability (US Public)</h2>
                    <p className="text-muted text-sm mt-1">{data.overall.dateRange}</p>
                </div>
                <div className="text-right text-xs text-muted">
                    Last updated: {formatDate(data.overall.lastUpdated)}
                </div>
            </div>

            {/* Hero Metric */}
            <HeroMetric data={data.overall} />

            {/* Trend Strip */}
            <Card
                title={`${filters.timeRange === 'all' ? 'All-Time' : filters.timeRange === '24h' ? '24-Hour' : filters.timeRange === '7d' ? '7-Day' : filters.timeRange === '30d' ? '30-Day' : '90-Day'} Trend`}
                headerActions={
                    <MethodPopover
                        description="Net favorability over the selected time period. Calculated from aggregated polling and weighted sentiment data."
                    />
                }
            >
                {data.trend.length > 0 ? (
                    <TrendStrip
                        data={data.trend}
                        dataKey="value"
                        xKey="date"
                        height={180}
                        annotations={data.trendAnnotations}
                        color="var(--accent)"
                    />
                ) : (
                    <div className="flex items-center justify-center h-40 text-muted text-sm">No trend data available</div>
                )}
            </Card>

            {/* Polling vs Social Comparison */}
            <SourceComparison data={data.pollingVsSocial} />

            {/* Platform Comparison */}
            <Card title="Favorability by Platform">
                <div className="flex flex-col gap-6">


                    <div className="flex flex-col gap-8">
                        {data.byPlatform && data.byPlatform.map((platform, i) => (
                            <div key={i}>
                                <div className="flex justify-between items-center mb-2">
                                    <span className={`badge ${platform.group === 'Social Media' || platform.group === 'Reddit' ? 'badge-accent' : 'badge-neutral'}`}>
                                        {platform.group === 'reddit_post' ? 'Reddit' : platform.group}
                                    </span>
                                    <span className="text-xs text-muted">
                                        Net: {platform.favorable - platform.unfavorable > 0 ? '+' : ''}{(platform.favorable - platform.unfavorable).toFixed(0)}%
                                    </span>
                                </div>
                                <SentimentBar
                                    positive={platform.favorable}
                                    negative={platform.unfavorable}
                                    neutral={platform.neutral}
                                    height={24}
                                    showLabels={true}
                                />
                            </div>
                        ))}
                    </div>
                    <div className="card-note">
                        Compare how favorability metrics differ between traditional news coverage and social media discussions.
                    </div>
                </div>
            </Card>

            {/* Data Footnote */}
            <Card className="bg-neutral-50" style={{ background: 'var(--neutral-50)' }}>
                <h4 className="font-medium mb-2">Data Sources and Methodology</h4>
                <p className="text-sm text-muted mb-3">
                    Favorability data is aggregated from {data.overall.sourceCount} polling sources and online sentiment analysis.
                    Polling data includes results from major national pollsters. Online sentiment is derived from sampled
                    Reddit discussions and news article comment sections.
                </p>
                <p className="text-sm text-muted">
                    <strong>Not included:</strong> Private social media posts, non-English content, content behind paywalls,
                    and platforms without public API access.
                </p>
            </Card>
        </div>
    );
}

export default GOPFavorability;
