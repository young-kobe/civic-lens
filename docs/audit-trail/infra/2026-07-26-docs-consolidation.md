# Documentation consolidated onto the Postgres architecture

**Date:** 2026-07-26
**Layer:** infra (repo-wide docs; code changes were comment-only)
**Todo:** docs/todos/post-rewrite-cutover.md

Every core doc now describes the live Postgres system; the SQLite-era
descriptions are gone.

## The system as it is now

- **Rewritten:** `CLAUDE.md`, root `README.md`, `docs/ARCHITECTURE_DIAGRAM.md`,
  `docs/SCORING_METHODOLOGY.md` (traceability chain is `analysis.runs`;
  favorability and geo sections deleted), `docs/DATABASE_SCHEMA.md` (banner
  flipped to live, SQLite appendix dropped), `docs/INVARIANTS.md` (B1/B3
  repointed at `etl/documents.py` and `engine/propaganda.py`),
  `.agent/workflows/{global,python-ai-reporting,go-ingestion}.md`,
  `.agent/rules/code-style.md`.
- **Walkthroughs collapsed:** the 67-file `docs/walkthroughs/` linear log is
  deleted; its content lives as per-layer digests in
  `docs/audit-trail/<layer>/timeline.md` (five files). Superseded outcomes
  are marked as such in place. `docs/audit-trail/README.md` seeds from the
  timelines.
- **Todos pruned:** deleted `walkthrough-consolidation`, `ui-rework`,
  `containerization`, `bot-propaganda-entity-signals`,
  `backend-aggregator-audit`, `cross-tier-narrative-clustering`,
  `news-visibility-prod`, `verify-backup-artifacts` (live remnants salvaged
  into `ui-feature-restoration`, `eval-expansion`, the new
  `bot-propaganda-signal-calibration`, and `post-rewrite-cutover`).
  Rewritten against the current stack: `eval-expansion`,
  `dead-code-cleanup`, `ui-consistency-audit`,
  `disk-and-replica-health-alerting`.
- **Superseded banners:** `docs/proposals/scale-out-and-targeted-classification.md`
  and `docs/deployment/plan.md` (their stay-on-SQLite recommendations were
  reversed by the redesign); status headers on the two pre-rewrite proposal
  drafts.
- **Comment trim:** lineage comments referencing the deleted stack
  (job_runner, ai_outputs, favorability, walkthrough numbers) removed from
  live analysis/ui/ingest code; comments stating live constraints kept.
  The only remaining walkthrough references in the repo are inside
  `docs/audit-trail/` timelines and the two cutover-era todo checklists.

Verification: full suite green with PG-gated tests (740 passed), UI
typecheck + build clean, `go build` clean, repo-wide stray-reference sweep
clean.
