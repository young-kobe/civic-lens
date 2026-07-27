import { useCallback, useEffect, useState } from 'react';
import { AdmissionBadge, Card } from '../../components/common';
import { submitReview } from '../../services/api';
import { sourceLabel } from '../../services/format';
import { COLORS } from '../../theme';
import type { ReviewQueueItem } from '../../types';

// --------------------------------------------------------------------------- //
//  Restores the pre-cutover per-task-type evidence rendering (the old        //
//  `modelLabel`/label-options blocks) onto the current review contract.       //
//  `raw_response` is typed as an opaque JSON blob (ReviewQueueItem in         //
//  types.ts carries no per-task narrowing, and the queue endpoint's task     //
//  filter -- see Review.tsx -- isn't threaded down to this card either), so   //
//  the per-task block below is picked by DUCK-TYPING raw_response's shape    //
//  rather than switching on a task name. Its actual shape still matches the  //
//  LLM structured-output schemas in analysis/src/llm/schemas.py for the      //
//  tasks that run through an LLM -- text (sentiment_label / evidence /       //
//  sarcasm), targets (per-target stance), propaganda (techniques + score),   //
//  bot (label / indicators), and claims (claim / confidence). `citations`    //
//  and `account_tier` are deterministic, non-LLM tasks with no documented    //
//  client-facing schema, so an unrecognized shape keeps the generic JSON     //
//  fallback the Phase 10 rewrite introduced. The verdict vocabulary          //
//  (correct/incorrect/uncertain) and golden-set flow are the CURRENT         //
//  contract (see analysis/src/api/routers/review.py) -- restoring the old    //
//  is_correct/human_label/human_confidence submission shape would not match  //
//  the live API and is not restored.                                        //
// --------------------------------------------------------------------------- //

interface TargetStance {
    target: string;
    topic: string;
    stance: string;
    confidence: number;
    evidence_spans?: string[];
}

interface PropagandaTechnique {
    technique: string;
    confidence: number;
    evidence_span: string;
}

interface Claim {
    claim: string;
    confidence: number;
    evidence_span: string;
}

/** Task-specific summary of `raw_response`, restoring the old card's model-
 *  output block. Returns null for tasks with no documented client shape
 *  (citations, account_tier) or a response that doesn't match any known
 *  schema -- callers fall back to the generic JSON dump in that case. */
function TaskOutput({ output }: { output: Record<string, any> }) {
    if (typeof output.sentiment_label === 'string') {
        const spans = (output.sentiment_evidence_spans ?? []) as string[];
        return (
            <>
                <div className="flex items-baseline gap-3 mb-2">
                    <span style={{ fontSize: 'var(--text-base)', fontWeight: 700 }}>{output.sentiment_label}</span>
                    <span className="num" style={{ color: 'var(--neutral-500)' }}>
                        confidence {typeof output.sentiment_confidence === 'number' ? output.sentiment_confidence.toFixed(2) : '—'}
                    </span>
                    {output.sarcasm_detected && <span className="badge badge-neutral">sarcasm flagged</span>}
                </div>
                {spans.length > 0 && <EvidenceLine spans={spans} />}
            </>
        );
    }

    if (Array.isArray(output.targets)) {
        const targets = output.targets as TargetStance[];
        return (
            <ul style={{ margin: 0, paddingLeft: 'var(--space-5)', fontSize: 'var(--text-sm)' }}>
                {targets.map((t, i) => (
                    <li key={i} className="mb-1">
                        about <strong>{t.target}</strong> ({t.topic}) · {t.stance}{' '}
                        <span className="num" style={{ color: 'var(--neutral-500)' }}>({t.confidence.toFixed(2)})</span>
                        {t.evidence_spans && t.evidence_spans.length > 0 && <EvidenceLine spans={t.evidence_spans} />}
                    </li>
                ))}
            </ul>
        );
    }

    if (Array.isArray(output.techniques)) {
        const techniques = output.techniques as PropagandaTechnique[];
        return (
            <>
                <div className="flex items-baseline gap-3 mb-2">
                    <span className="num" style={{ color: 'var(--neutral-500)' }}>
                        overall score {typeof output.overall_propaganda_score === 'number' ? output.overall_propaganda_score.toFixed(2) : '—'}
                    </span>
                </div>
                {techniques.length === 0 ? (
                    <p className="text-sm text-muted">No techniques flagged.</p>
                ) : (
                    <ul style={{ margin: 0, paddingLeft: 'var(--space-5)', fontSize: 'var(--text-sm)' }}>
                        {techniques.map((t, i) => (
                            <li key={i} className="mb-1">
                                {t.technique} <span className="num" style={{ color: 'var(--neutral-500)' }}>({t.confidence.toFixed(2)})</span>
                                {' — '}<em>"{t.evidence_span}"</em>
                            </li>
                        ))}
                    </ul>
                )}
            </>
        );
    }

    if (typeof output.label === 'string') {
        const indicators = (output.indicators ?? []) as string[];
        return (
            <>
                <div className="flex items-baseline gap-3 mb-2">
                    <span style={{ fontSize: 'var(--text-base)', fontWeight: 700 }}>{output.label}</span>
                    <span className="num" style={{ color: 'var(--neutral-500)' }}>
                        confidence {typeof output.confidence === 'number' ? output.confidence.toFixed(2) : '—'}
                    </span>
                </div>
                {indicators.length > 0 && (
                    <ul style={{ margin: 0, paddingLeft: 'var(--space-5)', fontSize: 'var(--text-sm)' }}>
                        {indicators.map((ind, i) => <li key={i}>{ind}</li>)}
                    </ul>
                )}
            </>
        );
    }

    if (Array.isArray(output.claims)) {
        const claims = output.claims as Claim[];
        return claims.length === 0 ? (
            <p className="text-sm text-muted">No claims extracted.</p>
        ) : (
            <ul style={{ margin: 0, paddingLeft: 'var(--space-5)', fontSize: 'var(--text-sm)' }}>
                {claims.map((c, i) => (
                    <li key={i} className="mb-1">
                        {c.claim} <span className="num" style={{ color: 'var(--neutral-500)' }}>({c.confidence.toFixed(2)})</span>
                        {' — '}<em>"{c.evidence_span}"</em>
                    </li>
                ))}
            </ul>
        );
    }

    return (
        <pre style={{ margin: 0, fontSize: 'var(--text-xs)', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
            {JSON.stringify(output, null, 2)}
        </pre>
    );
}

function EvidenceLine({ spans }: { spans: string[] }) {
    return (
        <div className="text-xs" style={{ color: 'var(--neutral-600)' }}>
            Evidence: {spans.map((e, i) => (
                <span key={i} style={{ marginRight: 8, fontStyle: 'italic' }}>"{e}"</span>
            ))}
        </div>
    );
}

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
                <TaskOutput output={item.raw_response} />
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
