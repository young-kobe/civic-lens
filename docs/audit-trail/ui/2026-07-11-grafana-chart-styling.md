# 2026-07-11 — Grafana-style charts: back gridlines + brighter series

All full-size charts now sit on subtle horizontal back-gridlines with brighter, higher-contrast
series colors — a Grafana register instead of the prior muted monochrome.

## What shipped

- **Back gridlines** (recharts `CartesianGrid`, horizontal only, `--chart-grid` dashed): added to
  the shared `Sparkline` when it renders as a full chart (`showXAxis` — e.g. the narrative
  "Daily volume"), and to the Tone-over-time line/area/weekday charts (`ToneTrendPanel.tsx`). Tiny
  sparklines (small multiples, top-metrics trail) stay grid-free — gridlines on a 40px strip are
  noise.
- **Brighter colors** (`index.css` `:root`):
  - `--chart-accent` `#1a3a6b` → `#2563eb` (bright blue — the area/line default, GOP trend,
    lifecycle origin fallback).
  - Speaker-tier trio brightened to `--tier-news #2f6fe8` / `--tier-officials #12b3a0` /
    `--tier-public #ea8800` (still blue/teal/amber — a CVD-safe categorical set).
  - `--chart-grid` bumped to `rgba(13,14,18,0.10)` so the gridlines read.
  - Weekday bars now use the vivid `--semantic-positive/-negative` (was the muted chart ramp).
  - Area-fill gradients deepened slightly (0.35 → near-0 opacity).
  - **Source-type trio** (`--source-news/-reddit/-x`) swapped from dark navy / rust / near-black to
    bright blue / orange / purple (`#3b82f6` / `#f97316` / `#a855f7`) — updates the source-mix bar,
    post-card borders, digest story bars, and lifecycle origin colors everywhere at once.

## Dead code removed
- The unused chart ramp — `--chart-positive/-negative/-neutral/-neutral-soft/-accent-soft` and the
  four `--chart-gradient-*` tokens, plus their theme.ts `COLORS` exports (only `chartAccent` and the
  `--chart-grid` token remain). Repointed the one live consumer (`FRESHNESS_SHADE` 7-day step) to
  `--accent-muted`.

## Why

- Review ask: "brighter higher contrast colors and gridlines in back to give a more Grafana-style
  feel; all of our graphs should use this."

## Follow-ups
- The brightened tier trio was set by judgment (the dataviz validator's bundled dir was
  unavailable this session) — re-run `validate_palette.js "#2f6fe8,#12b3a0,#ea8800"` when convenient.
