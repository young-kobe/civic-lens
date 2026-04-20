import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Minimal fetch hook with an optional module-level cache keyed by `cacheKey`.
 *
 * Motivation: each page refetched on every mount, so tab-switching re-hit
 * the API. React Query / SWR is overkill for a handful of JSON endpoints;
 * a Map of "key → last resolved value" gets us instant tab switches without
 * adding a data-layer dependency.
 *
 * Behavior:
 *   - If `cacheKey` hits in the cache, `data` resolves synchronously and
 *     no fetch runs. Callers can still force a reload via `refetch()`.
 *   - When the `deps` array changes, the cache is consulted with the new
 *     key; a miss triggers a real fetch.
 *   - Concurrent unmounts are safe — late responses for a stale deps
 *     array are ignored via the mounted-ref guard.
 *
 * This is deliberately NOT a persistent cache (no localStorage, no TTL).
 * A page refresh clears it. Add TTL or invalidation when a real need arrives.
 */

const CACHE = new Map<string, unknown>();

export function invalidateFetchCache(cacheKey?: string) {
    if (cacheKey === undefined) {
        CACHE.clear();
    } else {
        CACHE.delete(cacheKey);
    }
}

interface UseFetchState<T> {
    data: T | null;
    loading: boolean;
    error: Error | null;
    refetch: () => void;
}

export function useFetch<T>(
    fn: () => Promise<T>,
    deps: React.DependencyList,
    cacheKey?: string,
): UseFetchState<T> {
    const initial = cacheKey && CACHE.has(cacheKey) ? (CACHE.get(cacheKey) as T) : null;
    const [data, setData] = useState<T | null>(initial);
    const [loading, setLoading] = useState<boolean>(initial === null);
    const [error, setError] = useState<Error | null>(null);
    // Bump to force a refetch; also used as an effect dep.
    const [reloadToken, setReloadToken] = useState(0);
    const mounted = useRef(true);

    useEffect(() => {
        mounted.current = true;
        return () => {
            mounted.current = false;
        };
    }, []);

    useEffect(() => {
        // Synchronous cache hit (reloadToken === 0) — skip the network.
        if (reloadToken === 0 && cacheKey && CACHE.has(cacheKey)) {
            setData(CACHE.get(cacheKey) as T);
            setLoading(false);
            setError(null);
            return;
        }
        let cancelled = false;
        setLoading(true);
        setError(null);
        fn()
            .then((result) => {
                if (cancelled || !mounted.current) return;
                if (cacheKey) CACHE.set(cacheKey, result);
                setData(result);
                setLoading(false);
            })
            .catch((err: unknown) => {
                if (cancelled || !mounted.current) return;
                setError(err instanceof Error ? err : new Error(String(err)));
                setLoading(false);
            });
        return () => {
            cancelled = true;
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [...deps, reloadToken]);

    const refetch = useCallback(() => {
        if (cacheKey) CACHE.delete(cacheKey);
        setReloadToken((t) => t + 1);
    }, [cacheKey]);

    return { data, loading, error, refetch };
}
