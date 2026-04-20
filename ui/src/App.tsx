import { useState } from 'react';
import { Tabs, GlobalFilters, ExportMenu } from './components/common';
import { Home, PublicSentiment, BotActivityProfiler, GlobalHeatmap, Narratives, Propaganda, Review } from './pages';
import type { Filters } from './types';

// Admin mode is now token-based. Visit once with ?admin=<CIVIC_ADMIN_TOKEN> to
// persist it; ?admin=0 clears it. The token is sent as X-Admin-Token on every
// admin-endpoint request (see services/api.ts). Non-admin users never see the
// Review tab. Global Heatmap is hidden for everyone (page kept, unlinked from nav).
const ADMIN_MODE: boolean = (() => {
    try {
        const params = new URLSearchParams(window.location.search);
        const raw = params.get('admin');
        if (raw === '0') {
            localStorage.removeItem('civic_admin_token');
            return false;
        }
        if (raw && raw !== '1') {
            localStorage.setItem('civic_admin_token', raw);
        }
        return !!localStorage.getItem('civic_admin_token');
    } catch {
        return false;
    }
})();

const BASE_TABS = [
    { id: 'sentiment', label: 'Public Sentiment' },
    { id: 'narratives', label: 'Narratives' },
    { id: 'propaganda', label: 'Propaganda' },
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
            case 'propaganda':
                return <Propaganda filters={filters} />;
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
                <div className="page-header-row">
                    <div className="page-header-brand">
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
                        <span className="page-header-separator" aria-hidden />
                        <p className="page-subtitle page-header-subtitle" style={{ marginTop: 0 }}>
                            Political Media · Narrative &amp; Bot Tracker
                        </p>
                    </div>
                    <div className="page-header-actions">
                        <span className="status-strip status-strip-full">
                            <span className="tick-up">●</span>
                            <span>LIVE</span>
                            <span className="sep" />
                            <span>{timestamp}</span>
                        </span>
                        <span
                            className="status-strip-mini"
                            title={`Live · ${timestamp}`}
                            aria-label={`Live · ${timestamp}`}
                        >
                            <span className="tick-up">●</span>
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
