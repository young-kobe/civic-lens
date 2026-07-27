import type { PropagandaTechniqueName } from '../../types';

/** Reader-facing name for each propaganda technique. Shared by PostCard,
 *  Propaganda.tsx, and TechniqueExplorer so the label reads identically
 *  wherever a technique tag renders. */
export const TECHNIQUE_LABEL: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Loaded language',
    name_calling: 'Name-calling',
    ad_hominem: 'Ad hominem',
    appeal_to_fear: 'Appeal to fear',
    whataboutism: 'Whataboutism',
    doubt_casting: 'Doubt-casting',
};
