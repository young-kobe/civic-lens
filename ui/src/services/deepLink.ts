import { useCallback, useEffect, useState } from 'react';

/**
 * Deep-link routing — `#<tab>?<param>=<value>` — without a router
 * dependency.
 *
 * The app already mirrors the active tab into `location.hash`
 * (`#sentiment`, `#narratives`, ...). This module extends that token with
 * page params AFTER the hash so tab + params travel as one unit:
 *
 *   #narratives?open=142
 *   #sentiment?topic=economy&entity=official:SenSchumer
 *   #propaganda?technique=loaded_language
 *
 * Old bare-hash links (`#sentiment`) keep working — parseRoute simply
 * yields empty params. Writers use history.replaceState so params don't
 * spam the back-button stack.
 */

export interface Route {
    tab: string;
    params: URLSearchParams;
}

/** Split `#tab?a=b` into { tab, params }. Accepts the raw location.hash. */
export function parseRoute(hash: string): Route {
    const raw = hash.replace(/^#/, '');
    const qIdx = raw.indexOf('?');
    if (qIdx === -1) return { tab: raw, params: new URLSearchParams() };
    return {
        tab: raw.slice(0, qIdx),
        params: new URLSearchParams(raw.slice(qIdx + 1)),
    };
}

/** Build a `#tab?a=b` hash token. Empty params → bare `#tab`; the home
 *  tab renders as an empty hash to match the app's existing convention. */
export function buildRoute(tab: string, params?: URLSearchParams | Record<string, string>): string {
    const qs = params
        ? (params instanceof URLSearchParams ? params : new URLSearchParams(params)).toString()
        : '';
    if (tab === 'home' || tab === '') return qs ? `#home?${qs}` : '';
    return qs ? `#${tab}?${qs}` : `#${tab}`;
}

/** Href for a cross-page link, preserving the current search string. */
export function deepLinkHref(tab: string, params?: Record<string, string>): string {
    return `${window.location.pathname}${window.location.search}${buildRoute(tab, params)}`;
}

/** Read one param from the current hash route. */
export function readHashParam(key: string): string | null {
    return parseRoute(window.location.hash).params.get(key);
}

/** Write (or delete, when value is null) one param on the current hash
 *  route in place, preserving the tab and other params. */
export function writeHashParam(key: string, value: string | null): void {
    const route = parseRoute(window.location.hash);
    if (value === null) {
        route.params.delete(key);
    } else {
        route.params.set(key, value);
    }
    const url = `${window.location.pathname}${window.location.search}${buildRoute(route.tab, route.params)}`;
    window.history.replaceState({}, '', url);
}

/**
 * One hash param as React state. Reads on mount and on every hashchange
 * (covers cross-page links, back/forward, and manual edits); setting
 * writes through to the hash via replaceState.
 */
export function useDeepLinkParam(key: string): [string | null, (value: string | null) => void] {
    const [value, setValue] = useState<string | null>(() => readHashParam(key));

    useEffect(() => {
        const onHashChange = () => setValue(readHashParam(key));
        window.addEventListener('hashchange', onHashChange);
        return () => window.removeEventListener('hashchange', onHashChange);
    }, [key]);

    const set = useCallback((next: string | null) => {
        writeHashParam(key, next);
        setValue(next);
    }, [key]);

    return [value, set];
}

/**
 * Navigate to another tab with params — the programmatic counterpart of
 * clicking a deep link. Uses a real hash assignment (not replaceState) so
 * the app's hashchange listener fires and switches tabs.
 */
export function navigateTo(tab: string, params?: Record<string, string>): void {
    window.location.hash = buildRoute(tab, params) || '#home';
}
