import { useState, useEffect, useCallback, useMemo } from 'react';
import { Card } from '../../components/common';
import { submitReview } from '../../services/api';
import type { ReviewQueueItem, ReviewTaskType } from '../../types';

const LABEL_OPTIONS_BY_TASK: Record<ReviewTaskType, string[]> = {
    sentiment: ['POSITIVE', 'NEGATIVE', 'NEUTRAL', 'MIXED'],
    favorability: ['favorable', 'unfavorable', 'neutral', 'mixed'],
    bot_detection: ['human', 'suspicious', 'bot'],
    // Multi-technique tasks — reviewer judges correctness overall, no single label pick.
    claims: [],
    propaganda: [],
};

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
        case 'propaganda': {
            const n = Array.isArray(output.techniques) ? output.techniques.length : 0;
            const score = typeof output.overall_propaganda_score === 'number'
                ? output.overall_propaganda_score.toFixed(2) : '—';
            return `${n} technique${n === 1 ? '' : 's'} (score ${score})`;
        }
        default:
            return '—';
    }
}

interface ReviewItemCardProps {
    item: ReviewQueueItem;
    reviewerId: string;
    onSubmitted: () => void;
}

export default function ReviewItemCard({ item, reviewerId, onSubmitted }: ReviewItemCardProps) {
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
                        aria-label="Your confidence in the review"
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
                        <strong>Add to golden set</strong>: use this row as ground truth for accuracy benchmarks
                    </span>
                </label>

                <textarea
                    placeholder="Notes (optional): why was this correct/incorrect, edge case, etc."
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
