import { useState } from 'react';
import { COLORS } from '../../theme';
import { formatPct } from '../../services/format';
import type { ClassificationSample } from '../../types';

interface ClassificationSampleCardProps {
    sample: ClassificationSample;
    /** Label → badge bg/text palette; defaults mirror the neutral scheme. */
    badgeStyle: { bg: string; text: string };
}

const MAX_EVIDENCE_DISPLAY = 5;

/** Reader-facing source label — the wire carries raw enums ("x_post") that
 *  shouldn't surface to end users (R-9). */
function friendlySourceType(sourceType: string): string {
    if (sourceType === 'x_post') return 'X';
    if (sourceType === 'news') return 'News';
    if (sourceType.startsWith('reddit')) return 'Reddit';
    return sourceType;
}

function meaningfulEvidence(spans: string[]): string[] {
    const seen = new Set<string>();
    const result: string[] = [];
    for (const span of spans || []) {
        const trimmed = span.trim();
        if (!trimmed || trimmed.length < 4) continue;
        // Filter LLM schema-example placeholder strings.
        if (/^exact quote/i.test(trimmed)) continue;
        if (/^<.*>$/.test(trimmed)) continue;
        const key = trimmed.toLowerCase();
        if (seen.has(key)) continue;
        seen.add(key);
        result.push(trimmed);
        if (result.length >= MAX_EVIDENCE_DISPLAY) break;
    }
    return result;
}

