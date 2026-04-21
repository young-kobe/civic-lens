import { useMemo, useState } from 'react';
import { Card, MethodPopover, Modal } from '../../components/common';
import type {
    ClassificationSample,
    SentimentBreakdown,
    SentimentDistribution,
    SentimentOverview,
    SentimentSegmentKey,
} from '../../types';
import { ClassificationSampleCard } from './ClassificationSampleCard';

interface SentimentDistributionCardProps {
    data: SentimentDistribution;
    /** Overview gives us volume + confidence context. */
    overview?: SentimentOverview;
    /** Platform breakdown, used to surface which source dominates. */
    byPlatform?: SentimentBreakdown[];
    /** Optional drill-down samples per intensity bucket. */
    samples?: Partial<Record<SentimentSegmentKey, ClassificationSample[]>>;
}

interface Segment {
    key: SentimentSegmentKey;
    label: string;
    value: number;
    gradient: string;
    solid: string;
    description: string;
    /** Palette used when this bucket's samples are shown in ClassificationSampleCard. */
    badge: { bg: string; text: string };
}

function formatPct(value: number, total: number): string {
    if (total <= 0) return '0.0%';
    return `${((value / total) * 100).toFixed(1)}%`;
}

function formatNet(score: number): string {
    const sign = score >= 0 ? '+' : '';
    return `${sign}${score.toFixed(1)}`;
}

