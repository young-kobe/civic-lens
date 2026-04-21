import { useState } from 'react';
import { Card, MetricCard, MethodPopover, Modal, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { Heatmap } from '../components/charts';
import { fetchBotActivity } from '../services/api';
import { useFetch } from '../services/useFetch';
import type { Filters, BotData, BotOverview, NarrativeAmplification, CoordinationStats, BehavioralSignals, ConfidenceLevel } from '../types';

interface BotOverviewMetricsProps {
    data: BotOverview;
}

function BotOverviewMetrics({ data }: BotOverviewMetricsProps) {
    return (
        <div className="grid-3 mb-4">
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
                <div className="flex flex-wrap gap-1">
                    {data.topClusters.map((cluster, i) => (
                        <span key={i} className="badge badge-warning">{cluster}</span>
                    ))}
                </div>
                <div className="eyebrow mt-3 num">
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
    const [modalOpen, setModalOpen] = useState(false);

    const getConfidenceBadge = (confidence: ConfidenceLevel) => {
        switch (confidence) {
            case 'high': return <span className="badge badge-negative">High likelihood</span>;
            case 'medium': return <span className="badge badge-warning">Medium likelihood</span>;
            case 'low': return <span className="badge badge-neutral">Low likelihood</span>;
            default: return null;
        }
    };

    const accentColor = narrative.confidence === 'high' ? 'var(--semantic-negative)'
        : narrative.confidence === 'medium' ? 'var(--semantic-warning)'
        : 'var(--neutral-500)';

    return (
        <>
            <Card>
                <div className="flex items-start justify-between mb-3" style={{ gap: 'var(--space-3)', flexWrap: 'wrap' }}>
                    <div style={{ minWidth: 0 }}>
                        <h4 className="font-semibold uppercase" style={{ letterSpacing: '0.02em' }}>{narrative.narrative}</h4>
                        <div className="flex items-center gap-2 mt-1" style={{ flexWrap: 'wrap' }}>
                            {getConfidenceBadge(narrative.confidence)}
                            <span className="eyebrow num">
                                {narrative.suspectedBotVolume.toLocaleString()} suspected bot posts
                            </span>
                        </div>
                    </div>
                    <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={() => setModalOpen(true)}
                        aria-haspopup="dialog"
                    >
                        View details
                    </button>
                </div>

                {/* Always visible: Why flagged */}
                <div>
                    <div className="eyebrow mb-2">Why Flagged · Coordination Indicators</div>
                    <ul style={{ margin: 0, paddingLeft: 'var(--space-5)' }} className="text-sm">
                        {narrative.whyFlagged.map((reason, i) => (
                            <li key={i} className="mb-1">{reason}</li>
                        ))}
                    </ul>
                </div>
            </Card>

            <Modal
                isOpen={modalOpen}
                onClose={() => setModalOpen(false)}
                title={narrative.narrative}
                subtitle={`${narrative.suspectedBotVolume.toLocaleString()} suspected bot posts · ${narrative.confidence} likelihood`}
                accentColor={accentColor}
            >
                {/* Example Posts */}
                <div className="mb-4">
                    <div className="eyebrow mb-2">Example Posts</div>
                    <div className="flex flex-col gap-2">
                        {narrative.examplePosts.map((post, i) => (
                            <div
                                key={i}
                                className="text-sm"
                                style={{
                                    padding: 'var(--space-2) var(--space-3)',
                                    background: 'var(--bg-inset)',
                                    borderLeft: '2px solid var(--neutral-300)',
                                    borderRadius: '2px',
                                    fontStyle: 'italic',
                                }}
                            >
                                "{post}"
                            </div>
                        ))}
                    </div>
                </div>

                {/* Hashtags and Phrases */}
                <div className="grid-2 gap-3 mb-4">
                    <div>
                        <div className="eyebrow mb-2">Top Hashtags</div>
                        <div className="flex flex-wrap gap-1">
                            {narrative.topHashtags.map((tag, i) => (
                                <span key={i} className="badge badge-accent">{tag}</span>
                            ))}
                        </div>
                    </div>
                    <div>
                        <div className="eyebrow mb-2">Key Phrases</div>
                        <div className="flex flex-wrap gap-1">
                            {narrative.topPhrases.map((phrase, i) => (
                                <span key={i} className="badge badge-neutral">{phrase}</span>
                            ))}
                        </div>
                    </div>
                </div>

                {/* Targets */}
                <div className="mb-4">
                    <div className="eyebrow mb-2">Primary Targets</div>
                    <div className="flex flex-wrap gap-1">
                        {narrative.targets.map((target, i) => (
                            <span key={i} className="badge badge-negative">{target}</span>
                        ))}
                    </div>
                </div>

                {/* Why Flagged (repeated in modal for full context) */}
                <div>
                    <div className="eyebrow mb-2">Why Flagged · Coordination Indicators</div>
                    <ul style={{ margin: 0, paddingLeft: 'var(--space-5)' }} className="text-sm">
                        {narrative.whyFlagged.map((reason, i) => (
                            <li key={i} className="mb-1">{reason}</li>
                        ))}
                    </ul>
                </div>
            </Modal>
        </>
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
            <div className="grid-2 gap-3">
                <div className="flex flex-col gap-2">
                    <div className="flex justify-between items-center" style={{ padding: '6px 0', borderBottom: '1px solid var(--neutral-150)' }}>
                        <span className="text-sm">Burst timing similarity</span>
                        <span className="num font-semibold">{(data.burstTimingSimilarity * 100).toFixed(0)}%</span>
                    </div>
                    <div className="flex justify-between items-center" style={{ padding: '6px 0' }}>
                        <span className="text-sm">Accounts showing reuse patterns</span>
                        <span className="num font-semibold">{data.accountReuse.toLocaleString()}</span>
                    </div>
                </div>
                <div className="flex flex-col gap-2">
                    <div className="flex justify-between items-center" style={{ padding: '6px 0', borderBottom: '1px solid var(--neutral-150)' }}>
                        <span className="text-sm">Identical text pairs detected</span>
                        <span className="num font-semibold">{data.identicalTextPairs.toLocaleString()}</span>
                    </div>
                    <div className="flex justify-between items-center" style={{ padding: '6px 0' }}>
                        <span className="text-sm">Avg posts per suspected account</span>
                        <span className="num font-semibold">{data.avgPostsPerSuspectedAccount.toFixed(1)}</span>
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

/**
 * Renders four behavioural-signal cards as direct children of the outer
 * dashboard grid. Account age + text similarity pair as 6-col siblings on
 * wide screens; the heatmap takes a full row because its 24-col matrix
 * needs horizontal room; link-domain concentration takes the remainder.
 */
function BehavioralSignalsPanel({ data }: BehavioralSignalsPanelProps) {
    return (
        <>
            {/* Row: account age (6) + text similarity (6) — both compact bar lists */}
            <div className="col-span-6">
                <Card title="Account Age Distribution">
                    <div className="flex flex-col gap-2">
                        {data.accountAgeDistribution.map((item, i) => (
                            <div key={i}>
                                <div className="flex justify-between text-sm mb-1">
                                    <span>{item.range}</span>
                                    <span className="num text-muted">{item.percentage}%</span>
                                </div>
                                <div
                                    style={{
                                        height: '6px',
                                        background: 'var(--neutral-100)',
                                        borderRadius: '1px',
                                        overflow: 'hidden',
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
            </div>

            <div className="col-span-6">
                <Card title="Text Similarity Distribution">
                    <div className="text-xs text-muted mb-3">
                        Pairwise text similarity across suspected-bot posts. Natural discourse typically sits in the
                        20–30% range. Values above 80% indicate near-duplicate content, a strong indicator of
                        copy-paste amplification.
                    </div>
                    <div className="flex flex-col gap-3">
                        <SimilarityBar
                            label="High similarity (>80%)"
                            value={data.copyPasteSimilarity.high}
                            color="var(--semantic-negative)"
                        />
                        <SimilarityBar
                            label="Medium similarity (50–80%)"
                            value={data.copyPasteSimilarity.medium}
                            color="var(--semantic-warning)"
                        />
                        <SimilarityBar
                            label="Low similarity (<50%)"
                            value={data.copyPasteSimilarity.low}
                            color="var(--accent)"
                        />
                    </div>
                </Card>
            </div>

            {/* Row: posting cadence heatmap (8) + link domain concentration (4).
                The 24-col matrix wants width; link domains is a short list that
                fits the narrow slot nicely. */}
            <div className="col-span-8">
                <Card title="Posting Cadence Heatmap">
                    <div className="text-xs text-muted mb-2">
                        Rows are days of the week, columns are hours (0–23, UTC). Darker cells indicate heavier
                        posting volume among suspected-bot accounts. Clusters in off-hours (02:00–05:00 UTC) are a
                        red flag for automation, not proof of it.
                    </div>
                    <Heatmap data={data.postingCadence} cellSize={12} gap={1} />
                    <div className="card-note mt-4">
                        Concentrated activity during off-hours (02:00–05:00 UTC) was detected in this window.
                    </div>
                </Card>
            </div>

            <div className="col-span-4">
                <Card title="Link Domain Concentration">
                    <div className="flex flex-col">
                        {data.linkDomainConcentration.map((item, i) => (
                            <div
                                key={i}
                                className="flex justify-between items-center"
                                style={{ padding: '6px 0', borderBottom: '1px solid var(--neutral-150)' }}
                            >
                                <span className="text-sm" style={{ fontFamily: 'var(--font-mono)' }}>
                                    {item.domain}
                                </span>
                                <span className="num text-sm text-muted">{item.percentage}%</span>
                            </div>
                        ))}
                    </div>
                    <div className="card-note mt-4">
                        High concentration of links to a small number of domains may indicate coordinated promotion.
                    </div>
                </Card>
            </div>
        </>
    );
}

interface SimilarityBarProps {
    label: string;
    value: number;
    color: string;
}

function SimilarityBar({ label, value, color }: SimilarityBarProps) {
    return (
        <div>
            <div className="flex justify-between text-sm mb-1">
                <span>{label}</span>
                <span className="font-medium" style={{ color }}>{value}%</span>
            </div>
            <div
                style={{
                    height: '8px',
                    background: 'var(--neutral-100)',
                    borderRadius: '1px',
                    overflow: 'hidden',
                }}
            >
                <div style={{ width: `${value}%`, height: '100%', background: color }} />
            </div>
        </div>
    );
}

interface BotActivityProfilerProps {
    filters: Filters;
}

function BotActivityProfiler({ filters: _filters }: BotActivityProfilerProps) {
    // Bot activity is not time-window filtered at the API layer (it's a
    // snapshot cache rebuilt on each pipeline run), so `filters` doesn't
    // influence the key. If that changes, fold the relevant filter fields
    // into the cache key.
    const { data, loading, error, refetch } = useFetch<BotData>(
        () => fetchBotActivity(),
        [],
        'bot-activity',
    );

    if (error) {
        return <ErrorState message={error.message} onRetry={refetch} />;
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
        <div className="dashboard-grid">
            {/* Row: disclaimer (full) */}
            <div
                className="col-span-12"
                style={{
                    background: 'var(--semantic-warning-light)',
                    borderLeft: '3px solid var(--semantic-warning)',
                    padding: 'var(--space-3) var(--space-4)',
                    borderRadius: '2px',
                }}
            >
                <div className="flex gap-3">
                    <svg
                        viewBox="0 0 20 20"
                        fill="currentColor"
                        style={{ width: '18px', height: '18px', color: 'var(--semantic-warning)', flexShrink: 0, marginTop: '2px' }}
                    >
                        <path fillRule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495zM10 5a.75.75 0 01.75.75v3.5a.75.75 0 01-1.5 0v-3.5A.75.75 0 0110 5zm0 9a1 1 0 100-2 1 1 0 000 2z" clipRule="evenodd" />
                    </svg>
                    <div>
                        <div className="eyebrow" style={{ color: 'var(--semantic-warning)' }}>
                            How to read this page
                        </div>
                        <p className="text-sm mt-1">
                            This page flags accounts and posts <strong>in our political-content sample</strong> that
                            look automated. The detector scores each account from behavioral signals: posting rate,
                            text repetition, account age, and coordinated timing. Some real users post in bot-like
                            ways, and some real bots post like humans. Treat flags as <strong>leads, not
                            verdicts</strong>.
                        </p>
                    </div>
                </div>
            </div>

            {/* Row: overview metrics — BotOverviewMetrics already renders 3
                items; wrap full-width and let its internal grid-3 handle the
                column layout so we don't double-nest grids. */}
            <div className="col-span-12">
                <BotOverviewMetrics data={data.overview} />
            </div>

            {/* Row: coordination summary (5) + narrative amplification section label (7).
                Coord is a compact 4-stat card; the amplification section headline pairs
                with it so the narrative-detail cards below get a clean section start. */}
            <div className="col-span-5">
                <CoordinationSummary data={data.coordinationStats} />
            </div>
            <div
                className="col-span-7"
                style={{
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'center',
                    padding: 'var(--space-3) var(--space-4)',
                    background: 'var(--bg-panel)',
                    border: '1px solid var(--neutral-200)',
                    borderRadius: 'var(--radius-md)',
                }}
            >
                <div className="eyebrow" style={{ marginBottom: 'var(--space-1)' }}>
                    Narratives with Suspected Bot Amplification
                </div>
                <div className="text-xs text-muted" style={{ lineHeight: 'var(--leading-relaxed)' }}>
                    Each row below is a narrative whose amplifying accounts score as likely bots. Expand to see
                    example posts, flagged hashtags, and the signals that triggered the flag.
                </div>
            </div>

            {/* Narrative amplification cards — each full-width so their
                expanded state has room for the example post grid. */}
            {data.narrativeAmplification.map((narrative) => (
                <div key={narrative.id} className="col-span-12">
                    <NarrativeAmplificationCard narrative={narrative} />
                </div>
            ))}

            {/* Behavioral signals — four cards rendered as direct grid children.
                See BehavioralSignalsPanel for per-card span decisions. */}
            <BehavioralSignalsPanel data={data.behavioralSignals} />
        </div>
    );
}

export default BotActivityProfiler;
