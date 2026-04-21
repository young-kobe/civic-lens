import { useState } from 'react';
import { Modal } from '../../components/common';
import { SEMANTIC_COLORS } from '../../theme';
import type { ClassificationSample, SentimentBreakdown } from '../../types';
import { ClassificationSampleCard } from './ClassificationSampleCard';
import { MiniDonut } from './MiniDonut';

interface TopicRowProps {
    item: SentimentBreakdown;
    /** Label → badge bg/text palette used by expanded sample cards. */
    labelBadgeStyles: Record<string, { bg: string; text: string }>;
}

export function TopicRow({ item, labelBadgeStyles }: TopicRowProps) {
    const [modalOpen, setModalOpen] = useState(false);
    const total = item.positive + item.negative + item.neutral || 1;
    const netScore = ((item.positive - item.negative) / total * 100);
    const netColor = netScore > 5 ? SEMANTIC_COLORS.positive
        : netScore < -5 ? SEMANTIC_COLORS.negative
        : SEMANTIC_COLORS.neutral;
    const hasSamples = (item.classificationSamples?.length ?? 0) > 0;

    return (
        <div
            className="topic-row"
            style={{
                background: 'var(--bg-card)', borderRadius: 'var(--radius-sm)',
                border: '1px solid var(--neutral-200)',
            }}
        >
            <button
                className="topic-row-button"
                onClick={() => hasSamples && setModalOpen(true)}
                aria-haspopup={hasSamples ? 'dialog' : undefined}
                id={`topic-row-${item.topic?.replace(/\s/g, '-').toLowerCase()}`}
            >
                <MiniDonut
                    positive={item.positive}
                    negative={item.negative}
                    neutral={item.neutral}
                    size={44}
                />

                <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                        <span style={{ fontWeight: 700, fontSize: '13px', textTransform: 'uppercase', letterSpacing: '0.03em' }}>{item.topic}</span>
                        <span className="num" style={{
                            fontSize: '11px', fontWeight: 700, padding: '1px 6px',
                            borderRadius: '2px', color: netColor,
                            background: netColor === SEMANTIC_COLORS.positive ? 'var(--semantic-positive-light)'
                                : netColor === SEMANTIC_COLORS.negative ? 'var(--semantic-negative-light)'
                                : 'var(--neutral-100)',
                        }}>
                            {netScore >= 0 ? '+' : ''}{netScore.toFixed(1)}%
                        </span>
                        {(item.sarcasm_rate ?? 0) > 0 && (
                            <span className="badge badge-warning num">
                                {item.sarcasm_rate?.toFixed(0)}% SARC
                            </span>
                        )}
                    </div>
                    <div style={{ display: 'flex', gap: '14px', fontSize: '11px', color: 'var(--neutral-500)', fontFamily: 'var(--font-mono)', fontVariantNumeric: 'tabular-nums' }}>
                        <span>{item.volume.toLocaleString()} docs</span>
                        <span style={{ color: SEMANTIC_COLORS.positive }}>
                            +{((item.positive / total) * 100).toFixed(0)}% pos
                        </span>
                        <span style={{ color: SEMANTIC_COLORS.negative }}>
                            -{((item.negative / total) * 100).toFixed(0)}% neg
                        </span>
                    </div>
                </div>

                {hasSamples && (
                    <span
                        aria-hidden
                        style={{
                            fontSize: '11px',
                            fontWeight: 700,
                            color: 'var(--accent)',
                            letterSpacing: '0.06em',
                            textTransform: 'uppercase',
                        }}
                    >
                        View ›
                    </span>
                )}
            </button>

            {hasSamples && (
                <Modal
                    isOpen={modalOpen}
                    onClose={() => setModalOpen(false)}
                    title={`${item.topic} · classification reasoning`}
                    subtitle={`Top ${item.classificationSamples!.length} of ${item.volume.toLocaleString()} docs · sorted by model confidence`}
                    accentColor={netColor}
                >
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {item.classificationSamples!.map((sample: ClassificationSample) => (
                            <ClassificationSampleCard
                                key={sample.doc_id}
                                sample={sample}
                                badgeStyle={labelBadgeStyles[sample.label] || labelBadgeStyles.NEUTRAL}
                            />
                        ))}
                    </div>
                </Modal>
            )}
        </div>
    );
}
