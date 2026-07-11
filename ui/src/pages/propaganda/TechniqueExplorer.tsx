import { useEffect, useState } from 'react';
import {
    Card, MethodPopover, Modal, PostCardList, propagandaExampleToPostCard,
} from '../../components/common';
import { dedupeById } from '../../services/dedupe';
import { formatPct } from '../../services/format';
import { useDeepLinkParam } from '../../services/deepLink';
import type {
    PropagandaExample, PropagandaTechniqueCount, PropagandaTechniqueName,
} from '../../types';

// --------------------------------------------------------------------------- //
//  TechniqueExplorer — the Propaganda page's signature interaction.           //
//                                                                             //
//  A horizontal bar graphic: one bar per rhetorical technique, sized by how   //
//  many flagged posts carry it. Hovering a bar surfaces a plain-language      //
//  explanation of the technique; clicking opens a modal of the flagged posts  //
//  carrying it, each with THAT technique's evidence span highlighted inline.  //
//  Deep-linkable via #propaganda?technique=<name> (opens the modal).          //
// --------------------------------------------------------------------------- //

const TECHNIQUE_LABEL: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Loaded language',
    name_calling: 'Name-calling',
    ad_hominem: 'Ad hominem',
    appeal_to_fear: 'Appeal to fear',
    whataboutism: 'Whataboutism',
    doubt_casting: 'Doubt-casting',
};

const TECHNIQUE_BLURB: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Emotionally charged framing designed to influence rather than inform.',
    name_calling: 'Dismissive labels applied to a person or group instead of engaging their argument.',
    ad_hominem: "Attacks on the speaker's character rather than the substance of what they said.",
    appeal_to_fear: 'Fear or catastrophic imagery used to bypass reasoning.',
    whataboutism: 'Deflecting criticism by pointing at an unrelated alleged misconduct by the other side.',
    doubt_casting: 'Insinuation of wrongdoing or unreliability without offering evidence.',
};

interface TechniqueExplorerProps {
    techniques: PropagandaTechniqueCount[];
    examples: PropagandaExample[];
}

function isTechniqueName(value: string): value is PropagandaTechniqueName {
    return value in TECHNIQUE_LABEL;
}

export function TechniqueExplorer({ techniques, examples }: TechniqueExplorerProps) {
    const [techParam, setTechParam] = useDeepLinkParam('technique');
    // The opened technique (modal). Null = no modal. A deep link opens it on
    // load; otherwise it stays closed until the reader clicks a bar.
    const [selected, setSelected] = useState<PropagandaTechniqueName | null>(() =>
        techParam && isTechniqueName(techParam) ? techParam : null);

    // Follow incoming deep-link changes (e.g. back/forward).
    useEffect(() => {
        if (techParam && isTechniqueName(techParam)) {
            setSelected(techParam);
        } else if (!techParam) {
            setSelected(null);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [techParam]);

    const open = (name: PropagandaTechniqueName) => {
        setSelected(name);
        setTechParam(name);
    };
    const close = () => {
        setSelected(null);
        setTechParam(null);
    };

    const maxCount = techniques.reduce((m, t) => Math.max(m, t.count), 0);
    const matching = selected
        ? dedupeById(
            examples.filter((ex) => ex.techniques.some((t) => t.technique === selected)),
            (ex) => ex.doc_id,
        )
        : [];

    // Highlight ONLY the opened technique's spans so the reader connects the
    // highlighted words to the technique they picked.
    const cards = matching.map((ex) => ({
        ...propagandaExampleToPostCard(ex),
        techniques: ex.techniques.filter((t) => t.technique === selected),
    }));

    return (
        <Card
            title="Techniques being used"
            subtitle="Each bar is a rhetorical technique, sized by how many flagged posts carry it. Hover a bar for what it means; click to read the flagged posts carrying it. One post can count toward multiple techniques."
            headerActions={
                <MethodPopover
                    description={
                        'Each post is scored for six rhetorical techniques. The model must quote '
                        + 'a verbatim phrase from the source as evidence for every flag. A flag '
                        + "measures rhetorical style — not truth, intent, or whether the post is "
                        + "'propaganda' in the everyday sense."
                    }
                />
            }
        >
            <div className="technique-explorer-list" role="list" aria-label="Propaganda techniques">
                {techniques.map((t) => {
                    const name = t.technique as PropagandaTechniqueName;
                    const label = TECHNIQUE_LABEL[name] || t.technique;
                    const blurb = TECHNIQUE_BLURB[name];
                    const widthPct = maxCount > 0 ? (t.count / maxCount) * 100 : 0;
                    return (
                        <button
                            key={t.technique}
                            type="button"
                            role="listitem"
                            className="technique-explorer-row"
                            onClick={() => open(name)}
                            title={blurb || undefined}
                            aria-label={`${label}: ${t.count.toLocaleString()} flagged posts, ${formatPct(t.pct_of_flagged_docs, { decimals: 0 })}. ${blurb ?? ''} Open the flagged posts.`}
                        >
                            <span className="technique-explorer-row-name">{label}</span>
                            <span className="technique-explorer-row-bar" aria-hidden>
                                <span
                                    className="technique-explorer-row-fill"
                                    style={{ width: `${widthPct}%` }}
                                />
                            </span>
                            <span className="technique-explorer-row-count">
                                {t.count.toLocaleString()}
                            </span>
                            <span className="technique-explorer-row-pct">
                                {formatPct(t.pct_of_flagged_docs, { decimals: 0 })}
                            </span>
                        </button>
                    );
                })}
            </div>

            <Modal
                isOpen={selected !== null}
                onClose={close}
                kicker="Propaganda technique"
                title={selected ? (TECHNIQUE_LABEL[selected] || selected) : ''}
                subtitle={selected ? TECHNIQUE_BLURB[selected] : undefined}
            >
                <PostCardList
                    posts={cards}
                    sampleNote={selected
                        ? `Flagged posts carrying ${TECHNIQUE_LABEL[selected]} — a sample, not a complete feed.`
                        : ''}
                    emptyNote={selected
                        ? `No stored examples carry ${TECHNIQUE_LABEL[selected]} in this window. Examples are a capped sample; the counts on the bars cover all flagged posts.`
                        : ''}
                />
            </Modal>
        </Card>
    );
}

export default TechniqueExplorer;
