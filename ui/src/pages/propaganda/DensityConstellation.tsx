import { useMemo, useState } from 'react';
import { TECHNIQUE_LABEL } from '../../components/common';
import { BREAKPOINTS, useMediaQuery } from '../../services/useMediaQuery';
import { COLORS } from '../../theme';
import type { PropagandaExample, PropagandaTechniqueName } from '../../types';

// --------------------------------------------------------------------------- //
//  DensityConstellation — the Propaganda page's centerpiece visualization.    //
//                                                                             //
//  A beeswarm of the flagged-example pool: every dot is one flagged post,     //
//  positioned by its technique density (overallScore, 0..1) and colored by    //
//  speaker tier. Hover or arrow-key a dot for the post behind it. Layout is   //
//  deterministic (histogram-bin stacking, docId-ordered) so the same payload  //
//  always paints the same picture.                                            //
// --------------------------------------------------------------------------- //

export type ConstellationTier = 'news' | 'officials' | 'public';

export interface ConstellationDot {
    example: PropagandaExample;
    tier: ConstellationTier;
}

const TIER_COLOR: Record<ConstellationTier, string> = {
    news: COLORS.tierNews,
    officials: COLORS.tierOfficials,
    public: COLORS.tierPublic,
};

export const CONSTELLATION_TIER_LABELS: Record<ConstellationTier, string> = {
    news: 'News',
    officials: 'Officials',
    public: 'Public',
};

const BIN_COUNT = 50;
// Horizontal padding of the score scale, in percent of the plot width, so
// r-px dots at score 0/1 don't clip at the SVG edge.
const X_PAD_PCT = 2;

export interface SwarmPlacement {
    /** Stack level within the bin: 0 = baseline, +1 above, -1 below, +2... */
    level: number;
}

/** Deterministic beeswarm layout: bin scores into BIN_COUNT fixed bins and
 *  stack each bin outward from the baseline in docId order (0, +1, -1, ...).
 *  Dots past maxLevels per half are clamped into a per-bin overflow count —
 *  reported, never silently dropped. Pure so the geometry is testable. */
export function layoutSwarm(
    items: { docId: number; score: number }[],
    maxLevels: number,
): { placed: Map<number, { bin: number; level: number }>; overflow: Map<number, number> } {
    const byBin = new Map<number, { docId: number; score: number }[]>();
    for (const item of items) {
        const clamped = Math.max(0, Math.min(1, item.score));
        const bin = Math.min(BIN_COUNT - 1, Math.floor(clamped * BIN_COUNT));
        const list = byBin.get(bin);
        if (list) list.push(item);
        else byBin.set(bin, [item]);
    }
    const placed = new Map<number, { bin: number; level: number }>();
    const overflow = new Map<number, number>();
    for (const [bin, list] of byBin) {
        list.sort((a, b) => a.docId - b.docId);
        list.forEach((item, i) => {
            const level = i === 0 ? 0 : i % 2 === 1 ? (i + 1) / 2 : -(i / 2);
            if (Math.abs(level) > maxLevels) {
                overflow.set(bin, (overflow.get(bin) ?? 0) + 1);
            } else {
                placed.set(item.docId, { bin, level });
            }
        });
    }
    return { placed, overflow };
}

function xPct(score: number): number {
    const clamped = Math.max(0, Math.min(1, score));
    return X_PAD_PCT + clamped * (100 - 2 * X_PAD_PCT);
}

function dotSourceLabel(ex: PropagandaExample): string {
    if (ex.authorHandle) return `@${ex.authorHandle}`;
    if (ex.domain) return ex.domain;
    return ex.sourceType === 'reddit_post' ? 'Reddit post' : 'Post';
}

function dotTechniqueLabels(ex: PropagandaExample): string {
    const names = ex.techniques.map(
        (t) => TECHNIQUE_LABEL[t.technique as PropagandaTechniqueName] || t.technique,
    );
    return names.length > 0 ? names.join(', ') : 'no stored technique spans';
}

interface DensityConstellationProps {
    dots: ConstellationDot[];
    /** Dim dots that don't carry this technique; null shows all. */
    selectedTechnique: PropagandaTechniqueName | null;
}

