import { useMemo, useState } from 'react';
import { Card, ClassificationSampleCard, MethodPopover, Modal } from '../../components/common';
import type {
    ClassificationSample,
    SentimentBreakdown,
    SentimentDistribution,
    SentimentOverview,
    SentimentSegmentKey,
} from '../../types';
import { COLORS } from '../../theme';
import { formatPct } from '../../services/format';

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

/** Share formatter — renders `value/total` as a bounded-and-guarded percent. */
function fracPct(value: number, total: number): string {
    if (total <= 0) return formatPct(0, { decimals: 1 });
    return formatPct((value / total) * 100, { decimals: 1 });
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
            gradient: COLORS.toneGradStrongNeg,
            solid:    COLORS.toneStrongNeg,
            description: 'Docs scored as clearly, emphatically negative. Evidence span carries intensifiers or explicit hostility.',
            badge: { bg: COLORS.unfavBg, text: COLORS.unfavText },
        },
        {
            key: 'mildNegative',
            label: 'Mild unfavorable',
            value: data.mildNegative,
            gradient: COLORS.toneGradMildNeg,
            solid:    COLORS.toneMildNeg,
            description: 'Docs leaning negative but qualified or mixed with acknowledgement. Softer tone, still a net-negative read.',
            badge: { bg: COLORS.unfavBg, text: COLORS.unfavSolid },
        },
        {
            key: 'neutral',
            label: 'Neutral',
            value: data.neutral,
            gradient: COLORS.toneGradNeu,
            solid:    COLORS.toneNeu,
            description: 'Docs with no clear leaning. Typically reportage, definitions, or multi-sided coverage.',
            badge: { bg: COLORS.neutralLight, text: COLORS.toneNeuText },
        },
        {
            key: 'mildPositive',
            label: 'Mild favorable',
            value: data.mildPositive,
            gradient: COLORS.toneGradMildPos,
            solid:    COLORS.toneMildPos,
            description: 'Docs leaning positive but qualified. Approving tone without strong endorsement.',
            badge: { bg: COLORS.favBg, text: COLORS.favSolid },
        },
        {
            key: 'strongPositive',
            label: 'Strong favorable',
            value: data.strongPositive,
            gradient: COLORS.toneGradStrongPos,
            solid:    COLORS.toneStrongPos,
            description: 'Docs scored as clearly, emphatically positive. Endorsement, celebration, or strong approval.',
            badge: { bg: COLORS.favBg, text: COLORS.favSolid },
        },
    ]), [data]);

    const total = segments.reduce((sum, s) => sum + s.value, 0);
    const pos = data.strongPositive + data.mildPositive;
    const neg = data.strongNegative + data.mildNegative;
    const neu = data.neutral;
    const intensePct = total > 0 ? ((data.strongPositive + data.strongNegative) / total) * 100 : 0;
    const measuredPct = total > 0 ? ((data.mildPositive + data.mildNegative) / total) * 100 : 0;
    const netPct = total > 0 ? ((pos - neg) / total) * 100 : 0;

    const skewChip = (() => {
        if (total === 0) return null;
        if (netPct >= 10) return { className: 'chip chip-positive', label: `Leans favorable ${formatPct(netPct, { min: -100, signed: true })}` };
        if (netPct <= -10) return { className: 'chip chip-negative', label: `Leans unfavorable ${formatPct(netPct, { min: -100, signed: true })}` };
        return { className: 'chip', label: `Near-even ${formatPct(netPct, { min: -100, signed: true })}` };
    })();

    const polarizationChip = total > 0 && intensePct >= 35
        ? { className: 'chip chip-accent', label: `Polarized · ${formatPct(intensePct, { decimals: 0 })} at extremes` }
        : null;

    // One-line plain-English interpretation. Priority: polarization > lean.
    const readsAs = (() => {
        if (total === 0) return null;
        if (intensePct >= 45) {
            return `Reads as: heavily polarized — ${formatPct(intensePct, { decimals: 0 })} of docs land in the strong-positive or strong-negative buckets.`;
        }
        if (intensePct >= 30) {
            const direction = data.strongNegative > data.strongPositive ? 'negative' : 'positive';
            return `Reads as: the sample is intense and leans ${direction} — ${formatPct(intensePct, { decimals: 0 })} sit at the extremes.`;
        }
        if (netPct <= -15) {
            return `Reads as: the sample leans clearly unfavorable (${netPct.toFixed(1)} pts), mostly in the mild-negative bucket.`;
        }
        if (netPct >= 15) {
            return `Reads as: the sample leans clearly favorable (+${netPct.toFixed(1)} pts), mostly in the mild-positive bucket.`;
        }
        return `Reads as: near-even tone with ${formatPct((neu / total) * 100, { decimals: 0 })} of docs reading as straight neutral reportage.`;
    })();

    const topPlatform = useMemo(() => {
        if (!byPlatform || byPlatform.length === 0) return null;
        return [...byPlatform].sort((a, b) => b.volume - a.volume)[0];
    }, [byPlatform]);

    const toggleBucket = (key: SentimentSegmentKey) => {
        setOpenBucket(current => (current === key ? null : key));
    };

    return (
        <Card
            title="Tone Intensity"
            subtitle={`How strongly ${total.toLocaleString()} docs lean in each direction (5-point scale)`}
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
            {/* Intensity summary — the primary takeaway for this card.
                Strong vs mild split is the real story; doc counts per bucket
                are secondary detail (legend handles those). */}
            <div className="flex items-baseline gap-4 mb-3" style={{ flexWrap: 'wrap' }}>
                <div>
                    <div className="eyebrow" style={{ color: 'var(--neutral-500)' }}>Intense</div>
                    <div
                        className="num"
                        style={{
                            fontFamily: 'var(--font-mono)',
                            fontVariantNumeric: 'tabular-nums',
                            fontSize: 'var(--text-2xl)',
                            fontWeight: 600,
                            letterSpacing: '-0.02em',
                            color: intensePct >= 30 ? COLORS.negative : 'var(--neutral-800)',
                            lineHeight: 1,
                        }}
                    >
                        {formatPct(intensePct, { decimals: 0 })}
                    </div>
                </div>
                <span aria-hidden style={{ width: 1, height: 32, background: 'var(--neutral-200)' }} />
                <div>
                    <div className="eyebrow" style={{ color: 'var(--neutral-500)' }}>Measured</div>
                    <div
                        className="num"
                        style={{
                            fontFamily: 'var(--font-mono)',
                            fontVariantNumeric: 'tabular-nums',
                            fontSize: 'var(--text-2xl)',
                            fontWeight: 600,
                            letterSpacing: '-0.02em',
                            color: 'var(--neutral-700)',
                            lineHeight: 1,
                        }}
                    >
                        {formatPct(measuredPct, { decimals: 0 })}
                    </div>
                </div>
                <span aria-hidden style={{ width: 1, height: 32, background: 'var(--neutral-200)' }} />
                <div>
                    <div className="eyebrow" style={{ color: 'var(--neutral-500)' }}>Neutral</div>
                    <div
                        className="num"
                        style={{
                            fontFamily: 'var(--font-mono)',
                            fontVariantNumeric: 'tabular-nums',
                            fontSize: 'var(--text-2xl)',
                            fontWeight: 600,
                            letterSpacing: '-0.02em',
                            color: 'var(--neutral-500)',
                            lineHeight: 1,
                        }}
                    >
                        {total > 0 ? Math.round((neu / total) * 100) : 0}%
                    </div>
                </div>
            </div>

            {/* Chips — context tags that qualify the big numbers above. */}
            <div className="flex items-center gap-2 mb-3" style={{ flexWrap: 'wrap' }}>
                {skewChip && <span className={skewChip.className}>{skewChip.label}</span>}
                {polarizationChip && <span className={polarizationChip.className}>{polarizationChip.label}</span>}
                {overview?.confidence && (
                    <span className="chip" title={`Coverage ${overview.coverage}, confidence ${overview.confidence}`}>
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

            {/* Plain-English interpretation — one line, honest about what the
                distribution actually implies. Picks the framing based on
                which pattern dominates (polarization, lean, or balance). */}
            {readsAs && (
                <div
                    style={{
                        marginBottom: 'var(--space-3)',
                        padding: 'var(--space-2) var(--space-3)',
                        background: 'var(--bg-inset)',
                        borderLeft: '2px solid var(--accent-muted)',
                        borderRadius: 'var(--radius-sm)',
                        fontSize: 'var(--text-xs)',
                        color: 'var(--neutral-700)',
                        lineHeight: 'var(--leading-relaxed)',
                    }}
                >
                    {readsAs}
                </div>
            )}

            {/* Relative wrapper anchors the floating hover card to the bar
                so it overlays content below instead of reflowing the page. */}
            <div style={{ position: 'relative', marginBottom: 'var(--space-3)' }}>
            <div
                role="group"
                aria-label={`Sentiment distribution: ${segments.map(s => `${s.label} ${fracPct(s.value, total)}`).join(', ')}`}
                style={{
                    display: 'flex',
                    height: '56px',
                    borderRadius: 'var(--radius-md)',
                    overflow: 'hidden',
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
                            aria-label={`${seg.label}: ${seg.value.toLocaleString()} docs, ${fracPct(seg.value, total)}${hasSamples ? ', click to view samples' : ''}`}
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

                {/* Floating hover card — sits inside the bar wrapper so it
                    anchors to the bar and overlays whatever's below. */}
                {active && (() => {
                    const seg = segments.find(s => s.key === active)!;
                    const count = samples?.[seg.key]?.length ?? 0;
                    return (
                        <div
                            className="popover"
                            role="tooltip"
                            aria-live="polite"
                            style={{
                                top: 'calc(100% + 8px)',
                                left: 0,
                                maxWidth: 360,
                                minWidth: 220,
                            }}
                        >
                            <div className="hover-card-title" style={{ color: seg.solid, marginBottom: 2 }}>
                                {seg.label}
                            </div>
                            <div className="hover-card-row">
                                <span>Docs</span>
                                <strong>{seg.value.toLocaleString()}</strong>
                            </div>
                            <div className="hover-card-row">
                                <span>Share</span>
                                <strong>{fracPct(seg.value, total)}</strong>
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
                })()}
            </div>

            {/* Static summary line — always visible, doesn't move on hover. */}
            <div
                className="text-xs text-muted mb-3"
                style={{ fontVariantNumeric: 'tabular-nums' }}
            >
                {total > 0 ? (
                    <>
                        Positive {fracPct(pos, total)} &middot; Neutral {fracPct(neu, total)} &middot; Negative {fracPct(neg, total)}.
                        {' '}Hover a segment for definition; click to view sample docs.
                    </>
                ) : 'No scored docs in this window.'}
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
                            {seg.value.toLocaleString()} &middot; {fracPct(seg.value, total)}
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
