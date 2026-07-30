import { useEffect, useMemo, useState } from 'react';
import { Card, MethodPopover, Modal, TECHNIQUE_LABEL } from '../../components/common';
import { formatPct } from '../../services/format';
import { useDeepLinkParam } from '../../services/deepLink';
import { dedupeById } from '../../services/dedupe';
import { COLORS } from '../../theme';
import type {
    PartySplit, PropagandaOverview, PropagandaTechniqueName, TechniqueCount,
} from '../../types';
import {
    ConstellationDot, ConstellationTier, DensityConstellation,
} from './DensityConstellation';

// --------------------------------------------------------------------------- //
//  TechniqueExplorer — the Propaganda page's signature card.                  //
//                                                                             //
//  A density constellation (every dot one flagged post, positioned by         //
//  technique density, colored by speaker tier) fronted by a row of technique  //
//  chips. A chip is both legend and filter: clicking isolates that            //
//  technique's dots and deep-links via ?technique=; the selected chip offers  //
//  the technique's stored evidence quotes (TechniqueCount.sampleEvidence)     //
//  in a modal.                                                                //
// --------------------------------------------------------------------------- //

const TECHNIQUE_BLURB: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Emotionally charged framing designed to influence rather than inform.',
    name_calling: 'Dismissive labels applied to a person or group instead of engaging their argument.',
    ad_hominem: "Attacks on the speaker's character rather than the substance of what they said.",
    appeal_to_fear: 'Fear or catastrophic imagery used to bypass reasoning.',
    whataboutism: 'Deflecting criticism by pointing at an unrelated alleged misconduct by the other side.',
    doubt_casting: 'Insinuation of wrongdoing or unreliability without offering evidence.',
};

interface TechniqueExplorerProps {
    data: PropagandaOverview;
}

function isTechniqueName(value: string): value is PropagandaTechniqueName {
    return value in TECHNIQUE_LABEL;
}

const PARTY_ACCENT: Record<string, string> = { republican: COLORS.leanRight, democrat: COLORS.leanLeft };
const LOW_SAMPLE_DOCS = 30;

function partyLabel(party: string): string {
    if (party === 'unknown') return 'Unknown';
    return party.charAt(0).toUpperCase() + party.slice(1);
}

function ByPartySection({ parties }: { parties: PartySplit[] }) {
    const known = parties.filter((p) => p.party !== 'unknown' && p.totalDocs > 0);
    if (known.length === 0) return null;
    const lowSample = known.filter((p) => p.totalDocs < LOW_SAMPLE_DOCS);
    return (
        <div className="technique-by-party">
            <div className="eyebrow technique-by-party-title" title="A density measure, not intent.">
                By party
            </div>
            <p className="text-xs text-muted" style={{ margin: '0 0 var(--space-2)' }}>
                Share of each party's scored posts flagged for any technique — an overall rate, not a
                breakdown of the techniques above.
            </p>
            <div className="party-bars">
                {known.map((p) => {
                    const accent = PARTY_ACCENT[p.party] ?? 'var(--neutral-500)';
                    return (
                        <div key={p.party} className="party-bar-row" title={`Saturation ${p.meanScore.toFixed(2)} / 1`}>
                            <span className="party-bar-label">{partyLabel(p.party)}</span>
                            <span className="party-bar-track" aria-hidden>
                                <span className="party-bar-fill" style={{ width: `${p.flaggedRatePct}%`, background: accent }} />
                            </span>
                            <span className="party-bar-value">{formatPct(p.flaggedRatePct)}</span>
                            <span className="party-bar-meta">
                                {p.flaggedDocs.toLocaleString()} of {p.totalDocs.toLocaleString()} posts
                            </span>
                        </div>
                    );
                })}
            </div>
            {lowSample.length > 0 && (
                <p className="text-xs text-muted" style={{ margin: 'var(--space-2) 0 0' }}>
                    Low sample: {lowSample.map((p) => `${partyLabel(p.party)} has only ${p.totalDocs.toLocaleString()} scored posts`).join('; ')}.
                </p>
            )}
        </div>
    );
}

function TechniqueChip({
    row, selected, onToggle,
}: {
    row: TechniqueCount;
    selected: boolean;
    onToggle: (name: PropagandaTechniqueName) => void;
}) {
    const name = row.technique as PropagandaTechniqueName;
    const label = TECHNIQUE_LABEL[name] || row.technique;
    return (
        <button
            type="button"
            className={`technique-chip${selected ? ' technique-chip-selected' : ''}${row.count === 0 ? ' technique-chip-zero' : ''}`}
            aria-pressed={selected}
            onClick={() => onToggle(name)}
            title={TECHNIQUE_BLURB[name]}
        >
            <span className="technique-chip-label">{label}</span>
            <span className="technique-chip-count">{row.count.toLocaleString()}</span>
            <span className="technique-chip-pct">{formatPct(row.pctOfFlaggedDocs, { decimals: 0 })}</span>
        </button>
    );
}

