import { useState, useEffect } from 'react';
import { Card, MetricCard, MethodPopover, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { Heatmap } from '../components/charts';
import { fetchBotActivity } from '../services/api';
import type { Filters, BotData, BotOverview, NarrativeAmplification, CoordinationStats, BehavioralSignals, ConfidenceLevel } from '../types';

interface BotOverviewMetricsProps {
    data: BotOverview;
}

function BotOverviewMetrics({ data }: BotOverviewMetricsProps) {
    return (
        <div className="grid-3 mb-6">
            <MetricCard
                label="Suspected Automation Rate"
                value={`${data.suspectedAutomationRate}%`}
                subtitle="of analyzed accounts"
                className={data.suspectedAutomationRate > 10 ? 'border-warning' : ''}
            />
            <MetricCard
                label="Coordination Index"
                value={data.coordinationIndex.toFixed(2)}
                subtitle="0 = none, 1 = highly coordinated"
            />
            <Card title="Top Amplified Clusters">
                <div className="flex flex-wrap gap-2">
                    {data.topClusters.map((cluster, i) => (
                        <span key={i} className="badge badge-warning">{cluster}</span>
                    ))}
                </div>
                <div className="text-xs text-muted mt-3">
                    {data.totalFlaggedAccounts.toLocaleString()} accounts flagged
                </div>
            </Card>
        </div>
    );
}

interface NarrativeAmplificationCardProps {
    narrative: NarrativeAmplification;
}

function NarrativeAmplificationCard({ narrative }: NarrativeAmplificationCardProps) {
    const [expanded, setExpanded] = useState(false);

    const getConfidenceBadge = (confidence: ConfidenceLevel) => {
        switch (confidence) {
            case 'high': return <span className="badge badge-negative">High likelihood</span>;
            case 'medium': return <span className="badge badge-warning">Medium likelihood</span>;
            case 'low': return <span className="badge badge-neutral">Low likelihood</span>;
            default: return null;
        }
    };

    return (
        <Card className="mb-4">
            <div className="flex items-start justify-between mb-3">
                <div>
                    <h4 className="font-semibold">{narrative.narrative}</h4>
                    <div className="flex items-center gap-2 mt-1">
                        {getConfidenceBadge(narrative.confidence)}
                        <span className="text-xs text-muted">
                            {narrative.suspectedBotVolume.toLocaleString()} suspected bot posts
                        </span>
                    </div>
                </div>
                <button
                    className="btn btn-ghost btn-sm"
                    onClick={() => setExpanded(!expanded)}
                >
                    {expanded ? 'Collapse' : 'Expand'}
                </button>
            </div>

            {/* Always visible: Why flagged */}
            <div className="mb-4">
                <div className="text-xs font-medium text-muted mb-2">Why flagged (suspected coordination indicators)</div>
                <ul style={{ margin: 0, paddingLeft: 'var(--space-5)' }} className="text-sm">
                    {narrative.whyFlagged.map((reason, i) => (
                        <li key={i} className="mb-1">{reason}</li>
                    ))}
                </ul>
            </div>

            {expanded && (
                <>
                    {/* Example Posts */}
                    <div className="mb-4">
                        <div className="text-xs font-medium text-muted mb-2">Example posts</div>
                        <div className="flex flex-col gap-2">
                            {narrative.examplePosts.map((post, i) => (
                                <div
                                    key={i}
                                    className="text-sm"
                                    style={{
                                        padding: 'var(--space-3)',
                                        background: 'var(--neutral-50)',
                                        borderRadius: 'var(--radius-md)',
                                        fontStyle: 'italic',
                                    }}
                                >
                                    "{post}"
                                </div>
                            ))}
                        </div>
                    </div>

                    {/* Hashtags and Phrases */}
                    <div className="grid-2 gap-4 mb-4">
                        <div>
                            <div className="text-xs font-medium text-muted mb-2">Top hashtags</div>
                            <div className="flex flex-wrap gap-1">
                                {narrative.topHashtags.map((tag, i) => (
                                    <span key={i} className="badge badge-accent">{tag}</span>
                                ))}
                            </div>
                        </div>
                        <div>
                            <div className="text-xs font-medium text-muted mb-2">Key phrases</div>
                            <div className="flex flex-wrap gap-1">
                                {narrative.topPhrases.map((phrase, i) => (
                                    <span key={i} className="badge badge-neutral">{phrase}</span>
                                ))}
                            </div>
                        </div>
                    </div>

                    {/* Targets */}
                    <div>
                        <div className="text-xs font-medium text-muted mb-2">Primary targets</div>
                        <div className="flex flex-wrap gap-1">
                            {narrative.targets.map((target, i) => (
                                <span key={i} className="badge badge-negative">{target}</span>
                            ))}
                        </div>
                    </div>
                </>
            )}
        </Card>
    );
}

interface CoordinationSummaryProps {
    data: CoordinationStats;
}

function CoordinationSummary({ data }: CoordinationSummaryProps) {
    return (
        <Card
            title="Coordination Indicators"
            headerActions={
                <MethodPopover
                    description="Coordination is detected through behavioral analysis including timing patterns, text similarity, and network analysis."
                    limitations={[
                        'Some legitimate coordinated campaigns may be flagged',
                        'Sophisticated actors may evade detection',
                        'Metrics are indicative, not definitive proof',
                    ]}
                />
            }
        >
            <div className="grid-2 gap-4">
                <div className="flex flex-col gap-3">
                    <div className="flex justify-between items-center">
                        <span className="text-sm">Burst timing similarity</span>
                        <span className="font-mono font-medium">{(data.burstTimingSimilarity * 100).toFixed(0)}%</span>
                    </div>
                    <div className="flex justify-between items-center">
                        <span className="text-sm">Accounts showing reuse patterns</span>
                        <span className="font-mono font-medium">{data.accountReuse.toLocaleString()}</span>
                    </div>
                </div>
                <div className="flex flex-col gap-3">
                    <div className="flex justify-between items-center">
                        <span className="text-sm">Identical text pairs detected</span>
                        <span className="font-mono font-medium">{data.identicalTextPairs.toLocaleString()}</span>
                    </div>
                    <div className="flex justify-between items-center">
                        <span className="text-sm">Avg posts per suspected account</span>
                        <span className="font-mono font-medium">{data.avgPostsPerSuspectedAccount.toFixed(1)}</span>
                    </div>
                </div>
            </div>
            <div className="card-note mt-4">
                These metrics indicate potential coordination but are not definitive proof of automated or malicious activity.
            </div>
        </Card>
    );
}

interface BehavioralSignalsPanelProps {
    data: BehavioralSignals;
}

function BehavioralSignalsPanel({ data }: BehavioralSignalsPanelProps) {
    return (
        <div className="grid-2 gap-6">
            {/* Account Age Distribution */}
            <Card title="Account Age Distribution">
                <div className="flex flex-col gap-2">
                    {data.accountAgeDistribution.map((item, i) => (
                        <div key={i}>
                            <div className="flex justify-between text-sm mb-1">
                                <span>{item.range}</span>
                                <span className="text-muted">{item.percentage}%</span>
                            </div>
                            <div
                                style={{
                                    height: '8px',
                                    background: 'var(--neutral-100)',
                                    borderRadius: 'var(--radius-sm)',
                                    overflow: 'hidden'
                                }}
                            >
                                <div
                                    style={{
                                        width: `${item.percentage}%`,
                                        height: '100%',
                                        background: item.range.includes('<') ? 'var(--semantic-warning)' : 'var(--accent)',
                                    }}
                                />
                            </div>
                        </div>
                    ))}
                </div>
                <div className="card-note mt-4">
                    Young accounts (&lt;30 days) are over-represented in suspected bot activity.
                </div>
            </Card>

            {/* Posting Cadence Heatmap */}
            <Card title="Posting Cadence Heatmap">
                <Heatmap data={data.postingCadence} cellSize={12} gap={1} />
                <div className="card-note mt-4">
                    Unusual posting patterns detected: high activity during off-hours (2-5 AM).
                </div>
            </Card>

            {/* Copy/Paste Similarity */}
            <Card title="Text Similarity Distribution">
                <div className="flex flex-col gap-3">
                    <div>
                        <div className="flex justify-between text-sm mb-1">
                            <span>High similarity (&gt;80%)</span>
                            <span className="font-medium" style={{ color: 'var(--semantic-negative)' }}>
                                {data.copyPasteSimilarity.high}%
                            </span>
                        </div>
                        <div
                            style={{
                                height: '12px',
                                background: 'var(--neutral-100)',
                                borderRadius: 'var(--radius-sm)',
                                overflow: 'hidden'
                            }}
                        >
                            <div
                                style={{
                                    width: `${data.copyPasteSimilarity.high}%`,
                                    height: '100%',
                                    background: 'var(--semantic-negative)',
                                }}
                            />
                        </div>
                    </div>
                    <div>
                        <div className="flex justify-between text-sm mb-1">
                            <span>Medium similarity (50-80%)</span>
                            <span className="font-medium" style={{ color: 'var(--semantic-warning)' }}>
                                {data.copyPasteSimilarity.medium}%
                            </span>
                        </div>
                        <div
                            style={{
                                height: '12px',
                                background: 'var(--neutral-100)',
                                borderRadius: 'var(--radius-sm)',
                                overflow: 'hidden'
                            }}
                        >
                            <div
                                style={{
                                    width: `${data.copyPasteSimilarity.medium}%`,
                                    height: '100%',
                                    background: 'var(--semantic-warning)',
                                }}
                            />
                        </div>
                    </div>
                    <div>
                        <div className="flex justify-between text-sm mb-1">
                            <span>Low similarity (&lt;50%)</span>
                            <span className="font-medium">{data.copyPasteSimilarity.low}%</span>
                        </div>
                        <div
                            style={{
                                height: '12px',
                                background: 'var(--neutral-100)',
                                borderRadius: 'var(--radius-sm)',
                                overflow: 'hidden'
                            }}
                        >
                            <div
                                style={{
                                    width: `${data.copyPasteSimilarity.low}%`,
                                    height: '100%',
                                    background: 'var(--accent)',
                                }}
                            />
                        </div>
                    </div>
                </div>
            </Card>

            {/* Link Domain Concentration */}
            <Card title="Link Domain Concentration">
                <div className="flex flex-col gap-2">
                    {data.linkDomainConcentration.map((item, i) => (
                        <div key={i} className="flex justify-between items-center">
                            <span className="text-sm font-mono">{item.domain}</span>
                            <span className="text-sm text-muted">{item.percentage}%</span>
                        </div>
                    ))}
                </div>
                <div className="card-note mt-4">
                    High concentration of links to a small number of domains may indicate coordinated promotion.
                </div>
            </Card>
        </div>
    );
}

interface BotActivityProfilerProps {
    filters: Filters;
}

function BotActivityProfiler({ filters }: BotActivityProfilerProps) {
    const [data, setData] = useState<BotData | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        setLoading(true);
        setError(null);

        fetchBotActivity()
            .then((result) => {
                if (!cancelled) {
                    setData(result);
                    setLoading(false);
                }
            })
            .catch((err) => {
                if (!cancelled) {
                    setError(err.message || 'Failed to fetch bot activity data');
                    setLoading(false);
                }
            });

        return () => {
            cancelled = true;
        };
    }, [filters]);

    if (error) {
        return <ErrorState message={error} onRetry={() => setError(null)} />;
    }

    if (loading) {
        return (
            <div className="flex flex-col gap-6">
                <div className="grid-3">
                    <LoadingCard />
                    <LoadingCard />
                    <LoadingCard />
                </div>
                <LoadingCard />
                <LoadingCard />
            </div>
        );
    }

    if (!data || data.overview.totalFlaggedAccounts === 0) {
        return (
            <EmptyState
                title="No Bot Activity Data"
                description="No bot detection analysis has been run yet. Run the analysis pipeline to generate data."
            />
        );
    }

    return (
        <div className="flex flex-col gap-6">
            {/* Important Disclaimer */}
            <div
                className="card"
                style={{
                    background: 'var(--semantic-warning-light)',
                    border: '1px solid rgba(217, 119, 6, 0.2)'
                }}
            >
                <div className="flex gap-3">
                    <svg
                        viewBox="0 0 20 20"
                        fill="currentColor"
                        style={{ width: '20px', height: '20px', color: 'var(--semantic-warning)', flexShrink: 0 }}
                    >
                        <path fillRule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495zM10 5a.75.75 0 01.75.75v3.5a.75.75 0 01-1.5 0v-3.5A.75.75 0 0110 5zm0 9a1 1 0 100-2 1 1 0 000 2z" clipRule="evenodd" />
                    </svg>
                    <div>
                        <h4 className="font-medium" style={{ color: 'var(--semantic-warning)' }}>
                            Calibrated Language Notice
                        </h4>
                        <p className="text-sm mt-1">
                            All activity described on this page is <strong>suspected</strong> or <strong>likely</strong> coordinated behavior.
                            These indicators suggest automated or coordinated patterns but do not constitute definitive proof.
                            Human verification is recommended before drawing conclusions.
                        </p>
                    </div>
                </div>
            </div>

            {/* Overview Metrics */}
            <BotOverviewMetrics data={data.overview} />

            {/* Narrative Amplification */}
            <div>
                <h3 className="text-lg font-semibold mb-4">Narratives with Suspected Bot Amplification</h3>
                {data.narrativeAmplification.map((narrative) => (
                    <NarrativeAmplificationCard key={narrative.id} narrative={narrative} />
                ))}
            </div>

            {/* Coordination Summary */}
            <CoordinationSummary data={data.coordinationStats} />

            {/* Behavioral Signals */}
            <div>
                <h3 className="text-lg font-semibold mb-4">Behavioral Signals</h3>
                <BehavioralSignalsPanel data={data.behavioralSignals} />
            </div>
        </div>
    );
}

export default BotActivityProfiler;
