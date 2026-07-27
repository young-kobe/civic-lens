import type { ReactNode } from 'react';
import type { EntityProfile, ReceivedTone } from '../../types';
import { COLORS } from '../../theme';
import { formatPts } from '../../services/format';
import { sourceGroupLabel, topGroupsByShare } from '../../services/provenanceLabels';

/** One stat cell in the card's stats row. */
export interface EntityStat {
    label: string;
    value: string;
    /** Optional color override for the value (e.g. tone red/green). */
    color?: string;
    /** Sort this stat visually prominent; at most one per card. */
    emphasis?: boolean;
    /** Optional hover tooltip on the stat (e.g. the net-tone definition). */
    title?: string;
    /** Optional one-line displayed context under the stats row (e.g.
     *  received-tone provenance) -- unlike `title`, this renders visibly,
     *  not just on hover. */
    hint?: string;
    /** Optional mini axis bar under the value — a visual anchor so a bare
     *  number like "-12.3 points" reads at a glance. Mirrors TierRow's dot-
     *  on-axis language. */
    bar?: {
        /** Dot position, 0-100 along the axis. */
        pct: number;
        color?: string;
        /** Render a midpoint tick (for -100..+100 tone axes). */
        zeroTick?: boolean;
    };
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
    /** 'compact' drops the blurb — for grids where entity identity is
     *  secondary to the page's own signal (e.g. Narratives). */
    variant?: 'full' | 'compact';
}

const BLURB_MAX_CHARS = 120;

/**
 * Local port of the pre-cutover `theme.ts` `leanClass(profile)` algorithm
 * (restored per docs/todos/ui-feature-restoration.md). Kept local rather
 * than reusing the current shared `theme.ts` helper: that helper was
 * rewritten for the Phase 9 LeanLabel-driven components to take the
 * flattened `corpus.political_lean` enum string (democrat/republican/...),
 * a different contract than this card's `EntityProfileModel`-driven raw
 * `lean`/`party` text (`lean` is `entities.lean_source`, the pre-flattening
 * citation string -- see `EntityProfileModel`'s docstring in
 * analysis/src/api/models/common.py). This restores the original
 * profile-shaped algorithm without changing that shared file.
 */
export function entityLeanClass(profile: EntityProfile): 'left' | 'center' | 'right' | 'mixed' | 'neutral' {
    if (profile.kind === 'catch_all') return 'neutral';
    if (profile.kind === 'official') {
        if (profile.party === 'R') return 'right';
        if (profile.party === 'D') return 'left';
        return 'neutral';
    }
    const l = (profile.lean || 'center').toLowerCase();
    if (l === 'mixed') return 'mixed';
    if (l.includes('left')) return 'left';
    if (l.includes('right')) return 'right';
    return 'center';
}

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
    variant = 'full',
}: EntityProfileCardProps) {
    const lean = entityLeanClass(profile);
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
                        <span
                            className={`entity-card-chip lean-chip-${lean}`}
                            title={entityChipTitle(profile)}
                        >
                            {chipLabel}
                        </span>
                    )}
                </div>
            </div>

            {variant === 'full' && clamped && <p className="entity-card-blurb">{clamped}</p>}

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

            {hasStats && stats.some((s) => s.hint) && (
                <p className="entity-card-stat-hint">
                    {stats.filter((s) => s.hint).map((s) => s.hint).join(' ')}
                </p>
            )}
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
    if ((profile.kind === 'official' || profile.kind === 'account') && profile.key && !profile.key.includes('-')) {
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
    if ((profile.kind === 'official' || profile.kind === 'account') && profile.key && !profile.key.includes('-')) {
        return `https://x.com/${profile.key}`;
    }
    if (profile.kind === 'subreddit' && profile.key) return `https://reddit.com/r/${profile.key}`;
    return null;
}

/** Short chip label. Officials/accounts show party letter; outlets/subreddits show lean/tilt; catch-alls none. */
export function entityChipLabel(profile: EntityProfile): string | null {
    if (profile.kind === 'catch_all') return null;
    if (profile.kind === 'official' || profile.kind === 'account') return profile.party || null;
    return profile.lean || null;
}

const PARTY_NAMES: Record<string, string> = {
    D: 'Democrat', R: 'Republican', I: 'Independent', L: 'Libertarian', G: 'Green',
};

