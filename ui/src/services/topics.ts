/**
 * Political-topic taxonomy used by the Overall Tone page's topic filter.
 *
 * Presentation-only config (labels, slugs, icons). The topic VALUES are
 * owned by the backend: the schema-enforced LLM topic
 * (`target_mentions.topic`) with a title-keyword fallback, surfaced as
 * `byTopic[].topic` and stamped per-sample as `sample.topic`. Keep the
 * `key` field byte-for-byte identical to those strings — the UI looks up
 * rows and filters samples by exact label. `General` is the backend's
 * honest "no topic signal" bucket and gets a real tab.
 */

export type TopicKey =
    | 'all'
    | 'Economy'
    | 'Immigration'
    | 'Healthcare'
    | 'Climate'
    | 'Foreign Policy'
    | 'Gun Policy'
    | 'Abortion'
    | 'Education'
    | 'Justice'
    | 'Technology'
    | 'Social Issues'
    | 'Democracy'
    | 'Housing'
    | 'National Security'
    | 'General';

export interface Topic {
    /** Canonical key matching backend `byTopic[].topic`. The pseudo-key
     *  `'all'` is UI-only and means "no topic filter". */
    key: TopicKey;
    /** Display label rendered in the tab bar and modal chip. */
    label: string;
    /** Slug used in the URL `?topic=` query param. Lowercase, hyphenated,
     *  reversible via `topicFromSlug`. */
    slug: string;
    /** Inline 24x24 stroke SVG path-d strings — the renderer wraps them in
     *  an `<svg>` with stroke=currentColor + fill=none, matching App.tsx's
     *  top-level tab-icon style. */
    iconPaths: string[];
}

const ICON_GRID = ['M4 4h7v7H4z', 'M13 4h7v7h-7z', 'M4 13h7v7H4z', 'M13 13h7v7h-7z'];
const ICON_DOLLAR = ['M12 3v18', 'M16 7c0-2-2-3-4-3s-4 1-4 3 2 3 4 3 4 1 4 3-2 3-4 3-4-1-4-3'];
const ICON_PEOPLE = ['M9 11a3 3 0 100-6 3 3 0 000 6z', 'M17 11a2.5 2.5 0 100-5 2.5 2.5 0 000 5z', 'M3 20c0-3 3-5 6-5s6 2 6 5', 'M15 20c0-2 2-3.5 4-3.5s2 0 2 0'];
const ICON_PLUS = ['M12 4v16', 'M4 12h16'];
const ICON_LEAF = ['M5 19c0-7 6-13 14-13 0 8-6 14-14 14z', 'M5 19c2-4 5-7 9-9'];
const ICON_GLOBE = ['M12 3a9 9 0 100 18 9 9 0 000-18z', 'M3 12h18', 'M12 3c3 4 3 14 0 18', 'M12 3c-3 4-3 14 0 18'];
const ICON_SHIELD = ['M12 3l8 4v5c0 5-3.5 8.5-8 9-4.5-.5-8-4-8-9V7l8-4z'];
const ICON_HEART = ['M12 20s-7-4.5-7-10a4 4 0 017-2.5A4 4 0 0119 10c0 5.5-7 10-7 10z'];
const ICON_BOOK = ['M4 5a2 2 0 012-2h12v18H6a2 2 0 01-2-2V5z', 'M8 7h8', 'M8 11h8', 'M8 15h5'];
const ICON_SCALES = ['M12 4v16', 'M4 8h16', 'M7 8l-3 6h6l-3-6z', 'M17 8l-3 6h6l-3-6z'];
const ICON_CHIP = ['M6 6h12v12H6z', 'M9 9h6v6H9z', 'M3 9h3', 'M3 15h3', 'M18 9h3', 'M18 15h3', 'M9 3v3', 'M15 3v3', 'M9 18v3', 'M15 18v3'];
const ICON_GROUP = ['M7 8a3 3 0 100-6 3 3 0 000 6z', 'M17 8a3 3 0 100-6 3 3 0 000 6z', 'M12 14a3 3 0 100-6 3 3 0 000 6z', 'M3 20c1-3 3-4 4-4', 'M21 20c-1-3-3-4-4-4', 'M7 22c1-3 3-4 5-4s4 1 5 4'];
const ICON_BALLOT = ['M5 4h14v16H5z', 'M9 9h6', 'M9 13h6', 'M9 17h4'];
const ICON_HOUSE = ['M3 11l9-7 9 7', 'M5 10v10h14V10', 'M10 20v-6h4v6'];
const ICON_LINES = ['M4 6h16', 'M4 12h16', 'M4 18h10'];
const ICON_SHIELD_STAR = ['M12 3l8 4v5c0 5-3.5 8.5-8 9-4.5-.5-8-4-8-9V7l8-4z', 'M12 9l1.2 2.5L16 12l-2 2 .5 2.5L12 15l-2.5 1.5L10 14l-2-2 2.8-.5L12 9z'];

export const TOPICS: Topic[] = [
    { key: 'all',              label: 'All Topics',       slug: 'all',              iconPaths: ICON_GRID },
    { key: 'Economy',          label: 'Economy',          slug: 'economy',          iconPaths: ICON_DOLLAR },
    { key: 'Immigration',      label: 'Immigration',      slug: 'immigration',      iconPaths: ICON_PEOPLE },
    { key: 'Healthcare',       label: 'Healthcare',       slug: 'healthcare',       iconPaths: ICON_PLUS },
    { key: 'Climate',          label: 'Climate',          slug: 'climate',          iconPaths: ICON_LEAF },
    { key: 'Foreign Policy',   label: 'Foreign Policy',   slug: 'foreign-policy',   iconPaths: ICON_GLOBE },
    { key: 'Gun Policy',       label: 'Gun Policy',       slug: 'gun-policy',       iconPaths: ICON_SHIELD },
    { key: 'Abortion',         label: 'Abortion',         slug: 'abortion',         iconPaths: ICON_HEART },
    { key: 'Education',        label: 'Education',        slug: 'education',        iconPaths: ICON_BOOK },
    { key: 'Justice',          label: 'Justice',          slug: 'justice',          iconPaths: ICON_SCALES },
    { key: 'Technology',       label: 'Technology',       slug: 'technology',       iconPaths: ICON_CHIP },
    { key: 'Social Issues',    label: 'Social Issues',    slug: 'social-issues',    iconPaths: ICON_GROUP },
    { key: 'Democracy',        label: 'Democracy',        slug: 'democracy',        iconPaths: ICON_BALLOT },
    { key: 'Housing',          label: 'Housing',          slug: 'housing',          iconPaths: ICON_HOUSE },
    { key: 'National Security',label: 'National Security',slug: 'national-security',iconPaths: ICON_SHIELD_STAR },
    // Backend bucket for scored posts with no topic signal (no resolved
    // LLM mention topic, no title keyword). A real tab keeps the taxonomy
    // honest — hiding unclassified posts would overstate topic coverage.
    { key: 'General',          label: 'General',          slug: 'general',          iconPaths: ICON_LINES },
];

const BY_KEY = new Map<TopicKey, Topic>(TOPICS.map(t => [t.key, t]));
const BY_SLUG = new Map<string, Topic>(TOPICS.map(t => [t.slug, t]));

export function topicByKey(key: string | null | undefined): Topic {
    if (!key) return TOPICS[0];
    return BY_KEY.get(key as TopicKey) ?? TOPICS[0];
}

export function topicFromSlug(slug: string | null | undefined): Topic {
    if (!slug) return TOPICS[0];
    return BY_SLUG.get(slug.toLowerCase()) ?? TOPICS[0];
}

