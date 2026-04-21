import type { ReactNode } from 'react';

export interface Tab {
    id: string;
    label: string;
    shortLabel?: string;
    icon?: ReactNode;
}

interface TabsProps {
    tabs: Tab[];
    activeTab: string;
    onTabChange: (tabId: string) => void;
}

/**
 * Top-level section navigation.
 *
 * Desktop: horizontal underline tabs under the header.
 * Mobile (<=640px): CSS repositions this into a fixed bottom bar with icon + shortLabel.
 */
function Tabs({ tabs, activeTab, onTabChange }: TabsProps) {
    return (
        <nav className="nav-tabs" role="tablist" aria-label="Primary">
            {tabs.map((tab) => {
                const isActive = activeTab === tab.id;
                return (
                    <button
                        key={tab.id}
                        role="tab"
                        type="button"
                        aria-selected={isActive}
                        className={`nav-tab ${isActive ? 'nav-tab-active' : ''}`}
                        onClick={() => onTabChange(tab.id)}
                    >
                        {tab.icon && (
                            <span className="nav-tab-icon" aria-hidden>
                                {tab.icon}
                            </span>
                        )}
                        <span className="nav-tab-label">{tab.label}</span>
                        {tab.shortLabel && (
                            <span className="nav-tab-label-short">{tab.shortLabel}</span>
                        )}
                    </button>
                );
            })}
        </nav>
    );
}

export default Tabs;
