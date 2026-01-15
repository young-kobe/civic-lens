import React from 'react';

interface Tab {
    id: string;
    label: string;
}

interface TabsProps {
    tabs: Tab[];
    activeTab: string;
    onTabChange: (tabId: string) => void;
}

/**
 * Tabs - Navigation tabs for the main dashboard sections.
 */
function Tabs({ tabs, activeTab, onTabChange }: TabsProps) {
    return (
        <nav className="nav-tabs" role="tablist">
            {tabs.map((tab) => (
                <button
                    key={tab.id}
                    role="tab"
                    aria-selected={activeTab === tab.id}
                    className={`nav-tab ${activeTab === tab.id ? 'nav-tab-active' : ''}`}
                    onClick={() => onTabChange(tab.id)}
                >
                    {tab.label}
                </button>
            ))}
        </nav>
    );
}

export default Tabs;
