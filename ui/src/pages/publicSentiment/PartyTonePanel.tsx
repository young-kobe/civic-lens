/**
 * "How the parties are talked about" — renders `TargetToneMeta.collectives`
 * (`gop_collective`/`dem_collective`), the received-tone rollup for posts
 * that mention a party collectively ("the GOP", "Democrats") rather than a
 * tracked individual. This is the sole surface for that data (docs/todos/
 * provenance-and-plain-language.md, "Party-collective received-tone
 * provenance has no UI surface" follow-up) -- it predates the 2026-07-27
 * provenance wave and has no `EntitySentimentItem` to attach to, so it gets
 * its own two-card panel instead of reusing the entity modal.
 */

import { useState } from 'react';
import { Card, CollapsibleInfo, DefinitionChip } from '../../components/common';
import { PostCardList, sampleToPostCard } from '../../components/common/PostCard';
import type { ReceivedTone } from '../../types';
import { formatPts } from '../../services/format';
import { COLORS } from '../../theme';
import { ReceivedProvenanceBlock, SPEAKER_TIER_LABELS, ToneBarRows } from './ReceivedToneBlocks';

/** Net-tone color convention shared by every tone readout on this page
 *  (duplicated per-module rather than imported -- see OutletSignalsPanel/
 *  PublicSentiment for the same three-line mapping). */
function toneColor(net: number): string {
    if (net > 10) return COLORS.positive;
    if (net < -10) return COLORS.negative;
    return 'var(--neutral-500)';
}

const TOP_TOPIC_ROWS = 5;

function PartyCollectiveCard({
    title, accentColor, tone,
}: {
    title: string;
    accentColor: string;
    tone: ReceivedTone | undefined;
}) {
    const [samplesOpen, setSamplesOpen] = useState(false);
    const volume = tone?.volume ?? 0;

    if (!tone || volume === 0) {
        return (
            <div className="surface-card">
                <div className="eyebrow" style={{ color: accentColor }}>{title}</div>
                <p className="text-sm text-muted" style={{ marginTop: 'var(--space-2)' }}>
                    No sampled posts mention {title.toLowerCase()} collectively in this window.
                </p>
            </div>
        );
    }

    const hasNet = tone.net != null;
    const topTopics = [...tone.byTopic]
        .sort((a, b) => b.volume - a.volume)
        .slice(0, TOP_TOPIC_ROWS);

    return (
        <div className="surface-card">
            <div className="eyebrow" style={{ color: accentColor }}>{title}</div>
            <div
                className="metric-value"
                style={{ color: hasNet ? toneColor(tone.net!) : 'var(--neutral-500)', marginTop: 'var(--space-1)' }}
            >
                {hasNet ? formatPts(tone.net) : '—'}
            </div>
            <div className="text-xs text-muted">
                {hasNet ? (
                    <>
                        {volume.toLocaleString()} post{volume === 1 ? '' : 's'}
                        {tone.engagementWeightedNet != null && (
                            <>
                                {' · '}
                                <DefinitionChip entry="engagement_weighted" label="engagement-weighted" />
                                {' '}
                                {formatPts(tone.engagementWeightedNet)}
                            </>
                        )}
                    </>
                ) : (
                    <>
                        only {volume} sampled post{volume === 1 ? '' : 's'} — <DefinitionChip entry="low_sample" label="too few to score reliably" />
                    </>
                )}
            </div>

            {tone.bySpeakerTier.length > 0 && (
                <>
                    <h4 className="card-title mt-4 mb-2">Who is talking about {title.toLowerCase()}</h4>
                    <ToneBarRows
                        rows={tone.bySpeakerTier.map((cell) => ({
                            key: cell.tier,
                            label: SPEAKER_TIER_LABELS[cell.tier] ?? cell.tier,
                            net: cell.net,
                            volume: cell.volume,
                        }))}
                    />
                </>
            )}

            {topTopics.length > 0 && (
                <>
                    <h4 className="card-title mt-4 mb-2">Top topics</h4>
                    <ToneBarRows
                        rows={topTopics.map((cell) => ({
                            key: cell.topic, label: cell.topic, net: cell.net, volume: cell.volume,
                        }))}
                    />
                </>
            )}

            <ReceivedProvenanceBlock
                displayName={title}
                groups={tone.receivedFromGroups}
                top={tone.receivedFromTop}
            />

            {tone.samples.length > 0 && (
                <div className="mt-2">
                    <button
                        type="button"
                        className="btn btn-secondary"
                        onClick={() => setSamplesOpen((v) => !v)}
                    >
                        {samplesOpen ? 'Hide sample posts' : `Show sample posts (${tone.samples.length})`}
                    </button>
                    {samplesOpen && (
                        <div className="mt-2">
                            <PostCardList
                                posts={tone.samples.map(sampleToPostCard)}
                                sampleNote={`A sample of posts mentioning ${title.toLowerCase()} collectively, not a complete feed. Highlighted text is the evidence the model quoted.`}
                            />
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}

export function PartyTonePanel({
    collectives,
}: {
    collectives: Record<string, ReceivedTone> | null | undefined;
}) {
    const dem = collectives?.['dem_collective'];
    const gop = collectives?.['gop_collective'];
    const bothEmpty = (!dem || dem.volume === 0) && (!gop || gop.volume === 0);

    return (
        <Card
            title="How the parties are talked about"
            subtitle={(
                <>
                    Tone of sampled posts that mention a party collectively (e.g. "the GOP",
                    "Democrats") -- not individual officials, not party approval, and not a
                    poll of how Americans feel.
                </>
            )}
        >
            {bothEmpty ? (
                <p className="text-sm text-muted">
                    No sampled posts mention either party collectively in this window.
                </p>
            ) : (
                <div className="grid-2">
                    <PartyCollectiveCard title="Democratic Party" accentColor={COLORS.leanLeft} tone={dem} />
                    <PartyCollectiveCard title="Republican Party" accentColor={COLORS.leanRight} tone={gop} />
                </div>
            )}
            <CollapsibleInfo summary="How this panel is built">
                <p className="text-xs text-muted">
                    Posts that name a party by alias ("the GOP", "Democrats", "the left",
                    "the right") without matching a tracked official are rolled up here as a
                    party collective. Nets below the sample floor read "too few to score
                    reliably" instead of a number.
                </p>
            </CollapsibleInfo>
        </Card>
    );
}
