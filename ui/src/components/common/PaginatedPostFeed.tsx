import { useCallback, useEffect, useRef, useState } from 'react';
import { PostCardList } from './PostCard';
import type { PostCardData } from './PostCard';

// --------------------------------------------------------------------------- //
//  PaginatedPostFeed — the shared public-column feed frame: PostCards behind  //
//  a Load-more, reset when the caller's filter identity (resetKey) changes,   //
//  with a monotonic request token so a stale response can't append into a     //
//  freshly reset feed. Callers supply fetchPage (already adapted to           //
//  PostCardData) and the page-specific sample/empty copy.                     //
// --------------------------------------------------------------------------- //

export interface PostFeedPage {
    items: PostCardData[];
    total: number;
}

interface PaginatedPostFeedProps {
    fetchPage: (page: number) => Promise<PostFeedPage>;
    /** Filter identity (window, topic, ...) — changing it resets the feed. */
    resetKey: string;
    sampleNote: string;
    emptyNote: string;
}

export function PaginatedPostFeed({ fetchPage, resetKey, sampleNote, emptyNote }: PaginatedPostFeedProps) {
    const [items, setItems] = useState<PostCardData[]>([]);
    const [total, setTotal] = useState(0);
    const [nextPage, setNextPage] = useState(1);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const requestSeq = useRef(0);

    const load = useCallback(async (page: number, replace: boolean) => {
        const seq = ++requestSeq.current;
        setLoading(true);
        setError(null);
        try {
            const resp = await fetchPage(page);
            if (seq !== requestSeq.current) return;
            setItems((prev) => (replace ? resp.items : [...prev, ...resp.items]));
            setTotal(resp.total);
            setNextPage(page + 1);
        } catch (e) {
            if (seq !== requestSeq.current) return;
            setError(e instanceof Error ? e.message : 'Failed to load posts');
        } finally {
            if (seq === requestSeq.current) setLoading(false);
        }
    }, [fetchPage]);

    useEffect(() => {
        setItems([]);
        setTotal(0);
        setNextPage(1);
        void load(1, true);
        // resetKey is the caller's declared filter identity; load already
        // changes with fetchPage, but resetKey keeps the reset explicit.
    }, [load, resetKey]);

    if (error) {
        return (
            <div className="public-post-feed">
                <p className="text-sm text-muted">Could not load posts: {error}</p>
                <button type="button" className="btn btn-secondary" onClick={() => void load(nextPage, nextPage === 1)}>
                    Retry
                </button>
            </div>
        );
    }

    if (!loading && total === 0) {
        return (
            <p className="text-xs text-muted" style={{ padding: 'var(--space-3)' }}>
                {emptyNote}
            </p>
        );
    }

    return (
        <div className="public-post-feed">
            <PostCardList posts={items} sampleNote={sampleNote} />
            {items.length < total && (
                <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => void load(nextPage, false)}
                    disabled={loading}
                >
                    {loading ? 'Loading…' : `Load more (${items.length} of ${total.toLocaleString()})`}
                </button>
            )}
        </div>
    );
}

export default PaginatedPostFeed;
