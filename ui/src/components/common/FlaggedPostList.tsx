import { dedupeById } from '../../services/dedupe';
import type { FlaggedExample } from '../../types';

interface FlaggedPostListProps {
    posts: FlaggedExample[];
    /** Copy shown when the list is empty. */
    emptyNote?: string;
}

/**
 * Evidence list of bot-flagged posts: quoted excerpt, source label, and a
 * "View original" outbound link when the backend synthesized one
 * (invariant C1). Shared by the Bot Detector's amplification modal and
 * entity drill-down modal — keep the rendering identical so evidence
 * reads the same everywhere.
 */
function FlaggedPostList({ posts, emptyNote }: FlaggedPostListProps) {
    const unique = dedupeById(posts, (ex) => ex.doc_id);
    if (unique.length === 0) {
        return (
            <div className="text-sm text-muted" style={{ fontStyle: 'italic' }}>
                {emptyNote ?? 'No individual posts surfaced yet.'}
            </div>
        );
    }
    return (
        <div className="flex flex-col gap-2">
            {unique.map((ex) => (
                <div
                    key={ex.doc_id}
                    style={{
                        padding: 'var(--space-2) var(--space-3)',
                        background: 'var(--bg-inset)',
                        borderLeft: '2px solid var(--neutral-300)',
                        borderRadius: '2px',
                    }}
                >
                    <div className="text-sm" style={{ fontStyle: 'italic', marginBottom: 4 }}>
                        "{ex.text}"
                    </div>
                    <div
                        className="text-xs text-muted"
                        style={{ display: 'flex', gap: 'var(--space-2)', alignItems: 'baseline' }}
                    >
                        <span>{ex.source_label}</span>
                        {ex.url && (
                            <a
                                href={ex.url}
                                target="_blank"
                                rel="noreferrer"
                                className="example-row-link"
                            >
                                View original ↗
                            </a>
                        )}
                    </div>
                </div>
            ))}
        </div>
    );
}

export default FlaggedPostList;
