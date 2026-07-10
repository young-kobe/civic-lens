import { TOPICS, type Topic, type TopicKey } from '../../services/topics';
import type { SentimentBreakdown } from '../../types';

interface TopicTabBarProps {
    activeKey: TopicKey;
    onChange: (key: TopicKey) => void;
    /** Per-topic volumes from `data.byTopic`, used to surface a small
     *  "n posts" affordance under each label so users can see at a glance
     *  which topics are dense in the current window. Tabs whose topic has
     *  zero docs render slightly muted but stay clickable — clicking
     *  them still scopes the page (it'll show the empty state). */
    byTopic: SentimentBreakdown[];
}

/**
 * Big-tab category bar for political topics — the visual anchor of the
 * Overall Tone page. One tab per backend topic plus an "All Topics"
 * option that disables filtering. On wide viewports renders a single
 * wrapping row; on narrow viewports the row scrolls horizontally so the
 * tabs stay full-size rather than crushing into a dropdown.
 */
export function TopicTabBar({ activeKey, onChange, byTopic }: TopicTabBarProps) {
    const volumes = new Map<string, number>();
    let totalVolume = 0;
    for (const row of byTopic) {
        if (!row.topic) continue;
        const v = row.volume ?? 0;
        volumes.set(row.topic, v);
        totalVolume += v;
    }

    return (
        <nav
            className="topic-tabbar"
            role="tablist"
            aria-label="Filter by political topic"
        >
            {TOPICS.map((topic) => {
                const volume = topic.key === 'all' ? totalVolume : (volumes.get(topic.key) ?? 0);
                const active = topic.key === activeKey;
                return (
                    <TopicTab
                        key={topic.key}
                        topic={topic}
                        active={active}
                        volume={volume}
                        onClick={() => onChange(topic.key)}
                    />
                );
            })}
        </nav>
    );
}

interface TopicTabProps {
    topic: Topic;
    active: boolean;
    volume: number;
    onClick: () => void;
}

function TopicTab({ topic, active, volume, onClick }: TopicTabProps) {
    const hasData = volume > 0;
    const isAll = topic.key === 'all';
    const className = [
        'topic-tab',
        active ? 'topic-tab-active' : '',
        !hasData ? 'topic-tab-empty' : '',
    ].filter(Boolean).join(' ');

    // "All Topics" sums the per-topic volumes, and since 'General' (the
    // unclassified bucket) is a real tab, that sum now covers every scored
    // post in the window — no more "topic-matched" caveat.
    const countLabel = hasData
        ? `${volume.toLocaleString()} ${volume === 1 ? 'post' : 'posts'}`
        : 'no posts';
    const title = isAll
        ? `All topics — every scored post in this window, including posts `
            + `with no topic signal (General).`
        : `${topic.label} — ${countLabel} in this window`;

    return (
        <button
            type="button"
            role="tab"
            aria-selected={active}
            className={className}
            onClick={onClick}
            title={title}
        >
            <span className="topic-tab-icon" aria-hidden>
                <svg
                    viewBox="0 0 24 24"
                    width={22}
                    height={22}
                    fill="none"
                    stroke="currentColor"
                    strokeWidth={1.75}
                    strokeLinecap="round"
                    strokeLinejoin="round"
                >
                    {topic.iconPaths.map((d, i) => <path key={i} d={d} />)}
                </svg>
            </span>
            <span className="topic-tab-label">{topic.label}</span>
            <span className="topic-tab-count">{countLabel}</span>
        </button>
    );
}

export default TopicTabBar;
