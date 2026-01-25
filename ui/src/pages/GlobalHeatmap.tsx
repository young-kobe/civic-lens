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

function getSentimentColor(sentiment: number): string {
    // Red (negative) -> Orange (slightly negative) -> Yellow (neutral) -> Green (positive)
    if (sentiment >= 0.3) return '#22c55e';  // Green
    if (sentiment >= 0.1) return '#84cc16';  // Lime
    if (sentiment >= -0.1) return '#eab308'; // Yellow
    if (sentiment >= -0.3) return '#f97316'; // Orange
    return '#ef4444';  // Red
}

function getMarkerSize(postCount: number, maxPosts: number): number {
    const minSize = 6;
    const maxSize = 30;
    const ratio = postCount / maxPosts;
    return minSize + (maxSize - minSize) * Math.sqrt(ratio);
}

// Dummy data for visual testing
const DUMMY_DATA: GeoSentimentData = {
    countries: [
        { country_code: 'US', country_name: 'United States', post_count: 15420, avg_sentiment: 0.12, avg_favorability: -0.08 },
        { country_code: 'GB', country_name: 'United Kingdom', post_count: 2340, avg_sentiment: -0.25, avg_favorability: -0.35 },
        { country_code: 'CA', country_name: 'Canada', post_count: 1890, avg_sentiment: 0.05, avg_favorability: -0.15 },
        { country_code: 'DE', country_name: 'Germany', post_count: 1250, avg_sentiment: -0.18, avg_favorability: -0.22 },
        { country_code: 'AU', country_name: 'Australia', post_count: 980, avg_sentiment: 0.08, avg_favorability: 0.12 },
        { country_code: 'IN', country_name: 'India', post_count: 870, avg_sentiment: 0.35, avg_favorability: 0.28 },
        { country_code: 'FR', country_name: 'France', post_count: 650, avg_sentiment: -0.32, avg_favorability: -0.40 },
        { country_code: 'RU', country_name: 'Russia', post_count: 420, avg_sentiment: -0.55, avg_favorability: 0.45 },
        { country_code: 'BR', country_name: 'Brazil', post_count: 380, avg_sentiment: 0.22, avg_favorability: 0.35 },
        { country_code: 'JP', country_name: 'Japan', post_count: 290, avg_sentiment: 0.02, avg_favorability: 0.05 },
        { country_code: 'MX', country_name: 'Mexico', post_count: 520, avg_sentiment: 0.15, avg_favorability: 0.08 },
        { country_code: 'IT', country_name: 'Italy', post_count: 340, avg_sentiment: -0.12, avg_favorability: -0.18 },
        { country_code: 'ES', country_name: 'Spain', post_count: 280, avg_sentiment: -0.08, avg_favorability: -0.22 },
        { country_code: 'NL', country_name: 'Netherlands', post_count: 220, avg_sentiment: 0.18, avg_favorability: -0.05 },
        { country_code: 'PL', country_name: 'Poland', post_count: 180, avg_sentiment: 0.25, avg_favorability: 0.32 },
    ],
    total_posts: 24500,
    posts_with_geo: 18200,
    geo_coverage_pct: 74.3,
    excluded_bots: 1250,
    country_count: 15,
};

function StatsOverview({ data }: { data: GeoSentimentData }) {
    return (
        <div className="stats-row">
            <div className="stat"><span className="stat-value">{data.country_count}</span><span className="stat-label">Countries</span></div>
            <div className="stat"><span className="stat-value">{data.posts_with_geo.toLocaleString()}</span><span className="stat-label">Geo-Tagged Posts</span></div>
            <div className="stat"><span className="stat-value">{data.geo_coverage_pct}%</span><span className="stat-label">Coverage</span></div>
            <div className="stat"><span className="stat-value">{data.excluded_bots}</span><span className="stat-label">Bots Excluded</span></div>
        </div>
    );
}

function SentimentLegend() {
    return (
        <div className="legend">
            <span className="legend-label">Sentiment:</span>
            <div className="legend-item"><span className="dot" style={{ background: '#ef4444' }} />Negative</div>
            <div className="legend-item"><span className="dot" style={{ background: '#f97316' }} />Slightly Negative</div>
            <div className="legend-item"><span className="dot" style={{ background: '#eab308' }} />Neutral</div>
            <div className="legend-item"><span className="dot" style={{ background: '#84cc16' }} />Slightly Positive</div>
            <div className="legend-item"><span className="dot" style={{ background: '#22c55e' }} />Positive</div>
        </div>
    );
}

