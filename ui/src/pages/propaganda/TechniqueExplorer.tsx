import { useEffect, useState } from 'react';
import { Card, MethodPopover, Modal, TECHNIQUE_LABEL } from '../../components/common';
import { formatPct } from '../../services/format';
import { useDeepLinkParam } from '../../services/deepLink';
import { COLORS } from '../../theme';
import type { PartySplit, PropagandaTechniqueName, TechniqueCount } from '../../types';

// --------------------------------------------------------------------------- //
//  TechniqueExplorer — the Propaganda page's signature interaction.           //
//                                                                             //
//  A horizontal bar graphic: one bar per rhetorical technique, sized by how   //
//  many flagged posts carry it. Clicking a bar opens a modal of that          //
//  technique's stored evidence quotes (TechniqueCount.sampleEvidence).        //
//  Phase 10 note: the strictly-live /propaganda response no longer ties       //
//  evidence spans to a specific doc/example (PropagandaOverviewModel carries  //
//  no per-example technique breakdown), so this modal shows verbatim quotes   //
//  rather than filtered post cards.                                          //
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
    techniques: TechniqueCount[];
    parties?: PartySplit[];
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
                        <div key={p.party} className="party-bar-row" title={`Mean score ${p.meanScore.toFixed(2)} / 1`}>
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

export function TechniqueExplorer({ techniques, parties = [] }: TechniqueExplorerProps) {
    const [techParam, setTechParam] = useDeepLinkParam('technique');
    const [selected, setSelected] = useState<PropagandaTechniqueName | null>(() =>
        techParam && isTechniqueName(techParam) ? techParam : null);

    useEffect(() => {
        if (techParam && isTechniqueName(techParam)) setSelected(techParam);
        else if (!techParam) setSelected(null);
    }, [techParam]);

    const open = (name: PropagandaTechniqueName) => { setSelected(name); setTechParam(name); };
    const close = () => { setSelected(null); setTechParam(null); };

    const maxCount = techniques.reduce((m, t) => Math.max(m, t.count), 0);
    const selectedRow = selected ? techniques.find((t) => t.technique === selected) : undefined;

    return (
        <Card
            title="Techniques being used"
            subtitle="Each bar is a rhetorical technique, sized by how many flagged posts carry it. Click a bar to read its stored evidence quotes."
            headerActions={
                <MethodPopover
                    description={
                        'Each post is scored for six rhetorical techniques. The model must quote '
                        + 'a verbatim phrase from the source as evidence for every flag.'
                    }
                />
            }
        >
            <div className="technique-explorer-list" role="list" aria-label="Propaganda techniques">
                {techniques.map((t) => {
                    const name = t.technique as PropagandaTechniqueName;
                    const label = TECHNIQUE_LABEL[name] || t.technique;
                    const widthPct = maxCount > 0 ? (t.count / maxCount) * 100 : 0;
                    return (
                        <button
                            key={t.technique}
                            type="button"
                            role="listitem"
                            className="technique-explorer-row"
                            onClick={() => open(name)}
                            title={TECHNIQUE_BLURB[name]}
                        >
                            <span className="technique-explorer-row-name">{label}</span>
                            <span className="technique-explorer-row-bar" aria-hidden>
                                <span className="technique-explorer-row-fill" style={{ width: `${widthPct}%` }} />
                            </span>
                            <span className="technique-explorer-row-count">{t.count.toLocaleString()}</span>
                            <span className="technique-explorer-row-pct">{formatPct(t.pctOfFlaggedDocs, { decimals: 0 })}</span>
                        </button>
                    );
                })}
            </div>

            <ByPartySection parties={parties} />

            <Modal
                isOpen={selected !== null}
                onClose={close}
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
