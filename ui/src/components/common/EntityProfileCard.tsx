import type { ReactNode } from 'react';
import type { EntityProfile } from '../../types';
import { COLORS, leanClass } from '../../theme';
import { formatPct } from '../../services/format';

/** One stat cell in the card's stats row. */
export interface EntityStat {
    label: string;
    value: string;
    /** Optional color override for the value (e.g. tone red/green). */
    color?: string;
    /** Sort this stat visually prominent; at most one per card. */
    emphasis?: boolean;
}

interface EntityProfileCardProps {
    profile: EntityProfile;
    /** Up to ~3 stats. Card shows an "empty" state when the list is empty. */
    stats: EntityStat[];
    /** Optional one-line plain-English interpretation. */
    readsAs?: string;
    /** If present, card renders as a button; takes precedence over href. */
    onClick?: () => void;
    /** If present (and no onClick), card renders as an external link. */
    href?: string;
    /** Accessibility override; generated from profile + first stat when omitted. */
    ariaLabel?: string;
    /** Title tooltip; defaults to the full blurb. */
    title?: string;
    /** Optional empty-state note when `stats` is empty. Defaults to tracked-not-yet copy. */
    emptyNote?: string;
}

const BLURB_MAX_CHARS = 120;

/**
 * Reusable three-way-frame card. One visual treatment, three interaction
 * modes (button / external link / static). Consumed by Overall Tone,
 * Propaganda, and Political Narratives — each supplies its own stats,
 * the card handles profile + avatar + blurb + lean chip + optional
 * "reads as" line.
 *
 * Callers that need a detail modal own the modal state themselves and
 * pass `onClick` to open it. The card no longer ships a built-in modal.
 */
