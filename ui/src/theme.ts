/**
 * Color tokens — wrappers around the CSS custom properties in
 * `ui/src/index.css` :root.
 *
 * **Source of truth = index.css**. This file exposes a typed
 * `COLORS` dictionary that returns `var(--token)` strings so
 * TS components get autocomplete + typo catching without
 * duplicating hex values.
 *
 * Rule: **components reference `COLORS.foo` OR plain CSS classes —
 * never inline `'var(--…)'` strings**. That keeps one convention
 * everywhere and makes renames safe. The only hex left in the
 * codebase should be inside CSS custom properties.
 */

/**
 * Typed color dictionary. Keys are camelCase; values are
 * `var(--token)` strings that resolve at paint time via the
 * CSS custom properties in `:root`.
 *
 * Grouped for readability; when you need a new token:
 *  1. Add the `--foo-bar` var to `:root` in `index.css`.
 *  2. Add a matching key to this object.
 *  3. Use `COLORS.fooBar` in components.
 */
export const COLORS = {
    // Semantic palette
    positive:      'var(--semantic-positive)',
    positiveLight: 'var(--semantic-positive-light)',
    negative:      'var(--semantic-negative)',
    negativeLight: 'var(--semantic-negative-light)',
    warning:       'var(--semantic-warning)',
    neutral:       'var(--semantic-neutral)',
    neutralLight:  'var(--semantic-neutral-light)',

    // Speaker-tier identity — News / Officials / Public. High-contrast and
    // CVD-distinct; used for the tier trend sparklines and tier-coded marks.
    tierNews:      'var(--tier-news)',
    tierOfficials: 'var(--tier-officials)',
    tierPublic:    'var(--tier-public)',

    // Partisan lean — the colored lean chips (a data legend).
    leanLeft:       'var(--lean-left)',
    leanRight:      'var(--lean-right)',

    // Interactive-chrome accent (monochrome — resolves to a dark neutral).
    accent:       'var(--accent)',

    // Chart ramp. Only chartAccent is consumed in JS; --chart-grid is used
    // directly via var() in the chart components.
    chartAccent:         'var(--chart-accent)',

    // Source-type
    sourceNews:   'var(--source-news)',
    sourceReddit: 'var(--source-reddit)',
    sourceX:      'var(--source-x)',

    // Review admin
    adminBannerBg:     'var(--admin-banner-bg)',
    adminBannerBorder: 'var(--admin-banner-border)',
    adminBannerText:   'var(--admin-banner-text)',
    adminCardBg:       'var(--admin-card-bg)',
} as const;

/**
 * Map a sentiment label to its semantic foreground color.
 * Mirrors the CSS `.badge-*` / `.metric-delta-*` classes.
 */
export function sentimentColor(label: 'POSITIVE' | 'NEGATIVE' | 'NEUTRAL' | 'MIXED' | string): string {
  if (label === 'POSITIVE') return COLORS.positive;
  if (label === 'NEGATIVE') return COLORS.negative;
  if (label === 'MIXED')    return COLORS.warning;
  return COLORS.neutral;
}

/**
 * Map a numeric net-sentiment score to a threshold-based color.
 * Used by narratives and review screens.
 */
export function netSentimentColor(score: number, strongThreshold = 0.3, mildThreshold = 0.1): string {
  if (score >= strongThreshold)  return COLORS.positive;
  if (score >= mildThreshold)    return COLORS.positiveLight;
  if (score <= -strongThreshold) return COLORS.negative;
  if (score <= -mildThreshold)   return COLORS.negativeLight;
  return COLORS.neutral;
}

/**
 * Accuracy-bucket color for review stats: positive high, warning mid, negative low.
 */
export function accuracyColor(accuracyPct: number): string {
  if (accuracyPct >= 80) return COLORS.positive;
  if (accuracyPct >= 60) return COLORS.warning;
  return COLORS.negative;
}

/**
 * Partisan-lean "class name" for an EntityProfile — one of
 * `'left' | 'center' | 'right' | 'mixed' | 'neutral'`. Used by
 * EntityProfileCard for the left-border color and chip styling; each
 * class is paired with a `.lean-*` CSS rule in `index.css`.
 *
 * Officials derive their class from party (R → right, D → left, else
 * neutral). Outlets/subreddits use the registry's lean/tilt value
 * (left / center-left → left; right / center-right → right; mixed →
 * mixed; else center). Catch-all profiles land on neutral.
 */
export function leanClass(profile: {
  kind: string;
  party?: string;
  lean?: string | null;
}): 'left' | 'center' | 'right' | 'mixed' | 'neutral' {
  if (profile.kind === 'catch_all') return 'neutral';
  if (profile.kind === 'official') {
    if (profile.party === 'R') return 'right';
    if (profile.party === 'D') return 'left';
    return 'neutral';
  }
  const l = (profile.lean || 'center').toLowerCase();
  if (l === 'mixed') return 'mixed';
  if (l.includes('left')) return 'left';
  if (l.includes('right')) return 'right';
  return 'center';
}
