import { COLORS } from '../../theme';
import type { NarrativeSummary } from '../../types';

// --------------------------------------------------------------------------- //
//  Shared source-mix helpers for the Narratives page. The pre-redesign model  //
//  grouped narratives by first_seen_tier_group (news/officials/public); that  //
//  field no longer exists on NarrativeSummary. sourceBreakdown (news/reddit/  //
//  x post counts) is the closest surviving cousin, so the lifecycle panel,    //
//  cross-source list, and three-way grid all key off it instead.             //
// --------------------------------------------------------------------------- //

export const SOURCE_LABEL: Record<string, string> = {
    news: 'News', reddit: 'Reddit', x: 'X',
};

export const SOURCE_COLOR: Record<string, string> = {
    news: COLORS.sourceNews, reddit: COLORS.sourceReddit, x: COLORS.sourceX,
};

/** Sources with at least one member doc, in sourceBreakdown order. */
export function sourcesPresent(n: NarrativeSummary): string[] {
    return n.sourceBreakdown.filter((item) => item.docCount > 0).map((item) => item.source);
}

/** The source contributing the most member docs, or null if the narrative
 *  carries no source breakdown at all. */
export function dominantSource(n: NarrativeSummary): string | null {
    let best: { source: string; docCount: number } | null = null;
    for (const item of n.sourceBreakdown) {
        if (!best || item.docCount > best.docCount) best = item;
    }
    return best ? best.source : null;
}
