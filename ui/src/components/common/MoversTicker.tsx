import { useEffect, useRef, useState } from 'react';
import type { EntityToneMover, FavorabilityMover, MoversResult } from '../../types';

interface MoversTickerProps {
    /**
     * Movers payload — window-over-window deltas computed by
     * `MoversAggregator.get_movers`. Null while loading; empty-ish values
     * (no entity movers + no favorability row) render nothing so the page
     * doesn't carry dead whitespace.
     */
    data: MoversResult | null;
    /** Optional handler fired when a user clicks an entity-mover item. */
    onEntityClick?: (mover: EntityToneMover) => void;
}

type TickerRow =
    | { kind: 'entity'; mover: EntityToneMover }
    | { kind: 'favorability'; mover: FavorabilityMover };

function formatDelta(delta: number): string {
    const sign = delta > 0 ? '+' : '';
    return `${sign}${delta.toFixed(1)} pts`;
}

function deltaClass(delta: number): string {
    if (delta > 0.5) return 'movers-delta movers-delta-up';
    if (delta < -0.5) return 'movers-delta movers-delta-down';
    return 'movers-delta movers-delta-flat';
}

function deltaGlyph(delta: number): string {
    if (delta > 0.5) return '▲';
    if (delta < -0.5) return '▼';
    return '▪';
}

function formatNet(net: number): string {
    const sign = net > 0 ? '+' : '';
    return `${sign}${net.toFixed(1)}`;
}

function EntityPill({ mover, onClick }: { mover: EntityToneMover; onClick?: () => void }) {
    const Wrapper = onClick ? 'button' : 'span';
    // Hover tooltip spells out what the delta represents, both window
    // endpoints, and the sample size on each side so a reader can tell
    // whether a big mover is driven by real shift or low-volume noise.
    const title =
        `${mover.displayName}: net tone moved from ${formatNet(mover.prev_net)} ` +
        `→ ${formatNet(mover.current_net)} (${formatDelta(mover.delta_pts)}) ` +
        `vs. the previous window, on a -100 to +100 net-tone scale. ` +
        `Sample: ${mover.current_volume.toLocaleString()} posts now, ` +
        `${mover.prev_volume.toLocaleString()} before.`;
    return (
        <Wrapper
            type={onClick ? 'button' : undefined}
            className={`movers-item ${onClick ? 'movers-item-clickable' : ''}`}
            onClick={onClick}
            title={title}
        >
            <span className="movers-item-label">{mover.displayName}</span>
            <span className={deltaClass(mover.delta_pts)}>
                <span aria-hidden>{deltaGlyph(mover.delta_pts)}</span>
                {formatDelta(mover.delta_pts)}
            </span>
        </Wrapper>
    );
}

function FavorabilityPill({ mover }: { mover: FavorabilityMover }) {
    const title =
        `${mover.label}: ${formatNet(mover.prev_net)} → ${formatNet(mover.current_net)} ` +
        `(${formatDelta(mover.delta_pts)}) vs. the previous window, on a -100 to +100 ` +
        `net-tone scale. Sample: ${mover.current_volume.toLocaleString()} posts now, ` +
        `${mover.prev_volume.toLocaleString()} before.`;
    return (
        <span className="movers-item movers-item-fav" title={title}>
            <span className="movers-item-label">{mover.label}</span>
            <span className={deltaClass(mover.delta_pts)}>
                <span aria-hidden>{deltaGlyph(mover.delta_pts)}</span>
                {formatDelta(mover.delta_pts)}
            </span>
        </span>
    );
}

/**
 * MoversTicker — horizontally scrolling marquee of biggest window-over-window
 * shifts in political tone (per-entity) and GOP favorability (one summary row).
 *
 * Looped with a CSS animation that duplicates the row list so the scroll is
 * seamless. The animation pauses on hover and is disabled entirely when the
 * user has `prefers-reduced-motion: reduce`; in that case the strip becomes a
 * regular horizontally-scrollable element.
 */
export function MoversTicker({ data, onEntityClick }: MoversTickerProps) {
    const trackRef = useRef<HTMLDivElement>(null);
    const [reducedMotion, setReducedMotion] = useState(false);

    useEffect(() => {
        const mq = window.matchMedia('(prefers-reduced-motion: reduce)');
        const handler = () => setReducedMotion(mq.matches);
        handler();
        mq.addEventListener('change', handler);
        return () => mq.removeEventListener('change', handler);
    }, []);

    if (!data) return null;

    const rows: TickerRow[] = [];
    if (data.favorability_mover) {
        rows.push({ kind: 'favorability', mover: data.favorability_mover });
    }
    for (const m of data.entity_movers) {
        rows.push({ kind: 'entity', mover: m });
    }

    if (rows.length === 0) return null;

    const renderRow = (row: TickerRow, key: string) => {
        if (row.kind === 'favorability') {
            return <FavorabilityPill key={key} mover={row.mover} />;
        }
        const handler = onEntityClick ? () => onEntityClick(row.mover) : undefined;
        return <EntityPill key={key} mover={row.mover} onClick={handler} />;
    };

    return (
        <div
            className={`movers-ticker ${reducedMotion ? 'movers-ticker-static' : ''}`}
            role="group"
            aria-label="Biggest movers in political tone and GOP favorability"
        >
            <span className="movers-ticker-eyebrow" aria-hidden>Biggest tone shifts</span>
            <div className="movers-ticker-viewport">
                <div className="movers-ticker-track" ref={trackRef}>
                    {rows.map((r, i) => renderRow(r, `a-${i}`))}
                    {!reducedMotion && rows.map((r, i) => renderRow(r, `b-${i}`))}
                </div>
            </div>
        </div>
    );
}

export default MoversTicker;
