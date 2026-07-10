import { useState } from 'react';
import type { ClassificationSample, SentimentBreakdown } from '../../types';
import {
    Card, MethodPopover, Modal, PostCardList, sampleToPostCard,
} from '../../components/common';
import { COLORS } from '../../theme';
import { formatPts } from '../../services/format';

interface TopicDivergencePanelProps {
    topics: SentimentBreakdown[];
}

const TIER_META = [
    { key: 'newsNet', label: 'News says', color: COLORS.neutral },
    { key: 'officialsNet', label: 'Officials say', color: COLORS.accent },
    { key: 'publicNet', label: 'Public says', color: COLORS.warning },
] as const;

/**
 * Topic-divergence panel (walkthrough 060 — Phase 5 redesign).
 *
 * Replaces the standalone TopicSentimentCard. Each row shows a topic
 * with three positioned dots (news / officials / public net tone) on a
 * −100..+100 axis; the visual range between the leftmost and rightmost
 * dot reads as "divergence". Rows sort by range desc so the most
 * polarized topics surface first.
 *
 * Clicking a row opens a modal with that topic's classification
 * samples — the UI the Sentiment by Topic card used to provide, now
 * attached per-topic rather than as a separate drawer.
 */
export function TopicDivergencePanel({ topics }: TopicDivergencePanelProps) {
    const [activeTopic, setActiveTopic] = useState<SentimentBreakdown | null>(null);

    const rows = topics
        .map((t) => ({ ...t, _range: divergenceRange(t) }))
        .filter((t) => t._range !== null)
        .sort((a, b) => (b._range ?? 0) - (a._range ?? 0));

    if (rows.length === 0) {
        return (
            <Card
                title="What each group is saying"
                subtitle="News, officials, and public net tone side-by-side per topic"
            >
                <p className="text-muted text-sm">
                    Not enough data yet — this panel appears once we have posts from at least
                    two groups (news, officials, public) on a topic.
                </p>
            </Card>
        );
    }

    return (
        <>
            <Card
                title="What each group is saying"
                subtitle="For each topic, where news outlets, officials, and the public land on the tone axis. The further apart the dots, the more the groups disagree."
                headerActions={
                    <MethodPopover
                        description="For each topic, we compute average tone separately from news articles, verified officials' posts, and everyone else (the general public). The dots on each row show where those three groups land between −100 (all negative) and +100 (all positive). A bigger spread between dots means more disagreement."
                        limitations={[
                            "Averages share the same underlying sample — a group with only a handful of posts on a topic can swing sharply.",
                            "Topic assignment is keyword-based; a post that straddles two topics gets counted under the first match.",
                        ]}
                    />
                }
            >
                <div className="topic-divergence-legend">
                    {TIER_META.map((tier) => (
                        <span key={tier.key} className="topic-divergence-legend-item">
                            <span
                                className="topic-divergence-legend-dot"
                                style={{ background: tier.color }}
                                aria-hidden
                            />
                            {tier.label}
                        </span>
                    ))}
                </div>
                <div className="topic-divergence-axis-caption" aria-hidden>
                    more negative &larr; 0 &rarr; more positive
                </div>
                <div className="topic-divergence-rows">
                    {rows.map((row) => (
                        <TopicDivergenceRow
                            key={row.topic}
                            row={row}
                            onOpen={() => setActiveTopic(row)}
                        />
                    ))}
                </div>
            </Card>
            {activeTopic && (
                <TopicSamplesModal
                    topic={activeTopic}
                    onClose={() => setActiveTopic(null)}
                />
            )}
        </>
    );
}

/** The divergence (range) between the extreme tier scores; null when
 *  fewer than two tiers have data. */
function divergenceRange(t: SentimentBreakdown): number | null {
    const values: number[] = [];
    if (t.newsNet != null) values.push(t.newsNet);
    if (t.officialsNet != null) values.push(t.officialsNet);
    if (t.publicNet != null) values.push(t.publicNet);
    if (values.length < 2) return null;
    return Math.max(...values) - Math.min(...values);
}

interface TopicDivergenceRowProps {
    row: SentimentBreakdown & { _range: number | null };
    onOpen: () => void;
}

/** Maps a -100..100 net score to a 0..100 axis position (percent). */
function axisPct(net: number): number {
    const clamped = Math.max(-100, Math.min(100, net));
    return ((clamped + 100) / 200) * 100;
}

function TopicDivergenceRow({ row, onOpen }: TopicDivergenceRowProps) {
    const total = (row.newsVolume ?? 0) + (row.officialsVolume ?? 0) + (row.publicVolume ?? 0);
    const rangeText = row._range !== null ? `${row._range.toFixed(1)} pts` : '—';
    const tooltip = tierTooltip(row);

    return (
        <button
            type="button"
            className="topic-divergence-row"
            onClick={onOpen}
            aria-label={`${row.topic} — ${tooltip}. Open samples.`}
            title={tooltip}
        >
            <div className="topic-divergence-row-label">
                <span className="topic-divergence-row-name">{row.topic}</span>
                <span className="topic-divergence-row-volume">
                    {total.toLocaleString()} posts
                </span>
            </div>
            <div className="topic-divergence-axis">
                {/* Zero marker */}
                <span className="topic-divergence-zero" aria-hidden />
                {TIER_META.map((tier) => {
                    const value = row[tier.key] as number | null | undefined;
                    if (value == null) return null;
                    return (
                        <span
                            key={tier.key}
                            className="topic-divergence-dot"
                            style={{
                                left: `${axisPct(value)}%`,
                                background: tier.color,
                            }}
                            title={`${tier.label}: ${formatPts(value)}`}
                        />
                    );
                })}
            </div>
            <div
                className="topic-divergence-row-range"
                title="Gap between the highest and lowest group tone"
            >
                {rangeText}
            </div>
        </button>
    );
}

function tierTooltip(row: SentimentBreakdown): string {
    const parts: string[] = [];
    for (const tier of TIER_META) {
        const value = row[tier.key] as number | null | undefined;
        if (value == null) continue;
        parts.push(`${tier.label} ${formatPts(value)}`);
    }
    return parts.join(' · ');
}

interface TopicSamplesModalProps {
    topic: SentimentBreakdown;
    onClose: () => void;
}

function TopicSamplesModal({ topic, onClose }: TopicSamplesModalProps) {
    const samples: ClassificationSample[] = topic.classificationSamples ?? [];

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={topic.topic || 'Topic'}
            subtitle={`${tierTooltip(topic)} · ${topic.volume.toLocaleString()} posts`}
        >
            {samples.length === 0 ? (
                <p className="text-muted text-sm">
                    No example posts stored for this topic yet.
                </p>
            ) : (
                <PostCardList
                    posts={samples.map(sampleToPostCard)}
                    sampleNote="A sample of this topic's classified posts, not a complete feed. Highlighted text is the evidence the model quoted."
                />
            )}
        </Modal>
    );
}

export default TopicDivergencePanel;
