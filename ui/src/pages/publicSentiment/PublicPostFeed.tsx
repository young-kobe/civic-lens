import { useCallback } from 'react';
import { PaginatedPostFeed } from '../../components/common/PaginatedPostFeed';
import { sampleToPostCard } from '../../components/common/PostCard';
import { fetchPublicPosts, type TimeWindow } from '../../services/api';
import type { Topic } from '../../services/topics';

// --------------------------------------------------------------------------- //
//  PublicPostFeed — the sentiment page's public column: the shared            //
//  PaginatedPostFeed frame over GET /public-posts (most-engaged sampled       //
//  Reddit/X posts, officials excluded server-side by the canonical            //
//  kind='official' predicate, topic filtered server-side).                    //
// --------------------------------------------------------------------------- //

interface PublicPostFeedProps {
    timeWindow: TimeWindow;
    activeTopic: Topic;
}

export function PublicPostFeed({ timeWindow, activeTopic }: PublicPostFeedProps) {
    const topicParam = activeTopic.key === 'all' ? null : activeTopic.key;

    const fetchPage = useCallback(async (page: number) => {
        const resp = await fetchPublicPosts(timeWindow, topicParam, page);
        return { items: resp.items.map(sampleToPostCard), total: resp.total };
    }, [timeWindow, topicParam]);

    return (
        <PaginatedPostFeed
            fetchPage={fetchPage}
            resetKey={`${timeWindow}:${topicParam ?? 'all'}`}
            sampleNote={
                (topicParam ? `Filtered to ${activeTopic.label}. ` : '')
                + 'Sampled public discourse (Reddit and X), ordered by engagement — a reach '
                + 'proxy, not verified audience. A sample, not the full corpus.'
            }
            emptyNote={topicParam
                ? `No sampled public posts about ${activeTopic.label} in this window.`
                : 'No sampled public posts in this window.'}
        />
    );
}

export default PublicPostFeed;
