# 2026-04-22 — Bot Detector flagged-post modal renders source links

UI counterpart to `../analysis/2026-04-22-bot-flagged-examples-with-source-links.md`. The Bot Detector's amplification drill-down used to show flagged posts as italicized quoted strings with no provenance. Now each row names its source (outlet, official, or subreddit/X handle) and carries a `View original ↗` permalink.

## What shipped

- `ui/src/types.ts` — new `FlaggedExample` interface (`doc_id`, `text`, `source_label`, `url`). `NarrativeAmplification.examplePosts` retyped from `string[]` to `FlaggedExample[]`.
- `ui/src/pages/BotActivityProfiler.tsx` — the modal's "Example Posts" section (renamed "Flagged Posts" for clarity) now iterates `FlaggedExample` rows. Each renders as an italic pull-quote plus a mono-styled source line: `{source_label}  View original ↗`. Uses the shared `.example-row-link` class already styled by the Propaganda page's examples. When `examplePosts` is empty, a muted "No individual posts surfaced yet for this indicator" message renders — honest fallback for indicators whose bot-flagged docs lacked text.
- `ui/src/services/fixtures.ts` — mocks updated to the new shape: every example now carries a synthesized X or Reddit permalink so dev-mock mode exercises the link rendering.

## Why

Invariant C1. Every piece of per-doc evidence across the site must name its source and link back to the original. The Bot Detector was the last page-level evidence surface without this guarantee.

## Follow-ups

None. The `FlaggedExample` shape is ready for the future "regroup amplification cards by entity" work tracked in `docs/todos/bot-propaganda-entity-signals.md`.
