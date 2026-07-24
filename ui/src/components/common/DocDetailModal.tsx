import { useState } from 'react';
import Modal from './Modal';
import { AdmissionBadge } from './AdmissionBadge';
import { fetchDocument } from '../../services/api';
import { useFetch } from '../../services/useFetch';
import { formatRelativeDate } from '../../services/format';
import type { AnalysisResult, CitationEdge, DocumentDetail } from '../../types';

// --------------------------------------------------------------------------- //
//  DocDetailModal — the universal doc drill-down (GET /docs/{doc_id}).       //
//  Every doc/citation reference with a docId anywhere in the app (narrative  //
//  cited docs, sample cards, review-queue rows) opens this. Citations        //
//  resolve regardless of age — the smallest useful surface: core fields,     //
//  every current analysis result, and citations in/out.                     //
// --------------------------------------------------------------------------- //

function unixSecondsFromIso(iso: string | null): number | null {
    if (!iso) return null;
    const t = Date.parse(iso);
    return Number.isNaN(t) ? null : Math.floor(t / 1000);
}

function AnalysisResultRow({ result }: { result: AnalysisResult }) {
    const fieldEntries = Object.entries(result.fields);
    return (
        <div className="doc-analysis-row">
            <div className="doc-analysis-row-head">
                <span className="badge badge-accent">{result.task}</span>
                <span className="text-xs text-muted">
                    {result.modelId}
                    {result.promptVersion ? ` · ${result.promptVersion}` : ''}
                    {result.confidence != null ? ` · confidence ${(result.confidence * 100).toFixed(0)}%` : ''}
                </span>
            </div>
            {fieldEntries.length > 0 && (
                <dl className="doc-analysis-fields">
                    {fieldEntries.map(([key, value]) => (
                        <div key={key} className="doc-analysis-field">
                            <dt>{key}</dt>
                            <dd>{typeof value === 'object' ? JSON.stringify(value) : String(value)}</dd>
                        </div>
                    ))}
                </dl>
            )}
        </div>
    );
}

function CitationRow({ edge }: { edge: CitationEdge }) {
    const [showNested, setShowNested] = useState(false);
    const href = edge.sourceUrl ?? edge.targetUrl ?? undefined;

    return (
        <li className="doc-citation-row">
            <span className="badge badge-neutral">{edge.linkType}</span>
            {edge.docId != null ? (
                <button type="button" className="link-button" onClick={() => setShowNested(true)}>
                    Doc #{edge.docId}
                </button>
            ) : href ? (
                <a href={href} target="_blank" rel="noreferrer">{edge.targetUrl}</a>
            ) : (
                <span>unresolved link</span>
            )}
            {edge.admissionClass && <AdmissionBadge admissionClass={edge.admissionClass} />}
            {showNested && edge.docId != null && (
                <DocDetailModal docId={edge.docId} onClose={() => setShowNested(false)} />
            )}
        </li>
    );
}

function DocDetailBody({ doc }: { doc: DocumentDetail }) {
    const published = formatRelativeDate(unixSecondsFromIso(doc.publishedAt));
    return (
        <>
            <div className="doc-detail-meta">
                <AdmissionBadge admissionClass={doc.admissionClass} />
                <span className="text-xs text-muted">{doc.sourceType} · {published}</span>
                <a href={doc.sourceUrl} target="_blank" rel="noreferrer" className="example-row-link">
                    View original
                </a>
            </div>
            {doc.title && <h3 className="card-title mt-2">{doc.title}</h3>}
            <p className="doc-detail-body text-sm">{doc.body}</p>

            {doc.analysisResults.length > 0 && (
                <>
                    <h3 className="card-title mt-4 mb-2">Analysis results</h3>
                    <div className="doc-analysis-list">
                        {doc.analysisResults.map((r) => (
                            <AnalysisResultRow key={`${r.task}-${r.runId}`} result={r} />
                        ))}
                    </div>
                </>
            )}

            {(doc.citationsOut.length > 0 || doc.citationsIn.length > 0) && (
                <>
                    <h3 className="card-title mt-4 mb-2">Citations</h3>
                    {doc.citationsOut.length > 0 && (
                        <>
                            <div className="eyebrow">Links out</div>
                            <ul className="doc-citation-list">
                                {doc.citationsOut.map((edge, i) => <CitationRow key={`out-${i}`} edge={edge} />)}
                            </ul>
                        </>
                    )}
                    {doc.citationsIn.length > 0 && (
                        <>
                            <div className="eyebrow mt-2">Linked from</div>
                            <ul className="doc-citation-list">
                                {doc.citationsIn.map((edge, i) => <CitationRow key={`in-${i}`} edge={edge} />)}
                            </ul>
                        </>
                    )}
                    <p className="text-xs text-muted mt-2">
                        Citation edges connect documents we sampled — they do not establish where a
                        claim originated or how it spread outside our sample.
                    </p>
                </>
            )}
        </>
    );
}

export function DocDetailModal({ docId, onClose }: { docId: number; onClose: () => void }) {
    const { data, loading, error } = useFetch<DocumentDetail>(
        () => fetchDocument(docId), [docId], `doc:${docId}`,
    );

    return (
        <Modal isOpen onClose={onClose} kicker="Document" title={`Doc #${docId}`}>
            {loading && <p className="text-sm text-muted">Loading…</p>}
            {error && <p className="text-sm text-muted">Could not load this document: {error.message}</p>}
            {data && <DocDetailBody doc={data} />}
        </Modal>
    );
}

export default DocDetailModal;