/** Hover tooltip that spells out the bare "R" / "left" chip so a reader
 *  who doesn't know the shorthand can still read the card. */
export function entityChipTitle(profile: EntityProfile): string | undefined {
    if (profile.kind === 'catch_all') return undefined;
    if (profile.kind === 'official' || profile.kind === 'account') {
        if (!profile.party) return undefined;
        return `Party: ${PARTY_NAMES[profile.party] ?? profile.party}`;
    }
    if (!profile.lean) return undefined;
    const noun = profile.kind === 'subreddit' ? 'community' : 'outlet';
    const base = `Typical editorial lean of this ${noun}: ${profile.lean}`;
    return profile.leanSource ? `${base} (per ${profile.leanSource})` : base;
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
/** Map a -100..+100 net score onto the 0..100 mini-bar axis. */
function toneBar(net: number): NonNullable<EntityStat['bar']> {
    return {
        pct: ((Math.max(-100, Math.min(100, net)) + 100) / 200) * 100,
        color: toneStatColor(net),
        zeroTick: true,
    };
}

export function sentimentStats({
    netTone, volume,
}: {
    netTone: number; volume: number;
}): EntityStat[] {
    const color = netTone > 10 ? COLORS.positive
        : netTone < -10 ? COLORS.negative
        : 'var(--neutral-500)';
    return [
        {
            label: 'Net tone',
            value: formatPts(netTone),
            color,
            emphasis: true,
            title: "Positive minus negative share of this source's posts, from -100 (all negative) to +100 (all positive).",
            bar: toneBar(netTone),
        },
        { label: 'Posts', value: volume.toLocaleString() },
    ];
}

const RECEIVED_TONE_TITLE =
    'Average tone of sampled posts that talk ABOUT this person, from -100 '
    + '(all negative) to +100 (all positive). This is the reputational '
    + 'signal — it does not include their own posts about others.';
const EXPRESSED_TONE_TITLE =
    "Average tone of this person's OWN posts, from -100 to +100. A very "
    + 'negative value means they post negatively (often about opponents) — '
    + 'it says nothing about how others talk about them.';

function toneStatColor(net: number): string {
    return net > 10 ? COLORS.positive
        : net < -10 ? COLORS.negative
        : 'var(--neutral-500)';
}

/** Stats trio for officials cards. Received tone (posts about them) is the
 *  emphasis stat when available; expressed tone (their own posts) is kept
 *  but explicitly labeled. Received nets below the aggregator's sample
 *  floor arrive as null and render as a low-sample note, never a number —
 *  one classified tweet must not read as +100.0. */
export function officialToneStats({
    received, netTone, volume,
}: {
    received?: ReceivedTone | null;
    netTone: number;
    volume: number;
}): EntityStat[] {
    const stats: EntityStat[] = [];
    if (received && received.volume > 0) {
        // "Mostly from ..." suffix — the two biggest provenance groups,
        // when the backend attached any (empty arrays render no suffix,
        // never a guess). Shares the group-label builder with
        // PublicSentiment's "Where this tone comes from" block so the
        // same group reads identically on both surfaces.
        const topGroups = topGroupsByShare(received.receivedFromGroups ?? []);
        const provenanceLine = topGroups.length > 0
            ? `Mostly from ${topGroups.map((g) => sourceGroupLabel(g.sourceClass, g.lean)).join(', ')}.`
            : '';
        stats.push({
            label: `Received tone (${received.volume.toLocaleString()} posts)`,
            value: received.net != null ? formatPts(received.net) : 'low sample',
            color: received.net != null ? toneStatColor(received.net) : 'var(--neutral-500)',
            emphasis: true,
            title: (received.net != null
                ? RECEIVED_TONE_TITLE
                : `Only ${received.volume} classified post${received.volume === 1 ? '' : 's'} `
                  + 'mention this person in this window — too few to score reliably.')
                + (provenanceLine ? ` ${provenanceLine}` : ''),
            hint: provenanceLine || undefined,
            bar: received.net != null ? toneBar(received.net) : undefined,
        });
    }
    if (volume > 0) {
        stats.push({
            label: 'Expressed tone',
            value: formatPts(netTone),
            color: toneStatColor(netTone),
            emphasis: stats.length === 0,
            title: EXPRESSED_TONE_TITLE,
            bar: toneBar(netTone),
        });
        stats.push({ label: 'Posts', value: volume.toLocaleString() });
    }
    return stats;
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