interface TooltipData {
    country: CountryStats;
    x: number;
    y: number;
}

function GlobalHeatmap({ filters }: GlobalHeatmapProps) {
    const [data, setData] = useState<GeoSentimentData | null>(null);
    const [loading, setLoading] = useState(true);
    const [useDummy, setUseDummy] = useState(false);
    const [tooltip, setTooltip] = useState<TooltipData | null>(null);

    useEffect(() => {
        async function loadData() {
            try {
                setLoading(true);
                const result = await fetchGeoSentiment(filters.timeRange as TimeWindow);
                if (result.countries.length === 0) {
                    setData(DUMMY_DATA);
                    setUseDummy(true);
                } else {
                    setData(result);
                    setUseDummy(false);
                }
            } catch {
                setData(DUMMY_DATA);
                setUseDummy(true);
            } finally {
                setLoading(false);
            }
        }
        loadData();
    }, [filters.timeRange]);

    if (loading) {
        return <LoadingCard />;
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
            {useDummy && (
                <div className="demo-banner">
                    Showing demo data - connect X API for live data
                </div>
            )}

            <Card title="Global Sentiment Distribution">
                <p className="description">
                    Geographic distribution of X political posts. Dot size = volume, color = sentiment.
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
                                            fill="#e5e7eb"
                                            stroke="#d1d5db"
                                            strokeWidth={0.5}
                                            style={{
                                                default: { outline: 'none' },
                                                hover: { outline: 'none', fill: '#d1d5db' },
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
                    <div className="tooltip-row">
                        <span>GOP Favorability:</span>
                        <span>{tooltip.country.avg_favorability >= 0 ? '+' : ''}{tooltip.country.avg_favorability.toFixed(2)}</span>
                    </div>
                </div>
            )}

            <style>{`
                .global-heatmap {
                    display: flex;
                    flex-direction: column;
                    gap: var(--space-4);
                }
                
                .demo-banner {
                    padding: 12px 16px;
                    background: #fef3c7;
                    border: 1px solid #f59e0b;
                    border-radius: 8px;
                    color: #92400e;
                    font-size: 0.875rem;
                }
                
                .description {
                    color: var(--text-secondary);
                    margin-bottom: var(--space-4);
                    font-size: 0.875rem;
                }
                
                .stats-row {
                    display: flex;
                    gap: var(--space-8);
                    margin-bottom: var(--space-4);
                }
                
                .stat {
                    display: flex;
                    flex-direction: column;
                }
                
                .stat-value {
                    font-size: 1.25rem;
                    font-weight: 600;
                }
                
                .stat-label {
                    font-size: 0.75rem;
                    color: var(--text-secondary);
                    text-transform: uppercase;
                }
                
                .legend {
                    display: flex;
                    align-items: center;
                    gap: var(--space-4);
                    margin-bottom: var(--space-4);
                    font-size: 0.75rem;
                }
                
                .legend-label {
                    font-weight: 500;
                    color: var(--text-secondary);
                }
                
                .legend-item {
                    display: flex;
                    align-items: center;
                    gap: 4px;
                }
                
                .dot {
                    width: 10px;
                    height: 10px;
                    border-radius: 50%;
                }
                
                .map-container {
                    height: 500px;
                    background: #f9fafb;
                    border-radius: 8px;
                    overflow: hidden;
                }
                
                .map-tooltip {
                    position: fixed;
                    background: white;
                    border: 1px solid #e5e7eb;
                    border-radius: 8px;
                    padding: 12px;
                    box-shadow: 0 4px 12px rgba(0,0,0,0.15);
                    z-index: 1000;
                    pointer-events: none;
                    min-width: 180px;
                }
                
                .tooltip-header {
                    font-weight: 600;
                    margin-bottom: 8px;
                    padding-bottom: 8px;
                    border-bottom: 1px solid #e5e7eb;
                }
                
                .tooltip-row {
                    display: flex;
                    justify-content: space-between;
                    font-size: 0.875rem;
                    margin-bottom: 4px;
                }
                
                .tooltip-row span:first-child {
                    color: var(--text-secondary);
                }
            `}</style>
        </div>
    );
}

export default GlobalHeatmap;
