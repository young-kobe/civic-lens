import { useCallback, useEffect, useState } from 'react';
import { AdmissionBadge, Card } from '../../components/common';
import { submitReview } from '../../services/api';
import { sourceLabel } from '../../services/format';
import { COLORS } from '../../theme';
import type { ReviewQueueItem } from '../../types';

// --------------------------------------------------------------------------- //
//  Phase 10 adaptation note: the pre-redesign per-task label pickers          //
//  (sentiment/favorability/bot_detection-specific dropdowns reading           //
//  model_output.label) assumed a shape the Phase 9 review contract doesn't    //
//  guarantee -- `raw_response` is an opaque per-task JSON blob, and the       //
//  verdict vocabulary changed to correct/incorrect/uncertain (see             //
//  analysis/src/api/routers/review.py). This card renders raw_response as     //
//  formatted JSON and asks for a free-text expected_label only when the       //
//  reviewer marks a row golden -- honest given the task-shape is no longer    //
//  known client-side, rather than guessing at per-task fields.                //
// --------------------------------------------------------------------------- //

interface ReviewItemCardProps {
    item: ReviewQueueItem;
    reviewerId: string;
    onSubmitted: () => void;
}

type Verdict = 'correct' | 'incorrect' | 'uncertain';

export default function ReviewItemCard({ item, reviewerId, onSubmitted }: ReviewItemCardProps) {
    const [verdict, setVerdict] = useState<Verdict | null>(null);
    const [isGolden, setIsGolden] = useState(false);
    const [expectedLabel, setExpectedLabel] = useState('');
    const [notes, setNotes] = useState('');
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        setVerdict(null);
        setIsGolden(false);
        setExpectedLabel('');
        setNotes('');
        setError(null);
    }, [item.run_id]);

    const submit = useCallback(async () => {
        if (verdict === null) {
            setError('Pick a verdict first.');
            return;
        }
        if (isGolden && !expectedLabel.trim()) {
            setError('Marking a row golden requires an expected label.');
            return;
        }
        setSubmitting(true);
        setError(null);
        try {
            await submitReview({
                run_id: item.run_id,
                verdict,
                reviewer_id: reviewerId || null,
                notes: notes.trim() || null,
                is_golden: isGolden,
                expected_label: isGolden ? expectedLabel.trim() : null,
            });
            onSubmitted();
        } catch (err: any) {
            setError(err?.message ?? 'Submit failed');
        } finally {
            setSubmitting(false);
        }
    }, [item.run_id, verdict, isGolden, expectedLabel, notes, reviewerId, onSubmitted]);

    return (
        <Card
            title={`Doc #${item.doc_id} · run #${item.run_id}`}
            subtitle={item.doc.title || sourceLabel(item.doc.source_type, null)}
            headerActions={(
                <>
                    <AdmissionBadge admissionClass={item.doc.admission_class} />
                    <a href={item.doc.source_url} target="_blank" rel="noreferrer" className="example-row-link" style={{ fontSize: 'var(--text-xs)' }}>
                        View original
                    </a>
                </>
            )}
        >
            <details open style={{ marginBottom: 'var(--space-4)' }}>
                <summary className="eyebrow" style={{ cursor: 'pointer', marginBottom: 'var(--space-2)' }}>
                    Source text {item.doc.text_truncated && '(truncated)'}
                </summary>
                <div
                    style={{
                        padding: 'var(--space-3)', background: 'var(--neutral-50)',
                        borderLeft: '3px solid var(--neutral-300)', fontSize: 'var(--text-sm)',
                        whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', wordBreak: 'break-word',
                        maxHeight: 240, overflow: 'auto',
                    }}
                >
                    {item.doc.text_preview || '(empty)'}
                </div>
            </details>

            <div
                style={{
                    padding: 'var(--space-3)', background: COLORS.adminCardBg,
                    border: '1px solid var(--neutral-200)', marginBottom: 'var(--space-4)',
                }}
            >
                <div className="eyebrow" style={{ marginBottom: 'var(--space-2)' }}>
                    Model output · {item.model_id} · v{item.prompt_version ?? '?'}
                    {' · confidence '}{item.confidence != null ? item.confidence.toFixed(2) : '—'}
                </div>
                <pre style={{ margin: 0, fontSize: 'var(--text-xs)', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                    {JSON.stringify(item.raw_response, null, 2)}
                </pre>
            </div>

            <div className="flex flex-col gap-3">
                <div className="flex items-center gap-3">
                    <span className="eyebrow">Verdict:</span>
                    {(['correct', 'incorrect', 'uncertain'] as Verdict[]).map((v) => (
                        <button
                            key={v}
                            className={`btn btn-sm ${verdict === v ? 'btn-primary' : ''}`}
                            onClick={() => setVerdict(v)}
                            type="button"
                        >
                            {v}
                        </button>
                    ))}
                </div>

                <label className="flex items-center gap-2">
                    <input type="checkbox" checked={isGolden} onChange={(e) => setIsGolden(e.target.checked)} />
                    <span className="text-sm">
                        <strong>Add to golden set</strong> — requires an expected label below
                    </span>
                </label>

                {isGolden && (
                    <input
                        placeholder="Expected label (ground truth for this run)"
                        value={expectedLabel}
                        onChange={(e) => setExpectedLabel(e.target.value)}
                        style={{ padding: '6px 8px', border: '1px solid var(--neutral-300)', fontSize: 'var(--text-sm)' }}
                    />
                )}

                <textarea
                    placeholder="Notes (optional)"
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                    rows={2}
                    style={{ padding: '6px 8px', border: '1px solid var(--neutral-300)', fontFamily: 'inherit', fontSize: 'var(--text-sm)', resize: 'vertical' }}
                />

                {error && <div style={{ color: COLORS.negative, fontSize: 'var(--text-sm)' }}>{error}</div>}

                <div className="flex items-center justify-between">
                    <span className="eyebrow" style={{ color: 'var(--neutral-500)' }}>
                        Reviewer: {reviewerId || '(anonymous)'}
                    </span>
                    <button className="btn btn-primary" onClick={submit} disabled={submitting || verdict === null} type="button">
                        {submitting ? 'Saving…' : 'Submit & next'}
                    </button>
                </div>
            </div>
        </Card>
    );
}
