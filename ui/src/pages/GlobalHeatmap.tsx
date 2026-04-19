import { useState, useEffect } from 'react';
import { ComposableMap, Geographies, Geography, Marker, ZoomableGroup } from 'react-simple-maps';
import { Card, LoadingCard, EmptyState } from '../components/common';
import { fetchGeoSentiment, GeoSentimentData, CountryStats, TimeWindow } from '../services/api';
import { Filters } from '../types';

// World map TopoJSON URL
const GEO_URL = "https://cdn.jsdelivr.net/npm/world-atlas@2/countries-110m.json";

// Country centroids for scatter plot positioning
const COUNTRY_COORDS: Record<string, [number, number]> = {
    'US': [-95.7, 37.1],
    'GB': [-1.2, 52.2],
    'CA': [-106.3, 56.1],
    'DE': [10.5, 51.2],
    'AU': [133.8, -25.3],
    'IN': [78.9, 21.0],
    'FR': [2.2, 46.2],
    'RU': [105.3, 61.5],
    'BR': [-51.9, -14.2],
    'JP': [138.3, 36.2],
    'MX': [-102.5, 23.6],
    'CN': [104.2, 35.9],
    'KR': [127.8, 35.9],
    'IT': [12.6, 41.9],
    'ES': [-3.7, 40.5],
    'NL': [5.3, 52.1],
    'SE': [18.6, 60.1],
    'PL': [19.1, 51.9],
    'UA': [31.2, 48.4],
    'TR': [35.2, 38.9],
    'SA': [45.1, 23.9],
    'ZA': [22.9, -30.6],
    'AR': [-63.6, -38.4],
    'CO': [-74.3, 4.6],
    'PH': [122.0, 12.9],
    'ID': [113.9, -0.8],
    'NG': [8.7, 9.1],
    'EG': [30.8, 26.8],
};

interface GlobalHeatmapProps {
    filters: Filters;
}

interface TooltipData {
    country: CountryStats;
    x: number;
    y: number;
}

function getSentimentColor(sentiment: number): string {
    if (sentiment >= 0.3) return '#008a4c';  // Strong positive
    if (sentiment >= 0.1) return '#4b9e6d';  // Mild positive
    if (sentiment >= -0.1) return '#8e8e96'; // Neutral
    if (sentiment >= -0.3) return '#b26100'; // Mild negative
    return '#d41e0e';  // Strong negative
}

function getMarkerSize(postCount: number, maxPosts: number): number {
    const minSize = 6;
    const maxSize = 30;
    const ratio = postCount / maxPosts;
    return minSize + (maxSize - minSize) * Math.sqrt(ratio);
}

function StatsOverview({ data }: { data: GeoSentimentData }) {
    return (
        <div className="stats-row">
            <div className="stat">
                <span className="stat-value">{data.total_posts.toLocaleString()}</span>
                <span className="stat-label">Total Posts</span>
            </div>
            <div className="stat">
                <span className="stat-value">{data.posts_with_geo.toLocaleString()}</span>
                <span className="stat-label">Mapped</span>
            </div>
            <div className="stat">
                <span className="stat-value">{data.geo_coverage_pct.toFixed(1)}%</span>
                <span className="stat-label">Coverage</span>
            </div>
            <div className="stat">
                <span className="stat-value">{data.country_count}</span>
                <span className="stat-label">Countries</span>
            </div>
        </div>
    );
}

function SentimentLegend() {
    return (
        <div className="legend">
            <span className="legend-label">Sentiment</span>
            <div className="legend-item">
                <span className="dot" style={{ backgroundColor: '#008a4c' }}></span>
                <span>Positive</span>
            </div>
            <div className="legend-item">
                <span className="dot" style={{ backgroundColor: '#8e8e96' }}></span>
                <span>Neutral</span>
            </div>
            <div className="legend-item">
                <span className="dot" style={{ backgroundColor: '#d41e0e' }}></span>
                <span>Negative</span>
            </div>
        </div>
    );
}