export function EntityProfileCard({
    profile,
    stats,
    readsAs,
    onClick,
    href,
    ariaLabel,
    title,
    emptyNote = 'Tracked — no coverage in this window yet.',
}: EntityProfileCardProps) {
    const lean = leanClass(profile);
    const hasStats = stats.length > 0;
    const clamped = profile.blurb.length > BLURB_MAX_CHARS
        ? profile.blurb.slice(0, BLURB_MAX_CHARS).trimEnd() + '…'
        : profile.blurb;
    const chipLabel = entityChipLabel(profile);
    const emphasisStat = stats.find((s) => s.emphasis) ?? stats[0];
    const autoAria = emphasisStat
        ? `${profile.displayName}: ${emphasisStat.label} ${emphasisStat.value}. ${onClick ? 'Open details.' : href ? 'Opens in a new tab.' : ''}`
        : `${profile.displayName}: tracked, no coverage yet.`;

    const className = `entity-card lean-${lean}${hasStats ? '' : ' entity-card-empty'}`;

    const content = (
        <>
            <div className="entity-card-head">
                <EntityAvatar profile={profile} />
                <div className="entity-card-head-text">
                    <h4 className="entity-card-name">{profile.displayName}</h4>
                    {chipLabel && (
                        <span className={`entity-card-chip lean-chip-${lean}`}>{chipLabel}</span>
                    )}
                </div>
            </div>

            {clamped && <p className="entity-card-blurb">{clamped}</p>}

            {hasStats ? (
                <div className="entity-card-stats">
                    {stats.map((s) => (
                        <span key={s.label} className="entity-card-stat">
                            <span
                                className="entity-card-stat-value"
                                style={s.color ? { color: s.color } : undefined}
                            >
                                {s.value}
                            </span>
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

    const sharedProps = {
        className,
        'aria-label': ariaLabel ?? autoAria,
        title: title ?? profile.blurb,
    };

    if (onClick) {
        return <button type="button" onClick={onClick} {...sharedProps}>{content}</button>;
    }
    if (href) {
        return <a href={href} target="_blank" rel="noreferrer" {...sharedProps}>{content}</a>;
    }
    return <div {...sharedProps}>{content}</div>;
}

export default EntityProfileCard;


// --------------------------------------------------------------------------- //
//  Shared helpers — exported so Tone/Propaganda/Narratives all use the same    //
//  avatar + outbound-link resolution rules.                                    //
// --------------------------------------------------------------------------- //

/**
 * Small round/square visual for a registry entity. Outlets get the
 * Google favicon service; officials get unavatar.io proxying the X
 * profile image; subreddits + catch-alls get a letter monogram.
 *
 * Both external services are free, no-key, CDN-cached. Image errors
 * fall through to the monogram via the <img onError> handler.
 */
export function EntityAvatar({ profile }: { profile: EntityProfile }) {
    const monogramLetter = (profile.displayName || '?').trim().charAt(0).toUpperCase();

    if (profile.kind === 'outlet') {
        return (
            <span className="entity-avatar entity-avatar-img" aria-hidden>
                <img
                    src={`https://www.google.com/s2/favicons?domain=${profile.key}&sz=64`}
                    alt=""
                    width={32}
                    height={32}
                    loading="lazy"
                    referrerPolicy="no-referrer"
                    onError={(e) => { e.currentTarget.style.display = 'none'; }}
                />
            </span>
        );
    }
    if (profile.kind === 'official' && profile.key && !profile.key.includes('-')) {
        return (
            <span className="entity-avatar entity-avatar-img" aria-hidden>
                <img
                    src={`https://unavatar.io/twitter/${profile.key}?fallback=false`}
                    alt=""
                    width={32}
                    height={32}
                    loading="lazy"
                    referrerPolicy="no-referrer"
                    onError={(e) => { e.currentTarget.style.display = 'none'; }}
                />
            </span>
        );
    }
    return <span className="entity-avatar entity-avatar-mono" aria-hidden>{monogramLetter}</span>;
}

/** External URL for the entity — outlet homepage, X profile, or subreddit. */
export function entityExternalUrl(profile: EntityProfile): string | null {
    if (profile.kind === 'outlet' && profile.key) return `https://${profile.key}`;
    if (profile.kind === 'official' && profile.key && !profile.key.includes('-')) {
        return `https://x.com/${profile.key}`;
    }
    if (profile.kind === 'subreddit' && profile.key) return `https://reddit.com/r/${profile.key}`;
    return null;
}

/** Short chip label. Officials show party letter; outlets/subreddits show lean/tilt; catch-alls none. */
export function entityChipLabel(profile: EntityProfile): string | null {
    if (profile.kind === 'catch_all') return null;
    if (profile.kind === 'official') return profile.party || null;
    return profile.lean || null;
}

/** Accent color for this entity's lean — matches the `.lean-*` CSS rules. */
export function entityLeanAccent(profile: EntityProfile): string {
    const l = leanClass(profile);
    switch (l) {
        case 'left':    return COLORS.chartAccent;
        case 'right':   return COLORS.negative;
        case 'mixed':   return COLORS.warning;
        case 'neutral': return 'var(--neutral-400)';
        case 'center':
        default:        return 'var(--neutral-500)';
    }
}


// --------------------------------------------------------------------------- //
//  Reusable helpers for callers that own a detail modal                        //
// --------------------------------------------------------------------------- //

/** Build the sentiment-page stats trio from raw values. Handy so the
 *  Overall Tone page doesn't repeat the color/emphasis logic inline.
 *  Confidence is intentionally not stamped here: the sentiment aggregator
 *  currently hardcodes a single "medium" value, so surfacing it per-entity
 *  read as a live, computed trust signal it isn't. It returns once the
 *  aggregator derives real per-entity confidence. */
export function sentimentStats({
    netTone, volume,
}: {
    netTone: number; volume: number;
}): EntityStat[] {
    const color = netTone > 10 ? COLORS.positive
        : netTone < -10 ? COLORS.negative
        : 'var(--neutral-500)';
    return [
        { label: 'How they lean', value: formatPct(netTone, { min: -100, signed: true }), color, emphasis: true },
        { label: 'Posts', value: volume.toLocaleString() },
    ];
}


/** Entity profile + blurb + avatar rendered as a standalone block.
 *  Useful inside modals where the card's compact layout doesn't fit —
 *  e.g. Overall Tone's drill-down modal wants a bigger avatar and the
 *  full (un-clamped) blurb. */
export function EntityHeader({ profile, extra }: { profile: EntityProfile; extra?: ReactNode }) {
    return (
        <div className="entity-modal-head">
            <EntityAvatar profile={profile} />
            <div>
                <p className="lead" style={{ margin: 0 }}>{profile.blurb}</p>
                {extra}
            </div>
        </div>
    );
}
