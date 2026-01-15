import { useState, useEffect } from 'react';
import { Card, MetricCard, ConfidenceBadge, MethodPopover, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { Sparkline, StackedBar } from '../components/charts';
import type { Filters, Cluster, SourceMixItem, TimelinePoint, Article } from '../types';

// Mock data for demonstration
const MOCK_CLUSTERS: Cluster[] = [
    {
        id: 1,
        title: 'Federal Reserve Interest Rate Decision',
        articleCount: 847,
        momentum: { delta24h: 12.5, delta7d: 45.2 },
        primarySources: ['reuters.com', 'wsj.com', 'bloomberg.com'],
        summary: [
            'Federal Reserve signals potential rate cuts in 2024',
            'Markets react positively to dovish commentary',
            'Inflation data showing signs of cooling',
        ],
        keyClaims: ['Rate cuts likely in Q2 2024', 'Inflation target within reach'],
        entities: {
            people: ['Jerome Powell', 'Janet Yellen'],
            organizations: ['Federal Reserve', 'Treasury Department'],
            locations: ['Washington D.C.'],
        },
        sourceMix: [
            { name: 'News', value: 523, type: 'news' },
            { name: 'Reddit', value: 234, type: 'reddit' },
            { name: 'Social', value: 90, type: 'social' },
        ],
        timeline: [
            { date: 'Mon', value: 45 },
            { date: 'Tue', value: 78 },
            { date: 'Wed', value: 156 },
            { date: 'Thu', value: 234 },
            { date: 'Fri', value: 189 },
            { date: 'Sat', value: 98 },
            { date: 'Sun', value: 47 },
        ],
        articles: [
            { id: 1, title: 'Fed Signals Rate Cuts Ahead', source: 'Reuters', snippet: 'The Federal Reserve indicated it may begin cutting interest rates...', reason: 'High engagement, primary source' },
            { id: 2, title: 'Markets Rally on Fed Commentary', source: 'WSJ', snippet: 'Stock markets surged following dovish comments from Fed officials...', reason: 'Major outlet coverage' },
            { id: 3, title: 'What the Fed Decision Means for You', source: 'Bloomberg', snippet: 'Consumer borrowing costs could decline if the Fed follows through...', reason: 'Consumer impact angle' },
        ],
    },
    {
        id: 2,
        title: 'Tech Layoffs and Industry Restructuring',
        articleCount: 634,
        momentum: { delta24h: -5.2, delta7d: 23.1 },
        primarySources: ['techcrunch.com', 'theverge.com', 'wired.com'],
        summary: [
            'Major tech companies announce workforce reductions',
            'AI investment cited as reason for restructuring',
            'Remote work policies under review',
        ],
        keyClaims: ['Over 50,000 tech jobs cut in Q1', 'AI roles expanding'],
        entities: {
            people: ['Sundar Pichai', 'Satya Nadella'],
            organizations: ['Google', 'Microsoft', 'Meta'],
            locations: ['Silicon Valley', 'Seattle'],
        },
        sourceMix: [
            { name: 'News', value: 312, type: 'news' },
            { name: 'Reddit', value: 256, type: 'reddit' },
            { name: 'Social', value: 66, type: 'social' },
        ],
        timeline: [
            { date: 'Mon', value: 89 },
            { date: 'Tue', value: 112 },
            { date: 'Wed', value: 98 },
            { date: 'Thu', value: 145 },
            { date: 'Fri', value: 123 },
            { date: 'Sat', value: 45 },
            { date: 'Sun', value: 22 },
        ],
        articles: [
            { id: 1, title: 'Google Announces 12,000 Layoffs', source: 'TechCrunch', snippet: 'Alphabet Inc announced it would cut approximately 12,000 jobs...', reason: 'Breaking news, high impact' },
        ],
    },
    {
        id: 3,
        title: 'Climate Policy and COP Agreements',
        articleCount: 423,
        momentum: { delta24h: 8.3, delta7d: -12.4 },
        primarySources: ['theguardian.com', 'nytimes.com', 'bbc.com'],
        summary: [
            'New climate commitments announced at international summit',
            'Developing nations seek additional funding',
            'Fossil fuel industry response mixed',
        ],
        keyClaims: ['2030 emissions targets reaffirmed'],
        entities: {
            people: ['John Kerry'],
            organizations: ['United Nations', 'EPA'],
            locations: ['Dubai', 'Brussels'],
        },
        sourceMix: [
            { name: 'News', value: 298, type: 'news' },
            { name: 'Reddit', value: 89, type: 'reddit' },
            { name: 'Social', value: 36, type: 'social' },
        ],
        timeline: [
            { date: 'Mon', value: 67 },
            { date: 'Tue', value: 54 },
            { date: 'Wed', value: 78 },
            { date: 'Thu', value: 89 },
            { date: 'Fri', value: 72 },
            { date: 'Sat', value: 38 },
            { date: 'Sun', value: 25 },
        ],
        articles: [],
    },
];

interface ClusterListItemProps {
    cluster: Cluster;
    isSelected: boolean;
    onClick: () => void;
}

function ClusterListItem({ cluster, isSelected, onClick }: ClusterListItemProps) {
    return (
        <button
            className={`card ${isSelected ? 'ring' : ''}`}
            onClick={onClick}
            style={{
                width: '100%',
                textAlign: 'left',
                marginBottom: 'var(--space-2)',
                cursor: 'pointer',
                border: isSelected ? '2px solid var(--accent)' : '1px solid var(--neutral-200)',
            }}
        >
            <h4 className="font-semibold truncate" style={{ marginBottom: 'var(--space-1)' }}>
                {cluster.title}
            </h4>
            <div className="flex items-center gap-3 text-sm text-muted">
                <span>{cluster.articleCount} articles</span>
                <span className={cluster.momentum.delta24h >= 0 ? 'metric-delta-positive' : 'metric-delta-negative'}>
                    {cluster.momentum.delta24h >= 0 ? '+' : ''}{cluster.momentum.delta24h}% 24h
                </span>
            </div>
            <div className="text-xs text-muted mt-2 truncate">
                {cluster.primarySources.slice(0, 2).join(', ')}
            </div>
        </button>
    );
}

interface ClusterDetailProps {
    cluster: Cluster | null;
}

function ClusterDetail({ cluster }: ClusterDetailProps) {
    if (!cluster) {
        return (
            <EmptyState
                title="Select a cluster"
                description="Choose a story cluster from the list to view details"
            />
        );
    }

    return (
        <div className="flex flex-col gap-6">
            {/* Header */}
            <div>
                <h2 className="text-xl font-semibold mb-2">{cluster.title}</h2>
                <div className="flex items-center gap-4">
                    <span className="text-muted">{cluster.articleCount} articles/posts</span>
                    <ConfidenceBadge coverage="high" confidence="medium" sampleSize={cluster.articleCount} />
                </div>
            </div>

            {/* Narrative Summary */}
            <Card
                title="Narrative Summary"
                headerActions={
                    <MethodPopover
                        description="Summary generated using extractive summarization from top articles by engagement."
                        limitations={['May not capture minority viewpoints', 'Recency bias in article selection']}
                    />
                }
            >
                <ul style={{ margin: 0, paddingLeft: 'var(--space-5)' }}>
                    {cluster.summary.map((point, i) => (
                        <li key={i} className="mb-2">{point}</li>
                    ))}
                </ul>
                {cluster.keyClaims.length > 0 && (
                    <div className="mt-4 pt-4" style={{ borderTop: '1px solid var(--neutral-100)' }}>
                        <div className="text-xs font-medium text-muted mb-2">Key Claims</div>
                        <div className="flex flex-wrap gap-2">
                            {cluster.keyClaims.map((claim, i) => (
                                <span key={i} className="badge badge-neutral">{claim}</span>
                            ))}
                        </div>
                    </div>
                )}
            </Card>

            {/* Key Entities */}
            <Card title="Key Entities">
                <div className="grid-3">
                    <div>
                        <div className="text-xs font-medium text-muted mb-2">People</div>
                        <div className="flex flex-wrap gap-1">
                            {cluster.entities.people.map((p, i) => (
                                <span key={i} className="badge badge-accent">{p}</span>
                            ))}
                        </div>
                    </div>
                    <div>
                        <div className="text-xs font-medium text-muted mb-2">Organizations</div>
                        <div className="flex flex-wrap gap-1">
                            {cluster.entities.organizations.map((o, i) => (
                                <span key={i} className="badge badge-neutral">{o}</span>
                            ))}
                        </div>
                    </div>
                    <div>
                        <div className="text-xs font-medium text-muted mb-2">Locations</div>
                        <div className="flex flex-wrap gap-1">
                            {cluster.entities.locations.map((l, i) => (
                                <span key={i} className="badge badge-neutral">{l}</span>
                            ))}
                        </div>
                    </div>
                </div>
            </Card>

            {/* Source Mix & Timeline */}
            <div className="grid-2">
                <Card title="Source Mix">
                    <StackedBar data={cluster.sourceMix} height={160} layout="vertical" />
                </Card>
                <Card title="Volume Over Time">
                    <Sparkline data={cluster.timeline} dataKey="value" height={120} />
                </Card>
            </div>

            {/* Representative Articles */}
            <Card title="Representative Articles">
                {cluster.articles.length === 0 ? (
                    <p className="text-muted">No representative articles available</p>
                ) : (
                    <div className="flex flex-col gap-3">
                        {cluster.articles.map((article) => (
                            <div
                                key={article.id}
                                style={{
                                    padding: 'var(--space-3)',
                                    background: 'var(--neutral-50)',
                                    borderRadius: 'var(--radius-md)'
                                }}
                            >
                                <div className="flex items-start justify-between gap-3">
                                    <div>
                                        <h4 className="font-medium mb-1">{article.title}</h4>
                                        <p className="text-sm text-muted mb-2">{article.snippet}</p>
                                        <div className="flex items-center gap-2">
                                            <span className="text-xs badge badge-neutral">{article.source}</span>
                                            <span className="text-xs text-muted">{article.reason}</span>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </Card>
        </div>
    );
}

interface StoryClustersProps {
    filters: Filters;
}

function StoryClusters({ filters }: StoryClustersProps) {
    const [clusters, setClusters] = useState<Cluster[]>([]);
    const [selectedCluster, setSelectedCluster] = useState<Cluster | null>(null);
    const [searchQuery, setSearchQuery] = useState('');
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        // Simulate API fetch
        setLoading(true);
        const timer = setTimeout(() => {
            setClusters(MOCK_CLUSTERS);
            setSelectedCluster(MOCK_CLUSTERS[0]);
            setLoading(false);
        }, 800);
        return () => clearTimeout(timer);
    }, [filters]);

    const filteredClusters = clusters.filter(c =>
        c.title.toLowerCase().includes(searchQuery.toLowerCase())
    );

    if (error) {
        return <ErrorState message={error} onRetry={() => setError(null)} />;
    }

    return (
        <div className="flex gap-6" style={{ minHeight: '600px' }}>
            {/* Left Panel - Cluster List */}
            <div style={{ width: 'var(--sidebar-width)', flexShrink: 0 }}>
                <div className="mb-4">
                    <input
                        type="text"
                        className="input"
                        placeholder="Search clusters..."
                        value={searchQuery}
                        onChange={(e) => setSearchQuery(e.target.value)}
                    />
                </div>

                <div style={{ maxHeight: 'calc(100vh - 300px)', overflowY: 'auto' }}>
                    {loading ? (
                        <>
                            <LoadingCard />
                            <LoadingCard />
                            <LoadingCard />
                        </>
                    ) : filteredClusters.length === 0 ? (
                        <EmptyState
                            title="No clusters found"
                            description="Try adjusting your search or filters"
                        />
                    ) : (
                        filteredClusters.map(cluster => (
                            <ClusterListItem
                                key={cluster.id}
                                cluster={cluster}
                                isSelected={selectedCluster?.id === cluster.id}
                                onClick={() => setSelectedCluster(cluster)}
                            />
                        ))
                    )}
                </div>
            </div>

            {/* Main Panel - Cluster Detail */}
            <div style={{ flex: 1, minWidth: 0 }}>
                {loading ? (
                    <div className="flex flex-col gap-4">
                        <LoadingCard />
                        <LoadingCard />
                    </div>
                ) : (
                    <ClusterDetail cluster={selectedCluster} />
                )}
            </div>
        </div>
    );
}

export default StoryClusters;
