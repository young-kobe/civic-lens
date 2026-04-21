import { useMemo, useState } from 'react';
import { Card, MethodPopover } from '../../components/common';
import type { SentimentBreakdown, SentimentDistribution, SentimentOverview } from '../../types';

interface SentimentDistributionCardProps {
    data: SentimentDistribution;
    /** Overview gives us volume + confidence context to surface next to the bar. */
    overview?: SentimentOverview;
    /** Platform breakdown, used to tell the reader which source dominates the sampled discourse. */
    byPlatform?: SentimentBreakdown[];
}

type SegmentKey = 'strongNegative' | 'mildNegative' | 'neutral' | 'mildPositive' | 'strongPositive';

interface Segment {
    key: SegmentKey;
    label: string;
    short: string;
    value: number;
    gradient: string;
    solid: string;
    description: string;
}

function formatPct(value: number, total: number): string {
    if (total <= 0) return '0.0%';
    return `${((value / total) * 100).toFixed(1)}%`;
}

function formatNet(score: number): string {
    const sign = score >= 0 ? '+' : '';
    return `${sign}${score.toFixed(1)}`;
}

export function SentimentDistributionCard({ data, overview, byPlatform }: SentimentDistributionCardProps) {
    const [active, setActive] = useState<SegmentKey | null>(null);

    const segments: Segment[] = useMemo(() => ([
        {
            key: 'strongNegative',
            label: 'Strong unfavorable',
            short: 'Strong neg.',
            value: data.strongNegative,
            gradient: 'linear-gradient(135deg, #7a1109 0%, #c91b0e 100%)',
            solid: '#991b1b',
            description: 'Docs scored as clearly, emphatically negative. Evidence span carries intensifiers or explicit hostility.',
        },
        {
            key: 'mildNegative',
            label: 'Mild unfavorable',
            short: 'Mild neg.',
            value: data.mildNegative,
            gradient: 'linear-gradient(135deg, #c91b0e 0%, #f0705f 100%)',
            solid: '#dc2626',
            description: 'Docs leaning negative but qualified or mixed with acknowledgement. Softer tone, still a net-negative read.',
        },
        {
            key: 'neutral',
            label: 'Neutral',
            short: 'Neutral',
            value: data.neutral,
            gradient: 'linear-gradient(135deg, #8b919e 0%, #c0c4cd 100%)',
            solid: '#8b919e',
            description: 'Docs with no clear leaning. Typically reportage, definitions, or multi-sided coverage.',
        },
        {
            key: 'mildPositive',
            label: 'Mild favorable',
            short: 'Mild pos.',
            value: data.mildPositive,
            gradient: 'linear-gradient(135deg, #3ec37d 0%, #22c55e 100%)',
            solid: '#22c55e',
            description: 'Docs leaning positive but qualified. Approving tone without strong endorsement.',
        },
        {
            key: 'strongPositive',
            label: 'Strong favorable',
            short: 'Strong pos.',
            value: data.strongPositive,
            gradient: 'linear-gradient(135deg, #00a358 0%, #006b3b 100%)',
            solid: '#16a34a',
            description: 'Docs scored as clearly, emphatically positive. Endorsement, celebration, or strong approval.',
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

    return (
        <Card
            title="Sentiment Distribution"
            subtitle={`5-point intensity scale across ${total.toLocaleString()} scored docs`}
            headerActions={
                <MethodPopover
                    description="Sentiment intensity scored on a 5-point scale from each document's evidence span. Strong buckets require confident, unambiguous signal; mild buckets allow qualification."
                    limitations={[
                        'Intensity thresholds are model-dependent and calibrated against the current review set.',
                        'Sarcasm can collapse strong-positive/negative into neutral.',
                    ]}
                />
            }
        >
            {/* Context strip — chips + top-line numbers */}
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

            {/* Distribution bar with hover */}
            <div
                role="img"
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
                    const isActive = active === seg.key;
                    return (
                        <div
                            key={seg.key}
                            className={`seg ${isActive ? 'is-active' : ''}`}
                            style={{
                                width: `${pct}%`,
                                background: seg.gradient,
                                cursor: 'default',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                color: 'white',
                                fontSize: '11px',
                                fontWeight: 700,
                                letterSpacing: '0.04em',
                                textShadow: '0 1px 2px rgba(0, 0, 0, 0.25)',
                            }}
                            onMouseEnter={() => setActive(seg.key)}
                            onFocus={() => setActive(seg.key)}
                            onBlur={() => setActive(null)}
                            tabIndex={0}
                            role="button"
                            aria-label={`${seg.label}: ${seg.value.toLocaleString()} docs, ${formatPct(seg.value, total)}`}
                        >
                            {pct >= 8 && `${pct.toFixed(0)}%`}
                        </div>
                    );
                })}
            </div>

            {/* Active segment detail — replaces native title */}
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
                                </div>
                            </div>
                        );
                    })()
                ) : (
                    <span className="text-muted">
                        Hover or focus a segment for definition and counts. {total > 0 && (
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
                            background: active === seg.key ? 'var(--bg-inset)' : 'transparent',
                            cursor: 'default',
                            width: '100%',
                            textAlign: 'left',
                            color: 'inherit',
                        }}
                        onMouseEnter={() => setActive(seg.key)}
                        onMouseLeave={() => setActive(null)}
                        onFocus={() => setActive(seg.key)}
                        onBlur={() => setActive(null)}
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
        </Card>
    );
}