export function TechniqueExplorer({ data }: TechniqueExplorerProps) {
    const [techParam, setTechParam] = useDeepLinkParam('technique');
    const [selected, setSelected] = useState<PropagandaTechniqueName | null>(() =>
        techParam && isTechniqueName(techParam) ? techParam : null);
    const [evidenceOpen, setEvidenceOpen] = useState(false);

    useEffect(() => {
        if (techParam && isTechniqueName(techParam)) setSelected(techParam);
        else if (!techParam) setSelected(null);
    }, [techParam]);

    const toggle = (name: PropagandaTechniqueName) => {
        const next = selected === name ? null : name;
        setSelected(next);
        setTechParam(next);
        if (next === null) setEvidenceOpen(false);
    };
    const closeEvidence = () => setEvidenceOpen(false);

    // Pool the per-entity flagged examples into constellation dots. Tier
    // comes from which leaderboard the example's entity key belongs to
    // (the backend builds examplesByEntity in lockstep with those keys);
    // an unmatched key falls back on the doc's own source_type.
    const dots = useMemo<ConstellationDot[]>(() => {
        const tierByKey = new Map<string, ConstellationTier>();
        for (const it of data.byNewsOutlet ?? []) tierByKey.set(it.key, 'news');
        for (const it of data.byOfficial ?? []) tierByKey.set(it.key, 'officials');
        for (const it of data.byGeneralPublic ?? []) tierByKey.set(it.key, 'public');
        const all: ConstellationDot[] = [];
        for (const [key, examples] of Object.entries(data.examplesByEntity ?? {})) {
            const tier = tierByKey.get(key);
            for (const example of examples) {
                all.push({
                    example,
                    tier: tier ?? (example.sourceType === 'news' ? 'news' : 'public'),
                });
            }
        }
        return dedupeById(all, (d) => d.example.docId);
    }, [data]);

    const selectedRow = selected
        ? data.byTechnique.find((t) => t.technique === selected)
        : undefined;

    return (
        <Card
            title="Technique density, post by post"
            subtitle="Every dot is a flagged post, placed by how saturated it is with persuasion techniques and colored by who said it. Click a technique to isolate its posts."
            note="A sample of flagged posts (capped per speaker), not the full corpus. Density measures rhetorical style, not truth or intent."
            headerActions={
                <MethodPopover
                    description={
                        'Each post is scored for six rhetorical techniques; the model must quote a '
                        + 'verbatim phrase from the source as evidence for every flag. Density runs 0 '
                        + '(no techniques) to 1 (wall-to-wall). The dots are the flagged-example pool '
                        + '(up to 5 posts per speaker), colored News / Officials / Public.'
                    }
                />
            }
        >
            <div className="technique-chip-row" role="group" aria-label="Propaganda techniques">
                {data.byTechnique.map((t) => (
                    <TechniqueChip
                        key={t.technique}
                        row={t}
                        selected={selected === t.technique}
                        onToggle={toggle}
                    />
                ))}
            </div>
            {selected && selectedRow && (
                <div className="technique-chip-detail">
                    <span className="text-xs text-muted">{TECHNIQUE_BLURB[selected]}</span>
                    <button
                        type="button"
                        className="technique-evidence-link"
                        onClick={() => setEvidenceOpen(true)}
                    >
                        Read evidence quotes
                    </button>
                </div>
            )}

            <DensityConstellation dots={dots} selectedTechnique={selected} />

            <ByPartySection parties={data.byParty ?? []} />

            <Modal
                isOpen={evidenceOpen && selected !== null}
                onClose={closeEvidence}
                kicker="Propaganda technique"
                title={selected ? (TECHNIQUE_LABEL[selected] || selected) : ''}
                subtitle={selected ? TECHNIQUE_BLURB[selected] : undefined}
            >
                {selectedRow && selectedRow.sampleEvidence.length > 0 ? (
                    <ul className="technique-evidence-list">
                        {selectedRow.sampleEvidence.map((quote, i) => (
                            <li key={i}><em>"{quote}"</em></li>
                        ))}
                    </ul>
                ) : (
                    <p className="text-sm text-muted">
                        No stored evidence quotes for this technique in this window.
                    </p>
                )}
            </Modal>
        </Card>
    );
}

export default TechniqueExplorer;