export function SentimentDistributionCard({
    data,
    overview,
    byPlatform,
    samples,
}: SentimentDistributionCardProps) {
    const [active, setActive] = useState<SentimentSegmentKey | null>(null);
    const [openBucket, setOpenBucket] = useState<SentimentSegmentKey | null>(null);

    const segments: Segment[] = useMemo(() => ([
        {
            key: 'strongNegative',
            label: 'Strong unfavorable',
            value: data.strongNegative,
            gradient: 'linear-gradient(135deg, #7a1109 0%, #c91b0e 100%)',
            solid: '#991b1b',
            description: 'Docs scored as clearly, emphatically negative. Evidence span carries intensifiers or explicit hostility.',
            badge: { bg: '#fbe7e4', text: '#991b1b' },
        },
        {
            key: 'mildNegative',
            label: 'Mild unfavorable',
            value: data.mildNegative,
            gradient: 'linear-gradient(135deg, #c91b0e 0%, #f0705f 100%)',
            solid: '#dc2626',
            description: 'Docs leaning negative but qualified or mixed with acknowledgement. Softer tone, still a net-negative read.',
            badge: { bg: '#fbe7e4', text: '#dc2626' },
        },
        {
            key: 'neutral',
            label: 'Neutral',
            value: data.neutral,
            gradient: 'linear-gradient(135deg, #8b919e 0%, #c0c4cd 100%)',
            solid: '#8b919e',
            description: 'Docs with no clear leaning. Typically reportage, definitions, or multi-sided coverage.',
            badge: { bg: '#f4f5f7', text: '#45454d' },
        },
        {
            key: 'mildPositive',
            label: 'Mild favorable',
            value: data.mildPositive,
            gradient: 'linear-gradient(135deg, #3ec37d 0%, #22c55e 100%)',
            solid: '#22c55e',
            description: 'Docs leaning positive but qualified. Approving tone without strong endorsement.',
            badge: { bg: '#e3f6eb', text: '#16a34a' },
        },
        {
            key: 'strongPositive',
            label: 'Strong favorable',
            value: data.strongPositive,
            gradient: 'linear-gradient(135deg, #00a358 0%, #006b3b 100%)',
            solid: '#16a34a',
            description: 'Docs scored as clearly, emphatically positive. Endorsement, celebration, or strong approval.',
            badge: { bg: '#e3f6eb', text: '#16a34a' },
        },
    ]), [data]);

    const total = segments.reduce((sum, s) => sum + s.value, 0);
    const pos = data.strongPositive + data.mildPositive;
    const neg = data.strongNegative + data.mildNegative;
    const neu = data.neutral;
    const polarized = total > 0 ? (data.strongPositive + data.strongNegative) / total : 0;
    const netPct = total > 0 ? ((pos - neg) / total) * 100 : 0;

    const skewChip = (() => {
        if (total === 0) return null;
        if (netPct >= 10) return { className: 'chip chip-positive', label: `Skews favorable ${formatNet(netPct)}%` };
        if (netPct <= -10) return { className: 'chip chip-negative', label: `Skews unfavorable ${formatNet(netPct)}%` };
        return { className: 'chip', label: `Near-even ${formatNet(netPct)}%` };
    })();

    const polarizationChip = total > 0 && polarized >= 0.35
        ? { className: 'chip chip-accent', label: `Polarized · ${(polarized * 100).toFixed(0)}% at extremes` }
        : null;

    const topPlatform = useMemo(() => {
        if (!byPlatform || byPlatform.length === 0) return null;
        return [...byPlatform].sort((a, b) => b.volume - a.volume)[0];
    }, [byPlatform]);

    const toggleBucket = (key: SentimentSegmentKey) => {
        setOpenBucket(current => (current === key ? null : key));
    };

    return (
        <Card
            title="Sentiment Distribution"
            subtitle={`5-point intensity scale across ${total.toLocaleString()} scored docs`}
            headerActions={
                <MethodPopover
                    description="Sentiment intensity scored on a 5-point scale from each document's evidence span. Strong buckets require confident, unambiguous signal; mild buckets allow qualification. Click any segment to audit the underlying docs."
                    limitations={[
                        'Intensity thresholds are model-dependent and calibrated against the current review set.',
                        'Sarcasm can collapse strong-positive/negative into neutral.',
                        'Drill-down samples are the highest-confidence docs per bucket, not the full set.',
                    ]}
                />
            }
        >
            {/* Context strip — chips */}
            <div className="flex items-center gap-2 mb-3" style={{ flexWrap: 'wrap' }}>
                {skewChip && <span className={skewChip.className}>{skewChip.label}</span>}
                {polarizationChip && <span className={polarizationChip.className}>{polarizationChip.label}</span>}
                {overview?.confidence && (
                    <span className="chip">
                        <span className={`confidence-dot confidence-${overview.confidence}`} />
                        {overview.confidence} confidence
                    </span>
                )}
                {topPlatform?.platform && (
                    <span className="chip" title={`${topPlatform.volume.toLocaleString()} docs on ${topPlatform.platform}`}>
                        Most volume: {topPlatform.platform}
                    </span>
                )}
            </div>

            {/* Distribution bar — click to open drill-down */}
            <div
                role="group"
                aria-label={`Sentiment distribution: ${segments.map(s => `${s.label} ${formatPct(s.value, total)}`).join(', ')}`}
                style={{
                    display: 'flex',
                    height: '56px',
                    borderRadius: 'var(--radius-md)',
                    overflow: 'hidden',
                    marginBottom: 'var(--space-3)',
                    background: 'var(--bg-inset)',
                    boxShadow: 'inset 0 1px 0 rgba(15, 20, 35, 0.04)',
                }}
                onMouseLeave={() => setActive(null)}
            >
                {segments.map((seg) => {
                    const pct = total > 0 ? (seg.value / total) * 100 : 0;
                    if (pct === 0) return null;
                    const bucketSamples = samples?.[seg.key] ?? [];
                    const hasSamples = bucketSamples.length > 0;
                    const isActive = active === seg.key || openBucket === seg.key;
                    return (
                        <button
                            key={seg.key}
                            type="button"
                            className={`seg ${isActive ? 'is-active' : ''}`}
                            style={{
                                width: `${pct}%`,
                                background: seg.gradient,
                                cursor: hasSamples ? 'pointer' : 'default',
                                display: 'flex',
                                flexDirection: 'column',
                                alignItems: 'center',
                                justifyContent: 'center',
                                color: 'white',
                                fontSize: '11px',
                                fontWeight: 700,
                                letterSpacing: '0.04em',
                                textShadow: '0 1px 2px rgba(0, 0, 0, 0.25)',
                                border: 'none',
                                padding: 0,
                            }}
                            onMouseEnter={() => setActive(seg.key)}
                            onFocus={() => setActive(seg.key)}
                            onBlur={() => setActive(null)}
                            onClick={() => hasSamples && toggleBucket(seg.key)}
                            aria-expanded={openBucket === seg.key}
                            aria-label={`${seg.label}: ${seg.value.toLocaleString()} docs, ${formatPct(seg.value, total)}${hasSamples ? ', click to view samples' : ''}`}
                        >
                            {pct >= 8 && <span>{pct.toFixed(0)}%</span>}
                            {hasSamples && pct >= 12 && (
                                <span style={{ fontSize: '9px', fontWeight: 600, opacity: 0.85, marginTop: 2 }}>
                                    {openBucket === seg.key ? 'HIDE' : 'VIEW'}
                                </span>
                            )}
                        </button>
                    );
                })}
            </div>

            {/* Active segment hint — shows on hover before clicking */}
            <div
                aria-live="polite"
                style={{
                    minHeight: 54,
                    padding: 'var(--space-2) var(--space-3)',
                    borderRadius: 'var(--radius-sm)',
                    background: 'var(--bg-inset)',
                    border: '1px solid var(--neutral-150)',
                    marginBottom: 'var(--space-3)',
                    fontSize: 'var(--text-xs)',
                }}
            >
                {active ? (
                    (() => {
                        const seg = segments.find(s => s.key === active)!;
                        const count = samples?.[seg.key]?.length ?? 0;
                        return (
                            <div>
                                <div className="hover-card-title" style={{ color: seg.solid, marginBottom: 2 }}>
                                    {seg.label}
                                </div>
                                <div className="hover-card-row">
                                    <span>Docs</span>
                                    <strong>{seg.value.toLocaleString()}</strong>
                                </div>
                                <div className="hover-card-row">
                                    <span>Share</span>
                                    <strong>{formatPct(seg.value, total)}</strong>
                                </div>
                                <div className="hover-card-note" style={{ marginTop: 6 }}>
                                    {seg.description}
                                    {count > 0 && (
                                        <>
                                            {' '}Click the segment to view {count} sample doc{count === 1 ? '' : 's'}.
                                        </>
                                    )}
                                </div>
                            </div>
                        );
                    })()
                ) : (
                    <span className="text-muted">
                        Hover or tap a segment for definition and counts. {total > 0 && (
                            <>Positive {formatPct(pos, total)} &middot; Neutral {formatPct(neu, total)} &middot; Negative {formatPct(neg, total)}.</>
                        )}
                    </span>
                )}
            </div>

            {/* Legend grid */}
            <div className="grid-2 gap-2">
                {segments.map((seg) => (
                    <button
                        key={seg.key}
                        type="button"
                        className="flex items-center gap-2"
                        style={{
                            padding: 'var(--space-1) var(--space-2)',
                            borderRadius: 'var(--radius-sm)',
                            border: '1px solid transparent',
                            background: active === seg.key || openBucket === seg.key ? 'var(--bg-inset)' : 'transparent',
                            cursor: (samples?.[seg.key]?.length ?? 0) > 0 ? 'pointer' : 'default',
                            width: '100%',
                            textAlign: 'left',
                            color: 'inherit',
                        }}
                        onMouseEnter={() => setActive(seg.key)}
                        onMouseLeave={() => setActive(null)}
                        onFocus={() => setActive(seg.key)}
                        onBlur={() => setActive(null)}
                        onClick={() => (samples?.[seg.key]?.length ?? 0) > 0 && toggleBucket(seg.key)}
                    >
                        <span
                            aria-hidden
                            style={{
                                width: '12px',
                                height: '12px',
                                borderRadius: 'var(--radius-sm)',
                                background: seg.gradient,
                                flexShrink: 0,
                            }}
                        />
                        <span className="text-sm">{seg.label}</span>
                        <span className="text-xs text-muted ml-auto num">
                            {seg.value.toLocaleString()} &middot; {formatPct(seg.value, total)}
                        </span>
                    </button>
                ))}
            </div>

            {/* Drill-down modal — overlays the page instead of pushing content down */}
            {openBucket && (() => {
                const seg = segments.find(s => s.key === openBucket)!;
                const bucketSamples = samples?.[openBucket] ?? [];
                if (bucketSamples.length === 0) return null;
                return (
                    <Modal
                        isOpen
                        onClose={() => setOpenBucket(null)}
                        title={`${seg.label} · sample docs`}
                        subtitle={`Showing ${bucketSamples.length} of ${seg.value.toLocaleString()} docs · sorted by model confidence`}
                        accentColor={seg.solid}
                    >
                        <div className="flex flex-col gap-2">
                            {bucketSamples.map((sample) => (
                                <ClassificationSampleCard
                                    key={sample.doc_id}
                                    sample={sample}
                                    badgeStyle={seg.badge}
                                />
                            ))}
                        </div>
                    </Modal>
                );
            })()}
        </Card>
    );
}
