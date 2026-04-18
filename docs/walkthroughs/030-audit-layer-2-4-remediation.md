# 030 — Audit Remediation for Layers 2–4

## Context

Second pass on the open items from `docs/audits/04_16_2026.md` for layers 2–4 (Analysis/ETL, Data, UI). Walkthrough 029 closed the structural LLM-hardening gaps (evidence validation, post-parse schema validation, system-prompt archival). This walkthrough closes the supporting-code, schema, and UI-clarity gaps. Layer 1 gaps (golden-set evaluation, confidence calibration) are deliberately not in scope — those require labeled data and a separate workstream.

## Changes

### Layer 4 — UI clarity

- `ui/src/pages/PublicSentiment.tsx` — added a top-of-page sampling disclaimer banner covering Reddit + X. Renamed the "Social Media vs News Outlets" card to "Sampled Social Media vs News Outlets" and set the left-column badge to "Sampled Social (Reddit + X)". Method-popover enumerates what is and is not in the sample (included: Reddit political subreddits and X political queries we ingest; excluded: TikTok, Facebook, private Discords, etc.). The methodology panel's data-sources copy was updated to explicitly name Reddit and X rather than a generic "social media." An earlier draft of this walkthrough labeled the social side as Reddit-only based on a faulty audit note; corrected after review — X posts are a first-class social source for this project.
- `ui/src/pages/BotActivityProfiler.tsx` — added a `text-xs text-muted` legend above the posting-cadence heatmap explaining row/column axes, the UTC time zone, and the human baseline. Added a similar legend above the text-similarity distribution with a concrete baseline (20–30% on natural discourse) so the ">80%" threshold has meaning.

### Layer 3 — Data schema

- `data/migrations/005_drop_dead_tables.sql` — drops `author_profiles`, `engagement_metrics`, `reddit_comments_raw`, and the now-dead `clusters` / `cluster_assignments` tables (the latter two were orphaned when walkthrough 029 removed clustering). Associated indexes dropped too.
- `data/migrations/006_docs_country_column.sql` — promotes `place_country_code` from `metadata_json` JSON to a first-class column on `docs`, backfills existing x_post rows via `json_extract`, and adds `idx_docs_country`.
- `data/migrations/007_audit_and_propagation_tables.sql` — adds five new tables:
  - `prompt_versions(prompt_version PK, task_type, system_prompt, user_prompt_template, created_at, notes)` — full prompt text per version. **Writer wired:** `save_ai_output` now upserts on every inference; the previous `output_json._system_prompt` embedding was removed.
  - `ai_output_evals(eval_id PK, ai_output_id UNIQUE, doc_id, task_type, human_label, human_confidence, is_correct, is_golden, reviewer_id, reviewed_at, notes)` — human overrides and golden-set markers. **Writer not yet wired** (a review UI or CLI will populate these).
  - `narratives(narrative_id PK, name, description, first_seen_at, origin_doc_id, created_at, updated_at)` — identity for a distinct claim.
  - `narrative_docs(assignment_id PK, narrative_id, doc_id, discovered_at, confidence, UNIQUE(narrative_id, doc_id))` — doc-to-narrative membership edges.
  - `narrative_citations(citation_id PK, source_doc_id, target_doc_id OR target_url, link_type, discovered_at)` — cross-source reference edges (url_citation, quote, reply, retweet, repost). **Writers not yet wired** — narrative-extraction pipeline is a separate workstream.

### Layer 2 — Analysis / ETL

- `analysis/src/etl/loader.py:save_ai_output` — replaces the `_system_prompt` JSON embedding with an `INSERT OR IGNORE INTO prompt_versions` upsert. Rows in `ai_outputs` are now joined to `prompt_versions` via the `prompt_version` column for audit.
- `analysis/src/etl/loader.py:_load_x_batch` — writes the new `docs.place_country_code` column alongside `metadata_json` (metadata retained for bot-detection-time reads).
- `analysis/src/reporting/aggregators/geo.py` — rewritten to:
  1. Query `docs.place_country_code` directly (no JSON parse on the hot path).
  2. Fix a real bug: the old code read `sentiment["sentiment_score"]` and `sentiment["gop_favorability"]`, neither of which the sentiment task actually produces. Every country's `avg_sentiment` was silently 0.0. Now maps `sentiment.label` ∈ {POSITIVE, NEGATIVE, NEUTRAL, MIXED} to a signed score and multiplies by `ai_outputs.confidence`, producing a [-1, 1] value that matches the UI's color thresholds.
  3. Drops `avg_favorability` from the response (walkthrough 029 already removed it from the UI; the field was always 0.0 anyway).
- `analysis/src/api/server.py:_get_cached_or_fallback` — now uses `SnapshotCache.load_with_meta` and logs a warning when the cached snapshot is older than 24 hours. This surfaces "the nightly pipeline stopped running" without adding a live-compute path to UI requests.

## Verification

- Migrations 005/006/007 applied cleanly against a copy of the live dev DB. `docs` backfill: 2,784/2,784 x_post rows populated `place_country_code`. Final table list: `ai_output_evals, ai_outputs, articles_raw, docs, narrative_citations, narrative_docs, narratives, pages, prompt_versions, reddit_posts_raw, schema_version, sqlite_sequence, x_posts_raw, x_users_raw` (clusters + dead tables gone).
- `test_loader` (2), `test_engines` (6), `test_rich_aggregators` (4), `test_cache` (2) — all pass after migration changes.
- `cd ui && npm run typecheck` clean; `npm run build` clean.

## Deploy note

Run `.\run.ps1 migrate` on the live DB to apply migrations 005–007 before the next analysis pipeline run. The loader will start populating `docs.place_country_code` and `prompt_versions` from that point; historical `ai_outputs` rows will still have `_system_prompt` embedded in their `output_json` (harmless).

## What remains open from audit 04_16_2026

- Layer 1: golden-set evaluation harness + confidence calibration (prerequisite for any >95% accuracy claim).
- Layer 3: writers for `ai_output_evals`, `narratives`, `narrative_docs`, `narrative_citations` (schema landed here; populating them is a separate design).
- Layer 2: polling-scraper consolidation is still open but low-impact.
