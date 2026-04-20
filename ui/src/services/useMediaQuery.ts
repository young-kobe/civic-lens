import { useEffect, useState } from 'react';

/**
 * Subscribe to a CSS media query. Returns true when the query currently
 * matches. Used when JS has to branch on viewport (e.g. render bottom-tabs
 * vs top-tabs). For purely visual changes prefer a `@media` rule in
 * index.css — there's no JS re-render cost and the layout is correct on
 * first paint.
 *
 * SSR-safe: returns false until the first client-side effect.
 */
export function useMediaQuery(query: string): boolean {
    const getMatch = () =>
        typeof window !== 'undefined' && typeof window.matchMedia === 'function'
            ? window.matchMedia(query).matches
            : false;

    const [matches, setMatches] = useState<boolean>(getMatch);

    useEffect(() => {
        if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
            return;
        }
        const mql = window.matchMedia(query);
        // Sync in case the value changed between render and effect.
        setMatches(mql.matches);

        const onChange = (e: MediaQueryListEvent) => setMatches(e.matches);
        mql.addEventListener('change', onChange);
        return () => mql.removeEventListener('change', onChange);
    }, [query]);

    return matches;
}

// Shared breakpoint tokens so JS (useMediaQuery) and CSS (index.css)
// agree on the same cutoffs. Mirrors the SCSS/Tailwind convention.
export const BREAKPOINTS = {
    /** Phones — single-column layouts, bottom tab nav. */
    mobile: '(max-width: 640px)',
    /** Small tablets — single-column cards but top nav stays. */
    tablet: '(max-width: 768px)',
    /** Desktops — three-column grids collapse below this. */
    desktop: '(max-width: 1024px)',
} as const;
