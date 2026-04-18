# 031 — UI Terminal-Density Refactor (Light Theme)

## Context

The dashboard read as a generic card-based analytics UI. Goal: push it toward a Bloomberg-terminal feel — higher information density, sharper panels, monospace numerics, uppercase tracked labels — while staying on a white background so it remains a light theme. Scope was deliberately tokens-first so component code would inherit the new look without sweeping rewrites, with one reference page (`PublicSentiment`) hand-tuned to prove the direction before applying the same treatment to `BotActivityProfiler` and `GlobalHeatmap`.

## Changes

### Design tokens — `ui/src/index.css` (rewrite, same class names)

- Type ramp tightened (14px base, denser `--text-*` steps, `--leading-normal` 1.35) and typeface set to `IBM Plex Sans` with `JetBrains Mono` for numerics.
- Neutral scale rebuilt for higher contrast on pure-white surfaces (`--bg-app: #ffffff`, `--bg-panel: #fafafa`, `--bg-inset: #f4f4f5`); added intermediate `--neutral-75/150` stops.
- Semantic colors re-saturated to ticker-style `--semantic-positive: #008a4c`, `--semantic-negative: #d41e0e`, `--semantic-warning: #b26100`; `--accent: #0047b3`.
- All `--radius-*` collapsed to 2–3px; `--shadow-sm` removed so panels are delineated by 1px borders.
- Added `.num` (monospace + tabular-nums, auto-applied to `.metric-value`, `.metric-value-lg`, `.metric-delta`), `.eyebrow` (uppercase tracked small-caps label), `.status-strip` (flat terminal strip), and `.tick-up/.tick-down/.tick-flat` ticker helpers.
- `.card-header` is now a flush panel strip that bleeds to card edges via negative margins, giving each card a titled header rule instead of floating text.
- `.nav-tab`, `.filter-pill`, `.btn`, `.badge`, `.table th` all switched to uppercase + letter-spacing, sharper corners, and thinner paddings; `.table td.num` right-aligns and monospaces numeric columns.
- Recharts overrides updated: grid lines go to `--neutral-150`, axis/legend text switches to mono 10px.

### `ui/src/App.tsx`

- Header condensed to a single-row strip: page title + subtitle, a vertical divider, and a right-aligned `.status-strip` showing `LIVE` tick-up plus an ISO-formatted UTC timestamp. Footer rewritten in mono/uppercase with the same timestamp.

### `ui/src/pages/PublicSentiment.tsx` (reference page)

- `SENTIMENT_COLORS` and `LABEL_BADGE_STYLES` realigned to the new semantic tokens (`#008a4c` / `#d41e0e`; MIXED uses amber).
- Overview header: eyebrow labels, net-score tinted to match sign, volume rendered in mono.
- Topic rows: `MiniDonut` shrunk to 44px; rows use 10/12px padding, uppercase topic names, mono `+X%` net-score badges with semantic-tinted backgrounds, compact `+N% pos / -N% neg` stats.
- Classification sample cards: flatter 1px borders, mono `CONF` meter, source-metadata line in mono, source-text block now uses a `border-left` accent + `.eyebrow` header instead of a filled box.

### `ui/src/pages/BotActivityProfiler.tsx`

- Calibrated-language warning banner restyled as a `border-left` accent block (no heavy box).
- Section heads (`Narratives with Suspected Bot Amplification`, `Behavioral Signals`) converted to `.eyebrow`.
- Per-narrative card: narrative title uppercased/tracked; "Why flagged", "Example posts", "Top hashtags", "Key phrases", "Primary targets" all use `.eyebrow`. Example posts use left-rule insets instead of filled bubbles.
- Coordination indicator grid: each metric is a row with `border-bottom: 1px solid var(--neutral-150)` and a right-aligned mono value; progress bars for account-age / similarity are 6–8px with 1px radius.
- Link-domain table converted to row-rule list with mono values.

### `ui/src/pages/GlobalHeatmap.tsx`

- `getSentimentColor` palette realigned to the semantic tokens (`#008a4c → #4b9e6d → #8e8e96 → #b26100 → #d41e0e`) so map markers match the rest of the UI.
- Inline `<style>` block rewritten: stats-row now a bordered strip with mono values + eyebrow labels; legend uppercase/tracked; map container sharp-edged (`border-radius: 2px`) with token borders; tooltip mono with uppercased row labels; default country fill switched to `#ececef` / stroke `#d9d9de`.
- Unused `.demo-banner` rule removed.

## Verification

- `cd ui && npm run typecheck` — clean.
- `cd ui && npm run build` — clean (14.5 kB CSS gzipped to 3.56 kB; bundle size unchanged).
- Class names in `index.css` are stable, so existing components (`Card`, `MetricCard`, `Tabs`, `GlobalFilters`, `MethodPopover`, chart components) inherit the new look without edits.
- Palette is deliberately kept on the saturated-green/red pair; a separate "neo-tech" teal/magenta variant was prototyped and reverted pending further direction.
