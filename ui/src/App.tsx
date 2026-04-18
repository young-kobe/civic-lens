import { useState } from 'react';
import { Tabs, GlobalFilters, ExportMenu } from './components/common';
import { PublicSentiment, BotActivityProfiler, GlobalHeatmap } from './pages';
import type { Filters } from './types';

const TABS = [
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
            case 'sentiment':
                return <PublicSentiment filters={filters} />;
            case 'bots':
                return <BotActivityProfiler filters={filters} />;
            case 'heatmap':
                return <GlobalHeatmap filters={filters} />;
            default:
                return <PublicSentiment filters={filters} />;
        }
    };

    const now = new Date();
    const timestamp = now.toISOString().slice(0, 19).replace('T', ' ') + ' UTC';

    return (
        <div className="app-container">
            {/* Header */}
            <header className="page-header">
                <div className="flex items-center justify-between gap-4">
                    <div className="flex items-baseline gap-3">
                        <h1 className="page-title">CIVIC&nbsp;LENS</h1>
                        <span style={{
                            width: 1, height: 16, background: 'var(--neutral-300)',
                            alignSelf: 'center',
                        }} />
                        <p className="page-subtitle" style={{ marginTop: 0 }}>
                            Media Narrative &amp; Sentiment Analytics
                        </p>
                    </div>
                    <div className="flex items-center gap-3">
                        <span className="status-strip">
                            <span className="tick-up">●</span>
                            <span>LIVE</span>
                            <span className="sep" />
                            <span>{timestamp}</span>
                        </span>
                        <ExportMenu onExport={handleExport} />
                    </div>
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
            <main style={{ paddingTop: 'var(--space-4)', paddingBottom: 'var(--space-10)' }}>
                {renderPage()}
            </main>

            {/* Footer */}
            <footer
                style={{
                    borderTop: '1px solid var(--neutral-200)',
                    padding: 'var(--space-3) 0',
                    marginTop: 'var(--space-6)',
                    fontSize: 'var(--text-xs)',
                    color: 'var(--neutral-500)',
                    fontFamily: 'var(--font-mono)',
                    letterSpacing: '0.04em',
                }}
            >
                <div className="flex justify-between uppercase">
                    <span>Civic Lens · Open Source Media Analytics</span>
                    <span>Refreshed: {timestamp}</span>
                </div>
            </footer>
        </div>
    );
}

export default App;
