import { useEffect, useState } from 'react';
import {
    Card, MethodPopover, PostCardList, propagandaExampleToPostCard,
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
//  Merges the old TechniquesCard bar list with the flat examples feed:        //
//  selecting a technique on the left shows its flagged posts on the right,    //
//  each with THAT technique's evidence span highlighted inline. Deep-        //
//  linkable via #propaganda?technique=<name>.                                 //
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
    const defaultTechnique = techniques[0]?.technique ?? null;
    const [selected, setSelected] = useState<PropagandaTechniqueName | null>(() =>
        techParam && isTechniqueName(techParam) ? techParam : null);

    // Land on the top technique once data arrives, unless a deep link or a
    // click already chose one.
    useEffect(() => {
        if (selected === null && defaultTechnique) {
            setSelected(defaultTechnique as PropagandaTechniqueName);
        }
    }, [selected, defaultTechnique]);

    // Follow incoming deep-link changes (e.g. back/forward).
    useEffect(() => {
        if (techParam && isTechniqueName(techParam) && techParam !== selected) {
            setSelected(techParam);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [techParam]);

    const select = (name: PropagandaTechniqueName) => {
        setSelected(name);
        setTechParam(name);
    };

    const maxCount = techniques.reduce((m, t) => Math.max(m, t.count), 0);
    const matching = selected
        ? dedupeById(
            examples.filter((ex) => ex.techniques.some((t) => t.technique === selected)),
            (ex) => ex.doc_id,
        )
        : [];

    // Highlight ONLY the selected technique's spans so the reader connects
    // the highlighted words to the technique they picked.
    const cards = matching.map((ex) => ({
        ...propagandaExampleToPostCard(ex),
        techniques: ex.techniques.filter((t) => t.technique === selected),
    }));

    return (
        <Card
            title="Techniques being used"
            subtitle="Pick a technique to read the flagged posts carrying it — highlighted text is the verbatim evidence. One post can count toward multiple techniques."
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
            <div className="technique-explorer">
                <div className="technique-explorer-list" role="tablist" aria-label="Propaganda techniques">
                    {techniques.map((t) => {
                        const name = t.technique as PropagandaTechniqueName;
                        const label = TECHNIQUE_LABEL[name] || t.technique;
                        const active = name === selected;
                        const widthPct = maxCount > 0 ? (t.count / maxCount) * 100 : 0;
                        return (
                            <button
                                key={t.technique}
                                type="button"
                                role="tab"
                                aria-selected={active}
                                className={`technique-explorer-row${active ? ' technique-explorer-row-active' : ''}`}
                                onClick={() => select(name)}
                            >
                                <span className="technique-explorer-row-head">
                                    <span className="technique-explorer-row-name">{label}</span>
                                    <span className="technique-explorer-row-count">
                                        {t.count.toLocaleString()}
                                        <span className="technique-explorer-row-pct">
                                            {formatPct(t.pct_of_flagged_docs, { decimals: 0 })} of flagged
                                        </span>
                                    </span>
                                </span>
                                <span className="technique-explorer-row-bar" aria-hidden>
                                    <span
                                        className="technique-explorer-row-fill"
                                        style={{ width: `${widthPct}%` }}
                                    />
                                </span>
                                <span className="technique-explorer-row-blurb">
                                    {TECHNIQUE_BLURB[name] || ''}
                                </span>
                            </button>
                        );
                    })}
                </div>
                <div className="technique-explorer-examples">
                    {selected && (
                        <PostCardList
                            posts={cards}
                            sampleNote={`Flagged posts carrying ${TECHNIQUE_LABEL[selected]} — a sample, not a complete feed.`}
                            emptyNote={`No stored examples carry ${TECHNIQUE_LABEL[selected]} in this window. Examples are a capped sample; the counts on the left cover all flagged posts.`}
                        />
                    )}
                </div>
            </div>
        </Card>
    );
}

export default TechniqueExplorer;
