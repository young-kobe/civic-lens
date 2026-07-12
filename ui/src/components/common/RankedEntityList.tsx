import type { ReactNode } from 'react';
import type { EntityProfile } from '../../types';
import { clampWidthPct } from '../../services/format';
import { EntityAvatar, entityChipLabel, entityChipTitle } from './EntityProfileCard';
import { leanClass } from '../../theme';

/** One row of a ranked leaderboard column. */
export interface RankedEntity {
    profile: EntityProfile;
    /** Primary rate driving the rank — rendered as number + bar. */
    rateValue: string;
    /** Bar fill 0-100. */
    ratePct: number;
    rateColor?: string;
    /** Small mono annotation after the rate (e.g. "1,204 scanned"). */
    detail?: string;
    /** Optional explanatory content rendered UNDER the name (who they are /
     *  why they rank). Omit for the compact one-line form. */
    description?: ReactNode;
    onClick?: () => void;
}

interface RankedEntityListProps {
    items: RankedEntity[];
    /** Accessible label for the list. */
    ariaLabel?: string;
}

/**
 * RankedEntityList — leaderboard rows for rate-driven columns (Propaganda
 * flagged rate, Bot suspected-automation rate). A rank number, a small
 * avatar, the name + lean chip, and a rate bar say "who leans hardest"
 * in a fraction of the vertical space the profile-card grid used —
 * profile depth (blurb, full stats) stays in the drill-down modal.
 */
export function RankedEntityList({ items, ariaLabel }: RankedEntityListProps) {
    return (
        <ol className="ranked-entity-list" aria-label={ariaLabel}>
            {items.map((item, i) => {
                const lean = leanClass(item.profile);
                const chip = entityChipLabel(item.profile);
                const inner = (
                    <>
                        <span className="ranked-entity-rank" aria-hidden>{i + 1}</span>
                        <EntityAvatar profile={item.profile} />
                        <span className="ranked-entity-main">
                            <span className="ranked-entity-name-wrap">
                                <span className="ranked-entity-name">{item.profile.displayName}</span>
                                {chip && (
                                    <span
                                        className={`entity-card-chip lean-chip-${lean}`}
                                        title={entityChipTitle(item.profile)}
                                    >
                                        {chip}
                                    </span>
                                )}
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
                    <li key={`${item.profile.kind}:${item.profile.key}`}>
                        {item.onClick ? (
                            <button
                                type="button"
                                className="ranked-entity-row"
                                onClick={item.onClick}
                                aria-label={`${item.profile.displayName}: ${item.rateValue}. Open details.`}
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
