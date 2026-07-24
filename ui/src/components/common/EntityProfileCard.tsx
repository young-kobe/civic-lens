import type { LeanLabel as LeanLabelData } from '../../types';
import { COLORS } from '../../theme';
import { formatPts } from '../../services/format';
import { LeanLabel } from './LeanLabel';

// --------------------------------------------------------------------------- //
//  EntityProfileCard — Phase 10 adaptation.                                  //
//                                                                             //
//  The Phase 9 API no longer ships the rich registry bio (blurb, owner,      //
//  founded, office, party, subreddit subscriber proxy) that the pre-redesign //
//  EntityProfile carried — panels now hand back only display_name, kind, and //
//  (where available) a LeanLabel. This card is the corresponding thin shape: //
//  a monogram, the name, a LeanLabel chip when present, and the caller's     //
//  stats row. The removed bio fields have no replacement in this phase.      //
// --------------------------------------------------------------------------- //

/** The minimal per-entity identity every panel can supply. `kind` is
 *  corpus.entities.kind ('official' | 'collective' | 'outlet' | 'subreddit')
 *  or null for an unresolved/catch-all bucket. */
export interface EntityLike {
    kind: string | null;
    displayName: string;
    lean?: LeanLabelData | null;
}

/** One stat cell in the card's stats row. */
export interface EntityStat {
    label: string;
    value: string;
    color?: string;
    emphasis?: boolean;
    title?: string;
    bar?: { pct: number; color?: string; zeroTick?: boolean };
}

interface EntityProfileCardProps {
    entity: EntityLike;
    stats: EntityStat[];
    readsAs?: string;
    onClick?: () => void;
    ariaLabel?: string;
    emptyNote?: string;
}

const KIND_LABEL: Record<string, string> = {
    outlet: 'Outlet', official: 'Official', collective: 'Collective', subreddit: 'Community',
};

export function EntityAvatar({ entity }: { entity: EntityLike }) {
    const monogram = (entity.displayName || '?').trim().charAt(0).toUpperCase() || '?';
    return <span className="entity-avatar entity-avatar-mono" aria-hidden>{monogram}</span>;
}

export function EntityProfileCard({
    entity, stats, readsAs, onClick, ariaLabel,
    emptyNote = 'Tracked — no coverage in this window yet.',
}: EntityProfileCardProps) {
    const hasStats = stats.length > 0;
    const emphasisStat = stats.find((s) => s.emphasis) ?? stats[0];
    const autoAria = emphasisStat
        ? `${entity.displayName}: ${emphasisStat.label} ${emphasisStat.value}. ${onClick ? 'Open details.' : ''}`
        : `${entity.displayName}: tracked, no coverage yet.`;
    const className = `entity-card${hasStats ? '' : ' entity-card-empty'}`;

    const content = (
        <>
            <div className="entity-card-head">
                <EntityAvatar entity={entity} />
                <div className="entity-card-head-text">
                    <h4 className="entity-card-name">{entity.displayName}</h4>
                    <span className="text-xs text-muted">{entity.kind ? KIND_LABEL[entity.kind] ?? entity.kind : 'Unresolved'}</span>
                    {entity.lean && <LeanLabel lean={entity.lean} variant="chip" />}
                </div>
            </div>

            {hasStats ? (
                <div className="entity-card-stats">
                    {stats.map((s) => (
                        <span key={s.label} className="entity-card-stat" title={s.title}>
                            <span
                                className="entity-card-stat-value"
                                style={s.color ? { color: s.color } : undefined}
                            >
                                {s.value}
                            </span>
                            {s.bar && (
                                <span className="entity-card-stat-bar" aria-hidden>
                                    {s.bar.zeroTick && <span className="entity-card-stat-bar-zero" />}
                                    <span
                                        className="entity-card-stat-bar-dot"
                                        style={{
                                            left: `${Math.max(0, Math.min(100, s.bar.pct))}%`,
                                            background: s.bar.color ?? 'var(--neutral-500)',
                                        }}
                                    />
                                </span>
                            )}
                            <span className="entity-card-stat-label">{s.label}</span>
                        </span>
                    ))}
                </div>
            ) : (
                <div className="entity-card-empty-note">{emptyNote}</div>
            )}

            {readsAs && hasStats && <p className="entity-card-reads-as">Reads as: {readsAs}</p>}
        </>
    );

    const sharedProps = { className, 'aria-label': ariaLabel ?? autoAria };
    if (onClick) {
        return <button type="button" onClick={onClick} {...sharedProps}>{content}</button>;
    }
    return <div {...sharedProps}>{content}</div>;
}

export default EntityProfileCard;

// --------------------------------------------------------------------------- //
//  Shared stat-row builders                                                   //
// --------------------------------------------------------------------------- //

function toneStatColor(net: number): string {
    return net > 10 ? COLORS.positive : net < -10 ? COLORS.negative : 'var(--neutral-500)';
}

function toneBar(net: number): NonNullable<EntityStat['bar']> {
    return {
        pct: ((Math.max(-100, Math.min(100, net)) + 100) / 200) * 100,
        color: toneStatColor(net),
        zeroTick: true,
    };
}

/** Net-tone + volume stat pair, shared by every panel that scores an
 *  entity's own posts on the -100..+100 tone scale. */
export function toneStats({ netTone, volume }: { netTone: number | null; volume: number }): EntityStat[] {
    if (volume === 0 || netTone == null) return [];
    return [
        {
            label: 'Net tone',
            value: formatPts(netTone),
            color: toneStatColor(netTone),
            emphasis: true,
            title: "Positive minus negative share of this source's posts, from -100 (all negative) to +100 (all positive).",
            bar: toneBar(netTone),
        },
        { label: 'Posts', value: volume.toLocaleString() },
    ];
}

/** Entity profile + LeanLabel rendered as a standalone block for modals,
 *  where the card's compact layout doesn't fit. */
export function EntityHeader({ entity }: { entity: EntityLike }) {
    return (
        <div className="entity-modal-head">
            <EntityAvatar entity={entity} />
            <div>
                <p className="lead" style={{ margin: 0 }}>
                    {entity.kind ? KIND_LABEL[entity.kind] ?? entity.kind : 'Unresolved mentions'}
                </p>
                {entity.lean && <LeanLabel lean={entity.lean} />}
            </div>
        </div>
    );
}
