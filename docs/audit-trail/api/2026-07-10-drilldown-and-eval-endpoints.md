# 2026-07-10 — /entity-posts (first read-only DB path) + /eval-accuracy

Two endpoints added to `analysis/src/api/routers/data.py`.

## GET /api/v1/entity-posts

`?kind=&key=&window=&limit=&offset=` — the full, paginated, newest-first
list of classified posts behind one entity card, served live from SQLite
via `reporting.entity_posts.fetch_entity_posts`. Rate-limited 30/minute
(the `/movers` pattern). Unresolvable kind/key → 404.

This is the API layer's first database dependency, and it is deliberately
narrow: the snapshot cache remains the contract for every aggregate; this
path serves only row retrieval (indexed SELECT + LIMIT/OFFSET, page cap 50)
that the cache pre-truncates by design (~10 highest-confidence samples per
entity). The "no heavy aggregation at request time" invariant is unchanged.
Rationale: the "Other X users" card is the highest-volume card on the
Overall Tone page and its modal could previously show at most 10 posts —
the drill-down is precisely the thing a pre-computed aggregate cannot serve.

## GET /api/v1/eval-accuracy

Public per-task human-review agreement from `ai_output_evals`, served from
the `eval_accuracy` snapshot (written by `job_runner.save_snapshots`) with
live fallback to `ReviewService.get_public_accuracy()`. Aggregate-only:
task-level counts and percentages, accuracy suppressed server-side below
the 20-scored-reviews floor. Reviewer identity and per-row labels remain
behind the admin-gated `/review/*` endpoints.

Cross-links: `docs/audit-trail/analysis/2026-07-10-entity-accounts-and-drilldown-reads.md`,
`docs/audit-trail/ui/2026-07-10-account-cards-drilldown-overlays.md`.
