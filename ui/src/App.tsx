import { useState } from 'react';
import { Tabs, GlobalFilters, ExportMenu } from './components/common';
import { StoryClusters, PublicSentiment, BotActivityProfiler, GlobalHeatmap } from './pages';
import type { Filters } from './types';

const TABS = [
    // { id: 'clusters', label: 'Story Clusters' },
    { id: 'sentiment', label: 'Public Sentiment' },
    { id: 'bots', label: 'Bot Activity Profiler' },
    { id: 'heatmap', label: 'Global Heatmap' },
];

function App() {
    const [activeTab, setActiveTab] = useState('sentiment');
    const [filters, setFilters] = useState<Filters>({
        timeRange: '7d',
        sourceType: 'all',
        geography: 'all',
    });

    const handleExport = (format: string) => {
        console.log('Exporting as:', format);
        // Export implementation would go here
    };

    const renderPage = () => {
        switch (activeTab) {
            case 'clusters':
                return <StoryClusters filters={filters} />;
            case 'sentiment':
                return <PublicSentiment filters={filters} />;
            case 'bots':
                return <BotActivityProfiler filters={filters} />;
            case 'heatmap':
                return <GlobalHeatmap filters={filters} />;
            default:
                return <StoryClusters filters={filters} />;
        }
    };

    return (
        <div className="app-container">
            {/* Header */}
            <header className="page-header">
                <div className="flex items-center justify-between">
                    <div>
                        <h1 className="page-title">Civic Lens</h1>
                        <p className="page-subtitle">Media Narrative & Sentiment Analytics</p>
                    </div>
                    <ExportMenu onExport={handleExport} />
                </div>
            </header>

            {/* Navigation */}
            <Tabs tabs={TABS} activeTab={activeTab} onTabChange={setActiveTab} />

            {/* Global Filters */}
            <GlobalFilters
                filters={filters}
                onFilterChange={setFilters}
                showGeography={false}
            />

            {/* Main Content */}
            <main style={{ paddingTop: 'var(--space-6)', paddingBottom: 'var(--space-12)' }}>
                {renderPage()}
            </main>

            {/* Footer */}
            <footer
                style={{
                    borderTop: '1px solid var(--neutral-200)',
                    padding: 'var(--space-6) 0',
                    marginTop: 'var(--space-8)',
                    fontSize: 'var(--text-xs)',
                    color: 'var(--neutral-500)'
                }}
            >
                <div className="flex justify-between">
                    <span>Civic Lens - Open Source Media Analytics</span>
                    <span>Data last refreshed: {new Date().toLocaleString()}</span>
                </div>
            </footer>
        </div>
    );
}

export default App;