export function ClassificationSampleCard({ sample, badgeStyle }: ClassificationSampleCardProps) {
    const [showFull, setShowFull] = useState(false);
    const spans = meaningfulEvidence(sample.evidence_spans || []);

    const confPct = sample.confidence * 100;
    const confColor = confPct >= 80 ? COLORS.positive
        : confPct >= 50 ? COLORS.warning
        : COLORS.negative;
    const confLabel = confPct >= 80 ? 'High' : confPct >= 50 ? 'Medium' : 'Low';

    return (
        <div style={{
            background: 'var(--bg-card)', borderRadius: 'var(--radius-sm)',
            padding: '10px 12px', border: '1px solid var(--neutral-200)',
        }}>
            {/* Header row */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', marginBottom: '8px', flexWrap: 'wrap' }}>
                <span style={{
                    fontSize: '10px', fontWeight: 700, padding: '2px 6px',
                    borderRadius: '2px', background: badgeStyle.bg, color: badgeStyle.text,
                    textTransform: 'uppercase', letterSpacing: '0.06em',
                }}>
                    {sample.label}
                </span>
                <span
                    title={`Model confidence: ${formatPct(confPct)} (${confLabel})`}
                    role="progressbar"
                    aria-valuenow={Math.round(confPct)}
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-label={`Model confidence ${confLabel.toLowerCase()}, ${formatPct(confPct, { decimals: 0 })}`}
                    style={{
                        display: 'inline-flex', alignItems: 'center', gap: '4px',
                        fontSize: '10px', color: 'var(--neutral-500)',
                        fontFamily: 'var(--font-mono)', fontVariantNumeric: 'tabular-nums',
                    }}
                >
                    <span style={{ fontSize: '9px', opacity: 0.7, textTransform: 'uppercase', letterSpacing: '0.08em' }}>CONF</span>
                    <span style={{
                        display: 'inline-block', width: '36px', height: '3px',
                        borderRadius: '1px', background: 'var(--neutral-200)',
                        overflow: 'hidden', position: 'relative',
                    }}>
                        <span style={{
                            position: 'absolute', left: 0, top: 0, bottom: 0,
                            width: `${confPct}%`, background: confColor,
                            borderRadius: '1px', transition: 'width 0.3s ease',
                        }} />
                    </span>
                    <span style={{ color: confColor, fontWeight: 700 }}>
                        {formatPct(confPct, { decimals: 0 })}
                    </span>
                </span>
                {sample.sarcasm_detected && (
                    <span style={{
                        fontSize: '9px', fontWeight: 700, padding: '2px 5px',
                        borderRadius: '2px', background: COLORS.warningLight,
                        color: COLORS.warning, letterSpacing: '0.08em',
                    }}>
                        SARCASM
                    </span>
                )}
                <span style={{
                    fontSize: '10px', color: 'var(--neutral-500)', marginLeft: 'auto',
                    display: 'flex', alignItems: 'center', gap: '6px',
                    fontFamily: 'var(--font-mono)',
                }}>
                    {(sample.source_name || sample.date) && (
                        <span>
                            {[sample.source_name, sample.date].filter(Boolean).join(' · ')}
                        </span>
                    )}
                    <span style={{
                        padding: '1px 5px', borderRadius: '2px',
                        background: 'var(--neutral-100)', textTransform: 'uppercase', letterSpacing: '0.06em',
                        fontWeight: 600,
                    }}>
                        {friendlySourceType(sample.source_type)}
                    </span>
                </span>
            </div>

            {/* Title */}
            {sample.title && (
                <div style={{ fontSize: '12px', fontWeight: 500, marginBottom: '4px', lineHeight: 1.3 }}>
                    {sample.title.length > 100 ? sample.title.slice(0, 100) + '...' : sample.title}
                </div>
            )}

            {/* Reasoning */}
            <div style={{ fontSize: '12px', color: 'var(--neutral-700)', lineHeight: 1.5 }}>
                {showFull || sample.reasoning.length <= 150
                    ? sample.reasoning
                    : sample.reasoning.slice(0, 150) + '...'}
                {sample.reasoning.length > 150 && (
                    <button
                        onClick={() => setShowFull(!showFull)}
                        style={{
                            background: 'none', border: 'none', color: COLORS.accent,
                            cursor: 'pointer', fontSize: '11px', fontWeight: 600,
                            marginLeft: '4px', padding: 0, textTransform: 'uppercase',
                            letterSpacing: '0.06em',
                        }}
                    >
                        {showFull ? 'Show less' : 'Show more'}
                    </button>
                )}
            </div>

            {/* Evidence spans */}
            {spans.length > 0 && (
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px', marginTop: '8px' }}>
                    {spans.map((span, i) => (
                        <span
                            key={i}
                            title={span}
                            style={{
                                fontSize: '10px', padding: '2px 6px', borderRadius: '2px',
                                background: 'var(--neutral-100)',
                                color: 'var(--neutral-700)', fontStyle: 'italic',
                                maxWidth: '280px', overflow: 'hidden', textOverflow: 'ellipsis',
                                whiteSpace: 'nowrap', cursor: 'help',
                                borderLeft: `2px solid ${badgeStyle.text}`,
                            }}
                        >
                            &quot;{span}&quot;
                        </span>
                    ))}
                </div>
            )}

            {/* Source Text */}
            {sample.full_text && (
                <div style={{
                    marginTop: '10px', padding: '8px 10px', background: 'var(--bg-inset)',
                    borderRadius: '2px', borderLeft: '2px solid var(--neutral-300)',
                    fontSize: '11px', color: 'var(--neutral-700)',
                }}>
                    <div className="eyebrow" style={{
                        marginBottom: '6px',
                        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                    }}>
                        <span>Source Text</span>
                        {sample.url && (
                            <a href={sample.url} target="_blank" rel="noreferrer" style={{
                                color: COLORS.accent, textDecoration: 'none',
                                textTransform: 'none', fontSize: '11px', letterSpacing: 'normal',
                            }}>
                                View Original ↗
                            </a>
                        )}
                    </div>
                    <div style={{ lineHeight: 1.5, whiteSpace: 'pre-wrap' }}>
                        {showFull || sample.full_text.length <= 150
                            ? sample.full_text
                            : sample.full_text.slice(0, 150) + '...'}
                    </div>
                </div>
            )}
        </div>
    );
}
