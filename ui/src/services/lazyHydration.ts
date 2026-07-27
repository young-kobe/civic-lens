import { useEffect, useState } from 'react';

// --------------------------------------------------------------------------- //
//  createLazyResource — concurrency-capped, cache-backed per-id hydration.    //
//                                                                             //
//  A page can render dozens of cards at once (PostCardList, entity grids);    //
//  each card wants to hydrate one detail fetch (GET /docs/{id},               //
//  GET /entity-profile/{id}) without every card firing in the same tick and   //
//  saturating the connection pool. This factory builds one bounded queue +   //
//  a module-level cache per resource type, so a given id is fetched at most   //
//  once for the page's lifetime.                                             //
// --------------------------------------------------------------------------- //

const MAX_CONCURRENT = 4;

export interface LazyResource<T> {
    data: T | null;
    loading: boolean;
    error: Error | null;
}

/** Builds a `useResource(id)` hook backed by its own cache + queue. Two
 *  calls to `createLazyResource` never share state, even for the same T. */
export function createLazyResource<T>(fetcher: (id: number) => Promise<T>) {
    const cache = new Map<number, T>();
    const inFlight = new Map<number, Promise<T>>();
    const queue: Array<() => void> = [];
    let active = 0;

    function runNext(): void {
        if (active >= MAX_CONCURRENT || queue.length === 0) return;
        active += 1;
        const job = queue.shift()!;
        job();
    }

    function load(id: number): Promise<T> {
        const cached = cache.get(id);
        if (cached !== undefined) return Promise.resolve(cached);
        const pending = inFlight.get(id);
        if (pending) return pending;

        const promise = new Promise<T>((resolve, reject) => {
            queue.push(() => {
                fetcher(id)
                    .then((result) => {
                        cache.set(id, result);
                        resolve(result);
                    })
                    .catch(reject)
                    .finally(() => {
                        active -= 1;
                        inFlight.delete(id);
                        runNext();
                    });
            });
            runNext();
        });
        inFlight.set(id, promise);
        return promise;
    }

    return function useLazyResource(id: number | null | undefined): LazyResource<T> {
        const [data, setData] = useState<T | null>(id != null ? cache.get(id) ?? null : null);
        const [loading, setLoading] = useState<boolean>(id != null && !cache.has(id));
        const [error, setError] = useState<Error | null>(null);

        useEffect(() => {
            if (id == null) return;
            if (cache.has(id)) {
                setData(cache.get(id)!);
                setLoading(false);
                return;
            }
            let cancelled = false;
            setLoading(true);
            setError(null);
            load(id)
                .then((result) => {
                    if (cancelled) return;
                    setData(result);
                    setLoading(false);
                })
                .catch((err: unknown) => {
                    if (cancelled) return;
                    setError(err instanceof Error ? err : new Error(String(err)));
                    setLoading(false);
                });
            return () => {
                cancelled = true;
            };
        }, [id]);

        return { data, loading, error };
    };
}
