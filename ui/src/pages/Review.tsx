import { useState, useEffect, useCallback, useMemo } from 'react';
import { Card, LoadingCard, EmptyState, ErrorState } from '../components/common';
import {
    fetchReviewQueue, submitReview, fetchReviewStats,
} from '../services/api';
import type {
    ReviewQueueItem, ReviewStats, ReviewTaskType,
} from '../types';

const TASK_OPTIONS: Array<{ id: ReviewTaskType; label: string }> = [
    { id: 'sentiment', label: 'Sentiment' },
    { id: 'favorability', label: 'Favorability' },
    { id: 'bot_detection', label: 'Bot Detection' },
    { id: 'claims', label: 'Claim Extraction' },
];

const LABEL_OPTIONS_BY_TASK: Record<ReviewTaskType, string[]> = {
    sentiment: ['POSITIVE', 'NEGATIVE', 'NEUTRAL', 'MIXED'],
    favorability: ['favorable', 'unfavorable', 'neutral', 'mixed'],
    bot_detection: ['human', 'suspicious', 'bot'],
    // Claims aren't a single-label task; reviewer just judges correctness.
    claims: [],
};

const REVIEWER_KEY = 'civic_lens_reviewer_id';

function modelLabel(task: ReviewTaskType, output: Record<string, any>): string {
    switch (task) {
        case 'sentiment':
            return String(output.label ?? output.sentiment_label ?? '—');
        case 'favorability':
            return String(output.overall_gop_stance ?? output.stance ?? '—');
        case 'bot_detection':
            return String(output.label ?? '—');
        case 'claims': {
            const n = Array.isArray(output.claims) ? output.claims.length : 0;
            return `${n} claim${n === 1 ? '' : 's'} extracted`;
        }
        default:
            return '—';
    }
}

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
                No data for {activeTask} yet
            </div>
        );
    }
    const reviewedPct = taskStats.total_outputs
        ? ((taskStats.reviewed / taskStats.total_outputs) * 100).toFixed(1)
        : '0.0';
    return (
        <div
            style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(5, 1fr)',
                gap: 'var(--space-4)',
                padding: 'var(--space-3) var(--space-4)',
                background: 'var(--neutral-50)',
                border: '1px solid var(--neutral-200)',
                borderRadius: 'var(--radius-md)',
            }}
        >
            <Stat label="Total outputs" value={taskStats.total_outputs} />
            <Stat label="Reviewed" value={`${taskStats.reviewed} (${reviewedPct}%)`} />
            <Stat label="Correct" value={taskStats.correct} color="#008a4c" />
            <Stat label="Incorrect" value={taskStats.incorrect} color="#d41e0e" />
            <Stat
                label="Accuracy"
                value={taskStats.accuracy_pct === null ? '—' : `${taskStats.accuracy_pct}%`}
                color={
                    taskStats.accuracy_pct === null ? undefined
                        : taskStats.accuracy_pct >= 95 ? '#008a4c'
                        : taskStats.accuracy_pct >= 80 ? '#b26100'
                        : '#d41e0e'
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

interface ReviewItemCardProps {
    item: ReviewQueueItem;
    reviewerId: string;
    onSubmitted: () => void;
}

function ReviewItemCard({ item, reviewerId, onSubmitted }: ReviewItemCardProps) {
    const labelOptions = LABEL_OPTIONS_BY_TASK[item.task_type];
    const initialLabel = useMemo(() => modelLabel(item.task_type, item.model_output), [item]);

    const [isCorrect, setIsCorrect] = useState<number | null>(null);
    const [humanLabel, setHumanLabel] = useState<string>('');
    const [humanConfidence, setHumanConfidence] = useState<number>(1.0);
    const [isGolden, setIsGolden] = useState<boolean>(false);
    const [notes, setNotes] = useState<string>('');
    const [submitting, setSubmitting] = useState<boolean>(false);
    const [error, setError] = useState<string | null>(null);

    // Reset form whenever a new item loads.
    useEffect(() => {
        setIsCorrect(null);
        setHumanLabel('');
        setHumanConfidence(1.0);
        setIsGolden(false);
        setNotes('');
        setError(null);
    }, [item.ai_output_id]);

    const submit = useCallback(async () => {
        if (isCorrect === null) {
            setError('Mark as correct or incorrect first.');
            return;
        }
        setSubmitting(true);
        setError(null);
        try {
            await submitReview({
                ai_output_id: item.ai_output_id,
                is_correct: isCorrect,
                // If marked incorrect and a label is selected, send it. If marked
                // correct, mirror the model's label so downstream queries don't
                // need to special-case nulls.
                human_label: isCorrect === 1 ? initialLabel : (humanLabel || null),
                human_confidence: humanConfidence,
                is_golden: isGolden,
                reviewer_id: reviewerId || null,
                notes: notes.trim() || null,
            });
            onSubmitted();
        } catch (err: any) {
            setError(err?.message ?? 'Submit failed');
        } finally {
            setSubmitting(false);
        }
    }, [item, isCorrect, humanLabel, humanConfidence, isGolden, notes, reviewerId, initialLabel, onSubmitted]);

    const evidenceSpans: string[] = (item.model_output.evidence_spans
        ?? item.model_output.sentiment_evidence_spans
        ?? []) as string[];

    return (
        <Card title={`Doc #${item.doc_id} · ${item.doc.source_type}`} subtitle={item.doc.title || item.doc.ident}>
            {/* Doc text */}
            <details open style={{ marginBottom: 'var(--space-4)' }}>
                <summary className="eyebrow" style={{ cursor: 'pointer', marginBottom: 'var(--space-2)' }}>
                    Source text {item.doc.text_truncated && '(truncated)'}
                </summary>
                <div
                    style={{
                        padding: 'var(--space-3)',
                        background: 'var(--neutral-50)',
                        borderLeft: '3px solid var(--neutral-300)',
                        fontSize: 'var(--text-sm)',
                        whiteSpace: 'pre-wrap',
                        maxHeight: 240,
                        overflow: 'auto',
                    }}
                >
                    {item.doc.text_preview || '(empty)'}
                </div>
            </details>

            {/* Model output */}
            <div
                style={{
                    padding: 'var(--space-3)',
                    background: '#fbfaf6',
                    border: '1px solid var(--neutral-200)',
                    marginBottom: 'var(--space-4)',
                }}
            >
                <div className="eyebrow" style={{ marginBottom: 'var(--space-2)' }}>
                    Model output · {item.model_id || 'unknown'} · v{item.prompt_version || '?'}
                </div>
                <div className="flex items-baseline gap-3 mb-2">
                    <span style={{ fontSize: 'var(--text-base)', fontWeight: 700 }}>{initialLabel}</span>
                    <span className="num" style={{ color: 'var(--neutral-500)' }}>
                        confidence {item.model_confidence !== null ? item.model_confidence.toFixed(2) : '—'}
                    </span>
                </div>
                {evidenceSpans.length > 0 && (
                    <div className="text-xs" style={{ color: 'var(--neutral-600)' }}>
                        Evidence: {evidenceSpans.map((e, i) => (
                            <span key={i} style={{ marginRight: 8, fontStyle: 'italic' }}>
                                "{e}"
                            </span>
                        ))}
                    </div>
                )}
                {item.task_type === 'claims' && Array.isArray(item.model_output.claims) && (
                    <ul style={{ margin: 0, paddingLeft: 'var(--space-5)', fontSize: 'var(--text-sm)' }}>
                        {(item.model_output.claims as Array<any>).map((c, i) => (
                            <li key={i} className="mb-1">
                                {c.claim} <span className="num" style={{ color: 'var(--neutral-500)' }}>({(c.confidence ?? 0).toFixed(2)})</span>
                            </li>
                        ))}
                    </ul>
                )}
            </div>

            {/* Review form */}
            <div className="flex flex-col gap-3">
                <div className="flex items-center gap-3">
                    <span className="eyebrow">Verdict:</span>
                    <button
                        className={`btn btn-sm ${isCorrect === 1 ? 'btn-primary' : ''}`}
                        onClick={() => setIsCorrect(1)}
                        type="button"
                    >
                        Correct
                    </button>
                    <button
                        className={`btn btn-sm ${isCorrect === 0 ? 'btn-primary' : ''}`}
                        onClick={() => setIsCorrect(0)}
                        type="button"
                    >
                        Incorrect
                    </button>
                </div>

                {isCorrect === 0 && labelOptions.length > 0 && (
                    <div className="flex items-center gap-3">
                        <span className="eyebrow">Correct label:</span>
                        <select
                            value={humanLabel}
                            onChange={(e) => setHumanLabel(e.target.value)}
                            style={{ padding: '4px 8px', border: '1px solid var(--neutral-300)' }}
                        >
                            <option value="">— pick one —</option>
                            {labelOptions.map((l) => (
                                <option key={l} value={l}>{l}</option>
                            ))}
                        </select>
                    </div>
                )}

                <div className="flex items-center gap-3">
                    <span className="eyebrow">Your confidence:</span>
                    <input
                        type="range"
                        min={0}
                        max={1}
                        step={0.05}
                        value={humanConfidence}
                        onChange={(e) => setHumanConfidence(parseFloat(e.target.value))}
                        style={{ flex: 1, maxWidth: 220 }}
                    />
                    <span className="num">{humanConfidence.toFixed(2)}</span>
                </div>

                <label className="flex items-center gap-2">
                    <input
                        type="checkbox"
                        checked={isGolden}
                        onChange={(e) => setIsGolden(e.target.checked)}
                    />
                    <span className="text-sm">
                        <strong>Add to golden set</strong> — use this row as ground truth for accuracy benchmarks
                    </span>
                </label>

                <textarea
                    placeholder="Notes (optional) — why was this correct/incorrect, edge case, etc."
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                    rows={2}
                    style={{
                        padding: '6px 8px',
                        border: '1px solid var(--neutral-300)',
                        fontFamily: 'inherit',
                        fontSize: 'var(--text-sm)',
                        resize: 'vertical',
                    }}
                />

                {error && (
                    <div style={{ color: 'var(--semantic-negative)', fontSize: 'var(--text-sm)' }}>
                        {error}
                    </div>
                )}

                <div className="flex items-center justify-between">
                    <span className="eyebrow" style={{ color: 'var(--neutral-500)' }}>
                        Reviewer: {reviewerId || '(anonymous)'}
                    </span>
                    <button
                        className="btn btn-primary"
                        onClick={submit}
                        disabled={submitting || isCorrect === null}
                        type="button"
                    >
                        {submitting ? 'Saving…' : 'Submit & next'}
                    </button>
                </div>
            </div>
        </Card>
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

    const loadQueue = useCallback(async () => {
        setLoading(true);
        setError(null);
        try {
            const [items, statsResult] = await Promise.all([
                fetchReviewQueue({ task, confidenceMax: confidenceMax ?? undefined, limit: 20 }),
                fetchReviewStats(),
            ]);
            setQueue(items);
            setStats(statsResult);
        } catch (err: any) {
            setError(err?.message ?? 'Failed to load review queue.');
        } finally {
            setLoading(false);
        }
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
        setQueue((prev) => prev.slice(1));
    }, []);

    const current = queue[0] ?? null;

    return (
        <div className="flex flex-col gap-4">
            {/* Disclaimer */}
            <div
                style={{
                    padding: 'var(--space-3) var(--space-4)',
                    background: '#eff6ff',
                    border: '1px solid #93c5fd',
                    borderRadius: 'var(--radius-md)',
                    fontSize: 'var(--text-xs)',
                    color: '#1e3a8a',
                }}
            >
                <strong>Reviews populate <code>ai_output_evals</code></strong> — marking rows as <em>golden</em> builds the
                benchmark set used to compute calibrated accuracy. Lowest-confidence model outputs are surfaced first because
                those are where human review yields the most signal. This UI will be admin-gated once auth lands; for now,
                set a reviewer ID below for attribution.
            </div>

            {/* Controls */}
            <Card>
                <div className="flex items-center gap-4 flex-wrap">
                    <div className="flex items-center gap-2">
                        <span className="eyebrow">Task:</span>
                        <select
                            value={task}
                            onChange={(e) => setTask(e.target.value as ReviewTaskType)}
                            style={{ padding: '4px 8px', border: '1px solid var(--neutral-300)' }}
                        >
                            {TASK_OPTIONS.map((t) => (
                                <option key={t.id} value={t.id}>{t.label}</option>
                            ))}
                        </select>
                    </div>
                    <div className="flex items-center gap-2">
                        <span className="eyebrow">Only conf ≤</span>
                        <select
                            value={confidenceMax === null ? '' : String(confidenceMax)}
                            onChange={(e) => setConfidenceMax(e.target.value === '' ? null : parseFloat(e.target.value))}
                            style={{ padding: '4px 8px', border: '1px solid var(--neutral-300)' }}
                        >
                            <option value="">all</option>
                            <option value="0.5">0.50</option>
                            <option value="0.7">0.70</option>
                            <option value="0.9">0.90</option>
                        </select>
                    </div>
                    <div className="flex items-center gap-2">
                        <span className="eyebrow">Reviewer ID:</span>
                        <input
                            value={reviewerId}
                            onChange={(e) => setReviewerId(e.target.value)}
                            placeholder="e.g. kobe"
                            style={{ padding: '4px 8px', border: '1px solid var(--neutral-300)', width: 140 }}
                        />
                    </div>
                    <div style={{ marginLeft: 'auto' }}>
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
            {loading && !error && <LoadingCard />}
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
