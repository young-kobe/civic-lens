import { useState, useEffect, useCallback } from 'react';
import { Card, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { COLORS } from '../theme';
import { formatPct } from '../services/format';
import {
    fetchReviewQueue, fetchReviewStats,
} from '../services/api';
import ReviewItemCard from './review/ReviewItemCard';
import type {
    ReviewQueueItem, ReviewStats, ReviewTaskType,
} from '../types';

const TASK_OPTIONS: Array<{ id: ReviewTaskType; label: string }> = [
    { id: 'sentiment', label: 'Sentiment' },
    { id: 'favorability', label: 'Favorability' },
    { id: 'bot_detection', label: 'Bot Detection' },
    { id: 'claims', label: 'Claim Extraction' },
    { id: 'propaganda', label: 'Propaganda' },
];

const REVIEWER_KEY = 'civic_lens_reviewer_id';

interface StatsBarProps {
    stats: ReviewStats | null;
    activeTask: ReviewTaskType;
}

function StatsBar({ stats, activeTask }: StatsBarProps) {
    if (!stats) return null;
    const taskStats = stats.per_task.find((s) => s.task_type === activeTask);
    if (!taskStats) {
        return (
            <div className="eyebrow" style={{ color: 'var(--neutral-500)' }}>
                No data for {activeTask.replace(/_/g, ' ')} yet
            </div>
        );
    }
    const reviewedPct = taskStats.total_outputs
        ? (taskStats.reviewed / taskStats.total_outputs) * 100
        : 0;
    return (
        <div
            className="review-stats-grid"
            style={{
                padding: 'var(--space-3) var(--space-4)',
                background: 'var(--neutral-50)',
                border: '1px solid var(--neutral-200)',
                borderRadius: 'var(--radius-md)',
            }}
        >
            <Stat label="Total outputs" value={taskStats.total_outputs} />
            <Stat label="Reviewed" value={`${taskStats.reviewed} (${formatPct(reviewedPct, { decimals: 0 })})`} />
            <Stat label="Correct" value={taskStats.correct} color="var(--semantic-positive)" />
            <Stat label="Incorrect" value={taskStats.incorrect} color="var(--semantic-negative)" />
            <Stat
                label="Accuracy"
                value={formatPct(taskStats.accuracy_pct, { decimals: 0 })}
                color={
                    taskStats.accuracy_pct === null ? undefined
                        : taskStats.accuracy_pct >= 95 ? COLORS.positive
                        : taskStats.accuracy_pct >= 80 ? COLORS.warning
                        : COLORS.negative
                }
            />
        </div>
    );
}

function Stat({ label, value, color }: { label: string; value: string | number; color?: string }) {
    return (
        <div>
            <div className="eyebrow" style={{ color: 'var(--neutral-500)' }}>{label}</div>
            <div className="num" style={{ fontSize: 'var(--text-lg)', fontWeight: 700, color: color ?? 'var(--neutral-900)' }}>
                {value}
            </div>
        </div>
    );
}


function Review() {
    const [task, setTask] = useState<ReviewTaskType>('sentiment');
    const [confidenceMax, setConfidenceMax] = useState<number | null>(null);
    const [reviewerId, setReviewerId] = useState<string>(() => localStorage.getItem(REVIEWER_KEY) || '');
    const [queue, setQueue] = useState<ReviewQueueItem[]>([]);
    const [stats, setStats] = useState<ReviewStats | null>(null);
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    // Ids the reviewer skipped this session. A background refetch replaces
    // the queue wholesale, so without this the same skipped rows resurface;
    // we filter them out of every (re)fetched page.
    const [skippedIds, setSkippedIds] = useState<Set<number>>(() => new Set());

    const loadQueue = useCallback(async () => {
        setLoading(true);
        setError(null);
        try {
            const [items, statsResult] = await Promise.all([
                fetchReviewQueue({ task, confidenceMax: confidenceMax ?? undefined, limit: 20 }),
                fetchReviewStats(),
            ]);
            setQueue(items.filter((it) => !skippedIds.has(it.ai_output_id)));
            setStats(statsResult);
        } catch (err: any) {
            setError(err?.message ?? 'Failed to load review queue.');
        } finally {
            setLoading(false);
        }
    }, [task, confidenceMax, skippedIds]);

    // Switching task/confidence starts a fresh queue — clear the skip set so
    // a different task's rows aren't hidden by ids skipped under the old one.
    useEffect(() => {
        setSkippedIds(new Set());
    }, [task, confidenceMax]);

    useEffect(() => {
        loadQueue();
    }, [loadQueue]);

    useEffect(() => {
        if (reviewerId) localStorage.setItem(REVIEWER_KEY, reviewerId);
    }, [reviewerId]);

    const advanceQueue = useCallback(() => {
        setQueue((prev) => {
            const next = prev.slice(1);
            if (next.length < 5) {
                // Background-fetch the next page so the reviewer never hits an empty card.
                loadQueue();
            }
            return next;
        });
        // Refresh stats after each submit.
        fetchReviewStats().then(setStats).catch(() => undefined);
    }, [loadQueue]);

    const skip = useCallback(() => {
        setQueue((prev) => {
            const [head, ...rest] = prev;
            if (head) setSkippedIds((s) => new Set(s).add(head.ai_output_id));
            return rest;
        });
    }, []);

    const current = queue[0] ?? null;

    return (
        <div className="flex flex-col gap-4">
            {/* Disclaimer */}
            <div
                style={{
                    padding: 'var(--space-3) var(--space-4)',
                    background: COLORS.adminBannerBg,
                    border: `1px solid ${COLORS.adminBannerBorder}`,
                    borderRadius: 'var(--radius-md)',
                    fontSize: 'var(--text-xs)',
                    color: COLORS.adminBannerText,
                }}
            >
                <strong>Reviews populate <code>ai_output_evals</code></strong>. Marking rows as <em>golden</em> builds the
                benchmark set used to compute calibrated accuracy. Lowest-confidence model outputs are surfaced first because
                those are where human review yields the most signal. Admin-gated via the <code>X-Admin-Token</code> header;
                set a reviewer ID below for attribution.
            </div>

            {/* Controls — layout + mobile sizing live in `.review-controls`
                (index.css). Inputs shrink to full-width on phones and bump
                padding so selects/text fields hit the 44px touch target. */}
            <Card>
                <div className="review-controls">
                    <div className="review-controls-field">
                        <span className="eyebrow">Task:</span>
                        <select
                            value={task}
                            onChange={(e) => setTask(e.target.value as ReviewTaskType)}
                            className="review-controls-input"
                        >
                            {TASK_OPTIONS.map((t) => (
                                <option key={t.id} value={t.id}>{t.label}</option>
                            ))}
                        </select>
                    </div>
                    <div className="review-controls-field">
                        <span className="eyebrow">Only conf ≤</span>
                        <select
                            value={confidenceMax === null ? '' : String(confidenceMax)}
                            onChange={(e) => setConfidenceMax(e.target.value === '' ? null : parseFloat(e.target.value))}
                            className="review-controls-input"
                        >
                            <option value="">all</option>
                            <option value="0.5">0.50</option>
                            <option value="0.7">0.70</option>
                            <option value="0.9">0.90</option>
                        </select>
                    </div>
                    <div className="review-controls-field">
                        <span className="eyebrow">Reviewer ID:</span>
                        <input
                            value={reviewerId}
                            onChange={(e) => setReviewerId(e.target.value)}
                            placeholder="e.g. kobe"
                            className="review-controls-input review-controls-input-text"
                        />
                    </div>
                    <div className="review-controls-actions">
                        <button className="btn btn-sm" onClick={skip} disabled={!current} type="button">
                            Skip this one
                        </button>
                    </div>
                </div>
            </Card>

            {/* Stats */}
            <StatsBar stats={stats} activeTask={task} />

            {/* Queue */}
            {error && <ErrorState message={error} onRetry={loadQueue} />}
            {loading && !error && !current && <LoadingCard />}
            {!loading && !error && !current && (
                <EmptyState
                    title="Queue is empty"
                    description={`No unreviewed ${task} outputs match the current filter. Try widening the confidence cap or switching tasks.`}
                />
            )}
            {current && (
                <ReviewItemCard
                    item={current}
                    reviewerId={reviewerId}
                    onSubmitted={advanceQueue}
                />
            )}
        </div>
    );
}

export default Review;
