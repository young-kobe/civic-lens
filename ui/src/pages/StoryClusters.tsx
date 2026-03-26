import { useState, useEffect } from 'react';
import { Card, ConfidenceBadge, MethodPopover, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { Sparkline, StackedBar } from '../components/charts';
import type { Filters, Cluster, ContentType } from '../types';

import { fetchStories, ContentTypeFilter } from '../services/api';
import { transformStories } from '../services/transformers';

interface ContentTypeTabProps {
    active: ContentTypeFilter;
    onChange: (type: ContentTypeFilter) => void;
}

function ContentTypeTabs({ active, onChange }: ContentTypeTabProps) {
    const tabs: { value: ContentTypeFilter; label: string }[] = [
        { value: 'all', label: 'All' },
        { value: 'articles', label: 'Articles' },
        { value: 'social', label: 'Social Posts' },
    ];

    return (
        <div className="flex gap-2 mb-4">
            {tabs.map(tab => (
                <button
                    key={tab.value}
                    onClick={() => onChange(tab.value)}
                    className={`badge ${active === tab.value ? 'badge-accent' : 'badge-neutral'}`}
                    style={{
                        cursor: 'pointer',
                        padding: 'var(--space-2) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                        fontWeight: active === tab.value ? 600 : 400,
                        transition: 'all 0.2s ease',
                    }}
                >
                    {tab.label}
                </button>
            ))}
        </div>
    );
}

function getContentTypeLabel(contentType: ContentType): string {
    switch (contentType) {
        case 'articles': return 'Articles';
        case 'social': return 'Social Posts';
        case 'mixed': return 'Mixed';
    }
}

function getContentTypeBadgeClass(contentType: ContentType): string {
    switch (contentType) {
        case 'articles': return 'badge-neutral';
        case 'social': return 'badge-accent';
        case 'mixed': return 'badge-neutral';
    }
}

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
            <div className="flex items-center gap-2 mb-1">
                <h4 className="font-semibold truncate" style={{ flex: 1 }}>
                    {cluster.title}
                </h4>
                <span className={`badge ${getContentTypeBadgeClass(cluster.contentType)}`} style={{ fontSize: '0.65rem', flexShrink: 0 }}>
                    {getContentTypeLabel(cluster.contentType)}
                </span>
            </div>
            <div className="flex items-center gap-3 text-sm text-muted">
                <span>{cluster.articleCount} {cluster.contentType === 'social' ? 'posts' : cluster.contentType === 'articles' ? 'articles' : 'articles/posts'}</span>
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

    const itemLabel = cluster.contentType === 'social' ? 'posts' : cluster.contentType === 'articles' ? 'articles' : 'articles/posts';

    return (
        <div className="flex flex-col gap-6">
            {/* Header */}
            <div>
                <div className="flex items-center gap-3 mb-2">
                    <h2 className="text-xl font-semibold">{cluster.title}</h2>
                    <span className={`badge ${getContentTypeBadgeClass(cluster.contentType)}`}>
                        {getContentTypeLabel(cluster.contentType)}
                    </span>
                </div>
                <div className="flex items-center gap-4">
                    <span className="text-muted">{cluster.articleCount} {itemLabel}</span>
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
                {cluster.summary.length > 0 ? (
                    <ul style={{ margin: 0, paddingLeft: 'var(--space-5)' }}>
                        {cluster.summary.map((point, i) => (
                            <li key={i} className="mb-2">{point}</li>
                        ))}
                    </ul>
                ) : (
                    <p className="text-muted text-sm">No summary available.</p>
                )}

                {cluster.keyClaims && cluster.keyClaims.length > 0 && (
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
            {(cluster.entities.people.length > 0 || cluster.entities.organizations.length > 0 || cluster.entities.locations.length > 0) && (
                <Card title="Key Entities">
                    <div className="grid-3">
                        {cluster.entities.people.length > 0 && (
                            <div>
                                <div className="text-xs font-medium text-muted mb-2">People</div>
                                <div className="flex flex-wrap gap-1">
                                    {cluster.entities.people.map((p, i) => (
                                        <span key={i} className="badge badge-accent">{p}</span>
                                    ))}
                                </div>
                            </div>
                        )}
                        {cluster.entities.organizations.length > 0 && (
                            <div>
                                <div className="text-xs font-medium text-muted mb-2">Organizations</div>
                                <div className="flex flex-wrap gap-1">
                                    {cluster.entities.organizations.map((o, i) => (
                                        <span key={i} className="badge badge-neutral">{o}</span>
                                    ))}
                                </div>
                            </div>
                        )}
                        {cluster.entities.locations.length > 0 && (
                            <div>
                                <div className="text-xs font-medium text-muted mb-2">Locations</div>
                                <div className="flex flex-wrap gap-1">
                                    {cluster.entities.locations.map((l, i) => (
                                        <span key={i} className="badge badge-neutral">{l}</span>
                                    ))}
                                </div>
                            </div>
                        )}
                    </div>
                </Card>
            )}

            {/* Source Mix & Timeline */}
            <div className="grid-2">
                <Card title="Source Mix">
                    {cluster.sourceMix.length > 0 ? (
                        <StackedBar data={cluster.sourceMix} height={160} layout="vertical" />
                    ) : (
                        <div className="flex items-center justify-center h-40 text-muted text-sm">No source data</div>
                    )}
                </Card>
                <Card title="Volume Over Time">
                    {cluster.timeline.length > 0 ? (
                        <Sparkline data={cluster.timeline} dataKey="value" height={120} />
                    ) : (
                        <div className="flex items-center justify-center h-40 text-muted text-sm">No timeline data</div>
                    )}
                </Card>
            </div>

            {/* Representative Content */}
            <Card title={cluster.contentType === 'social' ? 'Representative Posts' : 'Representative Articles'}>
                {cluster.articles.length === 0 ? (
                    <p className="text-muted">No representative content available</p>
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
    const [contentTypeFilter, setContentTypeFilter] = useState<ContentTypeFilter>('all');
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const loadData = async () => {
            setLoading(true);
            setError(null);
            try {
                const rawData = await fetchStories(filters.timeRange, contentTypeFilter);
                const processedData = transformStories(rawData);
                setClusters(processedData);
                if (processedData.length > 0) {
                    setSelectedCluster(processedData[0]);
                } else {
                    setSelectedCluster(null);
                }
            } catch (err: unknown) {
                const message = err instanceof Error ? err.message : 'Failed to load story clusters.';
                console.error("Failed to load stories:", err);
                setError(message);
            } finally {
                setLoading(false);
            }
        };

        loadData();
    }, [filters, contentTypeFilter]);

    const filteredClusters = clusters.filter(c =>
        c.title.toLowerCase().includes(searchQuery.toLowerCase())
    );

    if (error) {
        return <ErrorState message={error} onRetry={() => window.location.reload()} />;
    }

    return (
        <div>
            {/* Content Type Tabs */}
            <ContentTypeTabs active={contentTypeFilter} onChange={setContentTypeFilter} />

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
        </div>
    );
}

export default StoryClusters;
