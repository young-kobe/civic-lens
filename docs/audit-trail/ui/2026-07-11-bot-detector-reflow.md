# 2026-07-11 — Bot Detector: 2-up amplification cards, full-width section header

The Bot Detector's narrative-amplification cards no longer each hog a full row, and the
"Narratives with Suspected Bot Amplification" section header no longer strands empty space beside
a tall coordination card.

## What shipped

- **Amplification cards 2-up** (`BotActivityProfiler.tsx`): the `data.narrativeAmplification` map
  wrapper went `col-span-12` → `col-span-6`. The in-grid `NarrativeAmplificationCard` holds only a
  title, likelihood badge, two "why flagged" bullets, and buttons (all already `flex-wrap`); the
  heavy example-post grid renders inside a `Modal`, so the narrower column is safe.
- **Coordination full width**: `CoordinationSummary` moved from `col-span-5` to `col-span-12` — its
  internal stat grid fills the row, so it no longer pairs a tall card with a short label band.
- **Section header full width**: the amplification section label moved from `col-span-7` (beside
  coordination) to a `col-span-12` band directly above the 2-up cards, eliminating the empty space
  the review circled.

## Why

- Round-2 review: "the rows I circled [the full-width amplification cards] do not need to take up
  the row" and the empty band beside the section label. Builds on the angular content-hug system in
  `2026-07-11-angular-module-system.md`.

## Follow-ups
- Round 2 (angular modules + per-page reflows) is complete. Queued: the round-1 data work
  (narrative news cards, propaganda↔party, clustering pipeline fix).
