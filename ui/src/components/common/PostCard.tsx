import { useState, type ReactNode } from 'react';
import { dedupeById } from '../../services/dedupe';
import { formatCount, formatPct, formatRelativeDate, sourceLabel } from '../../services/format';
import { useDocDetail } from '../../services/useDocDetail';
import { AdmissionBadge } from './AdmissionBadge';
import { DocDetailModal } from './DocDetailModal';
import type { AnalysisResult, DocumentDetail, SampleDoc } from '../../types';

// --------------------------------------------------------------------------- //
//  PostCard — the one way a sampled post renders anywhere on the site.        //
//                                                                             //
//  Self-rendered from ingested data (no embed widgets, no external scripts).  //
//  Renders in two stages: immediately from the SampleDoc the list endpoint    //
//  already carries (snippet, confidence, admission class, date, permalink),  //
//  then lazily hydrates the source/author/analysis overlay from the EXISTING //
//  GET /docs/{doc_id} endpoint via `useDocDetail` (a shared, concurrency-     //
//  capped, cache-backed fetch — see services/lazyHydration.ts) once it       //
//  arrives. Evidence spans highlight verbatim inside the text (with a        //
//  quoted fallback when a span falls outside the preview — evidence is       //
//  never silently dropped). Styled via the `.post-card*` rules in index.css. //
// --------------------------------------------------------------------------- //

export interface PostCardEngagement {
    // X counts.
    likes?: number | null;
    retweets?: number | null;
    replies?: number | null;
    quotes?: number | null;
    // Reddit counts.
    score?: number | null;
    num_comments?: number | null;
}

const TONE_BADGE_CLASS: Record<string, string> = {
    positive: 'badge-positive',
    negative: 'badge-negative',
    neutral: 'badge-neutral',
    mixed: 'badge-warning',
};

const TECHNIQUE_LABEL: Record<string, string> = {
    loaded_language: 'Loaded language',
    name_calling: 'Name-calling',
    ad_hominem: 'Ad hominem',
    appeal_to_fear: 'Appeal to fear',
    whataboutism: 'Whataboutism',
    doubt_casting: 'Doubt-casting',
};

