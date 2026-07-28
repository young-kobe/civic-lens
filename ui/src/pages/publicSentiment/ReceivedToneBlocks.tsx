/**
 * Shared received-tone display blocks — `ToneBarRows` (dot-on-axis rows for
 * a topic/tier/narrative breakdown) and `ReceivedProvenanceBlock`
 * ("Where this tone comes from"). Moved out of `PublicSentiment.tsx` so
 * `PartyTonePanel.tsx` can reuse them without the two page modules
 * importing each other.
 */

import { COLORS } from '../../theme';
import { clampWidthPct, formatPct, formatPts } from '../../services/format';
import { sourceGroupLabel, topGroupsByShare } from '../../services/provenanceLabels';
import { EntityAvatar, entityChipLabel, entityChipTitle, entityLeanClass } from '../../components/common/EntityProfileCard';
import type { ReceivedSourceCell } from '../../types';

/** Net-tone color convention shared by every tone readout on this page. */
export function toneColor(net: number): string {
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return 'var(--neutral-500)';
}

/** Display labels for `ReceivedSpeakerTierCell.tier`. */
export const SPEAKER_TIER_LABELS: Record<string, string> = {
    news: 'News outlets',
    officials: 'Officials',
    affiliated: 'Politically affiliated accounts',
    public: 'General public',
};

export interface ToneBarRow {
    key: string | number;
    label: string;
    net: number | null;
    volume: number;
}

/** Dot-on-axis rows for a topic/tier/narrative breakdown. Suppressed nets
 *  ("low sample") stay words, never numbers. */
export function ToneBarRows({ rows }: { rows: ToneBarRow[] }) {
    return (
        <div className="tone-bar-rows">
            {rows.map((row) => (
                <div key={row.key} className="tone-bar-row" title={row.net != null
                    ? `${row.label}: ${formatPts(row.net)} across ${row.volume} posts`
                    : `${row.label}: only ${row.volume} post${row.volume === 1 ? '' : 's'} — too few to score reliably`}
                >
                    {/* title repeats the label so an ellipsized narrative/topic
                        name is still readable on hover. */}
                    <span className="tone-bar-row-label" title={row.label}>{row.label}</span>
                    <span className="tone-bar-row-axis" aria-hidden>
                        <span className="tone-bar-row-zero" />
                        {row.net != null && (
                            <span
                                className="tone-bar-row-dot"
                                style={{
                                    left: `${((Math.max(-100, Math.min(100, row.net)) + 100) / 200) * 100}%`,
                                    background: toneColor(row.net),
                                }}
                            />
                        )}
                    </span>
                    <span className="tone-bar-row-value" style={row.net != null ? { color: toneColor(row.net) } : undefined}>
                        {row.net != null ? formatPts(row.net) : 'low sample'}
                    </span>
                    <span className="tone-bar-row-n">{row.volume} post{row.volume === 1 ? '' : 's'}</span>
                </div>
            ))}
        </div>
    );
}

// --------------------------------------------------------------------------- //
//  Received-tone provenance — "Where this tone comes from" (WHO the mentions //
//  come from, at the source level: outlet/official/account/subreddit/x-user //
//  x lean groups, plus up to 8 named sources). Renders nothing when the      //
//  backend attached no provenance cells — never a guessed breakdown.        //
// --------------------------------------------------------------------------- //

function ProvenanceGroupRow({ cell }: { cell: ReceivedSourceCell }) {
    const label = sourceGroupLabel(cell.sourceClass, cell.lean);
    const metaText = cell.net != null
        ? `${cell.volume.toLocaleString()} posts · ${formatPts(cell.net)}`
        : `${cell.volume} post${cell.volume === 1 ? '' : 's'} · too few to score reliably`;
    return (
        <div
            className="provenance-group-row"
            title={`${label}: ${formatPct(cell.share * 100, { decimals: 0 })} of sampled mentions, ${metaText}`}
        >
            <span className="provenance-group-label">{label}</span>
            <span className="provenance-group-share-track" aria-hidden>
                <span
                    className="provenance-group-share-fill"
                    style={{ width: `${clampWidthPct(cell.share * 100)}%` }}
                />
            </span>
            <span className="provenance-group-share-value">
                {formatPct(cell.share * 100, { decimals: 0 })}
            </span>
            <span className="provenance-group-meta">{metaText}</span>
        </div>
    );
}

function ProvenanceTopRow({ cell }: { cell: ReceivedSourceCell }) {
    const profile = cell.entityProfile;
    const chipLabel = profile ? entityChipLabel(profile) : null;
    const lean = profile ? entityLeanClass(profile) : 'neutral';
    const name = profile ? profile.displayName : `@${cell.label}`;
    const metaText = cell.net != null
        ? `${formatPct(cell.share * 100, { decimals: 0 })} · ${cell.volume.toLocaleString()} posts · ${formatPts(cell.net)}`
        : `${formatPct(cell.share * 100, { decimals: 0 })} · ${cell.volume} post${cell.volume === 1 ? '' : 's'} · too few to score reliably`;
    return (
        <div className="provenance-top-row">
            <span className="provenance-top-identity">
                {profile ? (
                    <EntityAvatar profile={profile} />
                ) : (
                    <span className="entity-avatar entity-avatar-mono" aria-hidden>
                        {(cell.label || '?').trim().charAt(0).toUpperCase()}
                    </span>
                )}
                <span className="provenance-top-name">{name}</span>
                {chipLabel && (
                    <span className={`entity-card-chip lean-chip-${lean}`} title={entityChipTitle(profile!)}>
                        {chipLabel}
                    </span>
                )}
            </span>
            <span className="provenance-top-meta">{metaText}</span>
        </div>
    );
}

export function ReceivedProvenanceBlock({
    displayName, groups, top,
}: {
    displayName: string;
    groups: ReceivedSourceCell[];
    top: ReceivedSourceCell[];
}) {
    if (groups.length === 0 && top.length === 0) return null;
    const leadGroups = topGroupsByShare(groups, 2);
    const lead = leadGroups.length > 0
        ? `Most of the tone aimed at ${displayName} comes from `
            + leadGroups
                .map((g) => `${sourceGroupLabel(g.sourceClass, g.lean)} (${formatPct(g.share * 100, { decimals: 0 })})`)
                .join(' and ')
            + '.'
        : null;

    return (
        <>
            <h3 className="card-title mt-4 mb-2">Where this tone comes from</h3>
            <p className="modal-section-lede">
                {lead ?? 'WHO the mentions come from, at the source level — a sample breakdown, not a complete accounting.'}
            </p>
            {groups.length > 0 && (
                <div className="provenance-groups">
                    {groups.map((g, i) => (
                        <ProvenanceGroupRow key={`${g.sourceClass}-${g.lean ?? 'none'}-${i}`} cell={g} />
                    ))}
                </div>
            )}
            {top.length > 0 && (
                <div className="provenance-top-list" style={{ marginTop: 'var(--space-2)' }}>
                    {top.map((cell, i) => (
                        <ProvenanceTopRow key={cell.entityKey ?? `${cell.label}-${i}`} cell={cell} />
                    ))}
                </div>
            )}
            <p className="text-xs text-muted">
                Share of sampled mentions in this window — not a complete accounting of who
                talks about {displayName}.
            </p>
        </>
    );
}
