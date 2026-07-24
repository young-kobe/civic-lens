import type { ReactNode } from 'react';
import { clampWidthPct } from '../../services/format';
import { EntityAvatar, type EntityLike } from './EntityProfileCard';
import { LeanLabel } from './LeanLabel';

/** One row of a ranked leaderboard column. */
export interface RankedEntity {
    entity: EntityLike;
    /** Primary rate driving the rank — rendered as number + bar. */
    rateValue: string;
    /** Bar fill 0-100. */
    ratePct: number;
    rateColor?: string;
    /** Small mono annotation after the rate (e.g. "1,204 scanned"). */
    detail?: string;
    /** Optional explanatory content rendered UNDER the name. */
    description?: ReactNode;
    onClick?: () => void;
}

interface RankedEntityListProps {
    items: RankedEntity[];
    ariaLabel?: string;
}

/**
 * RankedEntityList — leaderboard rows for rate-driven columns (Bot Detector
 * suspected-automation rate, entity bot rates). A rank number, a small
 * avatar, the name + optional LeanLabel chip, and a rate bar.
 */
export function RankedEntityList({ items, ariaLabel }: RankedEntityListProps) {
    return (
        <ol className="ranked-entity-list" aria-label={ariaLabel}>
            {items.map((item, i) => {
                const inner = (
                    <>
                        <span className="ranked-entity-rank" aria-hidden>{i + 1}</span>
                        <EntityAvatar entity={item.entity} />
                        <span className="ranked-entity-main">
                            <span className="ranked-entity-name-wrap">
                                <span className="ranked-entity-name">{item.entity.displayName}</span>
                                {item.entity.lean && <LeanLabel lean={item.entity.lean} variant="chip" />}
                            </span>
                            {item.description && (
                                <span className="ranked-entity-desc">{item.description}</span>
                            )}
                        </span>
                        <span className="ranked-entity-rate">
                            <span
                                className="ranked-entity-rate-value"
                                style={item.rateColor ? { color: item.rateColor } : undefined}
                            >
                                {item.rateValue}
                            </span>
                            <span className="ranked-entity-rate-bar" aria-hidden>
                                <span
                                    className="ranked-entity-rate-fill"
                                    style={{
                                        width: `${clampWidthPct(item.ratePct)}%`,
                                        background: item.rateColor ?? 'var(--neutral-400)',
                                    }}
                                />
                            </span>
                            {item.detail && (
                                <span className="ranked-entity-detail">{item.detail}</span>
                            )}
                        </span>
                    </>
                );
                return (
                    <li key={`${item.entity.kind}:${item.entity.displayName}:${i}`}>
                        {item.onClick ? (
                            <button
                                type="button"
                                className="ranked-entity-row"
                                onClick={item.onClick}
                                aria-label={`${item.entity.displayName}: ${item.rateValue}. Open details.`}
                            >
                                {inner}
                            </button>
                        ) : (
                            <span className="ranked-entity-row">{inner}</span>
                        )}
                    </li>
                );
            })}
        </ol>
    );
}

export default RankedEntityList;
