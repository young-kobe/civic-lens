import { useState } from 'react';
import type { ReactNode } from 'react';
import { dedupeById } from '../../services/dedupe';
import { formatRelativeDate } from '../../services/format';
import { AdmissionBadge } from './AdmissionBadge';
import { DocDetailModal } from './DocDetailModal';
import type { SampleDoc } from '../../types';

// --------------------------------------------------------------------------- //
//  SampleCard — the one way a sampled/official-record doc renders anywhere   //
//  on the site now that the strictly-live API only hands the UI a           //
//  SampleDocModel (docId, sourceUrl, snippet, confidence, admissionClass,    //
//  publishedAt). Per-sample AI labels (tone, propaganda technique, bot       //
//  reasoning, engagement, author) no longer travel with the sample — that    //
//  detail now lives behind "Read full document" (GET /docs/{doc_id}), which  //
//  this card links into via DocDetailModal.                                  //
// --------------------------------------------------------------------------- //

function unixSecondsFromIso(iso: string | null | undefined): number | null {
    if (!iso) return null;
    const t = Date.parse(iso);
    return Number.isNaN(t) ? null : Math.floor(t / 1000);
}

export function SampleCard({ sample }: { sample: SampleDoc }) {
    const [showDetail, setShowDetail] = useState(false);
    const confPct = Math.round(sample.confidence * 100);

    return (
        <article className="post-card">
            <header className="post-card-head">
                <div className="post-card-head-text">
                    <AdmissionBadge admissionClass={sample.admissionClass} />
                </div>
                <span className="post-card-when">
                    {formatRelativeDate(unixSecondsFromIso(sample.publishedAt))}
                </span>
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

            {sample.snippet && <p className="post-card-body">{sample.snippet}</p>}

            <div className="post-card-analysis">
                <span
                    className="post-card-confidence"
                    title="Model confidence in the current analysis run against this doc"
                >
                    {confPct}% confidence
                </span>
                <button
                    type="button"
                    className="link-button"
                    onClick={() => setShowDetail(true)}
                >
                    Read full document →
                </button>
            </div>

            {showDetail && (
                <DocDetailModal docId={sample.docId} onClose={() => setShowDetail(false)} />
            )}
        </article>
    );
}

interface SampleCardListProps {
    samples: SampleDoc[];
    /** Required sample framing rendered above the cards, e.g.
     *  "Highest-confidence samples from this window — not a complete feed." */
    sampleNote: string;
    /** Copy when the list is empty. Omit to render nothing on empty. */
    emptyNote?: ReactNode;
}

export function SampleCardList({ samples, sampleNote, emptyNote }: SampleCardListProps) {
    const unique = dedupeById(samples, (s) => s.docId);
    if (unique.length === 0) {
        return emptyNote
            ? <p className="text-sm text-muted" style={{ fontStyle: 'italic' }}>{emptyNote}</p>
            : null;
    }
    return (
        <div className="post-card-list">
            <p className="post-card-list-note">{sampleNote}</p>
            {unique.map((s) => <SampleCard key={s.docId} sample={s} />)}
        </div>
    );
}

export default SampleCard;
