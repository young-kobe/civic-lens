import type { ReactNode } from 'react';
import { dedupeById } from '../../services/dedupe';
import {
    formatCount, formatPct, formatRelativeDate, sourceLabel,
} from '../../services/format';
import { TECHNIQUE_LABEL } from './constants';
import type {
    ClassificationSample, FlaggedExample, PropagandaExample, PropagandaTechniqueName,
    PropagandaTechniqueSpan, SampleTarget,
} from '../../types';

// --------------------------------------------------------------------------- //
//  PostCard — the one way a sampled post renders anywhere on the site.        //
//                                                                             //
//  Self-rendered from ingested data (no embed widgets, no external scripts).  //
//  Three visual flavors keyed off source_type: x_post (tweet-like), reddit    //
//  (thread-like), news (clipping-like). Every card carries the audit          //
//  contract: the AI label always shows its confidence, evidence spans are     //
//  highlighted verbatim inside the text (with a quoted fallback when the      //
//  span falls outside the preview — evidence is never silently dropped),      //
//  reasoning sits behind a disclosure, and the permalink out is invariant     //
//  C1. Styled via the `.post-card*` rules in index.css.                       //
//                                                                             //
//  Restored verbatim from `pre-cutover-main` (docs/todos/                     //
//  ui-feature-restoration.md, Wave 3 UI): samples now arrive rich straight     //
//  off `ClassificationSample`/`FlaggedExample`/`PropagandaExample` (the       //
//  Wave 2 API additions), so this card renders inline with no lazy            //
//  hydration step. Only the adapter functions changed from the old file —     //
//  they read the current CamelModel wire shapes (camelCase) instead of the    //
//  old snake_case dicts; `PostCardData` itself is an unchanged internal       //
//  view-model, not a wire type. `supportingDocToPostCard` is dropped: the     //
//  old `SupportingDoc` wire shape it adapted no longer exists — narratives    //
//  now serve `topSupportingDocs: ClassificationSample[]` directly, so         //
//  `sampleToPostCard` covers that caller too.                                 //
// --------------------------------------------------------------------------- //

export interface PostCardEngagement {
    // X counts.
    likes?: number;
    retweets?: number;
    replies?: number;
    quotes?: number;
    // Reddit counts.
    score?: number;
    num_comments?: number;
}

export interface PostCardAuthor {
    display_name?: string | null;
    handle?: string | null;
    avatar_url?: string | null;
    verified_type?: string | null;
}

export interface PostCardData {
    doc_id: number;
    source_type: string;
    source_label: string;            // "News · nytimes.com" / "X · @handle" / "Reddit · r/politics"
    title: string | null;
    text: string | null;
    published_at: number | null;     // unix seconds
    url: string | null;
    // Analysis overlay — every label renders with its confidence.
    label?: string | null;
    labelKind?: 'tone' | 'propaganda' | 'bot';
    confidence?: number | null;      // 0..1
    reasoning?: string | null;
    evidenceSpans?: string[];
    techniques?: PropagandaTechniqueSpan[];
    sarcasm?: boolean;
    /** Small mono annotation in the analysis row (e.g. a per-doc score). */
    meta?: string | null;
    // Enrichment — rendered only when the backend supplied it.
    engagement?: PostCardEngagement | null;
    author?: PostCardAuthor | null;
    botIndicators?: string[] | null;
    /** Who the post's sentiment is directed at ("about Trump — negative"). */
    targets?: SampleTarget[] | null;
    // Avatar hints when no author payload exists (parsed by the adapters).
    domain?: string | null;          // news favicon
    handle?: string | null;          // x avatar via unavatar
}

const TONE_BADGE_CLASS: Record<string, string> = {
    positive: 'badge-positive',
    negative: 'badge-negative',
    neutral: 'badge-neutral',
    mixed: 'badge-warning',
};

