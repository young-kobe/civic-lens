import { deepLinkHref } from '../../services/deepLink';

// --------------------------------------------------------------------------- //
//  EntityHubLinks — "See this entity on: ..." row inside entity modals.       //
//                                                                             //
//  Every data page can resolve a "#<tab>?entity=<entityId>" deep link to      //
//  the matching entity's modal, so one entity's tone, propaganda, and bot     //
//  readings are one click apart. Adapted from the pre-cutover "kind:key"      //
//  param to the current API's numeric entityId — the identity token          //
//  GET /entity-posts and GET /entity-profile/{id} already key on. The         //
//  registry no longer exposes the old domain/handle/subreddit `key` at all.   //
//  An entity with no data on the target page no-ops there (the param         //
//  clears) — the link promises the page, not a guaranteed reading. Catch-all //
//  buckets (no entityId) render nothing: cross-page identity isn't           //
//  meaningful for "Other X users".                                           //
// --------------------------------------------------------------------------- //

const ENTITY_PARAM = 'entity';

/** Wire form of the entity param — just the numeric entityId. */
export function entityParamValue(entityId: number): string {
    return String(entityId);
}

/** Parse the entity param back into an entityId; null for malformed/missing values. */
export function parseEntityParam(value: string | null): number | null {
    if (!value) return null;
    const id = Number(value);
    return Number.isInteger(id) ? id : null;
}

const HUB_TABS: Array<{ tab: string; label: string }> = [
    { tab: 'sentiment', label: 'Overall Tone' },
    { tab: 'propaganda', label: 'Propaganda' },
    { tab: 'bots', label: 'Bot Detector' },
];

interface EntityHubLinksProps {
    /** Registry entity id — pass null for unresolved/catch-all buckets,
     *  which have no cross-page identity; the row renders nothing. */
    entityId: number | null;
    /** The tab the modal lives on — excluded from the row. */
    currentTab: string;
}

export function EntityHubLinks({ entityId, currentTab }: EntityHubLinksProps) {
    if (entityId == null) return null;
    const targets = HUB_TABS.filter((t) => t.tab !== currentTab);
    return (
        <div className="entity-hub-links">
            <span className="entity-hub-links-label">See this entity on:</span>
            {targets.map((t) => (
                <a
                    key={t.tab}
                    href={deepLinkHref(t.tab, { [ENTITY_PARAM]: entityParamValue(entityId) })}
                    className="entity-hub-link"
                >
                    {t.label} →
                </a>
            ))}
        </div>
    );
}

export default EntityHubLinks;