export function DensityConstellation({ dots, selectedTechnique }: DensityConstellationProps) {
    const isMobile = useMediaQuery(BREAKPOINTS.mobile);
    const height = isMobile ? 200 : 280;
    const radius = isMobile ? 2.5 : 3.5;
    const pitch = isMobile ? 6 : 8;
    // Reserve headroom for the per-bin overflow annotation.
    const maxLevels = Math.floor((height / 2 - 18) / pitch);

    const [activeId, setActiveId] = useState<number | null>(null);

    const { points, overflow } = useMemo(() => {
        const layout = layoutSwarm(
            dots.map((d) => ({ docId: d.example.docId, score: d.example.overallScore })),
            maxLevels,
        );
        const placedPoints = dots
            .filter((d) => layout.placed.has(d.example.docId))
            .map((d) => ({ ...d, ...layout.placed.get(d.example.docId)! }))
            .sort((a, b) => (a.example.overallScore - b.example.overallScore)
                || (a.example.docId - b.example.docId));
        return { points: placedPoints, overflow: layout.overflow };
    }, [dots, maxLevels]);

    const matches = (d: ConstellationDot) => selectedTechnique == null
        || d.example.techniques.some((t) => t.technique === selectedTechnique);

    // Keyboard navigation walks only the undimmed dots, in score order.
    const navIds = useMemo(
        () => points.filter(matches).map((p) => p.example.docId),
        // eslint-disable-next-line react-hooks/exhaustive-deps
        [points, selectedTechnique],
    );

    if (points.length === 0) {
        return (
            <p className="text-sm text-muted" style={{ margin: 0 }}>
                No flagged posts in this window to chart.
            </p>
        );
    }

    const active = activeId != null ? points.find((p) => p.example.docId === activeId) : undefined;
    const centerY = height / 2;

    const onKeyDown = (e: React.KeyboardEvent) => {
        if (navIds.length === 0) return;
        const idx = activeId != null ? navIds.indexOf(activeId) : -1;
        let next: number | null = null;
        if (e.key === 'ArrowRight' || e.key === 'ArrowDown') next = navIds[Math.min(idx + 1, navIds.length - 1)];
        else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') next = navIds[Math.max(idx - 1, 0)];
        else if (e.key === 'Home') next = navIds[0];
        else if (e.key === 'End') next = navIds[navIds.length - 1];
        else if (e.key === 'Escape') { setActiveId(null); return; }
        else return;
        e.preventDefault();
        setActiveId(next);
    };

    return (
        <div className="density-constellation">
            <div className="density-swarm-wrap">
                <svg
                    className="density-swarm"
                    style={{ height }}
                    role="group"
                    tabIndex={0}
                    aria-label={`${points.length} flagged posts by technique density. Use arrow keys to inspect each post.`}
                    aria-activedescendant={active ? `density-dot-${active.example.docId}` : undefined}
                    onKeyDown={onKeyDown}
                    onBlur={() => setActiveId(null)}
                >
                    {points.map((p) => {
                        const dimmed = !matches(p);
                        return (
                            <circle
                                key={p.example.docId}
                                id={`density-dot-${p.example.docId}`}
                                className={`density-dot${dimmed ? ' density-dot-dim' : ''}${p.example.docId === activeId ? ' density-dot-active' : ''}`}
                                cx={`${xPct(p.example.overallScore)}%`}
                                cy={centerY - p.level * pitch}
                                r={radius}
                                style={{ fill: TIER_COLOR[p.tier] }}
                                onMouseEnter={() => setActiveId(p.example.docId)}
                                onMouseLeave={() => setActiveId(null)}
                                aria-label={`${dotSourceLabel(p.example)} — density ${p.example.overallScore.toFixed(2)} — ${dotTechniqueLabels(p.example)}`}
                            />
                        );
                    })}
                    {[...overflow.entries()].map(([bin, n]) => (
                        <text
                            key={bin}
                            className="density-overflow-note"
                            x={`${xPct((bin + 0.5) / BIN_COUNT)}%`}
                            y={12}
                            textAnchor="middle"
                        >
                            +{n}
                        </text>
                    ))}
                </svg>
                {active && (
                    <div className="chart-tooltip density-tooltip">
                        <div className="chart-tooltip-label">
                            {dotSourceLabel(active.example)} · {CONSTELLATION_TIER_LABELS[active.tier]}
                        </div>
                        <div className="chart-tooltip-value">
                            density {active.example.overallScore.toFixed(2)} / 1
                        </div>
                        <div className="density-tooltip-techniques">{dotTechniqueLabels(active.example)}</div>
                        <div className="density-tooltip-snippet">{active.example.textPreview}</div>
                    </div>
                )}
            </div>
            <div className="density-axis" aria-hidden>
                {[0, 0.25, 0.5, 0.75, 1].map((v) => (
                    <span key={v} className="density-axis-tick" style={{ left: `${xPct(v)}%` }}>
                        {isMobile && v !== 0 && v !== 0.5 && v !== 1 ? '' : v}
                    </span>
                ))}
            </div>
            <p className="density-axis-caption">
                Technique density per flagged post — 0 none, 1 wall-to-wall.
            </p>
            <div className="density-tier-legend" role="list" aria-label="Speaker tiers">
                {(Object.keys(CONSTELLATION_TIER_LABELS) as ConstellationTier[]).map((tier) => {
                    const count = points.filter((p) => p.tier === tier).length;
                    return (
                        <span key={tier} className="density-tier-legend-item" role="listitem">
                            <span
                                className="density-tier-legend-dot"
                                style={{ background: TIER_COLOR[tier] }}
                                aria-hidden
                            />
                            {CONSTELLATION_TIER_LABELS[tier]}
                            <span className="density-tier-legend-count">{count}</span>
                        </span>
                    );
                })}
            </div>
        </div>
    );
}

export default DensityConstellation;
