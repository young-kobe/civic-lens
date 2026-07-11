# 2026-07-10 — Entity hub cross-links, claim/entity search, topic self-links

Phase 3 (final) of the UI depth overhaul — closes
`docs/todos/ui-depth-overhaul.md`, which is deleted with this entry per the
todo convention. Phase 1: `2026-07-10-ui-depth-overhaul-phase1.md`;
Phase 2: `2026-07-10-phase2-enrichment-consumption.md`.

## What shipped

- **Entity hub** (`components/common/EntityHubLinks.tsx`): every entity
  modal (Tone, Propaganda, Bot Detector, Narratives' first-seen-entity
  modal) carries a "See this entity on: ..." row deep-linking
  `#<tab>?entity=<kind>:<key>` to the other pages. All three signal pages
  resolve the `entity=` param after data load — the matching entity's
  modal opens; an entity with no data in the window clears the param and
  no-ops. Catch-all buckets render no hub row (tier aggregates, not
  entities). The Bots page's modal state lifted from the grid to the page
  to support resolution.
- **Search**: the Data Desk cross-signal matrix gained a name filter
  input; the Narratives page gained a claim search that filters the
  lifecycle strip, the three-way grid, and the cross-group panel together,
  with an honest "N of M claims match" line. Both are client-side over the
  already-loaded payloads.
- **Topic self-link**: the divergence panel's samples modal gained a
  "Filter the whole page to <topic>" action wired to the tab bar's
  setter (unknown/retired topics no-op).
- **Freshness strip**: the Tone page's how-this-works collapsible renders
  the previously unused `byTimeWindow[]` as an age-mix bar ("how old is
  what we scored"), darker = fresher, with visible percentage legend.

## Cleanup decisions (the todo's conditional boxes, resolved)

- **unavatar.io stays.** Backend `author.avatar_url` (Phase 2c) covers
  PostCard X samples, and PostCard prefers it; but entity profile cards
  draw officials' avatars from the registry, which stores no image URLs —
  coverage does not suffice to drop the fallback service.
- **`isNoiseLabel` display filter stays until ~2026-07-22.** Indicator
  sanitization landed at the write path on 2026-04-23; the 90d window can
  surface pre-fix `ai_outputs` rows until they age out in late July.
  Removing the filter then is a two-line deletion in
  `BotActivityProfiler.tsx` plus the adapter regex in `PostCard.tsx`.

## Why

- One entity's tone, propaganda, and bot readings were three separate
  navigations with no thread between them; recurring claims could only be
  found by scrolling. The deep-link scheme from Phase 1 already carried
  the routing — Phase 3 finished the resolution side on every page.

## Follow-ups

- None for this initiative. Future UI work starts a new todo.