// Plain-language reading of a target's stance (analysis.sentiment_label:
// positive/negative/neutral/mixed) for the "about X — <word>" chip below.
const STANCE_WORD: Record<string, string> = {
    positive: 'supportive',
    negative: 'critical',
    neutral: 'neutral',
    mixed: 'mixed',
};

const BODY_MAX_CHARS = 420;

// --------------------------------------------------------------------------- //
//  Evidence-span highlighting                                                 //
// --------------------------------------------------------------------------- //

interface HighlightResult {
    /** Alternating segments; `mark` segments render as <mark>. */
    segments: Array<{ text: string; mark: boolean }>;
    /** Spans that did not appear in the (possibly truncated) text —
     *  rendered as verbatim quotes below so evidence is never dropped. */
    unmatched: string[];
}

/** Case-insensitive first-match highlighting. Overlapping spans keep the
 *  earliest match and drop the overlap (the text is still highlighted —
 *  only the duplicate range is skipped). */
export function highlightSpans(text: string, spans: string[]): HighlightResult {
    const clean = spans.map((s) => s.trim()).filter((s) => s.length > 2);
    if (clean.length === 0) return { segments: [{ text, mark: false }], unmatched: [] };

    const lower = text.toLowerCase();
    const matches: Array<{ start: number; end: number }> = [];
    const unmatched: string[] = [];
    for (const span of clean) {
        const idx = lower.indexOf(span.toLowerCase());
        if (idx === -1) {
            unmatched.push(span);
        } else {
            matches.push({ start: idx, end: idx + span.length });
        }
    }
    matches.sort((a, b) => a.start - b.start);

    const segments: HighlightResult['segments'] = [];
    let cursor = 0;
    for (const m of matches) {
        if (m.start < cursor) continue; // overlap — earlier match already covers it
        if (m.start > cursor) segments.push({ text: text.slice(cursor, m.start), mark: false });
        segments.push({ text: text.slice(m.start, m.end), mark: true });
        cursor = m.end;
    }
    if (cursor < text.length) segments.push({ text: text.slice(cursor), mark: false });
    return { segments, unmatched };
}

/** Collapse whitespace and cap length for card bodies — full_text can be an
 *  entire article. Slice before collapsing: this runs per card in render. */
export function textSnippet(text: string, maxChars = BODY_MAX_CHARS): string {
    const collapsed = text.slice(0, maxChars * 4).split(/\s+/).join(' ').trim();
    return collapsed.length > maxChars
        ? collapsed.slice(0, maxChars - 1).trimEnd() + '…'
        : collapsed;
}

// --------------------------------------------------------------------------- //
//  Avatar                                                                     //
// --------------------------------------------------------------------------- //

function PostAvatar({ post }: { post: PostCardData }) {
    const src = post.author?.avatar_url
        ?? (post.handle ? `https://unavatar.io/twitter/${post.handle}?fallback=false` : null)
        ?? (post.source_type === 'news' && post.domain
            ? `https://www.google.com/s2/favicons?domain=${post.domain}&sz=64`
            : null);
    if (src) {
        return (
            <span className="post-card-avatar post-card-avatar-img" aria-hidden>
                <img
                    src={src}
                    alt=""
                    width={28}
                    height={28}
                    loading="lazy"
                    referrerPolicy="no-referrer"
                    onError={(e) => { e.currentTarget.style.display = 'none'; }}
                />
            </span>
        );
    }
    const monogram = (post.source_label.split('·').pop() ?? '?').trim().replace(/^[@r]\/?/, '')
        .charAt(0).toUpperCase() || '?';
    return <span className="post-card-avatar post-card-avatar-mono" aria-hidden>{monogram}</span>;
}

// --------------------------------------------------------------------------- //
//  Card                                                                       //
// --------------------------------------------------------------------------- //

function sourceFlavor(sourceType: string): 'x' | 'reddit' | 'news' {
    if (sourceType === 'x_post') return 'x';
    if (sourceType.startsWith('reddit')) return 'reddit';
    return 'news';
}

