import { COLORS } from '../../theme';
import { dedupeById } from '../../services/dedupe';
import type { ClassificationSample, SupportingDoc } from '../../types';

// --------------------------------------------------------------------------- //
//  Shared drill-down table used on Political Narratives and Overall Tone.     //
//  Each row surfaces one supporting doc with headline / source / when / tone  //
//  + confidence / LLM reasoning / outbound link. Styled via the paper-feel    //
//  `.supporting-docs-table*` rules in index.css.                              //
// --------------------------------------------------------------------------- //

const SENT_LABEL_COLOR: Record<string, string> = {
    positive: COLORS.positive,
    negative: COLORS.negative,
    neutral:  'var(--neutral-500)',
};

function formatRelativeDate(unixSeconds: number | null): string {
    if (!unixSeconds) return '—';
    const d = new Date(unixSeconds * 1000);
    const days = Math.floor((Date.now() - d.getTime()) / (1000 * 60 * 60 * 24));
    if (days === 0) return 'today';
    if (days === 1) return '1 day ago';
    if (days < 30) return `${days} days ago`;
    return d.toISOString().slice(0, 10);
}

interface SupportingDocsTableProps {
    docs: SupportingDoc[];
    /** Override the Date column's relative-time label. Defaults to the
     *  "2 days ago / 2026-04-14" formatter matching the Narratives page. */
    whenLabel?: (published_at: number | null) => string;
}

export function SupportingDocsTable({ docs, whenLabel = formatRelativeDate }: SupportingDocsTableProps) {
    // Dedupe defensively: backend joins on ai_outputs which has no
    // (doc_id, task_type) unique constraint, so the same doc can arrive
    // multiple times when prompts have been re-run.
    const unique = dedupeById(docs, (d) => d.doc_id);
    if (unique.length === 0) return null;
    return (
        <div className="supporting-docs-table-wrap">
            <table className="supporting-docs-table">
                <thead>
                    <tr>
                        <th scope="col">Headline</th>
                        <th scope="col">Source</th>
                        <th scope="col">When</th>
                        <th scope="col">Tone</th>
                        <th scope="col">Reasoning</th>
                        <th scope="col" aria-label="Open source"></th>
                    </tr>
                </thead>
                <tbody>
                    {unique.map((d) => {
                        const tone = d.sentiment_label;
                        const toneColor = tone ? SENT_LABEL_COLOR[tone] : 'var(--neutral-400)';
                        const conf = d.confidence != null ? Math.round(d.confidence * 100) : null;
                        return (
                            <tr key={d.doc_id}>
                                <td className="supporting-docs-headline" title={d.title ?? undefined}>
                                    {d.title || <span className="text-muted">(untitled)</span>}
                                </td>
                                <td className="supporting-docs-source">{d.source_label}</td>
                                <td className="supporting-docs-when text-muted">{whenLabel(d.published_at)}</td>
                                <td className="supporting-docs-tone">
                                    {tone ? (
                                        <span style={{ color: toneColor, fontWeight: 600, textTransform: 'capitalize' }}>
                                            {tone}
                                        </span>
                                    ) : <span className="text-muted">—</span>}
                                    {conf != null && (
                                        <span className="text-muted" style={{ marginLeft: 6, fontFamily: 'var(--font-mono)', fontSize: '10px' }}>
                                            {conf}%
                                        </span>
                                    )}
                                </td>
                                <td className="supporting-docs-reasoning" title={d.reasoning ?? undefined}>
                                    {d.reasoning ?? <span className="text-muted">—</span>}
                                </td>
                                <td className="supporting-docs-link">
                                    {d.url ? (
                                        <a href={d.url} target="_blank" rel="noreferrer" aria-label={`Open ${d.title || 'source'} in new tab`}>
                                            ↗
                                        </a>
                                    ) : null}
                                </td>
                            </tr>
                        );
                    })}
                </tbody>
            </table>
        </div>
    );
}

export default SupportingDocsTable;


// --------------------------------------------------------------------------- //
//  Adapter — convert a sentiment `ClassificationSample` into a SupportingDoc  //
//  row so the shared table can render samples alongside narrative-cluster    //
//  docs. Different shapes historically (backend sentiment sampler emits the   //
//  ClassificationSample shape; narrative aggregator emits the SupportingDoc   //
//  shape). Unifying the two wire shapes is tracked in the backend aggregator  //
//  audit (docs/todo-backend-aggregator-audit.md); until then this is the      //
//  boundary between them.                                                     //
// --------------------------------------------------------------------------- //

export function classificationSampleToSupportingDoc(s: ClassificationSample): SupportingDoc {
    let sentiment: SupportingDoc['sentiment_label'] = null;
    const raw = s.label?.toUpperCase();
    if (raw === 'POSITIVE') sentiment = 'positive';
    else if (raw === 'NEGATIVE') sentiment = 'negative';
    else if (raw === 'NEUTRAL' || raw === 'MIXED') sentiment = 'neutral';

    const st = s.source_type;
    const name = s.source_name;
    let sourceLabel: string;
    if (st === 'news') sourceLabel = name ? `News · ${name}` : 'News';
    else if (st === 'x_post') sourceLabel = name ? `X · @${name}` : 'X';
    else if (st?.startsWith('reddit')) sourceLabel = name ? `Reddit · r/${name}` : 'Reddit';
    else sourceLabel = st || 'unknown';

    let published: number | null = null;
    if (s.date) {
        const parsed = Date.parse(s.date);
        if (!isNaN(parsed)) published = Math.floor(parsed / 1000);
    }

    return {
        doc_id: s.doc_id,
        title: s.title || null,
        source_type: s.source_type || 'unknown',
        source_label: sourceLabel,
        url: s.url ?? null,
        published_at: published,
        sentiment_label: sentiment,
        confidence: typeof s.confidence === 'number' ? s.confidence : null,
        reasoning: s.reasoning || null,
    };
}
