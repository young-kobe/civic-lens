# Walkthrough consolidation + core-docs rework

The `docs/walkthroughs/` directory has 66 numbered entries. Many narrate earlier designs that no longer exist (pre-entity-registry sentiment, the deprecated heatmap, tier-split narrative frames superseded by the three-way frame). Since we're still pre-1.0, they're more weight than signal for a newcomer.

Goal: collapse the 66-entry linear log into a handful of human-readable timeline documents per layer, so anyone opening the repo can understand *how the current system works* without reading deprecated history. Pair that with a rewrite of the long-lived reference docs.

## Consolidation

- [ ] Read every file in `docs/walkthroughs/`, group by layer (ingestion / analysis / api / ui / infra) and by theme.
- [ ] Write one timeline-style summary per layer, living under `docs/audit-trail/<layer>/timeline.md`. Each entry: date range, what changed at a system level, which walkthrough numbers contributed. Skip the granular file-by-file diffs — those are in git history.
- [ ] Delete the original walkthrough files once their content is reflected in the timelines. Keep `docs/walkthroughs/README.md` as a brief pointer to the audit trail, or delete it entirely.
- [ ] Update any in-repo references to `walkthrough NNN` (there are many — CLAUDE.md, other docs, code comments) to point at the new audit-trail entry or drop the reference when the underlying decision no longer applies.

## Core-docs rework

The top-level reference docs still describe older designs:

- [ ] `docs/ARCHITECTURE_DIAGRAM.md` — re-render for the current four-layer + three-way entity frame.
- [ ] `docs/DATABASE_SCHEMA.md` — audit against the live migrations; remove deprecated tables/columns (place_country_code for the removed heatmap, anything else stale).
- [ ] `docs/SCORING_METHODOLOGY.md` — align with current confidence filter + net-tone formulas + propaganda scoring; remove the geo heatmap scale.
- [ ] `docs/INVARIANTS.md` — already maintained, but confirm every invariant still reflects current code. Particularly C1 (source links on evidence) — make sure every surface honors it.
- [ ] Top-level `README.md` — MVP-ready description: what the system measures, how to run it, where data comes from. Remove references to deprecated features.

## Out of scope

- Per-commit migration log. Git history covers that.
- Backfilling audit-trail entries for work that predates this directory. The timeline summaries are the backfill.
