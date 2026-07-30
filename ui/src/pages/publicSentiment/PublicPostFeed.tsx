import { useCallback, useEffect, useRef, useState } from 'react';
import { PostCardList, sampleToPostCard } from '../../components/common/PostCard';
import { fetchPublicPosts, type TimeWindow } from '../../services/api';
import type { Topic } from '../../services/topics';
import type { ClassificationSample } from '../../types';

// --------------------------------------------------------------------------- //
//  PublicPostFeed — the sentiment page's public column: a paginated feed of   //
//  the most-engaged sampled Reddit/X posts (officials excluded server-side    //
//  by the canonical kind='official' predicate). Topic and window filter       //
//  server-side; "Load more" accumulates pages, and switching either filter    //
//  resets the feed.                                                           //
// --------------------------------------------------------------------------- //

interface PublicPostFeedProps {
    timeWindow: TimeWindow;
    activeTopic: Topic;
}

export function PublicPostFeed({ timeWindow, activeTopic }: PublicPostFeedProps) {
    const [items, setItems] = useState<ClassificationSample[]>([]);
    const [total, setTotal] = useState(0);
    const [nextPage, setNextPage] = useState(1);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    // Monotonic token so a slow response for a stale (window, topic) pair
    // can't append into the freshly reset feed.
    const requestSeq = useRef(0);

    const topicParam = activeTopic.key === 'all' ? null : activeTopic.key;

    const load = useCallback(async (page: number, replace: boolean) => {
        const seq = ++requestSeq.current;
        setLoading(true);
        setError(null);
        try {
            const resp = await fetchPublicPosts(timeWindow, topicParam, page);
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
    }, [timeWindow, topicParam]);

    useEffect(() => {
        setItems([]);
        setTotal(0);
        setNextPage(1);
        void load(1, true);
    }, [load]);

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
                {topicParam
                    ? `No sampled public posts about ${activeTopic.label} in this window.`
                    : 'No sampled public posts in this window.'}
            </p>
        );
    }

    return (
        <div className="public-post-feed">
            <PostCardList
                posts={items.map(sampleToPostCard)}
                sampleNote={
                    (topicParam ? `Filtered to ${activeTopic.label}. ` : '')
                    + 'Sampled public discourse (Reddit and X), ordered by engagement — a reach '
                    + 'proxy, not verified audience. A sample, not the full corpus.'
                }
            />
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

export default PublicPostFeed;
