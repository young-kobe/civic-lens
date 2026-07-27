/**
 * Shared display-label builder for received-tone provenance cells
 * (`ReceivedSourceCell.sourceClass` + `.lean`) -- used by PublicSentiment's
 * "Where this tone comes from" block and EntityProfileCard's "mostly from"
 * suffix so both surfaces describe the same group the same way. The
 * backend emits only raw `sourceClass`/`lean` values (flat
 * `corpus.political_lean` enum: democrat/republican/independent/mixed/
 * unknown); labels are built here, once, never duplicated per caller.
 */

import type { ReceivedSourceCell } from '../types';

function leanAdjective(lean: string | null | undefined): string | null {
    switch (lean) {
        case 'democrat': return 'left-leaning';
        case 'republican': return 'right-leaning';
        case 'independent': return 'independent-leaning';
        case 'mixed': return 'mixed-lean';
        default: return null;
    }
}

function partyAdjective(lean: string | null | undefined): string {
    switch (lean) {
        case 'democrat': return 'Democratic';
        case 'republican': return 'Republican';
        case 'independent': return 'independent';
        case 'mixed': return 'cross-party';
        default: return 'unaffiliated';
    }
}

/** Plain display label for one provenance group cell, built from its
 *  sourceClass + lean -- never a pre-baked backend string. Never implies
 *  completeness; callers pair this with a "share of sampled mentions"
 *  caption rather than treating it as a full accounting. */
export function sourceGroupLabel(
    sourceClass: ReceivedSourceCell['sourceClass'],
    lean: string | null | undefined,
): string {
    switch (sourceClass) {
        case 'news_outlet': {
            const adj = leanAdjective(lean);
            return adj ? `${adj} outlets` : 'outlets with no rated lean';
        }
        case 'official':
            return `${partyAdjective(lean)} officials`;
        case 'account': {
            const party = partyAdjective(lean);
            return party === 'unaffiliated' ? 'unaffiliated accounts' : `${party}-aligned accounts`;
        }
        case 'subreddit': {
            const adj = leanAdjective(lean);
            return adj ? `${adj} subreddits` : 'sampled subreddits';
        }
        case 'x_user':
            return 'sampled X users';
        case 'other':
        default:
            return 'unattributed sources';
    }
}

/** Top-N provenance cells by share, descending -- the two biggest slices
 *  feed both the modal's lead sentence and the card's "mostly from" line. */
export function topGroupsByShare(cells: ReceivedSourceCell[], n = 2): ReceivedSourceCell[] {
    return [...cells].sort((a, b) => b.share - a.share).slice(0, n);
}
