/**
 * Political-topic taxonomy used by the Overall Tone page's topic filter.
 *
 * Mirrors the canonical backend list in
 * `analysis/src/reporting/aggregators/constants.py::TOPIC_KEYWORDS`. Keep
 * the `key` field byte-for-byte identical to the Python dict keys — the
 * sentiment aggregator emits `byTopic[].topic` strings using these exact
 * labels, and the UI looks up rows by label.
 *
 * The `keywords` list is duplicated here ONLY so the Overall Tone profile
 * modal can client-side-filter classification samples by topic until the
 * backend exposes per-entity per-topic rollups. When that backend work
 * lands, delete the keywords field and the `matchesTopic` helper below.
 *
 * Narratives and Propaganda do not yet consume this taxonomy. A follow-up
 * should migrate them to the same shared list.
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
    | 'National Security';

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
    /** Backend keyword list copied from `TOPIC_KEYWORDS`. Empty for `'all'`.
     *  Used by `matchesTopic` for client-side sample filtering — see
     *  module docstring. */
    keywords: string[];
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
const ICON_SHIELD_STAR = ['M12 3l8 4v5c0 5-3.5 8.5-8 9-4.5-.5-8-4-8-9V7l8-4z', 'M12 9l1.2 2.5L16 12l-2 2 .5 2.5L12 15l-2.5 1.5L10 14l-2-2 2.8-.5L12 9z'];

export const TOPICS: Topic[] = [
    { key: 'all',              label: 'All Topics',       slug: 'all',              iconPaths: ICON_GRID,        keywords: [] },
    { key: 'Economy',          label: 'Economy',          slug: 'economy',          iconPaths: ICON_DOLLAR,      keywords: ['economy', 'inflation', 'jobs', 'unemployment', 'tax', 'tariff', 'recession', 'gdp', 'fed', 'interest rate', 'stock market', 'wage', 'minimum wage', 'debt ceiling', 'trade war', 'supply chain', 'cost of living'] },
    { key: 'Immigration',      label: 'Immigration',      slug: 'immigration',      iconPaths: ICON_PEOPLE,      keywords: ['immigration', 'border', 'migrant', 'asylum', 'deportation', 'illegal', 'daca', 'visa', 'refugee', 'citizenship', 'undocumented', 'ice', 'border wall', 'sanctuary city', 'caravan', 'title 42'] },
    { key: 'Healthcare',       label: 'Healthcare',       slug: 'healthcare',       iconPaths: ICON_PLUS,        keywords: ['healthcare', 'obamacare', 'medicare', 'insurance', 'hospital', 'medical', 'medicaid', 'drug prices', 'pharma', 'mental health', 'pandemic', 'vaccine', 'public health', 'prescription', 'affordable care'] },
    { key: 'Climate',          label: 'Climate',          slug: 'climate',          iconPaths: ICON_LEAF,        keywords: ['climate', 'energy', 'green', 'carbon', 'emissions', 'fossil', 'renewable', 'solar', 'wind', 'ev', 'electric vehicle', 'paris agreement', 'epa', 'pollution', 'wildfire', 'natural disaster', 'oil'] },
    { key: 'Foreign Policy',   label: 'Foreign Policy',   slug: 'foreign-policy',   iconPaths: ICON_GLOBE,       keywords: ['foreign', 'russia', 'china', 'ukraine', 'military', 'nato', 'war', 'troops', 'iran', 'israel', 'gaza', 'palestine', 'north korea', 'taiwan', 'sanctions', 'diplomacy', 'pentagon', 'drone', 'intelligence'] },
    { key: 'Gun Policy',       label: 'Gun Policy',       slug: 'gun-policy',       iconPaths: ICON_SHIELD,      keywords: ['gun', 'firearm', 'second amendment', 'nra', 'shooting', 'mass shooting', 'gun control', 'gun violence', 'background check', 'assault weapon', 'concealed carry', 'red flag'] },
    { key: 'Abortion',         label: 'Abortion',         slug: 'abortion',         iconPaths: ICON_HEART,       keywords: ['abortion', 'roe', 'reproductive', 'pro-life', 'pro-choice', 'dobbs', 'planned parenthood', 'contraception', 'fetal'] },
    { key: 'Education',        label: 'Education',        slug: 'education',        iconPaths: ICON_BOOK,        keywords: ['education', 'school', 'student', 'college', 'university', 'teacher', 'student loan', 'tuition', 'charter school', 'curriculum', 'dei', 'critical race theory', 'book ban', 'homeschool'] },
    { key: 'Justice',          label: 'Justice',          slug: 'justice',          iconPaths: ICON_SCALES,      keywords: ['justice', 'supreme court', 'judges', 'crime', 'police', 'prison', 'criminal justice', 'bail reform', 'death penalty', 'sentencing', 'fbi', 'doj', 'attorney general', 'civil rights', 'qualified immunity'] },
    { key: 'Technology',       label: 'Technology',       slug: 'technology',       iconPaths: ICON_CHIP,        keywords: ['ai', 'artificial intelligence', 'social media', 'big tech', 'tiktok', 'data privacy', 'antitrust', 'crypto', 'bitcoin', 'regulation', 'section 230', 'deepfake', 'cybersecurity', 'surveillance'] },
    { key: 'Social Issues',    label: 'Social Issues',    slug: 'social-issues',    iconPaths: ICON_GROUP,       keywords: ['lgbtq', 'transgender', 'same-sex', 'racial justice', 'blm', 'woke', 'cancel culture', 'diversity', 'equity', 'inclusion', 'affirmative action', 'discrimination', 'hate crime'] },
    { key: 'Democracy',        label: 'Democracy',        slug: 'democracy',        iconPaths: ICON_BALLOT,      keywords: ['election', 'voting', 'ballot', 'gerrymandering', 'filibuster', 'electoral college', 'voter fraud', 'election integrity', 'campaign finance', 'dark money', 'term limits', 'january 6'] },
    { key: 'Housing',          label: 'Housing',          slug: 'housing',          iconPaths: ICON_HOUSE,       keywords: ['housing', 'rent', 'mortgage', 'homelessness', 'affordable housing', 'zoning', 'landlord', 'eviction', 'gentrification', 'real estate'] },
    { key: 'National Security',label: 'National Security',slug: 'national-security',iconPaths: ICON_SHIELD_STAR, keywords: ['terrorism', 'homeland security', 'border security', 'fisa', 'espionage', 'classified', 'national guard', 'veteran', 'va'] },
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

/**
 * Test whether a piece of text plausibly matches the given topic. Mirrors
 * the backend `_extract_topic` first-substring-wins logic but accepts any
 * field (title, body) so the modal can filter samples whose titles don't
 * carry the keyword but whose evidence spans do.
 */
export function matchesTopic(topic: Topic, ...fields: Array<string | null | undefined>): boolean {
    if (topic.key === 'all') return true;
    const haystack = fields.filter(Boolean).join(' ').toLowerCase();
    if (!haystack) return false;
    return topic.keywords.some(kw => haystack.includes(kw));
}