function EngagementRow({ engagement, flavor }: { engagement: PostCardEngagement; flavor: string }) {
    const parts: string[] = [];
    if (flavor === 'reddit') {
        if (engagement.score != null) parts.push(`${formatCount(engagement.score)} points`);
        if (engagement.num_comments != null) parts.push(`${formatCount(engagement.num_comments)} comments`);
    } else {
        if (engagement.likes != null) parts.push(`${formatCount(engagement.likes)} likes`);
        if (engagement.retweets != null) parts.push(`${formatCount(engagement.retweets)} reposts`);
        if (engagement.replies != null) parts.push(`${formatCount(engagement.replies)} replies`);
        if (engagement.quotes != null) parts.push(`${formatCount(engagement.quotes)} quotes`);
    }
    if (parts.length === 0) return null;
    return (
        <div
            className="post-card-engagement"
            title="Engagement counts at collection time — a reach proxy, not verified reach."
        >
            {parts.join(' · ')}
        </div>
    );
}

export function PostCard({ post }: { post: PostCardData }) {
    const flavor = sourceFlavor(post.source_type);

    // Body text: news cards lead with the headline, so the body is the dek;
    // social cards lead with the text itself.
    const rawBody = post.text ? textSnippet(post.text) : null;
    const allSpans = [
        ...(post.evidenceSpans ?? []),
        ...(post.techniques ?? [])
            .map((t) => t.evidenceSpan)
            .filter((s): s is string => s != null),
    ];
    const { segments, unmatched } = rawBody
        ? highlightSpans(rawBody, allSpans)
        : { segments: [], unmatched: allSpans.filter((s) => s.trim().length > 2) };

    const authorName = post.author?.display_name
        ?? (flavor === 'x' && post.author?.handle ? `@${post.author.handle}` : null);

    const toneBadgeClass = post.labelKind === 'tone' && post.label
        ? TONE_BADGE_CLASS[post.label.toLowerCase()] ?? 'badge-neutral'
        : null;

    const confPct = post.confidence != null
        ? formatPct(post.confidence * 100, { decimals: 0 })
        : null;

    return (
        <article className={`post-card post-card-${flavor}`}>
            <header className="post-card-head">
                <PostAvatar post={post} />
                <div className="post-card-head-text">
                    {authorName && <span className="post-card-author">{authorName}</span>}
                    <span className="post-card-source">{post.source_label}</span>
                </div>
                <span className="post-card-when">
                    {post.published_at ? formatRelativeDate(post.published_at) : ''}
                </span>
                {post.url && (
                    <a
                        className="post-card-permalink"
                        href={post.url}
                        target="_blank"
                        rel="noreferrer"
                        aria-label="Open the original post in a new tab"
                        title="Open the original in a new tab"
                    >
                        View original
                    </a>
                )}
            </header>

            {post.title && <h4 className="post-card-title">{post.title}</h4>}

            {segments.length > 0 && (
                <p className="post-card-body">
                    {segments.map((seg, i) => seg.mark
                        ? (
                            <mark
                                key={i}
                                className="post-card-evidence"
                                title="Verbatim evidence quoted by the model for its label"
                            >
                                {seg.text}
                            </mark>
                        )
                        : <span key={i}>{seg.text}</span>)}
                </p>
            )}

            {unmatched.length > 0 && (
                <div className="post-card-evidence-fallback">
                    {unmatched.map((span, i) => (
                        <div key={i} className="post-card-evidence-quote">
                            <span className="post-card-evidence-quote-label">Evidence:</span>{' '}
                            <em>"{span}"</em>
                        </div>
                    ))}
                </div>
            )}

            {post.engagement && <EngagementRow engagement={post.engagement} flavor={flavor} />}

            {(post.label || (post.techniques?.length ?? 0) > 0 || post.sarcasm || post.meta
                || (post.targets?.length ?? 0) > 0) && (
                <div className="post-card-analysis">
                    {post.labelKind === 'tone' && post.label && (
                        <span className={`badge ${toneBadgeClass}`}>{post.label.toLowerCase()}</span>
                    )}
                    {(post.targets ?? []).map((t, i) => (
                        <span
                            key={i}
                            className={`badge ${TONE_BADGE_CLASS[t.stance] ?? 'badge-neutral'}`}
                            title="Who the post's sentiment is directed at, per the model's target extraction"
                        >
                            about {t.label} · {STANCE_WORD[t.stance] ?? t.stance}
                        </span>
                    ))}
                    {post.labelKind === 'bot' && post.label && (
                        <span
                            className="badge badge-warning"
                            title="Our detector flags this post as likely automated. A lead, not a verdict."
                        >
                            {post.label}
                        </span>
                    )}
                    {(post.techniques ?? []).map((t, i) => (
                        <span
                            key={i}
                            className="badge badge-negative"
                            title="Model confidence that this technique is present"
                        >
                            {TECHNIQUE_LABEL[t.technique as PropagandaTechniqueName] ?? t.technique}
                            {' · '}
                            {formatPct(t.confidence * 100, { decimals: 0 })}
                        </span>
                    ))}
                    {post.sarcasm && (
                        <span
                            className="badge badge-neutral"
                            title="The model flagged possible sarcasm or irony — tone labels on sarcastic posts are less reliable."
                        >
                            sarcasm flagged
                        </span>
                    )}
                    {confPct && (
                        <span
                            className="post-card-confidence"
                            title="Model confidence in this label"
                        >
                            {confPct} confidence
                        </span>
                    )}
                    {post.meta && (
                        <span className="post-card-confidence">{post.meta}</span>
                    )}
                    {(post.botIndicators?.length ?? 0) > 0 && (
                        <span className="post-card-indicators">
                            {post.botIndicators!.map((ind, i) => (
                                <span key={i} className="badge badge-neutral">{ind}</span>
                            ))}
                        </span>
                    )}
                </div>
            )}

            {post.reasoning && (
                <details className="post-card-reasoning">
                    <summary>Why this label</summary>
                    <p>{post.reasoning}</p>
                </details>
            )}
        </article>
    );
}

