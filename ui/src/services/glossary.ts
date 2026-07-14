/**
 * Glossary — the single source for reader-facing definitions and the
 * plain-language buckets that replace bare 0-to-1 scores in page copy.
 *
 * Every DefinitionChip and every "saturation: light"-style phrase reads
 * from here so the same term is defined with the same words everywhere.
 * The numbers stay one click deeper (chip popover, modal stats); the
 * page-level copy leads with the plain-language reading.
 */

export interface GlossaryEntry {
    /** Reader-facing term as it appears in copy. */
    term: string;
    /** One-sentence plain definition. */
    definition: string;
    /** Optional "scale" line spelling out the axis. */
    scale?: string;
}

export const GLOSSARY = {
    net_tone: {
        term: 'Net tone',
        definition:
            'The share of sampled posts scored positive minus the share scored negative. '
            + 'It summarizes the posts we collected — a sample, not a poll.',
        scale: '−100 (all negative) to +100 (all positive); 0 means they balance out.',
    },
    received_tone: {
        term: 'Received tone',
        definition:
            'The average tone of sampled posts that talk ABOUT this person — the reputational '
            + 'signal. It does not include their own posts about others.',
        scale: '−100 (all negative) to +100 (all positive).',
    },
    expressed_tone: {
        term: 'Expressed tone',
        definition:
            "The average tone of this person's OWN posts. A very negative value means they "
            + 'post negatively (often about opponents) — not that others are negative about them.',
        scale: '−100 (all negative) to +100 (all positive).',
    },
    mean_score: {
        term: 'Technique saturation',
        definition:
            'On average, how saturated flagged posts are with persuasion techniques '
            + '(loaded language, name-calling, fear appeals, and three others).',
        scale: '0 (none) to 1 (wall-to-wall).',
    },
    flagged_rate: {
        term: 'Flagged rate',
        definition:
            'The share of scored posts where our model found at least one persuasion technique, '
            + 'each backed by a verbatim quote. A measure of rhetorical style, not truth or intent.',
        scale: '0% to 100% of scored posts.',
    },
    coordination: {
        term: 'Coordination',
        definition:
            'How much suspected accounts post the same things at the same times — timing '
            + 'overlap, near-duplicate text, and shared link targets.',
        scale: '0 (posting independently) to 1 (posting in lockstep).',
    },
    bot_rate: {
        term: 'Suspected bot rate',
        definition:
            "The share of a source's scored posts our detector flags as likely automated, from "
            + 'behavioral signals like posting rate, text repetition, and account age. '
            + 'A lead, not a verdict.',
        scale: '0% to 100% of scored posts.',
    },
    bot_pushed: {
        term: 'Bot-pushed',
        definition:
            'The share of unique X accounts repeating this claim that show automated-behavior '
            + 'signals in our detector. An estimate, not proof.',
        scale: '0% to 100% of unique accounts.',
    },
    first_seen: {
        term: 'First seen',
        definition:
            'The earliest post WE collected carrying this claim. The claim may have started '
            + 'elsewhere before we picked it up — this is not a world-origin claim.',
    },
    cites: {
        term: 'Citations',
        definition:
            'Links, quotes, replies, and reposts between documents in our sample. Edges only '
            + 'cover what we ingested — never the whole web, and never proof of origin.',
    },
    low_sample: {
        term: 'Low sample',
        definition:
            'Too few posts to score reliably. We withhold the number rather than show one '
            + 'built on a handful of posts.',
    },
    confidence: {
        term: 'Confidence',
        definition:
            "The model's own certainty in a label. Every AI classification on this site "
            + 'shows one — none is presented as fact.',
        scale: '0% to 100%.',
    },
} as const satisfies Record<string, GlossaryEntry>;

export type GlossaryKey = keyof typeof GLOSSARY;

// --------------------------------------------------------------------------- //
//  Plain-language buckets — page copy leads with these; the raw number        //
//  stays visible one click deeper.                                            //
// --------------------------------------------------------------------------- //

/** Technique-saturation buckets for propaganda mean_score (0..1). */
export function saturationLevel(meanScore: number): 'light' | 'moderate' | 'heavy' {
    if (meanScore >= 0.5) return 'heavy';
    if (meanScore >= 0.2) return 'moderate';
    return 'light';
}

/** Coordination-index buckets (0..1). */
export function coordinationLevel(index: number): 'low' | 'moderate' | 'high' {
    if (index >= 0.6) return 'high';
    if (index >= 0.3) return 'moderate';
    return 'low';
}
