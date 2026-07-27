import { useEffect, useRef, useState } from 'react';
import type { MoversResponse, ToneMover, FavorabilityMover } from '../../types';
import { formatPts } from '../../services/format';

interface MoversTickerProps {
    /** Movers payload (GET /movers) — window-over-window tone/favorability
     *  deltas. Null while loading; no tone movers + no favorability mover
     *  renders nothing so the page doesn't carry dead whitespace. */
    data: MoversResponse | null;
    /** Optional handler fired when a user clicks a tone-mover item. */
    onEntityClick?: (mover: ToneMover) => void;
}

type TickerRow =
    | { kind: 'tone'; mover: ToneMover }
    | { kind: 'favorability'; mover: FavorabilityMover };

function formatDelta(delta: number): string {
    return formatPts(delta);
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

function moverTitle(displayName: string, mover: { prevNet: number; currentNet: number; deltaPts: number; currentVolume: number; prevVolume: number }): string {
    return `${displayName}: net tone moved from ${formatNet(mover.prevNet)} `
        + `→ ${formatNet(mover.currentNet)} (${formatDelta(mover.deltaPts)}) `
        + `vs. the previous window, on a -100 to +100 net-tone scale. `
        + `Sample: ${mover.currentVolume.toLocaleString()} posts now, `
        + `${mover.prevVolume.toLocaleString()} before.`;
}

function ToneMoverPill({ mover, onClick }: { mover: ToneMover; onClick?: () => void }) {
    const Wrapper = onClick ? 'button' : 'span';
    return (
        <Wrapper
            type={onClick ? 'button' : undefined}
            className={`movers-item ${onClick ? 'movers-item-clickable' : ''}`}
            onClick={onClick}
            title={moverTitle(mover.displayName, mover)}
        >
            <span className="movers-item-label">{mover.displayName}</span>
            <span className={deltaClass(mover.deltaPts)}>
                <span aria-hidden>{deltaGlyph(mover.deltaPts)}</span>
                {formatDelta(mover.deltaPts)}
            </span>
        </Wrapper>
    );
}

function FavorabilityMoverPill({ mover }: { mover: FavorabilityMover }) {
    return (
        <span className="movers-item movers-item-fav" title={moverTitle(mover.displayName, mover)}>
            <span className="movers-item-label">{mover.displayName} (favorability)</span>
            <span className={deltaClass(mover.deltaPts)}>
                <span aria-hidden>{deltaGlyph(mover.deltaPts)}</span>
                {formatDelta(mover.deltaPts)}
            </span>
        </span>
    );
}

/**
 * MoversTicker — horizontally scrolling marquee of the biggest window-over-
 * window shifts in political tone (per-entity) and the single largest
 * favorability shift among entities with favorability coverage in both
 * periods.
 *
 * Looped with a CSS animation that duplicates the row list so the scroll is
 * seamless. The animation pauses on hover and is disabled entirely when the
 * user has `prefers-reduced-motion: reduce`.
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
    if (data.topFavorabilityMover) {
        rows.push({ kind: 'favorability', mover: data.topFavorabilityMover });
    }
    for (const m of data.toneMovers) {
        rows.push({ kind: 'tone', mover: m });
    }

    if (rows.length === 0) return null;

    const renderRow = (row: TickerRow, key: string) => {
        if (row.kind === 'favorability') {
            return <FavorabilityMoverPill key={key} mover={row.mover} />;
        }
        const handler = onEntityClick ? () => onEntityClick(row.mover) : undefined;
        return <ToneMoverPill key={key} mover={row.mover} onClick={handler} />;
    };

    return (
        <div
            className={`movers-ticker ${reducedMotion ? 'movers-ticker-static' : ''}`}
            role="group"
            aria-label="Biggest movers in political tone and favorability"
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