const BOT_LABEL_TEXT: Record<string, string> = {
    bot: 'Likely automated',
    suspicious: 'Suspected automation',
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

/** Collapse whitespace and cap length for card bodies — full body text can
 *  be an entire article. Slice before collapsing: this runs per card in
 *  render. */
export function textSnippet(text: string, maxChars = BODY_MAX_CHARS): string {
    const collapsed = text.slice(0, maxChars * 4).split(/\s+/).join(' ').trim();
    return collapsed.length > maxChars
        ? collapsed.slice(0, maxChars - 1).trimEnd() + '…'
        : collapsed;
}

/** Best-effort domain for the pre-hydration avatar/source line — parsed
 *  straight from the SampleDoc's sourceUrl (invariant C1: always present). */
function domainFromUrl(url: string): string | null {
    try {
        return new URL(url).hostname.replace(/^www\./, '');
    } catch {
        return null;
    }
}

// --------------------------------------------------------------------------- //
//  Hydrated analysis overlay — one per task, read straight off               //
//  DocumentDetail.analysisResults[].fields. Those field shapes come back     //
//  with their ORIGINAL snake_case keys (see analysis/src/api/queries/        //
//  docs.py) — CamelModel's alias generator only camelCases a model's own     //
//  declared fields, not the values inside this untyped Dict[str, Any].       //
// --------------------------------------------------------------------------- //

interface SentimentField {
    label: string;
    score: number;
    sarcasm_detected: boolean;
    evidence_spans: string[];
}

interface TargetMentionField {
    raw_target: string;
    stance: string;
    evidence_spans: string[];
}

interface TechniqueField {
    technique: string;
    evidence_span: string;
    confidence: number;
}

interface BotField {
    label: string;
}

function findResult(results: AnalysisResult[], task: string): AnalysisResult | undefined {
    return results.find((r) => r.task === task);
}

interface Overlay {
    sourceType: string;
    sourceLabel: string;
    domain: string | null;
    authorName: string | null;
    avatarUrl: string | null;
    title: string | null;
    body: string;
    engagement: PostCardEngagement | null;
    toneLabel: string | null;
    toneConfidence: number | null;
    sarcasm: boolean;
    evidenceSpans: string[];
    techniques: TechniqueField[];
    techniqueConfidence: number | null;
    botLabel: string | null;
    targets: Array<{ label: string; stance: string }>;
    reasoning: string | null;
}

function sourceFlavor(sourceType: string): 'x' | 'reddit' | 'news' {
    if (sourceType === 'x_post') return 'x';
    if (sourceType.startsWith('reddit')) return 'reddit';
    return 'news';
}

function buildOverlay(doc: DocumentDetail): Overlay {
    const textResult = findResult(doc.analysisResults, 'text');
    const sentiment = (textResult?.fields.sentiment as SentimentField | null) ?? null;

    const propagandaResult = findResult(doc.analysisResults, 'propaganda');
    const techniques = (propagandaResult?.fields.techniques as TechniqueField[] | undefined) ?? [];

    const botResult = findResult(doc.analysisResults, 'bot');
    const botField = botResult?.fields as BotField | undefined;

    const targetsResult = findResult(doc.analysisResults, 'targets');
    const targetMentions = (targetsResult?.fields.target_mentions as TargetMentionField[] | undefined) ?? [];

    const domain = doc.sourceType === 'news' ? (doc.newsExtras?.domain ?? doc.domainOrSubreddit) : null;
    const name = doc.sourceType === 'x_post'
        ? (doc.authorHandle ? `@${doc.authorHandle}` : null)
        : doc.sourceType === 'reddit_post' ? doc.domainOrSubreddit : domain;
    const authorName = doc.authorDisplayName
        ?? (doc.authorHandle ? `@${doc.authorHandle}` : null);
    const avatarUrl = doc.authorProfileImageUrl
        ?? (doc.sourceType === 'x_post' && doc.authorHandle
            ? `https://unavatar.io/twitter/${doc.authorHandle}?fallback=false`
            : null);

    const engagement: PostCardEngagement | null = doc.xPostExtras
        ? {
            likes: doc.xPostExtras.likeCount,
            retweets: doc.xPostExtras.retweetCount,
            replies: doc.xPostExtras.replyCount,
            quotes: doc.xPostExtras.quoteCount,
        }
        : doc.redditExtras
            ? { score: doc.redditExtras.score, num_comments: doc.redditExtras.numComments }
            : null;

    const evidenceSpans = [
        ...(sentiment?.evidence_spans ?? []),
        ...techniques.map((t) => t.evidence_span),
        ...targetMentions.flatMap((t) => t.evidence_spans ?? []),
    ];

    return {
        sourceType: doc.sourceType,
        sourceLabel: sourceLabel(doc.sourceType, name),
        domain,
        authorName,
        avatarUrl,
        title: doc.title,
        body: doc.body,
        engagement,
        toneLabel: sentiment?.label ?? null,
        toneConfidence: textResult?.confidence ?? null,
        sarcasm: sentiment?.sarcasm_detected ?? false,
        evidenceSpans,
        techniques,
        techniqueConfidence: propagandaResult?.confidence ?? null,
        botLabel: botField?.label && botField.label !== 'human' && botField.label !== 'unknown'
            ? botField.label
            : null,
        targets: targetMentions.map((t) => ({ label: t.raw_target, stance: t.stance })),
        reasoning: (propagandaResult?.fields.summary as string | undefined) ?? null,
    };
}

// --------------------------------------------------------------------------- //
//  Avatar                                                                     //
// --------------------------------------------------------------------------- //

function PostAvatar({ domain, sourceLabelText, avatarUrl }: { domain: string | null; sourceLabelText: string; avatarUrl?: string | null }) {
    if (avatarUrl) {
        return (
            <span className="post-card-avatar post-card-avatar-img" aria-hidden>
                <img src={avatarUrl} alt="" loading="lazy"
                    onError={(e) => { (e.target as HTMLImageElement).style.display = 'none'; }} />
            </span>
        );
    }
    if (domain) {
        return (
            <span className="post-card-avatar post-card-avatar-img" aria-hidden>
                <img
                    src={`https://www.google.com/s2/favicons?domain=${domain}&sz=64`}
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
    const monogram = (sourceLabelText.split('·').pop() ?? '?').trim().replace(/^[@r]\/?/, '')
        .charAt(0).toUpperCase() || '?';
    return <span className="post-card-avatar post-card-avatar-mono" aria-hidden>{monogram}</span>;
}

// --------------------------------------------------------------------------- //
//  Card                                                                       //
// --------------------------------------------------------------------------- //

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

export function PostCard({ sample }: { sample: SampleDoc }) {
    const [showDetail, setShowDetail] = useState(false);
    const { data: doc } = useDocDetail(sample.docId);
    const overlay = doc ? buildOverlay(doc) : null;
    const flavor = overlay ? sourceFlavor(overlay.sourceType) : null;

    const rawBody = overlay ? textSnippet(overlay.body) : (sample.snippet ? textSnippet(sample.snippet) : null);
    const { segments, unmatched } = rawBody && overlay
        ? highlightSpans(rawBody, overlay.evidenceSpans)
        : { segments: rawBody ? [{ text: rawBody, mark: false }] : [], unmatched: [] };

    const sourceLabelText = overlay ? overlay.sourceLabel : (domainFromUrl(sample.sourceUrl) ?? 'Source unknown');
    const domain = overlay ? overlay.domain : domainFromUrl(sample.sourceUrl);

    const toneBadgeClass = overlay?.toneLabel ? TONE_BADGE_CLASS[overlay.toneLabel] ?? 'badge-neutral' : null;
    const confPct = formatPct(sample.confidence * 100, { decimals: 0 });

    return (
        <article className={`post-card${flavor ? ` post-card-${flavor}` : ''}`}>
            <header className="post-card-head">
                <PostAvatar domain={domain} sourceLabelText={sourceLabelText} avatarUrl={overlay?.avatarUrl} />
                <div className="post-card-head-text">
                    {overlay?.authorName && <span className="post-card-author">{overlay.authorName}</span>}
                    <span className="post-card-source">{sourceLabelText}</span>
                </div>
                <span className="post-card-when">{formatRelativeDate(unixSecondsFromIso(sample.publishedAt))}</span>
                <a
                    className="post-card-permalink"
                    href={sample.sourceUrl}
                    target="_blank"
                    rel="noreferrer"
                    aria-label="Open the original post in a new tab"
                    title="Open the original in a new tab"
                >
                    View original
                </a>
            </header>

            {overlay?.title && <h4 className="post-card-title">{overlay.title}</h4>}

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

            {overlay?.engagement && <EngagementRow engagement={overlay.engagement} flavor={flavor!} />}

            <div className="post-card-analysis">
                <AdmissionBadge admissionClass={sample.admissionClass} />
                {toneBadgeClass && overlay?.toneLabel && (
                    <span
                        className={`badge ${toneBadgeClass}`}
                        title={overlay.toneConfidence != null
                            ? `Model confidence in this label: ${formatPct(overlay.toneConfidence * 100, { decimals: 0 })}`
                            : undefined}
                    >
                        {overlay.toneLabel}
                    </span>
                )}
                {overlay?.targets.map((t, i) => (
                    <span
                        key={i}
                        className={`badge ${TONE_BADGE_CLASS[t.stance] ?? 'badge-neutral'}`}
                        title="Who the post's sentiment is directed at, per the model's target extraction"
                    >
                        about {t.label} · {t.stance}
                    </span>
                ))}
                {overlay?.botLabel && (
                    <span
                        className="badge badge-warning"
                        title="Our detector flags this post as likely automated. A lead, not a verdict."
                    >
                        {BOT_LABEL_TEXT[overlay.botLabel] ?? overlay.botLabel}
                    </span>
                )}
                {overlay?.techniques.map((t, i) => (
                    <span
                        key={i}
                        className="badge badge-negative"
                        title="Model confidence that this technique is present"
                    >
                        {TECHNIQUE_LABEL[t.technique] ?? t.technique}
                        {' · '}
                        {formatPct(t.confidence * 100, { decimals: 0 })}
                    </span>
                ))}
                {overlay?.sarcasm && (
                    <span
                        className="badge badge-neutral"
                        title="The model flagged possible sarcasm or irony — tone labels on sarcastic posts are less reliable."
                    >
                        sarcasm flagged
                    </span>
                )}
                <span className="post-card-confidence" title="Model confidence in this run's label">
                    {confPct} confidence
                </span>
                <button type="button" className="link-button" onClick={() => setShowDetail(true)}>
                    Read full document →
                </button>
            </div>

            {overlay?.reasoning && (
                <details className="post-card-reasoning">
                    <summary>Why this label</summary>
                    <p>{overlay.reasoning}</p>
                </details>
            )}

            {showDetail && (
                <DocDetailModal docId={sample.docId} onClose={() => setShowDetail(false)} />
            )}
        </article>
    );
}

function unixSecondsFromIso(iso: string | null | undefined): number | null {
    if (!iso) return null;
    const t = Date.parse(iso);
    return Number.isNaN(t) ? null : Math.floor(t / 1000);
}

// --------------------------------------------------------------------------- //
//  List wrapper — samples stay labeled as samples                             //
// --------------------------------------------------------------------------- //

interface PostCardListProps {
    samples: SampleDoc[];
    /** Required sample framing rendered above the cards, e.g.
     *  "Highest-confidence samples from this window — not a complete feed." */
    sampleNote: string;
    /** Copy when the list is empty. Omit to render nothing on empty. */
    emptyNote?: ReactNode;
}

export function PostCardList({ samples, sampleNote, emptyNote }: PostCardListProps) {
    const unique = dedupeById(samples, (s) => s.docId);
    if (unique.length === 0) {
        return emptyNote
            ? <p className="text-sm text-muted" style={{ fontStyle: 'italic' }}>{emptyNote}</p>
            : null;
    }
    return (
        <div className="post-card-list">
            <p className="post-card-list-note">{sampleNote}</p>
            {unique.map((s) => <PostCard key={s.docId} sample={s} />)}
        </div>
    );
}

export default PostCard;
