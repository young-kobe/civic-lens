# 2026-07-10 — Bot amplification uses real narratives; rollups carry evidence

The Bot Detector's "narrative amplification" section is now driven by the
narratives tables instead of bot-indicator strings, and the per-entity
rollups carry flagged-post evidence and populate the officials tier.
UI-layer counterpart: `docs/audit-trail/ui/2026-07-10-bot-page-fixes.md`.

## What shipped

- `aggregators/bot.py::_fetch_narrative_amplification` replaces
  `_narrative_amplification`: amplified "narratives" are actual clustered
  claims (bot-flagged docs ∩ `narrative_docs`), ranked by bot-doc count.
  The old version set `narrative=<indicator string>` — an internal signal
  name is not a talking point, and LLM-echoed slugs
  ("zero_followers_following_listed") leaked verbatim into headline copy.
  Per narrative: example posts (excerpt + permalink), hashtags extracted
  from the actual texts, targets from write-time-resolved
  `target_mentions` (registry display name, raw string fallback), and
  humanized top indicators under whyFlagged only. Empty when no flagged
  doc belongs to a narrative — surfaced honestly, never invented.
- `_humanize_indicator`: snake_case indicator slugs render as words;
  prose indicators pass through untouched.
- `_fetch_entity_rollups` passes the ingestor's `is_official_tier`
  provenance flag to `resolve_entity` and buckets registry-less officials
  into the verified-officials catch-all (same shape as sentiment, audit
  D-4) — previously those rows were dropped and the Politicians &
  Officials column could never populate. Each entity also collects up to
  5 confidence-ranked `FlaggedExample` samples (`BotEntityItem.samples`)
  so the card opens an evidence modal instead of navigating off-site.

## Why

- User-reported: indicator slug shown as a "talking point" in the page
  headline, permanently-empty modal sections (hashtags/targets were
  always `[]`), an empty officials tier, and cards that dead-ended at
  external sites.

## Behavior notes

- The Bot page was ALREADY windowed backend-side (`bot_activity_{window}`
  snapshots + `/bot-activity?window=`, since audit U-1a); the UI simply
  never passed a window and fetched the API's 24h default while labeling
  it "full sample". Fixed UI-side; no backend change needed.
