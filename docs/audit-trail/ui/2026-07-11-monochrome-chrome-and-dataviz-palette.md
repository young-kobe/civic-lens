# 2026-07-11 — Monochromatic chrome + one standardized data-viz palette

The theme is now split into two clearly-labeled, independently-swappable token groups in
`index.css` `:root`: **CHROME** (monochrome neutrals only — links, tabs, focus rings, borders,
brand mark) and **DATA-VIZ** (the only place hue is allowed — chart series, bars, dots, legends,
value marks). This kills the palette drift where ink-blue and navy leaked into chrome across pages
(Bot and Propaganda especially) and reserves the high-contrast Grafana palette for data alone.

## What shipped

- **`--accent` family → neutrals.** `--accent`/`-hover`/`-muted`/`-light`/`-gradient` were an ink
  blue (#1a3a6b); they now resolve to `--neutral-800`/`-900`/`-500`/`-150` and a neutral gradient.
  This single redefinition flips every one of the ~60 CSS `var(--accent*)` chrome usages (links,
  active nav tab, `:focus-visible` outlines, `.badge-accent`, `.chip-accent`,
  `.cross-tier-chip-news`, brand mark, input focus, all the `.example-row-link` /
  `.post-card-permalink` / `.entity-hub-link` links) to monochrome at once. To retheme chrome, edit
  only the `--neutral-*` ramp.
- **Modals lost the colored left rail.** `Modal.tsx` dropped its `accentColor` prop entirely
  (border-left + kicker color); `.modal-surface` is a plain neutral-bordered box and `.modal-kicker`
  is `--neutral-500`. Removed the prop from all seven callers (Narratives x2, Propaganda,
  Bot x2, PublicSentiment) — the red/lean/confidence-tinted borders are gone.
- **`entityLeanAccent()` deleted.** It only fed modal/ticker accent borders; removed the function,
  its `common/index.ts` export, and the three page imports.
- **GlobalTicker lost its `accentColor` left-rail** (chrome) and the `.global-ticker-accent` CSS;
  the ticker's per-value tone colors (data) stay. The `'accent'` ticker tone now resolves to
  `--chart-accent` (a bright data highlight) instead of the old ink-blue chrome accent; Propaganda's
  mid-band ticker tone moved to `warning` so it isn't the same blue as its low band.
- **Lean + source card left-borders neutralized.** `.entity-card.lean-*` and
  `.post-card-x/reddit/news` left borders are now `--neutral-300`. The lean is still carried in
  color by the `.lean-chip-*` legend (kept colored — it names the measured lean); source is named by
  the card's platform label.
- **Navy purged from data marks.** The two data bars that leaned on the old ink-blue `--accent`
  (Bot account-age distribution fill, Bot low-similarity bar) now use `--chart-accent` (bright blue).
- **Admin review banner neutralized** — `--admin-banner-*` were a tinted blue; now `--neutral-*`.

## Dead code removed

- The fully-unused Political-stance palette (`--stance-*`, 6 vars) and Sentiment-favorability palette
  (`--sent-favorable/unfavorable-*`, 6 vars) — both were navy-based and referenced nowhere but their
  own `theme.ts` wrappers. Dropped the vars and the 12 `COLORS` keys.
- Dead `theme.ts` keys with zero JS consumers: `accentHover`, `accentMuted`, `accentLight`, the
  `bgApp/bgCard/bgPanel/bgInset` surface group, `leanLeftLight`, `leanRightLight`, `warningLight`.

## Color-only bar charts now carry a legend + hover key

Follow-up ask: "all bar charts that just use colors need legends and hovertip explanations." An
inventory of every color-encoded bar/segment found two where color was load-bearing with **neither**
a legend nor a hover explanation; both were fixed (the rest already had one or the other, or inline
value labels that make the color redundant):

- **Account-age distribution** (`BotActivityProfiler.tsx`): each bar now has a `title` explaining why
  the newest-account bucket is amber (fresh accounts skew toward automation) vs blue for older
  buckets, plus a `.chart-swatch-legend` keying the two colors.
- **Digest source-mix story bar** (`home/DigestSection.tsx`): the segment color was the *sole*
  encoding of the news/reddit/x split (bar was `aria-hidden`, no labels). Each segment now has a
  `title` (source + count + %), the bar exposes an `aria-label` summary, and a `SourceMixLegend`
  swatch key renders once under the story list.
- New shared `.chart-swatch-legend` / `.chart-swatch-item` / `.chart-swatch` CSS backs both keys.

## Why

- Review ask: "website needs to be monochromatic chrome styled, use high contrast bright colors only
  for DATA VISUALIZATIONS, GRAPHS or LEGENDS… too much palette drift across pages, ie in bot… entire
  app's theme needs to be defined in theme and easily swappable… clean code along way," and "the bot
  and propaganda pages still use navy… high contrast grafana style theme for data charts and graphs."

## Verification

- `npm run typecheck` and `npm run build` both green.
- Visual pass via fixtures still pending (dev env): confirm no colored modal/ticker/card borders
  remain, no navy on Bot/Propaganda data marks, and links/tabs/focus read as monochrome.
