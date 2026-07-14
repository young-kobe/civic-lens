import { deepLinkHref } from '../../services/deepLink';
import type { EntityProfile } from '../../types';

// --------------------------------------------------------------------------- //
//  EntityHubLinks — "See this entity on: ..." row inside entity modals.       //
//                                                                             //
//  Every data page can resolve a "#<tab>?entity=<kind>:<key>" deep link to    //
//  the matching entity's modal, so one entity's tone, propaganda, and bot     //
//  readings are one click apart. An entity with no data on the target page   //
//  no-ops there (the param clears) — the link promises the page, not a       //
//  guaranteed reading.                                                        //
// --------------------------------------------------------------------------- //

/** Wire form of the entity param: "kind:key". Keys (domains, handles,
 *  subreddits, catch-all sentinels) never contain ':'. */
export function entityParamValue(profile: Pick<EntityProfile, 'kind' | 'key'>): string {
    return `${profile.kind}:${profile.key}`;
}

/** Parse "kind:key" back out; null for malformed values. */
export function parseEntityParam(value: string | null): { kind: string; key: string } | null {
    if (!value) return null;
    const idx = value.indexOf(':');
    if (idx <= 0 || idx === value.length - 1) return null;
    return { kind: value.slice(0, idx), key: value.slice(idx + 1) };
}

const HUB_TABS: Array<{ tab: string; label: string }> = [
    { tab: 'sentiment', label: 'Overall Tone' },
    { tab: 'propaganda', label: 'Propaganda' },
    { tab: 'bots', label: 'Bot Detector' },
];

interface EntityHubLinksProps {
    profile: EntityProfile;
    /** The tab the modal lives on — excluded from the row. */
    currentTab: string;
}

export function EntityHubLinks({ profile, currentTab }: EntityHubLinksProps) {
    // Catch-all buckets are tier aggregates, not entities — cross-page
    // identity isn't meaningful for "Other X users".
    if (profile.kind === 'catch_all') return null;
    const targets = HUB_TABS.filter((t) => t.tab !== currentTab);
    return (
        <div className="entity-hub-links">
            <span className="entity-hub-links-label">See this entity on:</span>
            {targets.map((t) => (
                <a
                    key={t.tab}
                    href={deepLinkHref(t.tab, { entity: entityParamValue(profile) })}
                    className="entity-hub-link"
                >
                    {t.label} →
                </a>
            ))}
        </div>
    );
}

export default EntityHubLinks;