function GlobalHeatmap({ filters }: GlobalHeatmapProps) {
    const [data, setData] = useState<GeoSentimentData | null>(null);
    const [loading, setLoading] = useState(true);
    const [tooltip, setTooltip] = useState<TooltipData | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        async function loadData() {
            try {
                setLoading(true);
                setError(null);
                const result = await fetchGeoSentiment(filters.timeRange as TimeWindow);
                setData(result);
            } catch (err) {
                setError(err instanceof Error ? err.message : 'Failed to load data');
            } finally {
                setLoading(false);
            }
        }
        loadData();
    }, [filters.timeRange]);

    if (loading) {
        return <LoadingCard />;
    }

    if (error) {
        return (
            <div style={{ padding: '20px', textAlign: 'center', color: 'var(--text-secondary)' }}>
                <p>Failed to load heatmap data. Please enable the X integration.</p>
                <p style={{ fontSize: '0.8em', marginTop: '8px' }}>{error}</p>
            </div>
        );
    }

    if (!data || data.countries.length === 0) {
        return (
            <EmptyState
                title="No Geo-Tagged Data"
                description="No X posts with location data found for this time period."
            />
        );
    }

    const maxPosts = Math.max(...data.countries.map(c => c.post_count));

    return (
        <div className="global-heatmap">


            <Card title="Global Sentiment Distribution">
                <p className="description">
                    Country-level sentiment of <strong>political X posts</strong> that carry a geo tag. Dot size =
                    volume, color = net sentiment. Most X posts have no location metadata, so coverage is thin and
                    uneven — treat this view as indicative, not comprehensive.
                </p>
                <StatsOverview data={data} />
                <SentimentLegend />

                <div className="map-container">
                    <ComposableMap
                        projection="geoMercator"
                        projectionConfig={{ scale: 130, center: [0, 30] }}
                        style={{ width: '100%', height: '100%' }}
                    >
                        <ZoomableGroup>
                            <Geographies geography={GEO_URL}>
                                {({ geographies }) =>
                                    geographies.map((geo) => (
                                        <Geography
                                            key={geo.rsmKey}
                                            geography={geo}
                                            fill="#ececef"
                                            stroke="#d9d9de"
                                            strokeWidth={0.5}
                                            style={{
                                                default: { outline: 'none' },
                                                hover: { outline: 'none', fill: '#d9d9de' },
                                                pressed: { outline: 'none' },
                                            }}
                                        />
                                    ))
                                }
                            </Geographies>

                            {data.countries.map((country) => {
                                const coords = COUNTRY_COORDS[country.country_code];
                                if (!coords) return null;

                                const size = getMarkerSize(country.post_count, maxPosts);
                                const color = getSentimentColor(country.avg_sentiment);

                                return (
                                    <Marker
                                        key={country.country_code}
                                        coordinates={coords}
                                        onMouseEnter={(e) => {
                                            setTooltip({
                                                country,
                                                x: e.clientX,
                                                y: e.clientY,
                                            });
                                        }}
                                        onMouseLeave={() => setTooltip(null)}
                                    >
                                        <circle
                                            r={size}
                                            fill={color}
                                            fillOpacity={0.8}
                                            stroke="#fff"
                                            strokeWidth={1}
                                            style={{ cursor: 'pointer' }}
                                        />
                                    </Marker>
                                );
                            })}
                        </ZoomableGroup>
                    </ComposableMap>
                </div>
            </Card>

            {tooltip && (
                <div
                    className="map-tooltip"
                    style={{ left: tooltip.x + 10, top: tooltip.y - 10 }}
                >
                    <div className="tooltip-header">{tooltip.country.country_name}</div>
                    <div className="tooltip-row">
                        <span>Posts:</span>
                        <span>{tooltip.country.post_count.toLocaleString()}</span>
                    </div>
                    <div className="tooltip-row">
                        <span>Sentiment:</span>
                        <span style={{ color: getSentimentColor(tooltip.country.avg_sentiment) }}>
                            {tooltip.country.avg_sentiment >= 0 ? '+' : ''}{tooltip.country.avg_sentiment.toFixed(2)}
                        </span>
                    </div>
                </div>
            )}

            <style>{`
                .global-heatmap {
                    display: flex;
                    flex-direction: column;
                    gap: var(--space-3);
                }

                .description {
                    color: var(--neutral-500);
                    margin-bottom: var(--space-3);
                    font-size: var(--text-sm);
                }

                .stats-row {
                    display: flex;
                    gap: var(--space-6);
                    margin-bottom: var(--space-3);
                    padding: var(--space-2) var(--space-3);
                    background: var(--bg-panel);
                    border: 1px solid var(--neutral-200);
                    border-radius: 2px;
                }

                .stat {
                    display: flex;
                    flex-direction: column;
                    gap: 2px;
                }

                .stat-value {
                    font-family: var(--font-mono);
                    font-variant-numeric: tabular-nums;
                    font-size: var(--text-xl);
                    font-weight: 600;
                    color: var(--neutral-900);
                    letter-spacing: -0.01em;
                }

                .stat-label {
                    font-size: var(--text-xs);
                    color: var(--neutral-500);
                    text-transform: uppercase;
                    letter-spacing: 0.08em;
                    font-weight: 600;
                }

                .legend {
                    display: flex;
                    align-items: center;
                    gap: var(--space-3);
                    margin-bottom: var(--space-3);
                    font-size: var(--text-xs);
                    text-transform: uppercase;
                    letter-spacing: 0.06em;
                    color: var(--neutral-600);
                }

                .legend-label {
                    font-weight: 700;
                    color: var(--neutral-500);
                    letter-spacing: 0.08em;
                }

                .legend-item {
                    display: flex;
                    align-items: center;
                    gap: 6px;
                }

                .dot {
                    width: 8px;
                    height: 8px;
                    border-radius: 50%;
                }

                .map-container {
                    height: 500px;
                    background: var(--bg-panel);
                    border: 1px solid var(--neutral-200);
                    border-radius: 2px;
                    overflow: hidden;
                }

                .map-tooltip {
                    position: fixed;
                    background: var(--bg-card);
                    border: 1px solid var(--neutral-300);
                    border-radius: 2px;
                    padding: var(--space-2) var(--space-3);
                    box-shadow: var(--shadow-lg);
                    z-index: 1000;
                    pointer-events: none;
                    min-width: 180px;
                    font-family: var(--font-mono);
                    font-variant-numeric: tabular-nums;
                }

                .tooltip-header {
                    font-family: var(--font-family);
                    font-size: var(--text-xs);
                    font-weight: 700;
                    text-transform: uppercase;
                    letter-spacing: 0.08em;
                    color: var(--neutral-800);
                    margin-bottom: 6px;
                    padding-bottom: 6px;
                    border-bottom: 1px solid var(--neutral-200);
                }

                .tooltip-row {
                    display: flex;
                    justify-content: space-between;
                    font-size: var(--text-xs);
                    margin-bottom: 2px;
                }

                .tooltip-row span:first-child {
                    color: var(--neutral-500);
                    text-transform: uppercase;
                    letter-spacing: 0.06em;
                }
            `}</style>
        </div>
    );
}

export default GlobalHeatmap;