// --------------------------------------------------------------------------- //
//  List wrapper — samples stay labeled as samples                             //
// --------------------------------------------------------------------------- //

interface PostCardListProps {
    posts: PostCardData[];
    /** Required sample framing rendered above the cards, e.g.
     *  "Highest-confidence samples from this window — not a complete feed." */
    sampleNote: string;
    /** Copy when the list is empty. Omit to render nothing on empty. */
    emptyNote?: ReactNode;
}

export function PostCardList({ posts, sampleNote, emptyNote }: PostCardListProps) {
    const unique = dedupeById(posts, (p) => p.doc_id);
    if (unique.length === 0) {
        return emptyNote
            ? <p className="text-sm text-muted" style={{ fontStyle: 'italic' }}>{emptyNote}</p>
            : null;
    }
    return (
        <div className="post-card-list">
            <p className="post-card-list-note">{sampleNote}</p>
            {unique.map((p) => <PostCard key={p.doc_id} post={p} />)}
        </div>
    );
}

// --------------------------------------------------------------------------- //
//  Adapters — one per wire shape                                              //
// --------------------------------------------------------------------------- //

/** Date-only strings ("2026-04-14") parse as UTC midnight via Date.parse,
 *  which can render "1 day ago" off-by-one near local midnight. Parse those
 *  as a LOCAL date; fall back to Date.parse for full timestamps. */
function parseSampleDate(date: string | null | undefined): number | null {
    if (!date) return null;
    const dateOnly = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date);
    if (dateOnly) {
        const [, y, m, d] = dateOnly;
        return Math.floor(new Date(Number(y), Number(m) - 1, Number(d)).getTime() / 1000);
    }
    const parsed = Date.parse(date);
    return isNaN(parsed) ? null : Math.floor(parsed / 1000);
}

