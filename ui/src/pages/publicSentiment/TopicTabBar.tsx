import type { TopicSentiment } from '../../types';

// --------------------------------------------------------------------------- //
//  TopicTabBar — the Overall Tone page's visual anchor, restored from the     //
//  pre-cutover tree (see docs/todos and PublicSentiment.tsx's degradation     //
//  notes). The old bar read a fixed topic registry (services/topics.ts, with  //
//  per-topic icons and slugs) that no longer exists; this version rebuilds    //
//  the tabs straight from `byTopic` in SentimentPanelResponse, so the tab set //
//  and per-tab volumes are exactly what the backend currently publishes.      //
// --------------------------------------------------------------------------- //

export const ALL_TOPICS_KEY = 'all';

interface TopicTabBarProps {
    activeKey: string;
    onChange: (key: string) => void;
    byTopic: TopicSentiment[];
}

export function TopicTabBar({ activeKey, onChange, byTopic }: TopicTabBarProps) {
    const totalVolume = byTopic.reduce((sum, t) => sum + t.volume, 0);
    const tabs = [
        { key: ALL_TOPICS_KEY, label: 'All Topics', volume: totalVolume },
        ...[...byTopic]
            .sort((a, b) => b.volume - a.volume)
            .map((t) => ({ key: t.topic, label: t.topic, volume: t.volume })),
    ];

    return (
        <nav className="topic-tabbar" role="tablist" aria-label="Filter by political topic">
            {tabs.map((tab) => {
                const active = tab.key === activeKey;
                const hasData = tab.volume > 0;
                const className = [
                    'topic-tab',
                    active ? 'topic-tab-active' : '',
                    !hasData ? 'topic-tab-empty' : '',
                ].filter(Boolean).join(' ');
                const countLabel = hasData
                    ? `${tab.volume.toLocaleString()} ${tab.volume === 1 ? 'post' : 'posts'}`
                    : 'no posts';
                const title = tab.key === ALL_TOPICS_KEY
                    ? `All topics — every scored post in this window.`
                    : `${tab.label} — ${countLabel} in this window`;
                return (
                    <button
                        key={tab.key}
                        type="button"
                        role="tab"
                        aria-selected={active}
                        className={className}
                        onClick={() => onChange(tab.key)}
                        title={title}
                    >
                        <span className="topic-tab-label">{tab.label}</span>
                        <span className="topic-tab-count">{countLabel}</span>
                    </button>
                );
            })}
        </nav>
    );
}

export default TopicTabBar;
