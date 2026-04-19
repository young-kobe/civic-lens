import { useState } from 'react';
import { Tabs, GlobalFilters, ExportMenu } from './components/common';
import { Home, PublicSentiment, BotActivityProfiler, GlobalHeatmap, Narratives, Review } from './pages';
import type { Filters } from './types';

// Admin flag — set once via ?admin=1, persisted to localStorage for subsequent visits.
// Non-admin users never see the Review tab. Global Heatmap is hidden for everyone
// for now (keep the page code; just unlink from nav).
const ADMIN_MODE: boolean = (() => {
    try {
        const params = new URLSearchParams(window.location.search);
        if (params.get('admin') === '1') {
            localStorage.setItem('civic_admin', '1');
            return true;
        }
        if (params.get('admin') === '0') {
            localStorage.removeItem('civic_admin');
            return false;
        }
        return localStorage.getItem('civic_admin') === '1';
    } catch {
        return false;
    }
})();

const BASE_TABS = [
    { id: 'sentiment', label: 'Public Sentiment' },
    { id: 'narratives', label: 'Narratives' },
    { id: 'bots', label: 'Bot Detector' },
];

const ADMIN_TABS = [
    { id: 'review', label: 'Review' },
];

const TABS = ADMIN_MODE ? [...BASE_TABS, ...ADMIN_TABS] : BASE_TABS;

function App() {
    const [activeTab, setActiveTab] = useState('home');
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
            case 'home':
                return <Home onNavigate={setActiveTab} isAdmin={ADMIN_MODE} />;
            case 'sentiment':
                return <PublicSentiment filters={filters} />;
            case 'narratives':
                return <Narratives filters={filters} />;
            case 'bots':
                return <BotActivityProfiler filters={filters} />;
            case 'heatmap':
                return <GlobalHeatmap filters={filters} />;  // hidden from nav; reachable only if re-linked
            case 'review':
                return ADMIN_MODE ? <Review /> : <Home onNavigate={setActiveTab} isAdmin={ADMIN_MODE} />;
            default:
                return <Home onNavigate={setActiveTab} isAdmin={ADMIN_MODE} />;
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
                        <button
                            type="button"
                            onClick={() => setActiveTab('home')}
                            aria-label="Return to home"
                            title="Return to home"
                            style={{
                                background: 'transparent',
                                border: 'none',
                                padding: 0,
                                cursor: 'pointer',
                                font: 'inherit',
                                color: 'inherit',
                            }}
                        >
                            <h1 className="page-title" style={{ margin: 0 }}>CIVIC&nbsp;LENS</h1>
                        </button>
                        <span style={{
                            width: 1, height: 16, background: 'var(--neutral-300)',
                            alignSelf: 'center',
                        }} />
                        <p className="page-subtitle" style={{ marginTop: 0 }}>
                            Political Media · Narrative &amp; Bot Tracker
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

            {/* Global Filters — hidden on Home (no filtered data to scope). */}
            {activeTab !== 'home' && (
                <GlobalFilters
                    filters={filters}
                    onFilterChange={setFilters}
                    showGeography={false}
                />
            )}

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