/** Avatar hints from the canonical source label when the wire shape carries
 *  no structured author/domain ("X · @handle" / "News · foo.com" /
 *  "Reddit · r/politics"). Cosmetic only — drives the avatar, nothing else. */
function avatarHintsFromLabel(label: string): { handle: string | null; domain: string | null } {
    const x = /^X · @([\w.]+)/.exec(label);
    if (x) return { handle: x[1], domain: null };
    const news = /^News · ([\w.-]+)/.exec(label);
    if (news) return { handle: null, domain: news[1] };
    return { handle: null, domain: null };
}

export function sampleToPostCard(s: ClassificationSample): PostCardData {
    const label = sourceLabel(s.sourceType, s.sourceName);
    return {
        doc_id: s.docId,
        source_type: s.sourceType || 'unknown',
        source_label: label,
        title: s.title || null,
        text: s.fullText || null,
        published_at: parseSampleDate(s.date),
        url: s.url ?? null,
        label: s.label ?? null,
        labelKind: 'tone',
        confidence: typeof s.confidence === 'number' ? s.confidence : null,
        reasoning: s.reasoning || null,
        evidenceSpans: s.evidenceSpans ?? [],
        sarcasm: s.sarcasmDetected ?? false,
        engagement: s.engagement
            ? {
                likes: s.engagement.likes ?? undefined,
                retweets: s.engagement.retweets ?? undefined,
                replies: s.engagement.replies ?? undefined,
                quotes: s.engagement.quotes ?? undefined,
                score: s.engagement.score ?? undefined,
                num_comments: s.engagement.numComments ?? undefined,
            }
            : null,
        author: s.author
            ? {
                display_name: s.author.displayName,
                handle: s.author.handle,
                avatar_url: s.author.avatarUrl,
                verified_type: s.author.verifiedType,
            }
            : null,
        targets: s.targets ?? null,
        handle: s.sourceType === 'x_post' ? (s.sourceName ?? null) : null,
        domain: s.sourceType === 'news' ? (s.sourceName ?? null) : null,
    };
}

export function flaggedExampleToPostCard(ex: FlaggedExample): PostCardData {
    const hints = avatarHintsFromLabel(ex.sourceLabel);
    return {
        doc_id: ex.docId,
        source_type: hints.handle ? 'x_post' : hints.domain ? 'news' : 'reddit_post',
        source_label: ex.sourceLabel,
        title: null,
        text: ex.text,
        published_at: null,
        url: ex.url,
        label: 'Suspected automation',
        labelKind: 'bot',
        confidence: ex.confidence ?? null,
        // Transition filter mirroring BotActivityProfiler's isNoiseLabel:
        // pre-sanitization ai_outputs rows can leak raw signal-field
        // artifacts ("account_age=None days") until they age out of the
        // aggregation windows.
        botIndicators: (ex.indicators ?? []).filter(
            (i) => i.trim() && !/=\s*(None|null|undefined|0)?\s*$/i.test(i),
        ),
        reasoning: ex.reasoning ?? null,
        ...hints,
    };
}

export function propagandaExampleToPostCard(ex: PropagandaExample): PostCardData {
    return {
        doc_id: ex.docId,
        source_type: ex.sourceType,
        source_label: sourceLabel(
            ex.sourceType,
            ex.sourceType === 'x_post' ? ex.authorHandle : ex.domain,
        ),
        title: ex.title,
        text: ex.textPreview,
        published_at: null,
        url: ex.url ?? null,
        labelKind: 'propaganda',
        techniques: ex.techniques,
        meta: `score ${ex.overallScore.toFixed(2)} / 1`,
        handle: ex.sourceType === 'x_post' ? (ex.authorHandle ?? null) : null,
        domain: ex.domain,
    };
}

export default PostCard;
