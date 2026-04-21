import { useEffect, useState } from 'react';
import { Tabs, GlobalFilters, Footer, type Tab } from './components/common';
import { Home, PublicSentiment, BotActivityProfiler, GlobalHeatmap, Narratives, Propaganda, Review } from './pages';
import type { Filters } from './types';

/* Admin mode is token-based. Visit once with ?admin=<CIVIC_ADMIN_TOKEN> to persist it;
   ?admin=0 clears it. The token is sent as X-Admin-Token on every admin-endpoint
   request (see services/api.ts). Non-admin users never see the Review tab. The
   Global Heatmap is hidden for everyone (page kept, unlinked from nav).

   The URL is scrubbed after capture via history.replaceState so the token doesn't
   ride along in the Referer header on any outbound click. This is defense-in-depth
   on top of the Cloudflare Access SSO layer that actually gates admin endpoints. */
const ADMIN_MODE: boolean = (() => {
    try {
        const params = new URLSearchParams(window.location.search);
        const raw = params.get('admin');
        const tokenChanged = raw !== null;
        if (raw === '0') {
            localStorage.removeItem('civic_admin_token');
        } else if (raw && raw !== '1') {
            localStorage.setItem('civic_admin_token', raw);
        }
        if (tokenChanged) {
            params.delete('admin');
            const qs = params.toString();
            const cleanUrl = `${window.location.pathname}${qs ? `?${qs}` : ''}${window.location.hash}`;
            window.history.replaceState({}, '', cleanUrl);
        }
        return !!localStorage.getItem('civic_admin_token');
    } catch {
        return false;
    }
})();

const iconProps = {
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 2,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
};

const BASE_TABS: Tab[] = [
    {
        id: 'sentiment',
        label: 'Public Sentiment',
        shortLabel: 'Sentiment',
        icon: (
            <svg {...iconProps}>
                <path d="M3 3v18h18" />
                <path d="M7 14l4-4 4 4 5-6" />
            </svg>
        ),
    },
    {
        id: 'narratives',
        label: 'Narratives',
        shortLabel: 'Claims',
        icon: (
            <svg {...iconProps}>
                <path d="M4 7h16M4 12h16M4 17h10" />
            </svg>
        ),
    },
    {
        id: 'propaganda',
        label: 'Propaganda',
        shortLabel: 'Propaganda',
        icon: (
            <svg {...iconProps}>
                <path d="M12 3l8 4v5c0 5-3.5 8.5-8 9-4.5-.5-8-4-8-9V7l8-4z" />
                <path d="M12 8v4M12 16h.01" />
            </svg>
        ),
    },
    {
        id: 'bots',
        label: 'Bot Detector',
        shortLabel: 'Bots',
        icon: (
            <svg {...iconProps}>
                <rect x="4" y="7" width="16" height="12" rx="2" />
                <path d="M12 3v4M8 12h.01M16 12h.01M9 16h6" />
            </svg>
        ),
    },
];

const ADMIN_TABS: Tab[] = [
    {
        id: 'review',
        label: 'Review',
        shortLabel: 'Review',
        icon: (
            <svg {...iconProps}>
                <path d="M9 4h6a1 1 0 011 1v2H8V5a1 1 0 011-1z" />
                <rect x="5" y="6" width="14" height="15" rx="2" />
                <path d="M9 13l2 2 4-4" />
            </svg>
        ),
    },
];

const TABS: Tab[] = ADMIN_MODE ? [...BASE_TABS, ...ADMIN_TABS] : BASE_TABS;

function useScrolled(threshold = 4) {
    const [scrolled, setScrolled] = useState(false);
    useEffect(() => {
        const onScroll = () => setScrolled(window.scrollY > threshold);
        onScroll();
        window.addEventListener('scroll', onScroll, { passive: true });
        return () => window.removeEventListener('scroll', onScroll);
    }, [threshold]);
    return scrolled;
}

function App() {
    const [activeTab, setActiveTab] = useState('home');
    const [filters, setFilters] = useState<Filters>({
        timeRange: '7d',
        sourceType: 'all',
        geography: 'all',
    });
    const scrolled = useScrolled();

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
                return <GlobalHeatmap filters={filters} />;
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
            <header className={`page-header ${scrolled ? 'is-scrolled' : ''}`}>
                <div className="page-header-row">
                    <div className="page-header-brand">
                        <button
                            type="button"
                            className="brand-lockup"
                            onClick={() => setActiveTab('home')}
                            aria-label="Return to home"
                            title="Return to home"
                            style={{ padding: 0 }}
                        >
                            <span className="brand-mark" aria-hidden />
                            <h1 className="page-title" style={{ margin: 0 }}>CIVIC&nbsp;LENS</h1>
                        </button>
                        <span className="page-header-separator" aria-hidden />
                        <p className="page-subtitle page-header-subtitle" style={{ marginTop: 0 }}>
                            Political Media &middot; Narrative &amp; Bot Tracker
                        </p>
                    </div>
                    <div className="page-header-actions">
                        <span className="status-strip status-strip-full" aria-label={`Live, last refresh ${timestamp}`}>
                            <span className="tick-live" aria-hidden />
                            <span>LIVE</span>
                            <span className="sep" aria-hidden />
                            <span>{timestamp}</span>
                        </span>
                        <span
                            className="status-strip-mini"
                            title={`Live · ${timestamp}`}
                            aria-label={`Live · ${timestamp}`}
                        >
                            <span className="tick-live" aria-hidden />
                        </span>
                    </div>
                </div>
            </header>

            <Tabs tabs={TABS} activeTab={activeTab} onTabChange={setActiveTab} />

            {activeTab !== 'home' && (
                <GlobalFilters
                    filters={filters}
                    onFilterChange={setFilters}
                    showGeography={false}
                />
            )}

            <main style={{ paddingTop: 'var(--space-4)', paddingBottom: 'var(--space-10)' }}>
                {renderPage()}
            </main>

            <Footer timestamp={timestamp} />
        </div>
    );
}

export default App;
